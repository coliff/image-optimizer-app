namespace ImageOptimizer.Core.Tests;

public class ToolRunnerTests
{
  // A program that's on PATH on every machine, but never in the tools folder.
  private static readonly string OnPath = OperatingSystem.IsWindows() ? "cmd" : "sh";

  [Fact]
  public void Release_builds_only_use_the_bundled_tools()
  {
    var tools = new ToolRunner([Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))], searchPath: false);
    Assert.Null(tools.Locate(OnPath));
  }

  [Fact]
  public void Development_builds_can_use_tools_on_path()
  {
    var tools = new ToolRunner([Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))], searchPath: true);
    Assert.NotNull(tools.Locate(OnPath));
  }
}
