using System.Buffers.Binary;
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

  private static readonly byte[] Bmp = [40, 0, 0, 0, .. Enumerable.Range(0, 60).Select(i => (byte)i)];

  /// <summary>An icon (type 1) or cursor (type 2) whose entries each get their own size and hotspot or bit count.</summary>
  private static IcoFile Icon(ushort type, params byte[][] images) =>
      new(type, images.Select((data, i) =>
      {
        var header = new byte[16];
        header[0] = header[1] = (byte)(16 << i);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), (ushort)(type == 2 ? 3 : 1));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)(type == 2 ? 5 : 32));
        return new IcoEntry(header, data);
      }).ToList());

  private async Task<VerificationResult> VerifyIcons(IcoFile original, IcoFile candidate)
  {
    var originalPath = Path.Combine(_directory, "original.ico");
    var candidatePath = Path.Combine(_directory, "candidate.ico");
    File.WriteAllBytes(originalPath, original.ToBytes());
    File.WriteAllBytes(candidatePath, candidate.ToBytes());
    return await new WpfPixelVerifier().VerifyAsync(ImageFormat.Ico, originalPath, candidatePath, _directory, TestContext.Current.CancellationToken);
  }

  private static byte[] Png(string name) => File.ReadAllBytes(Fixture(name));

  private static IcoFile WithEntry(IcoFile icon, int index, Func<IcoEntry, IcoEntry> change) =>
      icon with { Entries = icon.Entries.Select((e, i) => i == index ? change(e) : e).ToList() };

  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  public async Task Icon_with_recompressed_png_is_pixel_identical(ushort type)
  {
    var original = Icon(type, Bmp, Png("unoptimized.png"));
    var candidate = WithEntry(original, 1, e => e with { Data = Png("optimized.png") });

    Assert.Equal(VerificationResult.Identical, await VerifyIcons(original, candidate));
  }

  [Fact]
  public async Task Icon_with_changed_png_pixels_is_rejected()
  {
    var changed = Png("unoptimized.png");
    // Flip one byte of raw pixel data inside the stored (uncompressed) deflate stream.
    changed[changed.Length - 200] ^= 0xFF;
    var original = Icon(1, Bmp, Png("unoptimized.png"));

    Assert.NotEqual(VerificationResult.Identical, await VerifyIcons(original, WithEntry(original, 1, e => e with { Data = changed })));
  }

  [Fact]
  public async Task Icon_with_changed_bmp_bytes_is_rejected()
  {
    var original = Icon(1, Bmp, Png("unoptimized.png"));
    var changed = (byte[])Bmp.Clone();
    changed[^1] ^= 0xFF;

    Assert.Equal(VerificationResult.Different, await VerifyIcons(original, WithEntry(original, 0, e => e with { Data = changed })));
  }

  [Theory]
  [InlineData(0)] // width
  [InlineData(4)] // planes, or a cursor's hotspot x
  [InlineData(6)] // bit count, or a cursor's hotspot y
  public async Task Cursor_with_changed_directory_entry_is_rejected(int field)
  {
    var original = Icon(2, Bmp, Png("unoptimized.png"));
    var candidate = WithEntry(original, 1, e =>
    {
      var header = (byte[])e.Header.Clone();
      header[field] ^= 0x01;
      return e with { Header = header };
    });

    Assert.Equal(VerificationResult.Different, await VerifyIcons(original, candidate));
  }

  [Fact]
  public async Task Icon_with_reordered_type_or_missing_images_is_rejected()
  {
    var original = Icon(1, Bmp, Png("unoptimized.png"));

    Assert.Equal(VerificationResult.Different, await VerifyIcons(original, original with { Entries = original.Entries.Reverse().ToList() }));
    Assert.Equal(VerificationResult.Different, await VerifyIcons(original, original with { Type = 2 }));
    Assert.Equal(VerificationResult.Different, await VerifyIcons(original, original with { Entries = original.Entries.Take(1).ToList() }));
  }
}
