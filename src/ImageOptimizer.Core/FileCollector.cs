namespace ImageOptimizer.Core;

public static class FileCollector
{
  /// <summary>Expands dropped files and folders (recursively) into the image files they contain.</summary>
  public static IEnumerable<string> Collect(IEnumerable<string> paths)
  {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in paths)
    {
      IEnumerable<string> files;
      if (Directory.Exists(path))
      {
        files = EnumerateImages(path);
      }
      else if (File.Exists(path) && ImageFormats.HasSupportedExtension(path))
      {
        files = [path];
      }
      else
      {
        continue;
      }

      foreach (var file in files)
      {
        var full = Path.GetFullPath(file);
        if (seen.Add(full))
          yield return full;
      }
    }
  }

  private static IEnumerable<string> EnumerateImages(string directory)
  {
    var options = new EnumerationOptions
    {
      RecurseSubdirectories = true,
      IgnoreInaccessible = true,
      AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
    };
    return Directory.EnumerateFiles(directory, "*", options)
        .Where(ImageFormats.HasSupportedExtension)
        .Order(StringComparer.OrdinalIgnoreCase);
  }
}

public static class SizeFormatter
{
  private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB"];

  /// <summary>Formats a byte count the way Windows Explorer does (1 KB = 1024 bytes).</summary>
  public static string Format(long bytes)
  {
    if (bytes < 1024)
      return bytes == 1 ? "1 byte" : $"{bytes:N0} bytes";

    double value = bytes;
    var unit = 0;
    while (value >= 1024 && unit < Units.Length - 1)
    {
      value /= 1024;
      unit++;
    }
    var format = value >= 100 ? "N0" : "0.#";
    return $"{value.ToString(format, System.Globalization.CultureInfo.CurrentCulture)} {Units[unit]}";
  }

  public static string Percent(double ratio) =>
      ratio.ToString("0.0%", System.Globalization.CultureInfo.CurrentCulture);
}
