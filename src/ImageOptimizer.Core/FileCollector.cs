using System.IO.Enumeration;

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
      else if (File.Exists(path) && ImageFormats.HasSupportedExtension(path) && !ImageOptimizerEngine.IsStagingFile(path))
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
      AttributesToSkip = FileAttributes.System,
    };
    return new FileSystemEnumerable<string>(directory, (ref entry) => entry.ToFullPath(), options)
    {
      ShouldIncludePredicate = (ref entry) =>
          !entry.IsDirectory && !IsLink(ref entry) && ImageFormats.HasSupportedExtension(entry.FileName.ToString()) &&
          !ImageOptimizerEngine.IsStagingFile(entry.FileName.ToString()),
      ShouldRecursePredicate = (ref entry) => !IsLink(ref entry),
    }.Order(StringComparer.OrdinalIgnoreCase);
  }

  /// <summary>
  /// True for symbolic links and junctions, which are skipped so a folder can't be visited twice (or forever) and a
  /// linked file isn't replaced by a copy. OneDrive and other cloud files are reparse points too, but not links.
  /// </summary>
  private static bool IsLink(ref FileSystemEntry entry) =>
      (entry.Attributes & FileAttributes.ReparsePoint) != 0 && entry.ToFileSystemInfo().LinkTarget is not null;
}

public static class SizeFormatter
{
  /// <summary>Formats a byte count the way Windows Explorer does (1 KB = 1024 bytes).</summary>
  public static string Format(long bytes)
  {
    if (bytes < 1024)
      return bytes == 1 ? Strings.OneByte : Strings.Bytes(bytes);

    // KB, MB, GB and TB in the display language (French writes Ko, Mo, Go and To).
    var units = Strings.SizeUnits.Split(',');
    double value = bytes / 1024.0;
    var unit = 0;
    while (value >= 1024 && unit < units.Length - 1)
    {
      value /= 1024;
      unit++;
    }
    var format = value >= 100 ? "N0" : "0.#";
    return $"{value.ToString(format, System.Globalization.CultureInfo.CurrentCulture)} {units[unit]}";
  }

  public static string Percent(double ratio) =>
      ratio.ToString("0.0%", System.Globalization.CultureInfo.CurrentCulture);
}
