using Microsoft.Win32;

namespace ImageOptimizer.App;

/// <summary>
/// The "Optimize with Image Optimizer" entry in File Explorer's right-click menu, for supported images and folders.
/// </summary>
/// <remarks>
/// The MSIX package declares the entry in its manifest (src/ImageOptimizer.Package) and src/ImageOptimizer.ShellMenu
/// provides it, so it shows in Windows 11's compact menu. The app can't add or remove it, so turning it off in
/// Settings stores a flag that the menu reads to hide itself.
/// </remarks>
public static class ExplorerContextMenu
{
  /// <summary>In the display language, like the menu itself (scripts/pack-msix.ps1 builds the menu's text from this string).</summary>
  public static string Label => Strings.ExplorerMenuLabel;

  // Keep in step with SettingsKey and EnabledValue in src/ImageOptimizer.ShellMenu/ExplorerCommand.cpp.
  internal const string SettingsPath = @"Software\ImageOptimizer";
  internal const string EnabledValue = "ExplorerMenu";

  public static bool IsEnabled() => WithSettings(IsEnabled);

  public static void SetEnabled(bool enabled) => WithSettings(settings => { SetEnabled(settings, enabled); return true; });

  /// <summary>On unless it's been turned off.</summary>
  internal static bool IsEnabled(RegistryKey settings) => settings.GetValue(EnabledValue) is not int value || value != 0;

  internal static void SetEnabled(RegistryKey settings, bool enabled) =>
      settings.SetValue(EnabledValue, enabled ? 1 : 0, RegistryValueKind.DWord);

  private static T WithSettings<T>(Func<RegistryKey, T> action)
  {
    using var settings = Registry.CurrentUser.CreateSubKey(SettingsPath);
    return action(settings);
  }
}
