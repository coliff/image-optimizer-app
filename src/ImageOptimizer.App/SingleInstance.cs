using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace ImageOptimizer.App;

/// <summary>
/// Keeps one window per user session. Later launches (opening the app again, or Explorer starting it once per
/// selected file) send their files to the first one over a named pipe and exit.
/// </summary>
public sealed class SingleInstance : IDisposable
{
  private readonly Mutex _mutex;
  private readonly string _pipeName;
  private readonly CancellationTokenSource _stop = new();

  public SingleInstance(string? name = null)
  {
    name ??= DefaultName();
    _pipeName = name;
    // Only the mutex's existence matters: it lasts as long as any copy of the app holds it open.
    _mutex = new Mutex(initiallyOwned: false, $@"Local\{name}", out var createdNew);
    IsFirst = createdNew;
  }

  /// <summary>True when no other copy of the app is running in this session.</summary>
  public bool IsFirst { get; }

  /// <summary>Hands paths to the running copy. False if it couldn't be reached (for example, it's closing).</summary>
  public bool TrySend(IEnumerable<string> paths, TimeSpan timeout)
  {
    try
    {
      using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
      client.Connect(timeout);
      // Lets the running copy bring its window to the front.
      if (OperatingSystem.IsWindows())
        AllowSetForegroundWindow(AsfwAny);
      // Explorer passes full paths, but a command line may not, and the running copy has its own working folder.
      var message = string.Join('\n', paths.Select(Path.GetFullPath));
      var bytes = Encoding.UTF8.GetBytes(message);
      client.Write(bytes);
      client.Flush();
      return true;
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
    {
      return false;
    }
  }

  /// <summary>Starts accepting paths from later launches. <paramref name="received"/> runs on a background thread.</summary>
  public void Listen(Action<string[]> received)
  {
    if (!IsFirst)
      throw new InvalidOperationException("Only the first copy of the app listens.");
    _ = Task.Run(() => ListenAsync(received, _stop.Token));
  }

  private async Task ListenAsync(Action<string[]> received, CancellationToken cancel)
  {
    while (!cancel.IsCancellationRequested)
    {
      NamedPipeServerStream server;
      try
      {
        server = new NamedPipeServerStream(_pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await server.WaitForConnectionAsync(cancel);
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (IOException)
      {
        // Not expected, but a broken connection mustn't stop later ones from getting through.
        await Task.Delay(100, CancellationToken.None);
        continue;
      }

      // Read on the side so the next launch can connect straight away; Explorer starts many at once.
      _ = Task.Run(async () =>
      {
        await using (server)
        {
          try
          {
            using var reader = new StreamReader(server, Encoding.UTF8);
            var message = await reader.ReadToEndAsync(cancel);
            received(message.Split('\n', StringSplitOptions.RemoveEmptyEntries));
          }
          catch (Exception ex) when (ex is IOException or OperationCanceledException)
          {
          }
        }
      }, CancellationToken.None);
    }
  }

  public void Dispose()
  {
    _stop.Cancel();
    _mutex.Dispose();
  }

  /// <summary>Unique to the user and their sign-in session, so other people on the same PC get their own window.</summary>
  private static string DefaultName()
  {
    var user = OperatingSystem.IsWindows() ? WindowsIdentity.GetCurrent().User?.Value : null;
    return $"ImageOptimizer.{user ?? Environment.UserName}.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
  }

  private const int AsfwAny = -1;

  [DllImport("user32.dll")]
  private static extern bool AllowSetForegroundWindow(int processId);
}
