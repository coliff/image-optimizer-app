using Velopack;

namespace ImageOptimizer.App;

public static class Program
{
  [STAThread]
  public static void Main(string[] args)
  {
    // Handles the installer's and updater's hooks (which exit straight away) and applies an update
    // that was downloaded last time, before any window opens.
    VelopackApp.Build().Run();

    var app = new App();
    app.InitializeComponent();
    app.Run();
  }
}
