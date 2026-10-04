using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace ImageOptimizer.App;

/// <summary>Follows the Windows light/dark app mode and High Contrast, including the window title bar.</summary>
public static class ThemeManager
{
  private const int DwmUseImmersiveDarkMode = 20;
  private static ResourceDictionary? _current;

  public static bool IsDark { get; private set; }

  /// <summary>True while Windows High Contrast is on, when the app uses the system's contrast colors.</summary>
  public static bool IsHighContrast { get; private set; }

  public static void Initialize(Application app)
  {
    Apply(app);
    SystemEvents.UserPreferenceChanged += (_, e) =>
    {
      if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        app.Dispatcher.BeginInvoke(() => Refresh(app));
    };
    // Raised on the UI thread once WPF has picked up a High Contrast switch.
    SystemParameters.StaticPropertyChanged += (_, e) =>
    {
      if (e.PropertyName == nameof(SystemParameters.HighContrast))
        Refresh(app);
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

  private static void Refresh(Application app)
  {
    if (SystemParameters.HighContrast != IsHighContrast || (!IsHighContrast && ReadSystemPrefersDark() != IsDark))
      Apply(app);
    else if (IsHighContrast)
      IsDark = IsDarkColor(SystemColors.WindowColor); // Switched contrast themes; the brushes follow by themselves.
    else
      return;
    foreach (Window window in app.Windows)
      ApplyTitleBar(window);
  }

  private static void Apply(Application app)
  {
    IsHighContrast = SystemParameters.HighContrast;
    IsDark = IsHighContrast ? IsDarkColor(SystemColors.WindowColor) : ReadSystemPrefersDark();
    var name = IsHighContrast ? "HighContrast" : IsDark ? "Dark" : "Light";
    var theme = new ResourceDictionary
    {
      Source = new Uri($"pack://application:,,,/ImageOptimizer;component/Themes/{name}.xaml"),
    };
    var dictionaries = app.Resources.MergedDictionaries;
    if (_current is not null)
      dictionaries.Remove(_current);
    dictionaries.Insert(0, theme);
    _current = theme;
  }

  private static bool IsDarkColor(Color color) => (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B) < 128;

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
