using System.IO;
using System.Xml.Linq;

namespace ImageOptimizer.App.Tests;

public class ThemeTests
{
  private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

  private static SortedSet<string> Keys(string theme)
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (!File.Exists(Path.Combine(dir.FullName, "ImageOptimizer.sln")))
      dir = dir.Parent ?? throw new DirectoryNotFoundException("Repository root not found.");
    var path = Path.Combine(dir.FullName, "src", "ImageOptimizer.App", "Themes", $"{theme}.xaml");
    return [.. XDocument.Load(path).Root!.Elements().Select(e => (string)e.Attribute(Xaml + "Key")!)];
  }

  [Theory]
  [InlineData("Dark")]
  [InlineData("HighContrast")]
  public void Every_theme_defines_the_same_brushes(string theme)
  {
    Assert.Equal(Keys("Light"), Keys(theme));
  }
}
