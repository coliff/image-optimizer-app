using System.IO;
using System.Reflection;
using System.Security;
using System.Windows;
using ImageOptimizer.Core;

namespace ImageOptimizer.App;

public partial class SettingsWindow : Window
{
  public SettingsWindow(OptimizerSettings settings)
  {
    InitializeComponent();
    Settings = settings;
    Load(settings);
    _explorerMenu = TryReadExplorerMenu();
    ExplorerMenu.IsChecked = _explorerMenu;
    SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

    // The release workflow builds with -p:Version from the vX.Y.Z tag, so this matches the installer.
    // Local builds show the <Version> from Directory.Build.props.
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
    VersionLabel.Text = version is null ? "" : $"Version {version}";
  }

  private readonly bool _explorerMenu;

  public OptimizerSettings Settings { get; private set; }

  private void Load(OptimizerSettings settings)
  {
    Png.IsChecked = settings.OptimizePng;
    Jpeg.IsChecked = settings.OptimizeJpeg;
    WebP.IsChecked = settings.OptimizeWebP;
    Avif.IsChecked = settings.OptimizeAvif;
    Gif.IsChecked = settings.OptimizeGif;
    Svg.IsChecked = settings.OptimizeSvg;
    StripMetadata.IsChecked = settings.StripMetadata;
    MaximumCompression.IsChecked = settings.MaximumCompression;
    AllowProgressiveJpeg.IsChecked = settings.AllowProgressiveJpeg;
    PreserveModifiedDate.IsChecked = settings.PreserveModifiedDate;
    CheckForUpdates.IsChecked = settings.CheckForUpdates;
  }

  private void OnRestoreDefaults(object sender, RoutedEventArgs e)
  {
    Load(new OptimizerSettings());
    ExplorerMenu.IsChecked = true;
  }

  private static bool TryReadExplorerMenu()
  {
    try
    {
      return ExplorerContextMenu.IsRegistered();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
    {
      return false;
    }
  }

  /// <summary>The Explorer menu lives in the registry rather than settings.json, so it's applied straight away.</summary>
  private void ApplyExplorerMenu()
  {
    var wanted = ExplorerMenu.IsChecked == true;
    if (wanted == _explorerMenu)
      return;
    try
    {
      if (wanted)
        ExplorerContextMenu.Register();
      else
        ExplorerContextMenu.Unregister();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
    {
      MessageBox.Show(this, $"The File Explorer menu couldn't be changed:\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void OnSave(object sender, RoutedEventArgs e)
  {
    Settings = new OptimizerSettings
    {
      OptimizePng = Png.IsChecked == true,
      OptimizeJpeg = Jpeg.IsChecked == true,
      OptimizeWebP = WebP.IsChecked == true,
      OptimizeAvif = Avif.IsChecked == true,
      OptimizeGif = Gif.IsChecked == true,
      OptimizeSvg = Svg.IsChecked == true,
      StripMetadata = StripMetadata.IsChecked == true,
      MaximumCompression = MaximumCompression.IsChecked == true,
      AllowProgressiveJpeg = AllowProgressiveJpeg.IsChecked == true,
      PreserveModifiedDate = PreserveModifiedDate.IsChecked == true,
      CheckForUpdates = CheckForUpdates.IsChecked == true,
    };
    ApplyExplorerMenu();
    DialogResult = true;
  }
}
