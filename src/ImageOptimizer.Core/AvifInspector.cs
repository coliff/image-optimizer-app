using System.Buffers.Binary;
using System.Text;

namespace ImageOptimizer.Core;

/// <summary>
/// Reads the parts of an AVIF (HEIF/ISOBMFF) container that decide how the image looks, so a re-encode can
/// be checked against the original and anything the encoder can't reproduce is left alone.
/// </summary>
public static class AvifInspector
{
  // Item properties whose meaning we understand and can reproduce with avifenc. Anything else on the primary
  // item (HDR metadata, layered/progressive coding, ...) makes the file unsupported.
  private static readonly HashSet<string> KnownProperties = ["ispe", "av1C", "colr", "pixi", "irot", "imir", "clap", "pasp", "clli"];

  private static readonly string[] AlphaAuxTypes = ["urn:mpeg:mpegB:cicp:systems:auxiliary:alpha", "urn:mpeg:hevc:2015:auxid:1"];

  /// <summary>True when the file starts with an ISOBMFF ftyp box naming an AVIF brand.</summary>
  public static bool HasAvifBrand(ReadOnlySpan<byte> data)
  {
    if (data.Length < 12 || !data[4..8].SequenceEqual("ftyp"u8))
      return false;
    var size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data), (uint)data.Length);
    if (IsAvifBrand(data[8..12]))
      return true;
    // Compatible brands follow the major brand and minor version.
    for (var offset = 16; offset + 4 <= size; offset += 4)
    {
      if (IsAvifBrand(data.Slice(offset, 4)))
        return true;
    }
    return false;
  }

  private static bool IsAvifBrand(ReadOnlySpan<byte> brand) => brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8);

  public static AvifInfo Inspect(ReadOnlySpan<byte> data)
  {
    try
    {
      return Parse(data);
    }
    catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException or OverflowException)
    {
      return AvifInfo.Invalid;
    }
  }

  private static AvifInfo Parse(ReadOnlySpan<byte> data)
  {
    if (!HasAvifBrand(data))
      return AvifInfo.Invalid;

    var animated = false;
    var metaStart = -1;
    var metaEnd = -1;
    foreach (var box in Boxes(data, 0, data.Length))
    {
      if (box.Type == "ftyp")
      {
        for (var offset = box.Start; offset + 4 <= box.End; offset += 4)
        {
          if (offset != box.Start + 4 && data.Slice(offset, 4).SequenceEqual("avis"u8))
            animated = true;
        }
      }
      else if (box.Type == "moov")
      {
        animated = true;
      }
      else if (box.Type == "meta" && metaStart < 0)
      {
        // meta is a FullBox: skip version and flags.
        metaStart = box.Start + 4;
        metaEnd = box.End;
      }
    }
    if (metaStart < 0)
      return AvifInfo.Invalid;

    uint primary = 0;
    var itemTypes = new Dictionary<uint, string>();
    var mimeTypes = new Dictionary<uint, string>();
    var locations = new Dictionary<uint, ItemLocation>();
    var references = new List<(string Type, uint From, uint To)>();
    var properties = new List<(string Type, int Start, int End)>();
    var associations = new Dictionary<uint, List<int>>();
    (int Start, int End) idat = (0, 0);

    foreach (var box in Boxes(data, metaStart, metaEnd))
    {
      var body = data[box.Start..box.End];
      switch (box.Type)
      {
        case "pitm":
          primary = body[0] == 0 ? BinaryPrimitives.ReadUInt16BigEndian(body[4..]) : BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
          break;
        case "iinf":
          {
            var first = box.Start + (body[0] == 0 ? 6 : 8);
            foreach (var infe in Boxes(data, first, box.End))
            {
              if (infe.Type != "infe")
                continue;
              var entry = data[infe.Start..infe.End];
              var version = entry[0];
              if (version < 2)
                continue;
              var at = 4;
              uint id = version == 2 ? BinaryPrimitives.ReadUInt16BigEndian(entry[at..]) : BinaryPrimitives.ReadUInt32BigEndian(entry[at..]);
              at += version == 2 ? 2 : 4;
              at += 2; // item_protection_index
              var type = Encoding.ASCII.GetString(entry.Slice(at, 4));
              itemTypes[id] = type;
              if (type == "mime")
              {
                at += 4;
                ReadCString(entry, ref at); // item_name
                mimeTypes[id] = ReadCString(entry, ref at);
              }
            }
            break;
          }
        case "iloc":
          ParseIloc(body, locations);
          break;
        case "iref":
          {
            var version = body[0];
            foreach (var reference in Boxes(data, box.Start + 4, box.End))
            {
              var entry = data[reference.Start..reference.End];
              var at = 0;
              var from = ReadId(entry, ref at, version);
              var count = BinaryPrimitives.ReadUInt16BigEndian(entry[at..]);
              at += 2;
              for (var i = 0; i < count; i++)
                references.Add((reference.Type, from, ReadId(entry, ref at, version)));
            }
            break;
          }
        case "iprp":
          foreach (var child in Boxes(data, box.Start, box.End))
          {
            if (child.Type == "ipco")
            {
              foreach (var property in Boxes(data, child.Start, child.End))
                properties.Add((property.Type, property.Start, property.End));
            }
            else if (child.Type == "ipma")
            {
              ParseIpma(data[child.Start..child.End], associations);
            }
          }
          break;
        case "idat":
          idat = (box.Start, box.End);
          break;
      }
    }

    if (primary == 0 || !itemTypes.TryGetValue(primary, out var primaryType))
      return AvifInfo.Invalid;

    var info = new AvifInfo { IsValid = true, IsAnimated = animated };
    if (primaryType != "av01")
      return info with { UnsupportedReason = primaryType == "grid" ? Strings.AvifGrid : Strings.AvifUnsupportedType };

    // Find the alpha plane, and fail on any other auxiliary image (depth maps, gain maps, ...).
    uint alpha = 0;
    foreach (var (type, from, to) in references)
    {
      if (type == "auxl" && to == primary)
      {
        string? auxType = null;
        foreach (var property in PropertiesOf(from, associations, properties))
        {
          if (property.Type == "auxC")
          {
            auxType = ReadAuxType(data[property.Start..property.End]);
            break;
          }
        }
        if (auxType is null || !AlphaAuxTypes.Contains(auxType) || alpha != 0)
          return info with { UnsupportedReason = Strings.AvifAuxiliaryImages };
        alpha = from;
      }
    }
    var premultiplied = alpha != 0 && references.Any(r => r.Type == "prem" && r.From == primary && r.To == alpha);

    byte[]? exif = null;
    byte[]? xmp = null;
    foreach (var (id, type) in itemTypes)
    {
      if (id == primary || id == alpha)
        continue;
      var describesPrimary = references.Any(r => r.Type == "cdsc" && r.From == id && r.To == primary);
      if (type == "Exif" && describesPrimary && exif is null)
        exif = ReadItem(data, locations, idat, id);
      else if (type == "mime" && describesPrimary && xmp is null && mimeTypes.GetValueOrDefault(id) == "application/rdf+xml")
        xmp = ReadItem(data, locations, idat, id);
      else
        return info with { UnsupportedReason = Strings.AvifEmbeddedItems };
    }

    info = info with { HasAlpha = alpha != 0, AlphaPremultiplied = premultiplied, Exif = exif, Xmp = xmp };
    foreach (var (type, start, end) in PropertiesOf(primary, associations, properties))
    {
      var body = data[start..end];
      switch (type)
      {
        case "ispe":
          info = info with { Width = BinaryPrimitives.ReadUInt32BigEndian(body[4..]), Height = BinaryPrimitives.ReadUInt32BigEndian(body[8..]) };
          break;
        case "av1C":
          {
            var flags = body[2];
            var highBitDepth = (flags & 0x40) != 0;
            var twelveBit = (flags & 0x20) != 0;
            info = info with
            {
              HasAv1Config = true,
              Depth = twelveBit ? 12 : highBitDepth ? 10 : 8,
              Monochrome = (flags & 0x10) != 0,
              ChromaSubsampled = (flags & 0x0C) != 0,
            };
            break;
          }
        case "colr":
          {
            var colorType = Encoding.ASCII.GetString(body[..4]);
            if (colorType == "nclx")
            {
              info = info with
              {
                Cicp = (BinaryPrimitives.ReadUInt16BigEndian(body[4..]), BinaryPrimitives.ReadUInt16BigEndian(body[6..]), BinaryPrimitives.ReadUInt16BigEndian(body[8..])),
                FullRange = (body[10] & 0x80) != 0,
              };
            }
            else if (colorType is "prof" or "rICC")
            {
              info = info with { IccProfile = body[4..].ToArray() };
            }
            break;
          }
        case "irot":
          info = info with { Rotation = body[0] & 0x03 };
          break;
        case "imir":
          info = info with { MirrorAxis = body[0] & 0x01 };
          break;
        case "clap":
          {
            var clap = new uint[8];
            for (var i = 0; i < clap.Length; i++)
              clap[i] = BinaryPrimitives.ReadUInt32BigEndian(body[(i * 4)..]);
            info = info with { CleanAperture = clap };
            break;
          }
        case "pasp":
          info = info with { PixelAspectRatio = (BinaryPrimitives.ReadUInt32BigEndian(body), BinaryPrimitives.ReadUInt32BigEndian(body[4..])) };
          break;
        case "clli":
          info = info with { ContentLightLevel = (BinaryPrimitives.ReadUInt16BigEndian(body), BinaryPrimitives.ReadUInt16BigEndian(body[2..])) };
          break;
        default:
          if (!KnownProperties.Contains(type))
            return info with { UnsupportedReason = Strings.AvifProperty(type) };
          break;
      }
    }

    return info.HasAv1Config ? info : AvifInfo.Invalid;
  }

  private readonly record struct Box(string Type, int Start, int End);

  private readonly record struct ItemLocation(int ConstructionMethod, List<(long Offset, long Length)> Extents);

  /// <summary>Lists the boxes in data[start..end], giving the range of each box's payload.</summary>
  private static List<Box> Boxes(ReadOnlySpan<byte> data, int start, int end)
  {
    var boxes = new List<Box>();
    var offset = start;
    while (offset + 8 <= end)
    {
      long size = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
      var type = Encoding.ASCII.GetString(data.Slice(offset + 4, 4));
      var header = 8;
      if (size == 1)
      {
        size = checked((long)BinaryPrimitives.ReadUInt64BigEndian(data[(offset + 8)..]));
        header = 16;
      }
      else if (size == 0)
      {
        size = end - offset;
      }
      if (size < header || offset + size > end)
        throw new InvalidDataException($"Truncated {type} box");
      boxes.Add(new Box(type, offset + header, (int)(offset + size)));
      offset = (int)(offset + size);
    }
    return boxes;
  }

  private static void ParseIloc(ReadOnlySpan<byte> body, Dictionary<uint, ItemLocation> locations)
  {
    var version = body[0];
    var offsetSize = body[4] >> 4;
    var lengthSize = body[4] & 0x0F;
    var baseOffsetSize = body[5] >> 4;
    var indexSize = version is 1 or 2 ? body[5] & 0x0F : 0;
    var at = 6;
    var count = version < 2 ? BinaryPrimitives.ReadUInt16BigEndian(body[at..]) : BinaryPrimitives.ReadUInt32BigEndian(body[at..]);
    at += version < 2 ? 2 : 4;
    for (var i = 0; i < count; i++)
    {
      var id = ReadId(body, ref at, version < 2 ? 0 : 1);
      var constructionMethod = 0;
      if (version is 1 or 2)
      {
        constructionMethod = BinaryPrimitives.ReadUInt16BigEndian(body[at..]) & 0x0F;
        at += 2;
      }
      at += 2; // data_reference_index
      var baseOffset = ReadSized(body, ref at, baseOffsetSize);
      var extentCount = BinaryPrimitives.ReadUInt16BigEndian(body[at..]);
      at += 2;
      var extents = new List<(long, long)>();
      for (var e = 0; e < extentCount; e++)
      {
        ReadSized(body, ref at, indexSize);
        var offset = ReadSized(body, ref at, offsetSize);
        var length = ReadSized(body, ref at, lengthSize);
        extents.Add((checked(baseOffset + offset), length));
      }
      locations[id] = new ItemLocation(constructionMethod, extents);
    }
  }

  private static void ParseIpma(ReadOnlySpan<byte> body, Dictionary<uint, List<int>> associations)
  {
    var version = body[0];
    var wideIndex = (body[3] & 1) != 0;
    var count = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
    var at = 8;
    for (var i = 0; i < count; i++)
    {
      var id = ReadId(body, ref at, version);
      var associationCount = body[at++];
      var list = new List<int>();
      for (var a = 0; a < associationCount; a++)
      {
        int index;
        if (wideIndex)
        {
          index = BinaryPrimitives.ReadUInt16BigEndian(body[at..]) & 0x7FFF;
          at += 2;
        }
        else
        {
          index = body[at++] & 0x7F;
        }
        if (index > 0)
          list.Add(index);
      }
      associations[id] = list;
    }
  }

  private static IEnumerable<(string Type, int Start, int End)> PropertiesOf(
      uint item, Dictionary<uint, List<int>> associations, List<(string Type, int Start, int End)> properties)
  {
    if (!associations.TryGetValue(item, out var indices))
      return [];
    if (indices.Any(i => i > properties.Count))
      throw new InvalidDataException("Property index out of range");
    return indices.Select(i => properties[i - 1]).ToList();
  }

  private static byte[] ReadItem(ReadOnlySpan<byte> data, Dictionary<uint, ItemLocation> locations, (int Start, int End) idat, uint id)
  {
    if (!locations.TryGetValue(id, out var location) || location.ConstructionMethod > 1)
      throw new InvalidDataException("Unsupported item location");
    var source = location.ConstructionMethod == 1 ? data[idat.Start..idat.End] : data;
    var bytes = new List<byte>();
    foreach (var (offset, length) in location.Extents)
    {
      // A zero length extent runs to the end of the data.
      var end = length == 0 ? source.Length : checked(offset + length);
      if (offset < 0 || end > source.Length)
        throw new InvalidDataException("Item data out of range");
      bytes.AddRange(source[(int)offset..(int)end]);
    }
    return [.. bytes];
  }

  private static string? ReadAuxType(ReadOnlySpan<byte> body)
  {
    var at = 4;
    return ReadCString(body, ref at);
  }

  private static string ReadCString(ReadOnlySpan<byte> data, ref int at)
  {
    if (at >= data.Length)
      return "";
    var length = data[at..].IndexOf((byte)0);
    if (length < 0)
      length = data.Length - at;
    var value = Encoding.UTF8.GetString(data.Slice(at, length));
    at += length + 1;
    return value;
  }

  private static uint ReadId(ReadOnlySpan<byte> data, ref int at, int version)
  {
    uint id = version == 0 ? BinaryPrimitives.ReadUInt16BigEndian(data[at..]) : BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
    at += version == 0 ? 2 : 4;
    return id;
  }

  private static long ReadSized(ReadOnlySpan<byte> data, ref int at, int size)
  {
    long value = size switch
    {
      0 => 0,
      4 => BinaryPrimitives.ReadUInt32BigEndian(data[at..]),
      8 => checked((long)BinaryPrimitives.ReadUInt64BigEndian(data[at..])),
      _ => throw new InvalidDataException($"Unsupported field size {size}"),
    };
    at += size;
    return value;
  }
}

