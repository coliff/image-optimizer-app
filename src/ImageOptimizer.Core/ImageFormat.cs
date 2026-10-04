namespace ImageOptimizer.Core;

public enum ImageFormat
{
  Unknown,
  Png,
  Jpeg,
  WebP,
  Avif,
  Gif,
  Svg,
  JpegXl,
}

public static class ImageFormats
{
  private static readonly Dictionary<string, ImageFormat> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
  {
    [".png"] = ImageFormat.Png,
    [".jpg"] = ImageFormat.Jpeg,
    [".jpeg"] = ImageFormat.Jpeg,
    [".jpe"] = ImageFormat.Jpeg,
    [".jfif"] = ImageFormat.Jpeg,
    [".webp"] = ImageFormat.WebP,
    [".avif"] = ImageFormat.Avif,
    [".gif"] = ImageFormat.Gif,
    [".svg"] = ImageFormat.Svg,
    [".jxl"] = ImageFormat.JpegXl,
  };

  public static IReadOnlyCollection<string> SupportedExtensions => ExtensionMap.Keys;

  public static bool HasSupportedExtension(string path) =>
      ExtensionMap.ContainsKey(Path.GetExtension(path));

  public static ImageFormat FromExtension(string path) =>
      ExtensionMap.TryGetValue(Path.GetExtension(path), out var format) ? format : ImageFormat.Unknown;

  /// <summary>Identifies an image by its signature bytes rather than trusting the file extension.</summary>
  public static ImageFormat Detect(ReadOnlySpan<byte> header)
  {
    if (header.Length >= 8 && header[..8].SequenceEqual(PngSignature))
      return ImageFormat.Png;
    if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
      return ImageFormat.Jpeg;
    if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
      return ImageFormat.WebP;
    if (AvifInspector.HasAvifBrand(header))
      return ImageFormat.Avif;
    if (JxlInspector.HasSignature(header))
      return ImageFormat.JpegXl;
    if (header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)))
      return ImageFormat.Gif;
    return ImageFormat.Unknown;
  }

  public static ImageFormat Detect(string path)
  {
    // Long enough for an ftyp box with a few compatible brands.
    Span<byte> header = stackalloc byte[64];
    using var stream = File.OpenRead(path);
    var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
    var format = Detect(header[..read]);
    if (format != ImageFormat.Unknown || !Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
      return format;

    // SVG is text, so look for the root element near the start (after any XML declaration, comments or DOCTYPE).
    stream.Position = 0;
    var start = new byte[4096];
    read = stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
    return LooksLikeSvg(start.AsSpan(0, read)) ? ImageFormat.Svg : ImageFormat.Unknown;
  }

  internal static bool LooksLikeSvg(ReadOnlySpan<byte> start) =>
      !start.Contains((byte)0) && start.IndexOf("<svg"u8) >= 0;

  public static string DisplayName(ImageFormat format) => format switch
  {
    ImageFormat.Png => "PNG",
    ImageFormat.Jpeg => "JPEG",
    ImageFormat.WebP => "WebP",
    ImageFormat.Avif => "AVIF",
    ImageFormat.Gif => "GIF",
    ImageFormat.Svg => "SVG",
    ImageFormat.JpegXl => "JPEG XL",
    _ => "Unknown",
  };

  internal static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
}
