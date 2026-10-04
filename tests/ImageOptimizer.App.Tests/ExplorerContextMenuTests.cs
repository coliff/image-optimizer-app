using Microsoft.Win32;

namespace ImageOptimizer.App.Tests;

public sealed class ExplorerContextMenuTests : IDisposable
{
  // A throwaway stand-in for HKCU\Software\Classes, so the tests never touch the real Explorer menu.
  private readonly string _rootPath = $@"Software\ImageOptimizerTests\{Guid.NewGuid():N}";
  private readonly RegistryKey _classes;

  public ExplorerContextMenuTests()
  {
    _classes = Registry.CurrentUser.CreateSubKey(_rootPath);
  }

  public void Dispose()
  {
    _classes.Dispose();
    Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
  }

  [Fact]
  public void Registers_every_image_type_and_folders()
  {
    const string exe = @"C:\Users\me\AppData\Local\ImageOptimizer\current\ImageOptimizer.exe";
    Assert.False(ExplorerContextMenu.IsRegistered(_classes));

    ExplorerContextMenu.Register(_classes, exe);

    Assert.True(ExplorerContextMenu.IsRegistered(_classes));
    foreach (var path in new[] { @"SystemFileAssociations\.png", @"SystemFileAssociations\.jpg", @"SystemFileAssociations\.svg", "Directory" })
    {
      using var verb = _classes.OpenSubKey($@"{path}\shell\ImageOptimizer");
      Assert.NotNull(verb);
      Assert.Equal(ExplorerContextMenu.Label, verb.GetValue("MUIVerb"));
      Assert.Equal("Player", verb.GetValue("MultiSelectModel"));
      using var command = verb.OpenSubKey("command");
      Assert.Equal($"\"{exe}\" \"%1\"", command?.GetValue(null));
    }
  }

  [Fact]
  public void Unregistering_removes_only_its_own_keys()
  {
    using (var other = _classes.CreateSubKey(@"SystemFileAssociations\.png\shell\SomeOtherApp"))
      other.SetValue("MUIVerb", "Another app's entry");

    ExplorerContextMenu.Register(_classes, @"C:\ImageOptimizer.exe");
    ExplorerContextMenu.Unregister(_classes);

    Assert.False(ExplorerContextMenu.IsRegistered(_classes));
    using (var png = _classes.OpenSubKey(@"SystemFileAssociations\.png\shell"))
    {
      Assert.NotNull(png);
      Assert.Equal(["SomeOtherApp"], png.GetSubKeyNames());
    }
    // Keys only this app created are cleaned up too.
    Assert.Null(_classes.OpenSubKey(@"SystemFileAssociations\.jpg"));
    Assert.Null(_classes.OpenSubKey("Directory"));
  }
}
