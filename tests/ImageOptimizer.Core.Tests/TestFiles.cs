using System.Buffers.Binary;

namespace ImageOptimizer.Core.Tests;

/// <summary>A scratch directory holding copies of the fixtures, deleted after each test.</summary>
public sealed class TestFiles : IDisposable
{
  public string Directory { get; } = Path.Combine(Path.GetTempPath(), "ImageOptimizerTests", Guid.NewGuid().ToString("N"));

  public TestFiles() => System.IO.Directory.CreateDirectory(Directory);

  public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

  public string CopyFixture(string name, string? as_ = null)
  {
    var destination = Path.Combine(Directory, as_ ?? name);
    File.Copy(FixturePath(name), destination);
    return destination;
  }

  public string Write(string name, byte[] contents)
  {
    var destination = Path.Combine(Directory, name);
    File.WriteAllBytes(destination, contents);
    return destination;
  }

  public void Dispose()
  {
    try
    {
      System.IO.Directory.Delete(Directory, recursive: true);
    }
    catch (IOException)
    {
    }
  }

  /// <summary>Inserts an acTL chunk after IHDR, turning a PNG into an (unusual) APNG.</summary>
  public static byte[] MakeAnimatedPng(byte[] png)
  {
    const int ihdrEnd = 8 + 4 + 4 + 13 + 4;
    var data = new byte[8];
    BinaryPrimitives.WriteUInt32BigEndian(data, 1);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 0);
    return [.. png[..ihdrEnd], .. Chunk("acTL", data), .. png[ihdrEnd..]];
  }

  private static byte[] Chunk(string type, byte[] data)
  {
    var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
    var chunk = new byte[12 + data.Length];
    BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
    typeBytes.CopyTo(chunk, 4);
    data.CopyTo(chunk, 8);
    BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32([.. typeBytes, .. data]));
    return chunk;
  }

  private static uint Crc32(byte[] bytes)
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

  /// <summary>Inserts an EXIF APP1 segment with the given orientation right after SOI.</summary>
  public static byte[] WithExifOrientation(byte[] jpeg, ushort orientation, bool littleEndian = false)
  {
    byte[] payload = [.. "Exif\0\0"u8, .. ExifTiff(orientation, littleEndian)];
    var segment = new byte[4 + payload.Length];
    segment[0] = 0xFF;
    segment[1] = 0xE1;
    BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
    payload.CopyTo(segment, 4);
    return [.. jpeg[..2], .. segment, .. jpeg[2..]];
  }

  /// <summary>Inserts an eXIf chunk with the given orientation after IHDR.</summary>
  public static byte[] WithPngExifOrientation(byte[] png, ushort orientation)
  {
    const int ihdrEnd = 8 + 4 + 4 + 13 + 4;
    return [.. png[..ihdrEnd], .. Chunk("eXIf", ExifTiff(orientation, littleEndian: false)), .. png[ihdrEnd..]];
  }

  /// <summary>EXIF data (TIFF) holding only the given orientation.</summary>
  public static byte[] ExifTiff(ushort orientation, bool littleEndian = false)
  {
    var tiff = new byte[8 + 2 + 12 + 4];
    void U16(int at, ushort v)
    {
      if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(at), v);
      else BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at), v);
    }
    void U32(int at, uint v)
    {
      if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(at), v);
      else BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at), v);
    }
    tiff[0] = tiff[1] = (byte)(littleEndian ? 'I' : 'M');
    U16(2, 42);
    U32(4, 8);
    U16(8, 1);           // one IFD entry
    U16(10, 0x0112);     // Orientation
    U16(12, 3);          // SHORT
    U32(14, 1);          // count
    U16(18, orientation);
    U32(22, 0);          // no next IFD
    return tiff;
  }
}
