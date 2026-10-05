using System.Diagnostics;

namespace ImageOptimizer.Core.Tests;

public class ChildProcessJobTests
{
  [Fact]
  public async Task Closing_the_job_ends_its_processes()
  {
    if (!OperatingSystem.IsWindows())
      Assert.Skip("Job objects are a Windows feature.");

    using var job = ChildProcessJob.TryCreate();
    Assert.NotNull(job);

    // Stands in for a slow optimizer that's still running when the app exits.
    using var process = Process.Start(new ProcessStartInfo("ping", ["-n", "60", "127.0.0.1"])
    {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
    })!;
    try
    {
      Assert.True(job.TryAdd(process));
      job.Dispose();
      await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
      Assert.True(process.HasExited);
    }
    finally
    {
      if (!process.HasExited)
        process.Kill();
    }
  }

  [Fact]
  public void The_app_has_its_own_job_on_Windows()
  {
    Assert.Equal(OperatingSystem.IsWindows(), ChildProcessJob.Shared is not null);
  }
}
