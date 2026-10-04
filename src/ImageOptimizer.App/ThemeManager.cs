using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace ImageOptimizer.App;

/// <summary>Follows the Windows light/dark app mode, including the window title bar.</summary>
public static class ThemeManager
{
  private const int DwmUseImmersiveDarkMode = 20;
  private static ResourceDictionary? _current;

  public static bool IsDark { get; private set; }

  public static void Initialize(Application app)
  {
    Apply(app, ReadSystemPrefersDark());
    SystemEvents.UserPreferenceChanged += (_, e) =>
    {
      if (e.Category != UserPreferenceCategory.General)
        return;
      app.Dispatcher.Invoke(() =>
      {
        var dark = ReadSystemPrefersDark();
        if (dark == IsDark)
          return;
        Apply(app, dark);
        foreach (Window window in app.Windows)
          ApplyTitleBar(window);
      });
    };
  }

  public static void ApplyTitleBar(Window window)
  {
    var handle = new WindowInteropHelper(window).Handle;
    if (handle == IntPtr.Zero)
      return;
    var value = IsDark ? 1 : 0;
    _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int));
  }

  private static void Apply(Application app, bool dark)
  {
    IsDark = dark;
    var theme = new ResourceDictionary
    {
      Source = new Uri($"pack://application:,,,/ImageOptimizer;component/Themes/{(dark ? "Dark" : "Light")}.xaml"),
    };
    var dictionaries = app.Resources.MergedDictionaries;
    if (_current is not null)
      dictionaries.Remove(_current);
    dictionaries.Insert(0, theme);
    _current = theme;
  }

  private static bool ReadSystemPrefersDark()
  {
    try
    {
      using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
      return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }
    catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
    {
      return false;
    }
  }

  [DllImport("dwmapi.dll")]
  private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
