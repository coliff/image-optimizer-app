namespace ImageOptimizer.Core.Tests;

public class InspectorTests
{
  [Theory]
  [InlineData("unoptimized.png", ImageFormat.Png)]
  [InlineData("photo.jpg", ImageFormat.Jpeg)]
  [InlineData("lossless.webp", ImageFormat.WebP)]
  [InlineData("lossless.avif", ImageFormat.Avif)]
  [InlineData("lossy.avif", ImageFormat.Avif)]
  [InlineData("animation.gif", ImageFormat.Gif)]
  [InlineData("drawing.svg", ImageFormat.Svg)]
  public void Detects_format_from_signature(string fixture, ImageFormat expected)
  {
    Assert.Equal(expected, ImageFormats.Detect(TestFiles.FixturePath(fixture)));
  }

  [Fact]
  public void Detects_unknown_content_regardless_of_extension()
  {
    using var files = new TestFiles();
    var path = files.Write("fake.png", "not an image at all"u8.ToArray());
    Assert.Equal(ImageFormat.Unknown, ImageFormats.Detect(path));
    Assert.Equal(ImageFormat.Unknown, ImageFormats.Detect(ReadOnlySpan<byte>.Empty));
  }

  [Fact]
  public void Recognises_animated_png()
  {
    var png = File.ReadAllBytes(TestFiles.FixturePath("unoptimized.png"));
    Assert.False(ImageInspector.IsAnimatedPng(png));
    Assert.True(ImageInspector.IsAnimatedPng(TestFiles.MakeAnimatedPng(png)));
  }

  [Theory]
  [InlineData(6, false)]
  [InlineData(3, true)]
  [InlineData(1, false)]
  public void Reads_jpeg_exif_orientation(ushort orientation, bool littleEndian)
  {
    var jpeg = File.ReadAllBytes(TestFiles.FixturePath("photo.jpg"));
    Assert.Null(ImageInspector.GetJpegExifOrientation(jpeg));
    Assert.Equal(orientation, ImageInspector.GetJpegExifOrientation(TestFiles.WithExifOrientation(jpeg, orientation, littleEndian)));
  }

  [Fact]
  public void Truncated_jpeg_does_not_throw()
  {
    var jpeg = TestFiles.WithExifOrientation(File.ReadAllBytes(TestFiles.FixturePath("photo.jpg")), 6);
    for (var length = 0; length < 60; length++)
      ImageInspector.GetJpegExifOrientation(jpeg.AsSpan(0, length));
  }

  [Fact]
  public void Classifies_webp_files()
  {
    var lossless = ImageInspector.InspectWebP(File.ReadAllBytes(TestFiles.FixturePath("lossless.webp")));
    Assert.True(lossless.IsStillLossless);

    var lossy = ImageInspector.InspectWebP(File.ReadAllBytes(TestFiles.FixturePath("lossy.webp")));
    Assert.True(lossy.IsValid);
    Assert.True(lossy.IsLossy);
    Assert.False(lossy.IsStillLossless);

    Assert.False(ImageInspector.InspectWebP("RIFF"u8).IsValid);
  }

  [Fact]
  public void Classifies_avif_files()
  {
    var lossless = AvifInspector.Inspect(File.ReadAllBytes(TestFiles.FixturePath("lossless.avif")));
    Assert.True(lossless.IsValid);
    Assert.Null(lossless.UnsupportedReason);
    Assert.True(lossless.IsLikelyLossless);
    Assert.Equal((96u, 64u), (lossless.Width, lossless.Height));
    Assert.Equal(8, lossless.Depth);
    Assert.Equal(1, lossless.Rotation);
    Assert.NotNull(lossless.ExifTiff);
    Assert.StartsWith("MM", System.Text.Encoding.ASCII.GetString(lossless.ExifTiff));
    Assert.Contains("Image Optimizer", System.Text.Encoding.UTF8.GetString(lossless.Xmp!));
    Assert.True(lossless.SameAppearance(lossless));

    var lossy = AvifInspector.Inspect(File.ReadAllBytes(TestFiles.FixturePath("lossy.avif")));
    Assert.True(lossy.IsValid);
    Assert.True(lossy.ChromaSubsampled);
    Assert.False(lossy.IsLikelyLossless);
    Assert.False(lossy.SameAppearance(lossless));

    Assert.False(AvifInspector.Inspect(File.ReadAllBytes(TestFiles.FixturePath("lossless.webp"))).IsValid);
  }

  [Fact]
  public void Truncated_avif_does_not_throw()
  {
    var avif = File.ReadAllBytes(TestFiles.FixturePath("lossless.avif"));
    for (var length = 0; length < avif.Length; length += 7)
      AvifInspector.Inspect(avif.AsSpan(0, length));
  }

  [Theory]
  [InlineData(0, "0 bytes")]
  [InlineData(1, "1 byte")]
  [InlineData(1023, "1,023 bytes")]
  [InlineData(1536, "1.5 KB")]
  [InlineData(150 * 1024, "150 KB")]
  [InlineData(5L * 1024 * 1024, "5 MB")]
  public void Formats_sizes(long bytes, string expected)
  {
    var previous = System.Globalization.CultureInfo.CurrentCulture;
    System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
    try
    {
      Assert.Equal(expected, SizeFormatter.Format(bytes));
    }
    finally
    {
      System.Globalization.CultureInfo.CurrentCulture = previous;
    }
  }

  [Fact]
  public void Collects_images_from_files_and_folders()
  {
    using var files = new TestFiles();
    var nested = Path.Combine(files.Directory, "nested");
    Directory.CreateDirectory(nested);
    var a = files.CopyFixture("photo.jpg");
    var b = files.CopyFixture("unoptimized.png", Path.Combine("nested", "b.PNG"));
    files.Write("notes.txt", [1, 2, 3]);

    var collected = FileCollector.Collect([files.Directory, a, Path.Combine(files.Directory, "missing.png")]).ToList();

    Assert.Equal(2, collected.Count);
    Assert.Contains(Path.GetFullPath(a), collected);
    Assert.Contains(Path.GetFullPath(b), collected);
  }

  [Fact]
  public void Settings_round_trip_and_survive_corruption()
  {
    using var files = new TestFiles();
    var path = Path.Combine(files.Directory, "sub", "settings.json");
    var settings = new OptimizerSettings { OptimizeGif = false, MaximumCompression = true };
    settings.Save(path);
    Assert.Equal(settings, OptimizerSettings.Load(path));

    File.WriteAllText(path, "{ not json");
    Assert.Equal(new OptimizerSettings(), OptimizerSettings.Load(path));
  }
}
