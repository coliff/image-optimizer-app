using Velopack;

namespace ImageOptimizer.App;

public static class Program
{
  [STAThread]
  public static void Main(string[] args)
  {
    // Handles the installer's and updater's hooks (which exit straight away) and applies an update
    // that was downloaded last time, before any window opens.
    VelopackApp.Build()
        .OnAfterInstallFastCallback(_ => ExplorerContextMenu.TryRun(ExplorerContextMenu.Register))
        .OnAfterUpdateFastCallback(_ => ExplorerContextMenu.TryRun(ExplorerContextMenu.Refresh))
        .OnBeforeUninstallFastCallback(_ => ExplorerContextMenu.TryRun(ExplorerContextMenu.Unregister))
        .Run();

    // Explorer starts one copy per selected file, so all but the first pass their files on and exit.
    using var instance = new SingleInstance();
    if (instance.IsFirst)
      instance.Listen();
    else if (instance.TrySend(args, TimeSpan.FromSeconds(5)))
      return;

    var app = new App(instance.IsFirst ? instance : null);
    app.InitializeComponent();
    app.Run();
  }
}
