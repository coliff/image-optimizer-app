using System.Diagnostics;

namespace ImageOptimizer.Core;

public sealed class ToolNotFoundException(string tool)
    : Exception(Strings.ToolMissing(tool))
{
  public string Tool { get; } = tool;
}

/// <summary>
/// A bundled tool stopped with an error. <see cref="Exception.Message"/> is a plain, translated explanation for
/// people; <see cref="Detail"/> has the tool's own (English) output, for bug reports.
/// </summary>
public sealed class ToolFailedException(string tool, int exitCode, string output)
    : Exception(Strings.ToolFailed)
{
  public string Tool { get; } = tool;
  public int ExitCode { get; } = exitCode;
  public string Output { get; } = output;
  public string Detail => $"{Tool} (exit code {ExitCode}): {Output}".Trim().TrimEnd(':');
}

public interface IToolRunner
{
  Task RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

/// <summary>Finds and runs the bundled command-line optimizers.</summary>
public sealed class ToolRunner : IToolRunner
{
  private readonly IReadOnlyList<string> _searchDirectories;
  private readonly bool _searchPath;

  /// <param name="searchDirectories">Folders holding the bundled tools; <see cref="DefaultSearchDirectories"/> when null.</param>
  /// <param name="searchPath">
  /// Also look on PATH when a tool isn't in those folders. Only debug builds do this by default, so development builds
  /// work with locally installed tools. A release build only ever runs the tools it ships with, never an unknown copy
  /// of the same name that happens to be on PATH.
  /// </param>
  public ToolRunner(IEnumerable<string>? searchDirectories = null, bool? searchPath = null)
  {
    _searchDirectories = (searchDirectories ?? DefaultSearchDirectories()).ToList();
    _searchPath = searchPath ?? IsDebugBuild;
  }

#if DEBUG
  private const bool IsDebugBuild = true;
#else
  private const bool IsDebugBuild = false;
#endif

  public static IEnumerable<string> DefaultSearchDirectories()
  {
    var overrideDir = Environment.GetEnvironmentVariable("IMAGEOPTIMIZER_TOOLS");
    if (!string.IsNullOrEmpty(overrideDir))
      yield return overrideDir;
    yield return Path.Combine(AppContext.BaseDirectory, "tools");
  }

  // Tools that ship with their own DLLs live in a subfolder, so their DLLs can't clash with other tools' DLLs
  // (libjxl and libjpeg-turbo both have a jpeg62.dll).
  private static readonly Dictionary<string, string> Subfolders = new(StringComparer.OrdinalIgnoreCase)
  {
    ["cjxl"] = "jxl",
    ["djxl"] = "jxl",
  };

  public string? Locate(string tool) => LocateFile(OperatingSystem.IsWindows() ? tool + ".exe" : tool);

  /// <summary>Finds a bundled file (a tool or a script) in the tools folders, then (in debug builds) on PATH.</summary>
  public string? LocateFile(string fileName)
  {
    Subfolders.TryGetValue(Path.GetFileNameWithoutExtension(fileName), out var subfolder);
    foreach (var dir in _searchDirectories)
    {
      var candidate = Path.Combine(dir, subfolder ?? "", fileName);
      if (File.Exists(candidate))
        return candidate;
    }

    if (!_searchPath)
      return null;

    // Fall back to PATH so development builds work with locally installed tools.
    var path = Environment.GetEnvironmentVariable("PATH") ?? "";
    foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
      var candidate = Path.Combine(dir, fileName);
      if (File.Exists(candidate))
        return candidate;
    }
    return null;
  }

  public async Task RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
  {
    var executable = Locate(tool) ?? throw new ToolNotFoundException(tool);
    var startInfo = new ProcessStartInfo(executable)
    {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      WorkingDirectory = Path.GetDirectoryName(executable)!,
    };
    foreach (var argument in arguments)
      startInfo.ArgumentList.Add(argument);

    using var process = new Process { StartInfo = startInfo };
    process.Start();
    // Closing the app mid-batch (or a crash) ends the tool too, rather than leaving it running in the background.
    ChildProcessJob.Shared?.TryAdd(process);
    try
    {
      // Keep the desktop responsive while large batches are crunching.
      process.PriorityClass = ProcessPriorityClass.BelowNormal;
    }
    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
    {
    }

    var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
    var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
    try
    {
      await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      try
      {
        process.Kill(entireProcessTree: true);
      }
      catch (InvalidOperationException)
      {
      }
      throw;
    }

    var output = (await stderr.ConfigureAwait(false)) + (await stdout.ConfigureAwait(false));
    if (process.ExitCode != 0)
      throw new ToolFailedException(tool, process.ExitCode, FirstLine(output));
  }

  private static string FirstLine(string text)
  {
    var trimmed = text.Trim();
    var newline = trimmed.IndexOfAny(['\r', '\n']);
    return newline < 0 ? trimmed : trimmed[..newline];
  }
}
