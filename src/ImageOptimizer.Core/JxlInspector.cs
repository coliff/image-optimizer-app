using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ImageOptimizer.Core;

/// <summary>
/// Reads the JPEG XL container boxes and the codestream's image header (everything that decides how the
/// image looks), so a re-encode can be checked against the original and anything cjxl can't reproduce is
/// left alone.
/// </summary>
public static class JxlInspector
{
  private static ReadOnlySpan<byte> ContainerSignature => [0x00, 0x00, 0x00, 0x0C, (byte)'J', (byte)'X', (byte)'L', (byte)' ', 0x0D, 0x0A, 0x87, 0x0A];

  // Boxes that don't affect how the image looks and that cjxl writes itself when it needs them.
  private static readonly HashSet<string> StructuralBoxes = ["JXL ", "ftyp", "jxll", "jxli", "jxlc", "jxlp"];

  /// <summary>True when the data starts like a JPEG XL file (bare codestream or ISOBMFF container).</summary>
  public static bool HasSignature(ReadOnlySpan<byte> data) =>
      (data.Length >= 2 && data[0] == 0xFF && data[1] == 0x0A) ||
      (data.Length >= 12 && data[..12].SequenceEqual(ContainerSignature));

  public static JxlInfo Inspect(ReadOnlySpan<byte> data)
  {
    try
    {
      return Parse(data);
    }
    catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException or OverflowException or EndOfStreamException)
    {
      return JxlInfo.Invalid;
    }
  }

  private static JxlInfo Parse(ReadOnlySpan<byte> data)
  {
    if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0x0A)
      return ParseCodestream(data[2..]) with { IsContainer = false };
    if (!HasSignature(data))
      return JxlInfo.Invalid;

    // The header is at the start of the codestream, so its first part is plenty. Partial codestream boxes
    // may be stored out of order, so they are put back in order by their sequence number.
    const int headerBytes = 1 << 16;
    var parts = new SortedDictionary<uint, byte[]>();
    byte[]? exif = null, xmp = null, jumbf = null;
    var jpegReconstruction = false;
    string? unsupported = null;
    var offset = 0;
    while (offset + 8 <= data.Length)
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
        size = data.Length - offset;
      }
      if (size < header || offset + size > data.Length)
        throw new InvalidDataException($"Truncated {type} box");
      var body = data[(offset + header)..(int)(offset + size)];

      var compressed = type == "brob";
      if (compressed)
      {
        type = Encoding.ASCII.GetString(body[..4]);
        body = body[4..];
      }

      switch (type)
      {
        case "jxlc":
          parts[0] = body[..Math.Min(body.Length, headerBytes)].ToArray();
          break;
        case "jxlp":
          parts[BinaryPrimitives.ReadUInt32BigEndian(body) & 0x7FFFFFFF] = body[4..][..Math.Min(body.Length - 4, headerBytes)].ToArray();
          break;
        case "jbrd":
          jpegReconstruction = true;
          break;
        case "Exif":
          exif ??= compressed ? Decompress(body) : body.ToArray();
          break;
        case "xml ":
          xmp ??= compressed ? Decompress(body) : body.ToArray();
          break;
        case "jumb":
          jumbf ??= compressed ? Decompress(body) : body.ToArray();
          break;
        default:
          if (!StructuralBoxes.Contains(type) && !compressed)
            unsupported ??= type == "jhgm" ? Strings.JxlGainMap : Strings.JxlBox(type.Trim());
          else if (compressed)
            unsupported ??= Strings.JxlCompressedBox(type.Trim());
          break;
      }
      offset = (int)(offset + size);
    }

    var codestream = parts.Values.SelectMany(part => part).Take(headerBytes).ToArray();
    if (codestream.Length < 2 || codestream[0] != 0xFF || codestream[1] != 0x0A)
      return JxlInfo.Invalid;
    var info = ParseCodestream(codestream.AsSpan(2));
    return info with
    {
      IsContainer = true,
      HasJpegReconstruction = jpegReconstruction,
      Exif = exif,
      Xmp = xmp,
      Jumbf = jumbf,
      UnsupportedReason = info.UnsupportedReason ?? unsupported,
    };
  }

  private static byte[] Decompress(ReadOnlySpan<byte> data)
  {
    using var input = new MemoryStream(data.ToArray());
    using var brotli = new BrotliStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    brotli.CopyTo(output);
    return output.ToArray();
  }

  private static JxlInfo ParseCodestream(ReadOnlySpan<byte> data)
  {
    var r = new BitReader(data);

    // SizeHeader
    uint height, width;
    var small = r.Bool();
    height = small ? (r.Bits(5) + 1) * 8 : r.U32(Bo(9, 1), Bo(13, 1), Bo(18, 1), Bo(30, 1));
    var ratio = r.Bits(3);
    if (ratio == 0)
      width = small ? (r.Bits(5) + 1) * 8 : r.U32(Bo(9, 1), Bo(13, 1), Bo(18, 1), Bo(30, 1));
    else
      width = FixedRatio(height, ratio);

    var info = new JxlInfo { IsValid = true, Width = width, Height = height, Orientation = 1, BitsPerSample = 8, XybEncoded = true };

    // ImageMetadata
    if (r.Bool()) // all_default
      return info;

    var extraFields = r.Bool();
    if (extraFields)
    {
      info = info with { Orientation = (int)r.Bits(3) + 1 };
      if (r.Bool())
        info = info with { IntrinsicSize = ReadSize(ref r) };
      if (r.Bool())
        info = info with { PreviewSize = ReadPreviewSize(ref r) };
      if (r.Bool())
      {
        info = info with { IsAnimated = true };
        r.U32(V(100), V(1000), Bo(10, 1), Bo(30, 1));
        r.U32(V(1), V(1001), Bo(8, 1), Bo(10, 1));
        r.U32(V(0), B(3), B(16), B(32));
        r.Bool();
      }
    }

    var (bits, exponentBits) = ReadBitDepth(ref r);
    info = info with { BitsPerSample = (int)bits, ExponentBitsPerSample = (int)exponentBits };
    r.Bool(); // modular_16_bit_buffer_sufficient: only a hint for decoders

    var extraChannelCount = r.U32(V(0), V(1), Bo(4, 2), Bo(12, 1));
    var extraChannels = new List<JxlExtraChannel>();
    for (var i = 0; i < extraChannelCount; i++)
      extraChannels.Add(ReadExtraChannel(ref r));
    info = info with { ExtraChannels = extraChannels };

    info = info with { XybEncoded = r.Bool() };
    info = info with { Color = ReadColorEncoding(ref r) };
    if (extraFields)
      info = info with { ToneMapping = ReadToneMapping(ref r) };

    if (r.U64() != 0)
      info = info with { UnsupportedReason = Strings.JxlHeaderExtensions };
    return info;
  }

  private static uint FixedRatio(uint height, uint ratio) => ratio switch
  {
    1 => height,
    2 => (uint)(height * 12UL / 10),
    3 => (uint)(height * 4UL / 3),
    4 => (uint)(height * 3UL / 2),
    5 => (uint)(height * 16UL / 9),
    6 => (uint)(height * 5UL / 4),
    _ => (uint)(height * 2UL),
  };

  private static (uint Width, uint Height) ReadSize(ref BitReader r)
  {
    var small = r.Bool();
    var height = small ? (r.Bits(5) + 1) * 8 : r.U32(Bo(9, 1), Bo(13, 1), Bo(18, 1), Bo(30, 1));
    var ratio = r.Bits(3);
    var width = ratio != 0 ? FixedRatio(height, ratio) : small ? (r.Bits(5) + 1) * 8 : r.U32(Bo(9, 1), Bo(13, 1), Bo(18, 1), Bo(30, 1));
    return (width, height);
  }

  private static (uint Width, uint Height) ReadPreviewSize(ref BitReader r)
  {
    var div8 = r.Bool();
    var height = div8 ? r.U32(V(16), V(32), Bo(5, 1), Bo(9, 33)) * 8 : r.U32(Bo(6, 1), Bo(8, 65), Bo(10, 321), Bo(12, 1345));
    var ratio = r.Bits(3);
    var width = ratio != 0 ? FixedRatio(height, ratio) : div8 ? r.U32(V(16), V(32), Bo(5, 1), Bo(9, 33)) * 8 : r.U32(Bo(6, 1), Bo(8, 65), Bo(10, 321), Bo(12, 1345));
    return (width, height);
  }

  private static (uint Bits, uint ExponentBits) ReadBitDepth(ref BitReader r)
  {
    if (!r.Bool())
      return (r.U32(V(8), V(10), V(12), Bo(6, 1)), 0);
    var bits = r.U32(V(32), V(16), V(24), Bo(6, 1));
    return (bits, r.Bits(4) + 1);
  }

  private static JxlExtraChannel ReadExtraChannel(ref BitReader r)
  {
    if (r.Bool()) // all_default: 8-bit unassociated alpha
      return new JxlExtraChannel(0, 8, 0, 0, "", false, "");

    var type = r.Enum();
    var (bits, exponentBits) = ReadBitDepth(ref r);
    var dimShift = r.U32(V(0), V(3), V(4), Bo(3, 1));
    var nameLength = r.U32(V(0), B(4), Bo(5, 16), Bo(10, 48));
    var name = new byte[nameLength];
    for (var i = 0; i < nameLength; i++)
      name[i] = (byte)r.Bits(8);
    var associated = false;
    var extra = "";
    if (type == 0)
      associated = r.Bool();
    else if (type == 2) // spot color: four F16 values
      extra = string.Join(',', r.Bits(16), r.Bits(16), r.Bits(16), r.Bits(16));
    else if (type == 5) // CFA channel
      extra = r.U32(V(1), B(2), Bo(4, 3), Bo(8, 19)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    return new JxlExtraChannel((int)type, (int)bits, (int)exponentBits, (int)dimShift, Encoding.UTF8.GetString(name), associated, extra);
  }

  private static string ReadColorEncoding(ref BitReader r)
  {
    if (r.Bool()) // all_default: sRGB
      return "default";

    var parts = new List<uint>();
    var wantIcc = r.Bool();
    var colorSpace = r.Enum();
    parts.Add(wantIcc ? 1u : 0u);
    parts.Add(colorSpace);
    if (!wantIcc)
    {
      const uint xyb = 2, gray = 1;
      if (colorSpace != xyb)
      {
        var whitePoint = r.Enum();
        parts.Add(whitePoint);
        if (whitePoint == 2)
          ReadCustomXy(ref r, parts);
      }
      if (colorSpace != xyb && colorSpace != gray)
      {
        var primaries = r.Enum();
        parts.Add(primaries);
        if (primaries == 2)
        {
          for (var i = 0; i < 3; i++)
            ReadCustomXy(ref r, parts);
        }
      }
      if (colorSpace != xyb)
      {
        var haveGamma = r.Bool();
        parts.Add(haveGamma ? 1u : 0u);
        parts.Add(haveGamma ? r.Bits(24) : r.Enum());
      }
      parts.Add(r.Enum()); // rendering intent
    }
    return string.Join('/', parts);
  }

  private static void ReadCustomXy(ref BitReader r, List<uint> parts)
  {
    for (var i = 0; i < 2; i++)
      parts.Add(r.U32(B(19), Bo(19, 524288), Bo(20, 1048576), Bo(21, 2097152)));
  }

  private static string ReadToneMapping(ref BitReader r)
  {
    if (r.Bool()) // all_default
      return "default";
    return string.Join('/', r.Bits(16), r.Bits(16), r.Bool() ? 1 : 0, r.Bits(16));
  }

  // U32 distributions: a constant, or a number of bits plus an offset.
  private static (uint Bits, uint Offset) V(uint value) => (0, value);
  private static (uint Bits, uint Offset) B(uint bits) => (bits, 0);
  private static (uint Bits, uint Offset) Bo(uint bits, uint offset) => (bits, offset);

  /// <summary>Reads the codestream's bit-packed fields (least significant bit first).</summary>
  private ref struct BitReader(ReadOnlySpan<byte> data)
  {
    private readonly ReadOnlySpan<byte> _data = data;
    private long _position;

    public uint Bits(uint count)
    {
      ulong value = 0;
      for (var i = 0; i < count; i++)
      {
        var index = (int)(_position >> 3);
        if (index >= _data.Length)
          throw new EndOfStreamException();
        value |= (ulong)((_data[index] >> (int)(_position & 7)) & 1) << i;
        _position++;
      }
      return (uint)value;
    }

    public bool Bool() => Bits(1) == 1;

    public uint U32((uint Bits, uint Offset) d0, (uint Bits, uint Offset) d1, (uint Bits, uint Offset) d2, (uint Bits, uint Offset) d3)
    {
      var d = Bits(2) switch { 0 => d0, 1 => d1, 2 => d2, _ => d3 };
      return checked(Bits(d.Bits) + d.Offset);
    }

    public uint Enum() => U32(V(0), V(1), Bo(4, 2), Bo(6, 18));

    public ulong U64()
    {
      switch (Bits(2))
      {
        case 0:
          return 0;
        case 1:
          return 1 + Bits(4);
        case 2:
          return 17 + Bits(8);
      }
      ulong value = Bits(12);
      var shift = 12;
      while (Bool())
      {
        if (shift == 60)
        {
          value |= (ulong)Bits(4) << shift;
          break;
        }
        value |= (ulong)Bits(8) << shift;
        shift += 8;
      }
      return value;
    }
  }
}

