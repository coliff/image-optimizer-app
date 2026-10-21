using System.IO;
using System.Xml.Linq;
using ImageOptimizer.Core;
using Microsoft.Win32;

namespace ImageOptimizer.App.Tests;

public sealed class ExplorerContextMenuTests : IDisposable
{
  // A throwaway stand-in for HKCU\Software\ImageOptimizer, so the tests never touch the real setting.
  private readonly string _rootPath = $@"Software\ImageOptimizerTests\{Guid.NewGuid():N}";
  private readonly RegistryKey _settings;

  public ExplorerContextMenuTests()
  {
    _settings = Registry.CurrentUser.CreateSubKey(_rootPath);
  }

  public void Dispose()
  {
    _settings.Dispose();
    Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
  }

  [Fact]
  public void Is_on_until_turned_off()
  {
    Assert.True(ExplorerContextMenu.IsEnabled(_settings));

    ExplorerContextMenu.SetEnabled(_settings, false);
    Assert.False(ExplorerContextMenu.IsEnabled(_settings));
    // The menu DLL reads a DWORD, so it must be stored as one.
    Assert.Equal(RegistryValueKind.DWord, _settings.GetValueKind(ExplorerContextMenu.EnabledValue));

    ExplorerContextMenu.SetEnabled(_settings, true);
    Assert.True(ExplorerContextMenu.IsEnabled(_settings));
  }

  [Fact]
  public void Package_manifest_adds_the_menu_to_every_image_type_and_folders()
  {
    var manifest = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "ImageOptimizer.Package", "Package.appxmanifest"));
    XNamespace desktop5 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/5";
    XNamespace com = "http://schemas.microsoft.com/appx/manifest/com/windows10";

    var itemTypes = manifest.Descendants(desktop5 + "ItemType").ToList();
    Assert.Equal(
        ImageFormats.SupportedExtensions.Append("Directory").Order(StringComparer.OrdinalIgnoreCase),
        itemTypes.Select(x => (string)x.Attribute("Type")!).Order(StringComparer.OrdinalIgnoreCase));

    // Every verb points at the COM class the package registers for the menu DLL.
    var clsid = (string)manifest.Descendants(com + "Class").Single().Attribute("Id")!;
    Assert.All(itemTypes, x => Assert.Equal(clsid, (string)x.Element(desktop5 + "Verb")!.Attribute("Clsid")!));
  }

  private static string RepositoryRoot()
  {
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
      if (File.Exists(Path.Combine(dir.FullName, "ImageOptimizer.sln")))
        return dir.FullName;
    }
    throw new InvalidOperationException("The repository root wasn't found.");
  }
}
