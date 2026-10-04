using System.Reflection;
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
    SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);

    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
    Credits.Text = $"Image Optimizer {version}. Powered by oxipng, libjpeg-turbo, libwebp, libavif, Gifsicle and SVGO.";
  }

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
  }

  private void OnRestoreDefaults(object sender, RoutedEventArgs e) => Load(new OptimizerSettings());

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
    };
    DialogResult = true;
  }
}
