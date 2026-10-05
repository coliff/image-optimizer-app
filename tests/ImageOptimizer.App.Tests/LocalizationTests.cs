using ImageOptimizer.Core;
using ImageOptimizer.Core.Tests;

namespace ImageOptimizer.App.Tests;

public class LocalizationTests
{
  [Theory]
  [MemberData(nameof(Translations.Languages), MemberType = typeof(Translations))]
  public void Every_string_is_translated(string language) =>
      Translations.AssertComplete(Strings.ResourceManager, language);

  [Fact]
  public void Every_string_exists() =>
      Translations.AssertMembersExist(typeof(Strings), Strings.ResourceManager);

  [Fact]
  public void Text_follows_the_display_language() => Translations.InLanguage("ja-JP", () =>
  {
    var item = new FileItem(@"C:\images\photo.png");
    Assert.Equal("photo.png、待機中", item.AccessibleName);
    Assert.Equal("Image Optimizer で最適化", ExplorerContextMenu.Label);
  });
}