public sealed record AvifInfo
{
  public static readonly AvifInfo Invalid = new();

  public bool IsValid { get; init; }
  public bool IsAnimated { get; init; }

  /// <summary>Why the file can't be re-encoded safely, or null when it can.</summary>
  public string? UnsupportedReason { get; init; }

  public uint Width { get; init; }
  public uint Height { get; init; }
  public bool HasAv1Config { get; init; }
  public int Depth { get; init; }
  public bool Monochrome { get; init; }
  public bool ChromaSubsampled { get; init; }

  /// <summary>Color primaries, transfer characteristics and matrix coefficients from the nclx colr box.</summary>
  public (int Primaries, int Transfer, int Matrix)? Cicp { get; init; }
  public bool FullRange { get; init; }
  public byte[]? IccProfile { get; init; }

  public int? Rotation { get; init; }
  public int? MirrorAxis { get; init; }
  public uint[]? CleanAperture { get; init; }
  public (uint Horizontal, uint Vertical)? PixelAspectRatio { get; init; }
  public (int MaxCll, int MaxPall)? ContentLightLevel { get; init; }

  public bool HasAlpha { get; init; }
  public bool AlphaPremultiplied { get; init; }

  public byte[]? Exif { get; init; }
  public byte[]? Xmp { get; init; }

