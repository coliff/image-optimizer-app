using System.Text;
using System.Text.Json;
using Jint;
using Jint.Native;
using Jint.Constraints;
using Jint.Runtime;

namespace ImageOptimizer.Core;

/// <summary>
/// SVG: SVGO's own browser build (svgo.browser.js, fetched with the other tools) running in the embedded
/// Jint JavaScript engine, so no Node.js install is needed.
/// </summary>
public sealed class SvgOptimizer(Func<string?> locateScript) : IFormatOptimizer
{
  public const string ScriptName = "svgo.browser.js";

  private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

  // Jint engines aren't thread-safe and loading SVGO takes a moment, so share one engine and take turns.
  private readonly SemaphoreSlim _gate = new(1, 1);
  private Engine? _engine;
  private JsValue? _optimize;

  public ImageFormat Format => ImageFormat.Svg;

  public async Task<CandidateSet> CreateCandidatesAsync(OptimizationContext context, CancellationToken cancellationToken)
  {
    // The result is always written as UTF-8, so text in any other encoding would come out garbled.
    var bytes = await File.ReadAllBytesAsync(context.InputPath, cancellationToken).ConfigureAwait(false);
    string input;
    try
    {
      input = StrictUtf8.GetString(bytes.AsSpan(bytes.AsSpan().StartsWith(StrictUtf8.Preamble) ? StrictUtf8.Preamble.Length : 0));
    }
    catch (DecoderFallbackException)
    {
      return CandidateSet.Skip(Strings.SvgNotUtf8);
    }
    var config = BuildConfig(context.Settings);

    string? output;
    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      output = await RunWithLargeStackAsync(() => Optimize(input, config, cancellationToken)).ConfigureAwait(false);
    }
    finally
    {
      _gate.Release();
    }

    if (output is null)
      return CandidateSet.Skip(Strings.SvgTooDeep);
    if (!output.Contains("<svg", StringComparison.Ordinal))
      throw new ToolFailedException("svgo", 1, "SVGO returned something that isn't an SVG");

    var candidate = context.WorkFile("svgo.svg");
    await File.WriteAllTextAsync(candidate, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
    return new CandidateSet([new Candidate(candidate)]);
  }

  internal static string BuildConfig(OptimizerSettings settings)
  {
    var overrides = new Dictionary<string, bool>();
    if (!settings.StripMetadata)
    {
      overrides["removeComments"] = false;
      overrides["removeMetadata"] = false;
      overrides["removeEditorsNSData"] = false;
    }

    return JsonSerializer.Serialize(new
    {
      multipass = settings.MaximumCompression,
      plugins = new object[] { new { name = "preset-default", @params = new { overrides } } },
    });
  }

  // SVGO walks the document recursively, and each level of JavaScript recursion takes a lot of .NET stack in
  // Jint. A stack overflow can't be caught and would close the app, so SVGO runs on a thread with a large stack
  // and Jint stops it long before that stack runs out. SVGs nested that deeply are left untouched.
  private const int StackSize = 256 * 1024 * 1024;
  private const int MaxRecursion = 10_000;

  private static Task<string?> RunWithLargeStackAsync(Func<string?> work)
  {
    var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
    var thread = new Thread(() =>
    {
      try
      {
        result.SetResult(work());
      }
      catch (Exception ex)
      {
        result.SetException(ex);
      }
    }, StackSize)
    {
      IsBackground = true,
      Name = "SVGO",
    };
    thread.Start();
    return result.Task;
  }

  /// <summary>Returns the optimized SVG, or null when it's nested too deeply for SVGO.</summary>
  private string? Optimize(string input, string config, CancellationToken cancellationToken)
  {
    try
    {
      if (_engine is null || _optimize is null)
      {
        var script = locateScript() ?? throw new ToolNotFoundException("svgo");
        var engine = new Engine(options => options
            .LimitRecursion(MaxRecursion)
            // A placeholder token that can be cancelled, so Jint adds the check that each run's token replaces.
            .CancellationToken(new CancellationTokenSource().Token));
        engine.Modules.Add("svgo", File.ReadAllText(script));
        _optimize = engine.Modules.Import("svgo").Get("optimize");
        _engine = engine;
      }

      // Lets closing the app stop a large SVG part way through, like the other optimizers.
      _engine.Constraints.Find<CancellationConstraint>()?.Reset(cancellationToken);
      var options = _engine.Evaluate($"({config})");
      return _engine.Invoke(_optimize, input, options).AsObject().Get("data").AsString();
    }
    catch (RecursionDepthOverflowException)
    {
      return null;
    }
    catch (ExecutionCanceledException)
    {
      throw new OperationCanceledException(cancellationToken);
    }
    catch (JintException ex)
    {
      throw new ToolFailedException("svgo", 1, ex.Message);
    }
  }
}
