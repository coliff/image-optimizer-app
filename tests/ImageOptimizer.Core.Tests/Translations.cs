using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;

namespace ImageOptimizer.Core.Tests;

/// <summary>Checks for the Strings.resx translations, shared by the Core and App tests.</summary>
internal static partial class Translations
{
  public static TheoryData<string> Languages => ["de", "es", "fr", "it", "ja"];

  /// <summary>The translation has every English string with the same placeholders, and nothing else.</summary>
  public static void AssertComplete(ResourceManager resources, string language)
  {
    var english = Read(resources, CultureInfo.InvariantCulture);
    var translated = Read(resources, CultureInfo.GetCultureInfo(language));
    Assert.NotEmpty(english);
    Assert.Equal(english.Keys.Order(), translated.Keys.Order());
    foreach (var (name, text) in english)
    {
      Assert.False(string.IsNullOrWhiteSpace(translated[name]), $"{language} {name} is empty");
      Assert.True(Placeholders(text).SetEquals(Placeholders(translated[name])), $"{language} {name} has different placeholders");
    }
  }

  /// <summary>Every public member of a Strings class reads a string that exists.</summary>
  public static void AssertMembersExist(Type strings, ResourceManager resources)
  {
    var english = Read(resources, CultureInfo.InvariantCulture);
    var names = strings.GetProperties(BindingFlags.Public | BindingFlags.Static).Select(p => p.Name)
        .Concat(strings.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => !m.IsSpecialName).Select(m => m.Name))
        .ToList();
    Assert.NotEmpty(names);
    Assert.All(names, name => Assert.Contains(name, english.Keys));
  }

  /// <summary>Runs <paramref name="action"/> as if Windows were set to <paramref name="language"/>.</summary>
  public static void InLanguage(string language, Action action)
  {
    var previous = CultureInfo.CurrentUICulture;
    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
    try
    {
      action();
    }
    finally
    {
      CultureInfo.CurrentUICulture = previous;
    }
  }

  private static Dictionary<string, string> Read(ResourceManager resources, CultureInfo culture)
  {
    var set = resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
    Assert.NotNull(set);
    return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
  }

  private static HashSet<string> Placeholders(string text) => [.. Placeholder().Matches(text).Select(m => m.Value)];

  [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
  private static partial Regex Placeholder();
}
