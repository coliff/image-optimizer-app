using System.IO;
using ImageOptimizer.Core;
using ImageOptimizer.Core.Tests;

namespace ImageOptimizer.App.Tests;

public class WpfPixelVerifierTests : IDisposable
{
  private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImageOptimizerAppTests", Guid.NewGuid().ToString("N"));
  private readonly ToolRunner _tools = new();

  public WpfPixelVerifierTests() => Directory.CreateDirectory(_directory);

  public void Dispose()
  {
    try
    {
      Directory.Delete(_directory, recursive: true);
    }
    catch (IOException)
    {
    }
  }

  private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

  private string Copy(string name)
  {
    var path = Path.Combine(_directory, name);
    File.Copy(Fixture(name), path);
    return path;
  }

  private void RequireTools(params string[] tools) => ToolRequirements.Require(_tools, tools);

  [Fact]
  public async Task Png_recompression_is_pixel_identical()
  {
    var result = await new WpfPixelVerifier().VerifyAsync(
        ImageFormat.Png, Fixture("unoptimized.png"), Fixture("optimized.png"), _directory, TestContext.Current.CancellationToken);
    Assert.Equal(VerificationResult.Identical, result);
  }

  [Fact]
  public async Task Different_images_are_rejected()
  {
    var other = Path.Combine(_directory, "other.png");
    var bytes = File.ReadAllBytes(Fixture("unoptimized.png"));
    // Flip one byte of raw pixel data inside the stored (uncompressed) deflate stream.
    bytes[bytes.Length - 200] ^= 0xFF;
    File.WriteAllBytes(other, bytes);

    var result = await new WpfPixelVerifier().VerifyAsync(
        ImageFormat.Png, Fixture("unoptimized.png"), other, _directory, TestContext.Current.CancellationToken);
    Assert.NotEqual(VerificationResult.Identical, result);
  }

  [Fact]
  public async Task Other_formats_are_left_to_other_verifiers()
  {
    var result = await new WpfPixelVerifier().VerifyAsync(
        ImageFormat.Gif, Fixture("animation.gif"), Fixture("animation.gif"), _directory, TestContext.Current.CancellationToken);
    Assert.Equal(VerificationResult.NotSupported, result);
  }

  [Theory]
  [InlineData("unoptimized.png")]
  [InlineData("photo.jpg")]
  [InlineData("lossless.webp")]
  [InlineData("animation.gif")]
  [InlineData("drawing.svg")]
  public async Task Full_pipeline_with_windows_verifier(string fixture)
  {
    RequireTools("oxipng", "jpegtran", "cwebp", "dwebp", "webpmux", "gifsicle");
    ToolRequirements.RequireFile(_tools, SvgOptimizer.ScriptName);
    var path = Copy(fixture);
    var before = new FileInfo(path).Length;
    var engine = ImageOptimizerEngine.CreateDefault(_tools, new WpfPixelVerifier());

    var result = await engine.OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.True(result.Status is OptimizationStatus.Optimized or OptimizationStatus.AlreadyOptimized, $"{result.Status}: {result.Message}");
    Assert.True(new FileInfo(path).Length <= before);
  }

  [Fact]
  public async Task Jpeg_baseline_and_progressive_decode_identically()
  {
    RequireTools("jpegtran");
    var source = Copy("photo.jpg");
    var progressive = Path.Combine(_directory, "progressive.jpg");
    await _tools.RunAsync("jpegtran", ["-copy", "none", "-progressive", "-outfile", progressive, source], TestContext.Current.CancellationToken);

    var result = await new WpfPixelVerifier().VerifyAsync(ImageFormat.Jpeg, source, progressive, _directory, TestContext.Current.CancellationToken);
    Assert.Equal(VerificationResult.Identical, result);
  }
}
