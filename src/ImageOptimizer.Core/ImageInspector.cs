using System.Buffers.Binary;

namespace ImageOptimizer.Core;

/// <summary>Lightweight container parsing used to pick a safe optimization strategy for a file.</summary>
public static class ImageInspector
{
  /// <summary>True when the PNG carries an animation control chunk (APNG).</summary>
  public static bool IsAnimatedPng(ReadOnlySpan<byte> data)
  {
    if (data.Length < 8 || !data[..8].SequenceEqual(ImageFormats.PngSignature))
      return false;

    var offset = 8;
    while (offset + 8 <= data.Length)
    {
      var length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
      var type = data.Slice(offset + 4, 4);
      if (type.SequenceEqual("acTL"u8))
        return true;
      // acTL must precede the first IDAT, so there is no need to read further.
      if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8))
        return false;
      var next = (long)offset + 12 + length;
      if (next > data.Length)
        return false;
      offset = (int)next;
    }
    return false;
  }

  /// <summary>
  /// Returns the EXIF orientation stored in a PNG's eXIf chunk (1–8), or null when there is none.
  /// </summary>
  public static int? GetPngExifOrientation(ReadOnlySpan<byte> data)
  {
    if (data.Length < 8 || !data[..8].SequenceEqual(ImageFormats.PngSignature))
      return null;

    var offset = 8;
    while (offset + 8 <= data.Length)
    {
      var length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
      var type = data.Slice(offset + 4, 4);
      if (type.SequenceEqual("IEND"u8) || (long)offset + 8 + length > data.Length)
        return null;
      if (type.SequenceEqual("eXIf"u8))
        return ReadTiffOrientation(data.Slice(offset + 8, (int)length));
      offset = (int)Math.Min((long)offset + 12 + length, data.Length);
    }
    return null;
  }

  /// <summary>
  /// True when something other than padding follows a JPEG's end-of-image marker, such as a motion photo's
  /// video or the extra images (HDR gain maps, stereo pairs) that an MPF segment points to. Lossless JPEG
  /// tools stop at the end-of-image marker, so that data would be lost.
  /// </summary>
  public static bool HasJpegTrailingData(ReadOnlySpan<byte> data)
  {
    var end = FindJpegEnd(data);
    return end >= 0 && data[end..].IndexOfAnyExcept((byte)0x00, (byte)0xFF) >= 0;
  }

  /// <summary>The offset just past the JPEG's end-of-image marker, or -1 if the file ends before it.</summary>
  private static int FindJpegEnd(ReadOnlySpan<byte> data)
  {
    if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
      return -1;

    var offset = 2;
    while (offset + 2 <= data.Length)
    {
      if (data[offset] != 0xFF)
        return -1;
      var marker = data[offset + 1];
      if (marker == 0xFF)
      {
        offset++;
        continue;
      }
      if (marker == 0xD9)
        return offset + 2;
      if (marker is >= 0xD0 and <= 0xD7 or 0x01)
      {
        offset += 2;
        continue;
      }

      if (offset + 4 > data.Length)
        return -1;
      var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
      if (segmentLength < 2)
        return -1;
      offset += 2 + segmentLength;
      if (marker != 0xDA)
        continue;

      // Entropy-coded data follows a start of scan, up to the next marker that isn't a stuffed zero byte,
      // a restart marker or fill.
      while (true)
      {
        if (offset >= data.Length)
          return -1;
        var next = data[offset..].IndexOf((byte)0xFF);
        if (next < 0 || offset + next + 1 >= data.Length)
          return -1;
        offset += next;
        if (data[offset + 1] is not (0x00 or 0xFF or (>= 0xD0 and <= 0xD7)))
          break;
        offset++;
      }
    }
    return -1;
  }

  /// <summary>
  /// Returns the EXIF orientation of a JPEG (1–8), or null when the file has no orientation tag.
  /// </summary>
  public static int? GetJpegExifOrientation(ReadOnlySpan<byte> data)
  {
    if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
      return null;

    var offset = 2;
    while (offset + 4 <= data.Length)
    {
      if (data[offset] != 0xFF)
        return null;
      var marker = data[offset + 1];
      if (marker == 0xFF)
      {
        offset++;
        continue;
      }
      // Start of scan or end of image: no more metadata segments follow.
      if (marker is 0xDA or 0xD9)
        return null;
      if (marker is >= 0xD0 and <= 0xD7 or 0x01)
      {
        offset += 2;
        continue;
      }

      var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
      if (segmentLength < 2 || offset + 2 + segmentLength > data.Length)
        return null;
      var segment = data.Slice(offset + 4, segmentLength - 2);
      if (marker == 0xE1 && segment.Length > 6 && segment[..6].SequenceEqual("Exif\0\0"u8))
        return ReadTiffOrientation(segment[6..]);
      offset += 2 + segmentLength;
    }
    return null;
  }

  private static int? ReadTiffOrientation(ReadOnlySpan<byte> tiff)
  {
    if (tiff.Length < 8)
      return null;
    bool littleEndian;
    if (tiff[0] == 'I' && tiff[1] == 'I')
      littleEndian = true;
    else if (tiff[0] == 'M' && tiff[1] == 'M')
      littleEndian = false;
    else
      return null;

    var ifd = ReadUInt32(tiff[4..], littleEndian);
    if (ifd > int.MaxValue || ifd + 2 > tiff.Length)
      return null;
    var count = ReadUInt16(tiff[(int)ifd..], littleEndian);
    for (var i = 0; i < count; i++)
    {
      var entry = (int)ifd + 2 + i * 12;
      if (entry + 12 > tiff.Length)
        return null;
      if (ReadUInt16(tiff[entry..], littleEndian) == 0x0112)
      {
        // SHORT value stored inline in the first two bytes of the value field.
        var value = ReadUInt16(tiff[(entry + 8)..], littleEndian);
        return value is >= 1 and <= 8 ? value : null;
      }
    }
    return null;
  }

  private static ushort ReadUInt16(ReadOnlySpan<byte> data, bool littleEndian) =>
      littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(data) : BinaryPrimitives.ReadUInt16BigEndian(data);

  private static uint ReadUInt32(ReadOnlySpan<byte> data, bool littleEndian) =>
      littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(data) : BinaryPrimitives.ReadUInt32BigEndian(data);

  public static WebPInfo InspectWebP(ReadOnlySpan<byte> data)
  {
    if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data[8..12].SequenceEqual("WEBP"u8))
      return WebPInfo.Invalid;

    var info = new WebPInfo { IsValid = true };
    var offset = 12;
    while (offset + 8 <= data.Length)
    {
      var type = data.Slice(offset, 4);
      var size = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
      if (type.SequenceEqual("VP8L"u8))
        info = info with { IsLossless = true };
      else if (type.SequenceEqual("VP8 "u8))
        info = info with { IsLossy = true };
      else if (type.SequenceEqual("ANIM"u8) || type.SequenceEqual("ANMF"u8))
        info = info with { IsAnimated = true };
      else if (type.SequenceEqual("ICCP"u8))
        info = info with { HasIccProfile = true };
      else if (type.SequenceEqual("EXIF"u8))
        info = info with { HasExif = true };
      else if (type.SequenceEqual("XMP "u8))
        info = info with { HasXmp = true };

      // Chunks are padded to an even size.
      var next = (long)offset + 8 + size + (size & 1);
      if (next > data.Length)
        break;
      offset = (int)next;
    }
    return info;
  }
}

public readonly record struct WebPInfo
{
  public static readonly WebPInfo Invalid = default;

  public bool IsValid { get; init; }
  public bool IsLossless { get; init; }
  public bool IsLossy { get; init; }
  public bool IsAnimated { get; init; }
  public bool HasIccProfile { get; init; }
  public bool HasExif { get; init; }
  public bool HasXmp { get; init; }

  /// <summary>A still image whose pixels are stored losslessly, so it can be re-encoded without loss.</summary>
  public bool IsStillLossless => IsValid && IsLossless && !IsLossy && !IsAnimated;
}
