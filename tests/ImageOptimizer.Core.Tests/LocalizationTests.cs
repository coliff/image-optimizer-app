namespace ImageOptimizer.Core.Tests;

public class LocalizationTests
{
  [Theory]
  [MemberData(nameof(Translations.Languages), MemberType = typeof(Translations))]
  public void Every_message_is_translated(string language) =>
      Translations.AssertComplete(Strings.ResourceManager, language);

  [Fact]
  public void Every_message_exists() =>
      Translations.AssertMembersExist(typeof(Strings), Strings.ResourceManager);

  [Fact]
  public void Messages_follow_the_display_language() => Translations.InLanguage("ja-JP", () =>
  {
    Assert.Equal("ファイルが空です", Strings.FileEmpty);
    Assert.Equal("有効な JPEG XL ファイルではありません", Strings.InvalidFile(ImageFormat.JpegXl));
    Assert.Equal("1 バイト", SizeFormatter.Format(1));
  });
}
