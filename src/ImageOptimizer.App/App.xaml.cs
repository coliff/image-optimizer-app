using System.Windows;
using ImageOptimizer.Core;

namespace ImageOptimizer.App;

public partial class App : Application
{
  private readonly UpdateChecker _updates = new();
  private readonly SingleInstance? _instance;

  /// <param name="instance">Receives files from later launches, or null if another copy already has that job.</param>
  public App(SingleInstance? instance = null)
  {
    _instance = instance;
  }

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);
    ThemeManager.Initialize(this);

    var engine = ImageOptimizerEngine.CreateDefault(new ToolRunner(), new WpfPixelVerifier());
    var viewModel = new MainViewModel(engine, OptimizerSettings.Load(OptimizerSettings.DefaultPath));
    var window = new MainWindow(viewModel, _updates);
    MainWindow = window;
    window.Show();

    // Files passed on the command line (e.g. dropped onto the .exe or a shortcut) start right away.
    if (e.Args.Length > 0)
      viewModel.Add(e.Args);

    // Files opened from Explorer's right-click menu (or the app opened again) arrive from the other copies.
    _instance?.Listen(paths => Dispatcher.BeginInvoke(() =>
    {
      if (paths.Length > 0)
        viewModel.Add(paths);
      window.BringToFront();
    }));

    if (viewModel.Settings.CheckForUpdates)
      _ = DownloadUpdateAsync(viewModel);
  }

  /// <summary>Quietly downloads a newer release in the background and offers it in the footer when it's ready.</summary>
  private async Task DownloadUpdateAsync(MainViewModel viewModel)
  {
    var version = await Task.Run(_updates.DownloadLatestAsync);
    if (version is not null)
      viewModel.UpdateVersion = version;
  }
}