  /// <summary>
  /// RGB stored as full-resolution GBR planes (identity matrix), which is how lossless AVIFs are written.
  /// Ordinary YUV AVIFs, and especially chroma-subsampled ones, are lossy.
  /// </summary>
  public bool IsLikelyLossless => IsValid && !Monochrome && !ChromaSubsampled && Cicp is { Matrix: 0 };

  /// <summary>True when both files display the same way, given identical decoded planes.</summary>
  public bool SameAppearance(AvifInfo other) =>
      IsValid && other.IsValid &&
      Width == other.Width && Height == other.Height &&
      Depth == other.Depth && Monochrome == other.Monochrome && ChromaSubsampled == other.ChromaSubsampled &&
      Cicp == other.Cicp && FullRange == other.FullRange &&
      ArraysEqual(IccProfile, other.IccProfile) &&
      Rotation == other.Rotation && MirrorAxis == other.MirrorAxis &&
      ArraysEqual(CleanAperture, other.CleanAperture) &&
      PixelAspectRatio == other.PixelAspectRatio && ContentLightLevel == other.ContentLightLevel &&
      HasAlpha == other.HasAlpha && AlphaPremultiplied == other.AlphaPremultiplied;

  /// <summary>The TIFF data inside the Exif item, without the offset field and any "Exif\0\0" prefix.</summary>
  public byte[]? ExifTiff
  {
    get
    {
      if (Exif is not { Length: >= 4 })
        return null;
      var offset = (long)BinaryPrimitives.ReadUInt32BigEndian(Exif) + 4;
      return offset <= Exif.Length ? Exif[(int)offset..] : null;
    }
  }

  internal static bool ArraysEqual<T>(T[]? a, T[]? b) where T : IEquatable<T> =>
      a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
}
