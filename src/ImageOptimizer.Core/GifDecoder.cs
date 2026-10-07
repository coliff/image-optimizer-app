using System.Buffers.Binary;

namespace ImageOptimizer.Core;

/// <summary>
/// Reads a GIF one frame at a time and draws each frame onto the canvas the way browsers show it (transparency,
/// disposal and interlacing included). gifsicle crops frames and changes their disposal, transparency and color
/// tables, so it's the drawn canvas, not the stored frames, that must stay the same.
/// </summary>
/// <remarks>Throws <see cref="InvalidDataException"/> when the file is damaged or isn't a GIF.</remarks>
public sealed class GifDecoder
{
  // Canvases and frames bigger than 4096 × 4096 aren't drawn, so comparing two GIFs (two canvases, the copies kept
  // for restoring them, and each frame's color indexes) stays within about 300 MB.
  private const long MaxPixels = 4096 * 4096;

  private readonly byte[] _data;
  private readonly uint[]? _globalColors;
  private int _offset;
  private int _previousDisposal;
  private (int X, int Y, int Width, int Height) _previousRect;
  private uint[]? _beforePrevious;
  private uint[]? _canvas;
  private byte[] _indexes = [];

  public GifDecoder(byte[] data)
  {
    _data = data;
    if (data.Length < 13 || !(data.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || data.AsSpan(0, 6).SequenceEqual("GIF89a"u8)))
      throw new InvalidDataException("Not a GIF");
    Width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6));
    Height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8));
    var flags = data[10];
    _offset = 13;
    if ((flags & 0x80) != 0)
      _globalColors = ReadColorTable(flags);
  }

  public int Width { get; }

  public int Height { get; }

  /// <summary>The canvas as shown after the current frame, as RGBA with fully transparent pixels stored as 0.</summary>
  public uint[] Canvas => _canvas ??= new uint[Width * Height];

  /// <summary>How long the current frame is shown, in hundredths of a second.</summary>
  public int Delay { get; private set; }

  /// <summary>How many times the animation repeats (0 is forever), or null when it plays once. Known once every frame is read.</summary>
  public int? LoopCount { get; private set; }

  /// <summary>The application extensions read so far, such as "NETSCAPE2.0", "XMP DataXMP" or "ICCRGBG1012".</summary>
  public List<string> ApplicationExtensions { get; } = [];

  /// <summary>Lists a GIF's application extensions without decoding its frames.</summary>
  public static IReadOnlyList<string> ReadApplicationExtensions(byte[] data)
  {
    var decoder = new GifDecoder(data);
    while (decoder.Advance(draw: false))
    {
    }
    return decoder.ApplicationExtensions;
  }

  /// <summary>Draws the next frame onto <see cref="Canvas"/>. False once there are no more frames.</summary>
  public bool ReadFrame() => Advance(draw: true);

  private bool Advance(bool draw)
  {
    int? transparent = null;
    var disposal = 0;
    var delay = 0;
    while (true)
    {
      // A missing trailer is common, and browsers don't mind.
      if (_offset == _data.Length)
        return false;
      switch (ReadByte())
      {
        case 0x3B:
          return false;

        case 0x21:
          var label = ReadByte();
          var first = ReadSubBlock();
          if (label == 0xF9 && first.Length >= 4)
          {
            disposal = (first[0] >> 2) & 7;
            delay = BinaryPrimitives.ReadUInt16LittleEndian(first[1..]);
            transparent = (first[0] & 1) != 0 ? first[3] : null;
          }
          if (label == 0xFF && first.Length == 11)
          {
            var name = System.Text.Encoding.ASCII.GetString(first);
            ApplicationExtensions.Add(name);
            var looping = name is "NETSCAPE2.0" or "ANIMEXTS1.0";
            for (var block = ReadSubBlock(); block.Length > 0; block = ReadSubBlock())
            {
              if (looping && block.Length >= 3 && block[0] == 1)
                LoopCount = BinaryPrimitives.ReadUInt16LittleEndian(block[1..]);
            }
          }
          else if (first.Length > 0)
          {
            SkipSubBlocks();
          }
          break;

        case 0x2C:
          ReadImage(draw, transparent, disposal);
          Delay = delay;
          return true;

        default:
          throw new InvalidDataException("Unexpected block in GIF");
      }
    }
  }

  private void ReadImage(bool draw, int? transparent, int disposal)
  {
    var header = Read(9);
    var x = BinaryPrimitives.ReadUInt16LittleEndian(header);
    var y = BinaryPrimitives.ReadUInt16LittleEndian(header[2..]);
    var width = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
    var flags = header[8];
    var colors = (flags & 0x80) != 0 ? ReadColorTable(flags) : _globalColors;
    var interlaced = (flags & 0x40) != 0;

    if (!draw)
    {
      ReadByte();
      SkipSubBlocks();
      return;
    }

    if ((long)Width * Height > MaxPixels || (long)width * height > MaxPixels)
      throw new InvalidDataException("The GIF is too large to check");
    if (colors is null)
      throw new InvalidDataException("GIF frame without a color table");
    var decoded = DecodeLzw(width * height);

    // Undo the previous frame as its disposal method asks, then remember the canvas if this one restores it.
    var rect = (X: (int)x, Y: (int)y, Width: (int)width, Height: (int)height);
    if (_previousDisposal == 2)
      Fill(_previousRect, 0);
    else if (_previousDisposal == 3 && _beforePrevious is not null)
      _beforePrevious.CopyTo(Canvas, 0);
    if (disposal == 3)
    {
      _beforePrevious ??= new uint[Canvas.Length];
      Canvas.CopyTo(_beforePrevious, 0);
    }
    _previousDisposal = disposal;
    _previousRect = rect;

    for (var row = 0; row < height; row++)
    {
      var canvasY = y + InterlacedRow(row, height, interlaced);
      if (canvasY >= Height)
        continue;
      for (var column = 0; column < width && x + column < Width; column++)
      {
        var position = row * width + column;
        // Pixels the data ran out before are left as they were, as browsers do.
        if (position >= decoded)
          break;
        var index = _indexes[position];
        if (index == transparent)
          continue;
        // An index past the end of the color table is drawn black, as browsers do.
        Canvas[canvasY * Width + x + column] = index < colors.Length ? colors[index] : 0xFF000000;
      }
    }
  }

  private void Fill((int X, int Y, int Width, int Height) rect, uint value)
  {
    for (var row = rect.Y; row < Math.Min(rect.Y + rect.Height, Height); row++)
    {
      for (var column = rect.X; column < Math.Min(rect.X + rect.Width, Width); column++)
        Canvas[row * Width + column] = value;
    }
  }

  private static readonly (int Start, int Step)[] InterlacePasses = [(0, 8), (4, 8), (2, 4), (1, 2)];

  /// <summary>Interlaced frames store every 8th row first, then the rows between them, in four passes.</summary>
  private static int InterlacedRow(int row, int height, bool interlaced)
  {
    if (!interlaced)
      return row;
    foreach (var (start, step) in InterlacePasses)
    {
      var count = (height - start + step - 1) / step;
      if (row < count)
        return start + row * step;
      row -= count;
    }
    return row;
  }

  /// <summary>Decodes a frame's LZW data into color indexes and returns how many pixels the data reaches.</summary>
  private int DecodeLzw(int pixelCount)
  {
    var minCodeSize = ReadByte();
    if (minCodeSize is < 1 or > 11)
      throw new InvalidDataException("Invalid GIF code size");

    if (_indexes.Length < pixelCount)
      _indexes = new byte[pixelCount];
    var pixels = _indexes;
    var clear = 1 << minCodeSize;
    var end = clear + 1;
    var prefix = new short[4096];
    var suffix = new byte[4096];
    var firstOf = new byte[4096];
    var stack = new byte[4097];
    for (var code = 0; code < clear; code++)
    {
      suffix[code] = (byte)code;
      firstOf[code] = (byte)code;
    }

    var codeSize = minCodeSize + 1;
    var next = clear + 2;
    var previous = -1;
    var written = 0;
    var bits = 0;
    var bitCount = 0;
    var finished = false;

    for (var block = ReadSubBlock(); block.Length > 0; block = ReadSubBlock())
    {
      foreach (var b in block)
      {
        bits |= b << bitCount;
        bitCount += 8;
        while (!finished && bitCount >= codeSize)
        {
          var code = bits & ((1 << codeSize) - 1);
          bits >>= codeSize;
          bitCount -= codeSize;

          if (code == clear)
          {
            codeSize = minCodeSize + 1;
            next = clear + 2;
            previous = -1;
            continue;
          }
          if (code == end)
          {
            finished = true;
            break;
          }
          if (code > next || (code == next && previous < 0))
            throw new InvalidDataException("Invalid GIF data");

          // Unwind the code into the stack, last pixel first.
          var depth = 0;
          var current = code;
          if (code == next)
          {
            stack[depth++] = firstOf[previous];
            current = previous;
          }
          while (current >= clear)
          {
            stack[depth++] = suffix[current];
            current = prefix[current];
          }
          stack[depth++] = (byte)current;

          while (depth > 0 && written < pixelCount)
            pixels[written++] = stack[--depth];

          if (previous >= 0 && next < 4096)
          {
            prefix[next] = (short)previous;
            suffix[next] = (byte)current;
            firstOf[next] = firstOf[previous];
            next++;
            if (next == 1 << codeSize && codeSize < 12)
              codeSize++;
          }
          previous = code;
        }
      }
    }
    return written;
  }

  private uint[] ReadColorTable(byte flags)
  {
    var count = 2 << (flags & 7);
    var table = Read(count * 3);
    var colors = new uint[count];
    for (var i = 0; i < count; i++)
      colors[i] = 0xFF000000 | (uint)table[i * 3] << 16 | (uint)table[i * 3 + 1] << 8 | table[i * 3 + 2];
    return colors;
  }

  private byte ReadByte() => Read(1)[0];

  private ReadOnlySpan<byte> Read(int count)
  {
    if (count > _data.Length - _offset)
      throw new InvalidDataException("The GIF ends early");
    var span = _data.AsSpan(_offset, count);
    _offset += count;
    return span;
  }

  private ReadOnlySpan<byte> ReadSubBlock() => Read(ReadByte());

  private void SkipSubBlocks()
  {
    while (ReadSubBlock().Length > 0)
    {
    }
  }
}
