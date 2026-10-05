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
  [InlineData("lossless.jxl", ImageFormat.JpegXl)]
  [InlineData("lossy.jxl", ImageFormat.JpegXl)]
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
  public void Reads_png_exif_orientation()
  {
    var png = File.ReadAllBytes(TestFiles.FixturePath("unoptimized.png"));
    Assert.Null(ImageInspector.GetPngExifOrientation(png));
    Assert.Equal(6, ImageInspector.GetPngExifOrientation(TestFiles.WithPngExifOrientation(png, 6)));
  }

  [Fact]
  public void Finds_data_after_the_end_of_a_jpeg()
  {
    var jpeg = File.ReadAllBytes(TestFiles.FixturePath("photo.jpg"));
    Assert.False(ImageInspector.HasJpegTrailingData(jpeg));
    Assert.False(ImageInspector.HasJpegTrailingData([.. jpeg, .. new byte[100]]));

    // A motion photo's video, or the second image of an MPF file, starts right after the first image.
    Assert.True(ImageInspector.HasJpegTrailingData([.. jpeg, .. "\0\0\0\u0018ftyp"u8, .. "mp42"u8]));
    Assert.True(ImageInspector.HasJpegTrailingData([.. jpeg, .. jpeg]));
  }

  [Fact]
  public void Truncated_jpeg_has_no_trailing_data()
  {
    var jpeg = File.ReadAllBytes(TestFiles.FixturePath("photo.jpg"));
    for (var length = 0; length < jpeg.Length; length++)
      Assert.False(ImageInspector.HasJpegTrailingData(jpeg.AsSpan(0, length)));
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

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void Reads_webp_exif_orientation(bool withExifHeader)
  {
    byte[] exif = [.. withExifHeader ? "Exif\0\0"u8.ToArray() : [], .. TestFiles.ExifTiff(6, littleEndian: true)];
    var chunk = new byte[8 + exif.Length + (exif.Length & 1)];
    "EXIF"u8.CopyTo(chunk);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)exif.Length);
    exif.CopyTo(chunk, 8);
    var webp = File.ReadAllBytes(TestFiles.FixturePath("lossy.webp"));

    var info = ImageInspector.InspectWebP([.. webp, .. chunk]);

    Assert.True(info.HasExif);
    Assert.Equal(6, info.ExifOrientation);
    Assert.True(info.ExifRotates);
    Assert.Null(ImageInspector.InspectWebP(webp).ExifOrientation);
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

  [Fact]
  public void Classifies_jpeg_xl_files()
  {
    var lossless = JxlInspector.Inspect(File.ReadAllBytes(TestFiles.FixturePath("lossless.jxl")));
    Assert.True(lossless.IsValid);
    Assert.True(lossless.IsContainer);
    Assert.Null(lossless.UnsupportedReason);
    Assert.False(lossless.XybEncoded);
    Assert.False(lossless.IsAnimated);
    Assert.False(lossless.HasJpegReconstruction);
    Assert.Equal((96u, 64u), (lossless.Width, lossless.Height));
    Assert.Equal(1, lossless.Orientation);
    Assert.Equal(8, lossless.BitsPerSample);
    Assert.Empty(lossless.ExtraChannels);
    Assert.StartsWith("MM", System.Text.Encoding.ASCII.GetString(lossless.ExifTiff!));
    Assert.Contains("xmpmeta", System.Text.Encoding.UTF8.GetString(lossless.Xmp!));
    Assert.True(lossless.SameAppearance(lossless));

    var lossy = JxlInspector.Inspect(File.ReadAllBytes(TestFiles.FixturePath("lossy.jxl")));
    Assert.True(lossy.IsValid);
    Assert.False(lossy.IsContainer);
    Assert.True(lossy.XybEncoded);
    Assert.Equal((96u, 64u), (lossy.Width, lossy.Height));
    Assert.False(lossy.SameAppearance(lossless));

    var jpeg = JxlInspector.Inspect(File.ReadAllBytes(TestFiles.FixturePath("recompressed-jpeg.jxl")));
    Assert.True(jpeg.HasJpegReconstruction);
    Assert.False(jpeg.XybEncoded);
    Assert.Equal((160u, 120u), (jpeg.Width, jpeg.Height));

    Assert.True(JxlInspector.Inspect(File.ReadAllBytes(TestFiles.FixturePath("animated.jxl"))).IsAnimated);
    Assert.False(JxlInspector.Inspect(File.ReadAllBytes(TestFiles.FixturePath("lossless.webp"))).IsValid);
  }

  [Fact]
  public void Truncated_jpeg_xl_does_not_throw()
  {
    foreach (var fixture in new[] { "lossless.jxl", "lossy.jxl", "recompressed-jpeg.jxl" })
    {
      var jxl = File.ReadAllBytes(TestFiles.FixturePath(fixture));
      for (var length = 0; length < jxl.Length; length += 7)
        JxlInspector.Inspect(jxl.AsSpan(0, length));
    }
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
  public void Skips_linked_files_and_folders_but_not_their_targets()
  {
    using var files = new TestFiles();
    var photos = Path.Combine(files.Directory, "photos");
    Directory.CreateDirectory(photos);
    var a = files.CopyFixture("photo.jpg", Path.Combine("photos", "a.jpg"));
    try
    {
      File.CreateSymbolicLink(Path.Combine(files.Directory, "link.jpg"), a);
      Directory.CreateSymbolicLink(Path.Combine(files.Directory, "linked folder"), photos);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      Assert.Skip("This account can't create symbolic links.");
    }

    var collected = FileCollector.Collect([files.Directory]).ToList();

    Assert.Equal([Path.GetFullPath(a)], collected);
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
