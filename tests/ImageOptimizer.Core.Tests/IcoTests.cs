using System.Buffers.Binary;

namespace ImageOptimizer.Core.Tests;

public class IcoTests : IDisposable
{
  private readonly TestFiles _files = new();
  private readonly ToolRunner _tools = new();

  public void Dispose() => _files.Dispose();

  /// <summary>A small stand-in for a BMP image: a BITMAPINFOHEADER followed by some pixel bytes.</summary>
  private static readonly byte[] Bmp = [40, 0, 0, 0, .. Enumerable.Range(0, 60).Select(i => (byte)i)];

  private static byte[] Png => File.ReadAllBytes(TestFiles.FixturePath("unoptimized.png"));

  /// <summary>Builds an icon (type 1) or cursor (type 2) holding the given images, with distinctive directory entries.</summary>
  private static byte[] Icon(ushort type, params byte[][] images)
  {
    var entries = images.Select((data, i) =>
    {
      var header = new byte[16];
      header[0] = (byte)(16 << i);
      header[1] = (byte)(16 << i);
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), (ushort)(type == 2 ? 3 : 1));
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)(type == 2 ? 5 : 32));
      return new IcoEntry(header, data);
    }).ToList();
    return new IcoFile(type, entries).ToBytes();
  }

  [Fact]
  public void Round_trips_through_parse_and_write()
  {
    var bytes = Icon(1, Bmp, Png);

    var icon = IcoFile.Parse(bytes);

    Assert.NotNull(icon);
    Assert.Equal(1, icon.Type);
    Assert.Equal([false, true], icon.Entries.Select(e => e.IsPng));
    Assert.Equal(Bmp, icon.Entries[0].Data);
    Assert.Equal(Png, icon.Entries[1].Data);
    Assert.Equal(bytes, icon.ToBytes());
  }

  [Fact]
  public void Rejects_images_outside_the_file()
  {
    var bytes = Icon(1, Bmp);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(6 + 8), (uint)bytes.Length);

    Assert.Null(IcoFile.Parse(bytes));
    Assert.Null(IcoFile.Parse(bytes.AsSpan(0, 10)));
    Assert.Null(IcoFile.Parse([0, 0, 1, 0, 0, 0]));
  }

  [Theory]
  [InlineData("icon.ico", ImageFormat.Ico)]
  [InlineData("cursor.CUR", ImageFormat.Ico)]
  [InlineData("data.bin", ImageFormat.Unknown)]
  public void Detects_icons_only_with_an_icon_extension(string name, ImageFormat expected)
  {
    var path = _files.Write(name, Icon(1, Bmp));
    Assert.Equal(expected, ImageFormats.Detect(path));
  }

  [Fact]
  public void Detects_a_png_renamed_to_ico_as_png()
  {
    var path = _files.Write("icon.ico", Png);
    Assert.Equal(ImageFormat.Png, ImageFormats.Detect(path));
  }

  [Fact]
  public void Same_pixel_format_compares_the_ihdr_chunk()
  {
    var png = Png;
    var palette = (byte[])png.Clone();
    palette[25] = 3;

    Assert.True(IcoOptimizer.SamePixelFormat(png, png));
    Assert.False(IcoOptimizer.SamePixelFormat(png, palette));
    Assert.False(IcoOptimizer.SamePixelFormat(png, Bmp));
  }

  [Fact]
  public async Task Icon_without_png_images_is_skipped()
  {
    var path = _files.Write("icon.ico", Icon(1, Bmp, Bmp));
    var before = File.ReadAllBytes(path);

    var result = await ImageOptimizerEngine.CreateDefault(_tools).OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Skipped, result.Status);
    Assert.Equal(before, File.ReadAllBytes(path));
  }

  [Fact]
  public async Task Icon_is_left_alone_when_turned_off()
  {
    var path = _files.Write("icon.ico", Icon(1, Bmp, Png));

    var result = await ImageOptimizerEngine.CreateDefault(_tools).OptimizeAsync(path, new OptimizerSettings { OptimizeIco = false }, TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Skipped, result.Status);
  }

  [Theory]
  [InlineData("icon.ico", 1)]
  [InlineData("cursor.cur", 2)]
  public async Task Png_images_get_smaller_and_everything_else_is_kept(string name, ushort type)
  {
    ToolRequirements.Require(_tools, "oxipng");
    var original = Icon(type, Bmp, Png, Bmp);
    var path = _files.Write(name, original);

    var result = await ImageOptimizerEngine.CreateDefault(_tools).OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.Optimized, result.Status);
    Assert.Equal(ImageFormat.Ico, result.Format);
    var before = IcoFile.Parse(original)!;
    var after = IcoFile.Parse(File.ReadAllBytes(path));
    Assert.NotNull(after);
    Assert.Equal(type, after.Type);
    Assert.Equal(3, after.Entries.Count);
    for (var i = 0; i < 3; i++)
      Assert.Equal(before.Entries[i].Header[..8], after.Entries[i].Header[..8]);
    Assert.Equal(Bmp, after.Entries[0].Data);
    Assert.Equal(Bmp, after.Entries[2].Data);
    Assert.True(after.Entries[1].IsPng);
    Assert.True(after.Entries[1].Data.Length < Png.Length);
    Assert.True(IcoOptimizer.SamePixelFormat(Png, after.Entries[1].Data));
  }

  [Fact]
  public async Task Already_optimized_icon_is_left_alone()
  {
    ToolRequirements.Require(_tools, "oxipng");
    var path = _files.Write("icon.ico", Icon(1, Bmp, Png));
    var engine = ImageOptimizerEngine.CreateDefault(_tools);
    await engine.OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);
    var optimized = File.ReadAllBytes(path);

    var result = await engine.OptimizeAsync(path, new OptimizerSettings(), TestContext.Current.CancellationToken);

    Assert.Equal(OptimizationStatus.AlreadyOptimized, result.Status);
    Assert.Equal(optimized, File.ReadAllBytes(path));
  }
}
