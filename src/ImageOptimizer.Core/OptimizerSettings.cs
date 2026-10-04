using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageOptimizer.Core;

public sealed record OptimizerSettings
{
  public bool OptimizePng { get; init; } = true;
  public bool OptimizeJpeg { get; init; } = true;
  public bool OptimizeWebP { get; init; } = true;
  public bool OptimizeGif { get; init; } = true;
  public bool OptimizeSvg { get; init; } = true;

  /// <summary>Removes comments, camera data and other metadata. Color profiles and orientation are always kept.</summary>
  public bool StripMetadata { get; init; } = true;

  /// <summary>Spends much more time searching for the smallest PNG and WebP encoding (Zopfli / maximum effort).</summary>
  public bool MaximumCompression { get; init; }

  /// <summary>Lets JPEGs be rewritten as progressive when that is smaller.</summary>
  public bool AllowProgressiveJpeg { get; init; } = true;

  /// <summary>Keeps each file's original "date modified" after it is replaced.</summary>
  public bool PreserveModifiedDate { get; init; } = true;

  public bool IsEnabled(ImageFormat format) => format switch
  {
    ImageFormat.Png => OptimizePng,
    ImageFormat.Jpeg => OptimizeJpeg,
    ImageFormat.WebP => OptimizeWebP,
    ImageFormat.Gif => OptimizeGif,
    ImageFormat.Svg => OptimizeSvg,
    _ => false,
  };

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
  };

  public static string DefaultPath => Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "ImageOptimizer",
      "settings.json");

  public static OptimizerSettings Load(string path)
  {
    try
    {
      if (File.Exists(path))
        return JsonSerializer.Deserialize<OptimizerSettings>(File.ReadAllText(path), JsonOptions) ?? new();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
      // A missing or corrupt settings file should never stop the app from working.
    }
    return new();
  }

  public void Save(string path)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
  }
}
