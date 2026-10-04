namespace ImageOptimizer.Core.Tests;

/// <summary>
/// Runs the real optimizers. They are skipped when a tool isn't installed, unless
/// IMAGEOPTIMIZER_REQUIRE_TOOLS=1 is set (as it is in CI, where the bundled tools are downloaded).
/// </summary>
public class ToolIntegrationTests : IDisposable
{
  private readonly TestFiles _files = new();
  private readonly ToolRunner _tools = new();

  public void Dispose() => _files.Dispose();

  private void RequireTools(params string[] tools) => ToolRequirements.Require(_tools, tools);

  private ImageOptimizerEngine Engine() =>
      ImageOptimizerEngine.CreateDefault(_tools, new ExactBytesVerifier());

  /// <summary>Stand-in for the WPF decoder used by the app: compares decoded pixels via the tools themselves.</summary>
  private sealed class ExactBytesVerifier : IPixelVerifier
  {
    public Task<VerificationResult> VerifyAsync(ImageFormat format, string originalPath, string candidatePath, string workDirectory, CancellationToken cancellationToken) =>
        Task.FromResult(VerificationResult.NotSupported);
  }

  [Fact]
  public async Task Png_gets_smaller()
  {
    RequireTools("oxipng");
    var path = _files.CopyFixture("unoptimized.png");
    var before = new FileInfo(path).Length;

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Optimized, result.Status);
    Assert.True(new FileInfo(path).Length < before);
    Assert.Equal(ImageFormat.Png, ImageFormats.Detect(path));
  }

  [Fact]
  public async Task Png_with_maximum_compression_gets_smaller()
  {
    RequireTools("oxipng");
    var path = _files.CopyFixture("unoptimized.png");

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings { MaximumCompression = true }, TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Optimized, result.Status);
  }

  [Fact]
  public async Task Already_optimized_png_is_left_alone()
  {
    RequireTools("oxipng");
    var path = _files.CopyFixture("optimized.png");
    var before = File.ReadAllBytes(path);

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.AlreadyOptimized, result.Status);
    Assert.Equal(before, File.ReadAllBytes(path));
  }

  [Fact]
  public async Task Animated_png_is_left_alone()
  {
    var path = _files.Write("anim.png", TestFiles.MakeAnimatedPng(File.ReadAllBytes(TestFiles.FixturePath("unoptimized.png"))));
    var before = File.ReadAllBytes(path);

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Skipped, result.Status);
    Assert.Equal(before, File.ReadAllBytes(path));
  }

  [Fact]
  public async Task Jpeg_gets_smaller()
  {
    RequireTools("jpegtran");
    var path = _files.CopyFixture("photo.jpg");
    var before = new FileInfo(path).Length;

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Optimized, result.Status);
    Assert.True(new FileInfo(path).Length < before);
    Assert.Equal(ImageFormat.Jpeg, ImageFormats.Detect(path));
  }

  [Fact]
  public async Task Jpeg_keeps_exif_orientation()
  {
    RequireTools("jpegtran");
    var jpeg = TestFiles.WithExifOrientation(File.ReadAllBytes(TestFiles.FixturePath("photo.jpg")), 6);
    var path = _files.Write("rotated.jpg", jpeg);

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Optimized, result.Status);
    Assert.Equal(6, ImageInspector.GetJpegExifOrientation(File.ReadAllBytes(path)));
  }

  [Fact]
  public async Task Lossless_webp_gets_smaller_with_identical_pixels()
  {
    RequireTools("cwebp", "dwebp", "webpmux");
    var path = _files.CopyFixture("lossless.webp");
    var before = new FileInfo(path).Length;

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Optimized, result.Status);
    Assert.True(new FileInfo(path).Length < before);
    Assert.True(ImageInspector.InspectWebP(File.ReadAllBytes(path)).IsStillLossless);

    var verifier = new WebPPixelVerifier(_tools);
    Assert.Equal(VerificationResult.Identical,
        await verifier.VerifyAsync(ImageFormat.WebP, TestFiles.FixturePath("lossless.webp"), path, _files.Directory, TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Lossy_webp_is_never_reencoded()
  {
    RequireTools("cwebp", "dwebp", "webpmux");
    var path = _files.CopyFixture("lossy.webp");
    var before = File.ReadAllBytes(path);

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Skipped, result.Status);
    Assert.Equal(before, File.ReadAllBytes(path));
  }

  [Fact]
  public async Task Webp_verifier_spots_different_pixels()
  {
    RequireTools("dwebp");
    var verifier = new WebPPixelVerifier(_tools);
    var result = await verifier.VerifyAsync(ImageFormat.WebP, TestFiles.FixturePath("lossless.webp"), TestFiles.FixturePath("lossy.webp"), _files.Directory, TestContext.Current.CancellationToken);
    Assert.Equal(VerificationResult.Different, result);
  }

  [Fact]
  public async Task Lossless_avif_gets_smaller_with_identical_pixels()
  {
    RequireTools("avifenc", "avifdec");
    var path = _files.CopyFixture("lossless.avif");
    var before = new FileInfo(path).Length;

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Optimized, result.Status);
    Assert.True(new FileInfo(path).Length < before);
    var original = AvifInspector.Inspect(File.ReadAllBytes(TestFiles.FixturePath("lossless.avif")));
    var optimized = AvifInspector.Inspect(File.ReadAllBytes(path));
    Assert.True(optimized.SameAppearance(original));
    Assert.Equal(1, optimized.Rotation);
    Assert.Null(optimized.Exif);
    Assert.Null(optimized.Xmp);

    var verifier = new AvifPixelVerifier(_tools);
    Assert.Equal(VerificationResult.Identical,
        await verifier.VerifyAsync(ImageFormat.Avif, TestFiles.FixturePath("lossless.avif"), path, _files.Directory, TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Avif_keeps_metadata_when_asked()
  {
    RequireTools("avifenc", "avifdec");
    var path = _files.CopyFixture("lossless.avif");

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings { StripMetadata = false, MaximumCompression = true }, TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Optimized, result.Status);
    var original = AvifInspector.Inspect(File.ReadAllBytes(TestFiles.FixturePath("lossless.avif")));
    var optimized = AvifInspector.Inspect(File.ReadAllBytes(path));
    Assert.Equal(original.ExifTiff, optimized.ExifTiff);
    Assert.Equal(original.Xmp, optimized.Xmp);
  }

  [Fact]
  public async Task Lossy_avif_is_never_reencoded()
  {
    var path = _files.CopyFixture("lossy.avif");
    var before = File.ReadAllBytes(path);

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Skipped, result.Status);
    Assert.Contains("Lossy AVIF", result.Message);
    Assert.Equal(before, File.ReadAllBytes(path));
  }

  [Fact]
  public async Task Avif_verifier_spots_different_pixels()
  {
    RequireTools("avifenc", "avifdec");
    var path = _files.CopyFixture("lossless.avif");
    var bytes = File.ReadAllBytes(path);
    // Re-encode the same container settings with a lossy quality, so only the pixels differ.
    var planes = Path.Combine(_files.Directory, "planes.y4m");
    var lossy = Path.Combine(_files.Directory, "lossy444.avif");
    await _tools.RunAsync("avifdec", [path, planes], TestContext.Current.CancellationToken);
    await _tools.RunAsync("avifenc", ["-q", "60", "--cicp", "1/2/0", "--irot", "1", planes, lossy], TestContext.Current.CancellationToken);

    var verifier = new AvifPixelVerifier(_tools);
    var result = await verifier.VerifyAsync(ImageFormat.Avif, path, lossy, _files.Directory, TestContext.Current.CancellationToken);
    Assert.Equal(VerificationResult.Different, result);
    Assert.Equal(bytes, File.ReadAllBytes(path));
  }

  [Fact]
  public async Task Gif_is_optimized_or_left_alone()
  {
    RequireTools("gifsicle");
    var path = _files.CopyFixture("animation.gif");
    var before = new FileInfo(path).Length;

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Contains(result.Status, new[] { OptimizationStatus.Optimized, OptimizationStatus.AlreadyOptimized });
    Assert.True(new FileInfo(path).Length <= before);
    Assert.Equal(ImageFormat.Gif, ImageFormats.Detect(path));
  }

  [Fact]
  public async Task Svg_gets_smaller_with_svgo()
  {
    ToolRequirements.RequireFile(_tools, SvgOptimizer.ScriptName);
    var path = _files.CopyFixture("drawing.svg");
    var before = new FileInfo(path).Length;

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Optimized, result.Status);
    Assert.True(new FileInfo(path).Length < before);
    var svg = File.ReadAllText(path);
    Assert.StartsWith("<svg", svg);
    Assert.DoesNotContain("inkscape", svg);
    Assert.Contains("viewBox", svg);
  }

  [Fact]
  public async Task Svg_keeps_metadata_when_asked()
  {
    ToolRequirements.RequireFile(_tools, SvgOptimizer.ScriptName);
    var path = _files.CopyFixture("drawing.svg");

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings { StripMetadata = false, MaximumCompression = true }, TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Optimized, result.Status);
    Assert.Contains("<metadata>", File.ReadAllText(path));
  }

  [Fact]
  public async Task Invalid_svg_is_left_alone()
  {
    ToolRequirements.RequireFile(_tools, SvgOptimizer.ScriptName);
    var path = _files.Write("broken.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><g></svg>"u8.ToArray());
    var before = File.ReadAllBytes(path);

    var result = await Engine().OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Failed, result.Status);
    Assert.Equal(before, File.ReadAllBytes(path));
  }
}
