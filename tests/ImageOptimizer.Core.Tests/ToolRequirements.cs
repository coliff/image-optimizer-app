namespace ImageOptimizer.Core.Tests;

/// <summary>
/// Skips a test when an optimizer isn't installed, unless IMAGEOPTIMIZER_REQUIRE_TOOLS=1 is set
/// (as it is in CI, where the bundled tools are downloaded), in which case the test fails instead.
/// </summary>
public static class ToolRequirements
{
  public static void Require(ToolRunner tools, params string[] names) =>
    Check(names.Where(t => tools.Locate(t) is null).ToList());

  public static void RequireFile(ToolRunner tools, string fileName) =>
    Check(tools.LocateFile(fileName) is null ? [fileName] : []);

  private static void Check(List<string> missing)
  {
    if (missing.Count == 0)
      return;
    var message = "Missing tools: " + string.Join(", ", missing);
    if (Environment.GetEnvironmentVariable("IMAGEOPTIMIZER_REQUIRE_TOOLS") == "1")
      Assert.Fail(message);
    Assert.Skip(message);
  }
}
