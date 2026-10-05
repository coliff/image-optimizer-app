using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace ImageOptimizer.Core;

/// <summary>
/// Messages shown to people, from Strings.resx (English) or a translation such as Strings.ja.resx, picked by
/// the Windows display language. Each member reads the resource with its own name.
/// </summary>
internal static class Strings
{
  internal static ResourceManager ResourceManager { get; } = new("ImageOptimizer.Core.Strings", typeof(Strings).Assembly);

  private static string Get([CallerMemberName] string name = "") =>
      ResourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;

  private static string Format(object? arg0, [CallerMemberName] string name = "") =>
      string.Format(CultureInfo.CurrentCulture, Get(name), arg0);

  public static string OneByte => Get();
  public static string Bytes(long count) => Format(count);
  public static string FileNotFound => Get();
  public static string NotAnImage => Get();
  public static string FormatTurnedOff(ImageFormat format) => Format(ImageFormats.DisplayName(format));
  public static string FileEmpty => Get();
  public static string PixelsDiffer => Get();
  public static string FileChanged => Get();
  public static string CannotWrite => Get();
  public static string ToolMissing(string tool) => Format(tool);
  public static string InvalidFile(ImageFormat format) => Format(ImageFormats.DisplayName(format));
  public static string AnimatedLeftUntouched(ImageFormat format) => Format(ImageFormats.DisplayName(format));
  public static string LossyCannotRecompress(ImageFormat format) => Format(ImageFormats.DisplayName(format));
  public static string AnimatedWebPNothingToRemove => Get();
  public static string JpegTrailingData => Get();
  public static string SvgNotUtf8 => Get();
  public static string AvifGrid => Get();
  public static string AvifUnsupportedType => Get();
  public static string AvifAuxiliaryImages => Get();
  public static string AvifEmbeddedItems => Get();
  public static string AvifProperty(string type) => Format(type);
  public static string AvifPremultipliedAlpha => Get();
  public static string JxlRotated => Get();
  public static string JxlHighBitDepth => Get();
  public static string JxlExtraChannels => Get();
  public static string JxlGainMap => Get();
  public static string JxlBox(string type) => Format(type);
  public static string JxlCompressedBox(string type) => Format(type);
  public static string JxlHeaderExtensions => Get();
}