/// <param name="Type">0 alpha, 1 depth, 2 spot color, 3 selection mask, 4 black (CMYK), 5 CFA, 6 thermal, ...</param>
public sealed record JxlExtraChannel(int Type, int BitsPerSample, int ExponentBitsPerSample, int DimShift, string Name, bool AlphaAssociated, string Extra);

public sealed record JxlInfo
{
  public static readonly JxlInfo Invalid = new();

  public bool IsValid { get; init; }
  public bool IsContainer { get; init; }

  /// <summary>Why the file can't be re-encoded safely, or null when it can.</summary>
  public string? UnsupportedReason { get; init; }

  public uint Width { get; init; }
  public uint Height { get; init; }
  public int Orientation { get; init; }
  public (uint Width, uint Height)? IntrinsicSize { get; init; }
  public (uint Width, uint Height)? PreviewSize { get; init; }
  public bool IsAnimated { get; init; }

  public int BitsPerSample { get; init; }
  public int ExponentBitsPerSample { get; init; }
  public IReadOnlyList<JxlExtraChannel> ExtraChannels { get; init; } = [];

  /// <summary>
  /// Pixels stored in the XYB color space, which only lossy encodes use. Lossless (modular) and recompressed
  /// JPEG files keep the original color space.
  /// </summary>
  public bool XybEncoded { get; init; }

  /// <summary>The header's color encoding, as a comparable string (the ICC profile itself is compared after decoding).</summary>
  public string? Color { get; init; }
  public string? ToneMapping { get; init; }

  /// <summary>Has a jbrd box, so the original JPEG file can be rebuilt bit for bit.</summary>
  public bool HasJpegReconstruction { get; init; }

  public byte[]? Exif { get; init; }
  public byte[]? Xmp { get; init; }
  public byte[]? Jumbf { get; init; }

  /// <summary>The TIFF data inside the Exif box, without the leading offset field.</summary>
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

  /// <summary>True when both files display the same way, given identical decoded pixels and ICC profiles.</summary>
  public bool SameAppearance(JxlInfo other) =>
      IsValid && other.IsValid &&
      Width == other.Width && Height == other.Height && Orientation == other.Orientation &&
      IntrinsicSize == other.IntrinsicSize && PreviewSize == other.PreviewSize && IsAnimated == other.IsAnimated &&
      BitsPerSample == other.BitsPerSample && ExponentBitsPerSample == other.ExponentBitsPerSample &&
      ExtraChannels.SequenceEqual(other.ExtraChannels) &&
      XybEncoded == other.XybEncoded && Color == other.Color && ToneMapping == other.ToneMapping;
}
