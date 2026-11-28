using System.Buffers.Binary;

namespace ImageOptimizer.Core;

/// <summary>
/// EXIF data that holds nothing but the orientation tag. When metadata is removed from a photo that's turned by its
/// EXIF data, the original EXIF block is swapped for this one, so the photo still shows the right way up but its
/// GPS location, camera details, dates and thumbnail are gone. Only the metadata changes, never the image data.
/// </summary>
public static class MinimalExif
{
  /// <summary>A TIFF header, one IFD with a single Orientation entry, and no next IFD.</summary>
  public const int TiffLength = 8 + 2 + 12 + 4;

  /// <summary>The TIFF data (as stored in a PNG eXIf or WebP EXIF chunk) holding only the given orientation.</summary>
  public static byte[] Tiff(int orientation)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(orientation, 1);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(orientation, 8);

    var tiff = new byte[TiffLength];
    tiff[0] = tiff[1] = (byte)'M';
    BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(2), 42);
    BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(4), 8);        // first IFD
    BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(8), 1);        // one entry
    BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(10), 0x0112);  // Orientation
    BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(12), 3);       // SHORT
    BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(14), 1);       // count
    BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(18), (ushort)orientation);
    // Bytes 20–25: the rest of the value field and the next-IFD offset, all zero.
    return tiff;
  }

  /// <summary>
  /// Puts an APP1 segment holding only the orientation into a JPEG that has no EXIF data (jpegtran with
  /// <c>-copy icc</c> leaves none). It goes straight after SOI, or after a JFIF APP0 segment, which must come first.
  /// </summary>
  public static byte[] InsertIntoJpeg(ReadOnlySpan<byte> jpeg, int orientation)
  {
    if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
      throw new InvalidDataException("Not a JPEG file.");

    var at = 2;
    if (jpeg.Length >= 6 && jpeg[2] == 0xFF && jpeg[3] == 0xE0)
    {
      var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[4..]);
      if (length >= 2 && 4 + length <= jpeg.Length)
        at = 4 + length;
    }

    var tiff = Tiff(orientation);
    var segment = new byte[4 + 6 + tiff.Length];
    segment[0] = 0xFF;
    segment[1] = 0xE1;
    BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(segment.Length - 2));
    "Exif\0\0"u8.CopyTo(segment.AsSpan(4));
    tiff.CopyTo(segment, 10);
    return [.. jpeg[..at], .. segment, .. jpeg[at..]];
  }

  /// <summary>
  /// Puts an eXIf chunk holding only the orientation into a PNG that has none, just before the first IDAT chunk
  /// (readers only look for it there).
  /// </summary>
  public static byte[] InsertIntoPng(ReadOnlySpan<byte> png, int orientation)
  {
    if (png.Length < 8 || !png[..8].SequenceEqual(ImageFormats.PngSignature))
      throw new InvalidDataException("Not a PNG file.");

    var offset = 8;
    while (true)
    {
      if (offset + 8 > png.Length)
        throw new InvalidDataException("The PNG file has no image data.");
      var length = BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
      var type = png.Slice(offset + 4, 4);
      if (type.SequenceEqual("IDAT"u8))
        break;
      if (type.SequenceEqual("IEND"u8) || (long)offset + 12 + length > png.Length)
        throw new InvalidDataException("The PNG file has no image data.");
      offset += 12 + (int)length;
    }

    var tiff = Tiff(orientation);
    var chunk = new byte[12 + tiff.Length];
    BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)tiff.Length);
    "eXIf"u8.CopyTo(chunk.AsSpan(4));
    tiff.CopyTo(chunk, 8);
    BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + tiff.Length), Crc32(chunk.AsSpan(4, 4 + tiff.Length)));
    return [.. png[..offset], .. chunk, .. png[offset..]];
  }

  private static uint Crc32(ReadOnlySpan<byte> bytes)
  {
    var crc = 0xFFFFFFFFu;
    foreach (var b in bytes)
    {
      crc ^= b;
      for (var k = 0; k < 8; k++)
        crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
    }
    return ~crc;
  }
}
