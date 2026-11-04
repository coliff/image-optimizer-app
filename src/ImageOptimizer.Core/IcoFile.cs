using System.Buffers.Binary;

namespace ImageOptimizer.Core;

/// <summary>One image inside an ICO or CUR file: its 16-byte directory entry and its data (a PNG or a BMP).</summary>
public sealed record IcoEntry(byte[] Header, byte[] Data)
{
  public bool IsPng => Data.AsSpan().StartsWith(ImageFormats.PngSignature);
}

/// <summary>
/// Reads and writes the ICO and CUR containers: a 6-byte header, a 16-byte directory entry per image, then the
/// images themselves. Writing lays the images out one after another in directory order, and only updates each
/// entry's size and offset, so sizes, bit depths and cursor hotspots are kept exactly.
/// </summary>
public sealed record IcoFile(ushort Type, IReadOnlyList<IcoEntry> Entries)
{
  private const int HeaderSize = 6;
  private const int EntrySize = 16;

  public static bool IsIcoExtension(string path) =>
      Path.GetExtension(path) is var extension &&
      (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) || extension.Equals(".cur", StringComparison.OrdinalIgnoreCase));

  /// <summary>Reserved word 0, type 1 (icon) or 2 (cursor), and at least one image.</summary>
  public static bool HasSignature(ReadOnlySpan<byte> data) =>
      data.Length >= HeaderSize &&
      data[0] == 0 && data[1] == 0 &&
      data[2] is 1 or 2 && data[3] == 0 &&
      BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) > 0;

  /// <summary>Returns null when the file isn't a well-formed icon or cursor.</summary>
  public static IcoFile? Parse(ReadOnlySpan<byte> data)
  {
    if (!HasSignature(data))
      return null;

    var type = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
    var count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
    var directoryEnd = HeaderSize + (count * EntrySize);
    if (data.Length < directoryEnd)
      return null;

    var entries = new List<IcoEntry>(count);
    for (var i = 0; i < count; i++)
    {
      var header = data.Slice(HeaderSize + (i * EntrySize), EntrySize);
      long size = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
      long offset = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
      if (size == 0 || offset < directoryEnd || offset + size > data.Length)
        return null;
      entries.Add(new IcoEntry(header.ToArray(), data.Slice((int)offset, (int)size).ToArray()));
    }
    return new IcoFile(type, entries);
  }

  public byte[] ToBytes()
  {
    var directoryEnd = HeaderSize + (Entries.Count * EntrySize);
    var output = new byte[directoryEnd + Entries.Sum(e => e.Data.Length)];
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(2), Type);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(4), (ushort)Entries.Count);

    var offset = directoryEnd;
    for (var i = 0; i < Entries.Count; i++)
    {
      var entry = Entries[i];
      var header = output.AsSpan(HeaderSize + (i * EntrySize), EntrySize);
      entry.Header.CopyTo(header);
      BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)entry.Data.Length);
      BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)offset);
      entry.Data.CopyTo(output.AsSpan(offset));
      offset += entry.Data.Length;
    }
    return output;
  }
}
