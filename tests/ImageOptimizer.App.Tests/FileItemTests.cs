using ImageOptimizer.Core;

namespace ImageOptimizer.App.Tests;

public class FileItemTests
{
  [Fact]
  public void Accessible_name_says_what_the_status_icon_shows()
  {
    var item = new FileItem(@"C:\images\photo.png");
    Assert.Equal("photo.png, Waiting", item.AccessibleName);

    item.Apply(new OptimizationResult(OptimizationStatus.Optimized, ImageFormat.Png, 1000, 500));
    Assert.StartsWith("photo.png, Optimized, ", item.AccessibleName);
    Assert.EndsWith($", saved {item.SavingsText}", item.AccessibleName);

    item.Apply(new OptimizationResult(OptimizationStatus.Failed, ImageFormat.Png, 0, 0, "Broken file."));
    Assert.Equal("photo.png, Failed", item.AccessibleName);
  }
}
