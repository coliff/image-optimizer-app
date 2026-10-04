using System.Windows;
using ImageOptimizer.Core;

namespace ImageOptimizer.App;

public partial class App : Application
{
  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);
    ThemeManager.Initialize(this);

    var engine = ImageOptimizerEngine.CreateDefault(new ToolRunner(), new WpfPixelVerifier());
    var viewModel = new MainViewModel(engine, OptimizerSettings.Load(OptimizerSettings.DefaultPath));
    var window = new MainWindow(viewModel);
    MainWindow = window;
    window.Show();

    // Files passed on the command line (e.g. dropped onto the .exe or a shortcut) start right away.
    if (e.Args.Length > 0)
      viewModel.Add(e.Args);
  }
}
