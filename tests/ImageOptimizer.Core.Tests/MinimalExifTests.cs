namespace ImageOptimizer.Core.Tests;

public class MinimalExifTests
{
  [Theory]
  [InlineData(1)]
  [InlineData(6)]
  [InlineData(8)]
  public void Jpeg_gets_an_exif_block_with_only_the_orientation(int orientation)
  {
    var jpeg = File.ReadAllBytes(TestFiles.FixturePath("photo.jpg"));
    Assert.Null(ImageInspector.GetJpegExifOrientation(jpeg));

    var withExif = MinimalExif.InsertIntoJpeg(jpeg, orientation);

    Assert.Equal(orientation, ImageInspector.GetJpegExifOrientation(withExif));
    Assert.Equal(jpeg.Length + 4 + 6 + MinimalExif.TiffLength, withExif.Length);
    Assert.False(ImageInspector.HasJpegTrailingData(withExif));
  }

  [Fact]
  public void Jpeg_exif_block_goes_after_jfif()
  {
    var jpeg = File.ReadAllBytes(TestFiles.FixturePath("photo.jpg"));
    byte[] jfif = [0xFF, 0xE0, 0x00, 0x10, .. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0];
    byte[] withJfif = [.. jpeg[..2], .. jfif, .. jpeg[2..]];

    var withExif = MinimalExif.InsertIntoJpeg(withJfif, 6);

    Assert.Equal(jfif, withExif[2..(2 + jfif.Length)]);
    Assert.Equal([0xFF, 0xE1], withExif[(2 + jfif.Length)..(4 + jfif.Length)]);
    Assert.Equal(6, ImageInspector.GetJpegExifOrientation(withExif));
  }

  [Fact]
  public void Png_gets_an_exif_chunk_with_only_the_orientation_before_the_image_data()
  {
    var png = File.ReadAllBytes(TestFiles.FixturePath("unoptimized.png"));

    var withExif = MinimalExif.InsertIntoPng(png, 6);

    Assert.Equal(6, ImageInspector.GetPngExifOrientation(withExif));
    var exif = withExif.AsSpan().IndexOf("eXIf"u8);
    var idat = withExif.AsSpan().IndexOf("IDAT"u8);
    Assert.InRange(exif, 0, idat);
    Assert.Equal(png.Length + 12 + MinimalExif.TiffLength, withExif.Length);
  }

  [Fact]
  public void Webp_with_only_the_orientation_has_nothing_more_to_remove()
  {
    var info = new WebPInfo { IsValid = true, HasExif = true, ExifOrientation = 6, ExifSize = MinimalExif.TiffLength };
    Assert.True(info.HasOnlyMinimalExif);
    Assert.False((info with { ExifSize = (uint)TestFiles.CameraExifTiff(6).Length }).HasOnlyMinimalExif);
  }

  [Fact]
  public void Orientation_must_be_valid()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => MinimalExif.Tiff(0));
    Assert.Throws<ArgumentOutOfRangeException>(() => MinimalExif.Tiff(9));
  }
}
