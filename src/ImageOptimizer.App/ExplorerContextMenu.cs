using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using ImageOptimizer.Core;
using Microsoft.Win32;

namespace ImageOptimizer.App;

/// <summary>
/// The "Optimize with Image Optimizer" entry in File Explorer's right-click menu, for supported images and folders.
/// It's registered for the current user only (HKCU), so no admin rights are needed.
/// </summary>
/// <remarks>
/// Windows 11 shows classic entries like this one under "Show more options". Putting it in the new compact menu
/// would need a signed sparse package with a COM shell extension.
/// </remarks>
public static class ExplorerContextMenu
{
  public const string Label = "Optimize with Image Optimizer";
  private const string VerbName = "ImageOptimizer";
  private const string ClassesPath = @"Software\Classes";

  /// <summary>Registry keys (relative to Software\Classes) that hold the menu entry.</summary>
  internal static IEnumerable<string> VerbKeys =>
      ImageFormats.SupportedExtensions
          .Select(ext => $@"SystemFileAssociations\{ext}\shell\{VerbName}")
          .Append($@"Directory\shell\{VerbName}");

  private static string CurrentExe => Environment.ProcessPath ?? throw new InvalidOperationException("The app's path is unknown.");

  public static bool IsRegistered() => WithClasses(classes => IsRegistered(classes));

  public static void Register() => WithClasses(classes => { Register(classes, CurrentExe); return true; });

  public static void Unregister() => WithClasses(classes => { Unregister(classes); return true; });

  /// <summary>Re-registers after an update (new formats, a moved exe), but only if it's still turned on.</summary>
  public static void Refresh()
  {
    if (IsRegistered())
      Register();
  }

  /// <summary>Install and uninstall hooks must never fail, so registry problems are ignored there.</summary>
  public static void TryRun(Action action)
  {
    try
    {
      action();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
    {
    }
  }

  /// <summary>True if any part is there, so an update that adds a format still refreshes it.</summary>
  internal static bool IsRegistered(RegistryKey classes) =>
      VerbKeys.Any(path => { using var key = classes.OpenSubKey(path); return key is not null; });

  internal static void Register(RegistryKey classes, string exePath)
  {
    foreach (var path in VerbKeys)
    {
      using var verb = classes.CreateSubKey(path);
      verb.SetValue("MUIVerb", Label);
      verb.SetValue("Icon", $"\"{exePath}\",0");
      // Lets any number of files be selected. Explorer starts the app once per file; the copies after the
      // first hand their file to the window that's already open (see SingleInstance).
      verb.SetValue("MultiSelectModel", "Player");
      using var command = verb.CreateSubKey("command");
      command.SetValue(null, $"\"{exePath}\" \"%1\"");
    }
    NotifyShell();
  }

  internal static void Unregister(RegistryKey classes)
  {
    foreach (var path in VerbKeys)
    {
      classes.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
      DeleteEmptyParents(classes, path);
    }
    NotifyShell();
  }

  /// <summary>Removes the "shell" and ".ext" keys left behind if nothing else uses them.</summary>
  private static void DeleteEmptyParents(RegistryKey classes, string path)
  {
    for (var parent = Path.GetDirectoryName(path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
    {
      using (var key = classes.OpenSubKey(parent))
      {
        if (key is null)
          continue;
        if (key.SubKeyCount > 0 || key.ValueCount > 0)
          return;
      }
      classes.DeleteSubKey(parent, throwOnMissingSubKey: false);
    }
  }

  private static T WithClasses<T>(Func<RegistryKey, T> action)
  {
    using var classes = Registry.CurrentUser.CreateSubKey(ClassesPath);
    return action(classes);
  }

  private static void NotifyShell()
  {
    if (OperatingSystem.IsWindows())
      SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
  }

  private const int ShcneAssocChanged = 0x08000000;

  [DllImport("shell32.dll")]
  private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
