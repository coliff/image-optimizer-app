namespace ImageOptimizer.App;

public static class Program
{
  [STAThread]
  public static void Main(string[] args)
  {
    // Opening the app again (or Explorer's menu splitting a huge selection) starts more copies, so all but the
    // first pass their files on and exit.
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
