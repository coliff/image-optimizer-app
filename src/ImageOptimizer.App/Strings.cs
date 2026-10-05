using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace ImageOptimizer.App;

/// <summary>
/// The app's text, from Strings.resx (English) or a translation such as Strings.ja.resx, picked by the
/// Windows display language. Each member reads the resource with its own name.
/// </summary>
public static class Strings
{
  internal static ResourceManager ResourceManager { get; } = new("ImageOptimizer.App.Strings", typeof(Strings).Assembly);

  private static string Get([CallerMemberName] string name = "") =>
      ResourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;

  private static string Format(object? arg0, [CallerMemberName] string name = "") =>
      string.Format(CultureInfo.CurrentCulture, Get(name), arg0);

  private static string Format(object? arg0, object? arg1, [CallerMemberName] string name = "") =>
      string.Format(CultureInfo.CurrentCulture, Get(name), arg0, arg1);

  // Main window
  public static string FileColumn => Get();
  public static string SizeColumn => Get();
  public static string SavingsColumn => Get();
  public static string SortedAscending(string column) => Format(column);
  public static string SortedDescending(string column) => Format(column);
  public static string SortByName => Get();
  public static string SortBySize => Get();
  public static string SortBySavings => Get();
  public static string ShowInFolder => Get();
  public static string RemoveFromList => Get();
  public static string ClearFinished => Get();
  public static string DropImagesHere => Get();
  public static string DropImagesHint => Get();
  public static string AddImages => Get();
  public static string AddImagesToolTip => Get();
  public static string OpenFileFilter(string patterns) => Format(patterns);
  public static string Settings => Get();
  public static string SettingsToolTip => Get();
  public static string RestartToUpdate => Get();
  public static string UpdateReady(string version) => Format(version);
  public static string SettingsNotSaved(string error) => Format(error);

  // Footer summary
  public static string Progress(int current, int total) => Format(current, total);
  public static string NothingToSave => Get();
  public static string NothingOptimized => Get();
  public static string SavedTotal(string saved, string original, string overall, string best) =>
      string.Format(CultureInfo.CurrentCulture, Get(), saved, original, overall, best);
  public static string FilesFailed(int count) => count == 1 ? Get("OneFileFailed") : Format(count);

  // File rows
  public static string StatusWaiting => Get();
  public static string StatusOptimizing => Get();
  public static string StatusOptimized => Get();
  public static string StatusAlreadyOptimized => Get();
  public static string StatusSkipped => Get();
  public static string StatusFailed => Get();
  public static string ListSeparator => Get();
  public static string SavedPercent(string percent) => Format(percent);
  public static string ToolTipOptimizing => Get();
  public static string ToolTipSaved(string saved, string percent, string original) =>
      string.Format(CultureInfo.CurrentCulture, Get(), saved, percent, original);
  public static string ToolTipAlreadyOptimized => Get();
  public static string ToolTipLeftUntouched => Get();
  public static string ToolTipFailed(string reason) => Format(reason);

  // Settings window
  public static string RestoreDefaults => Get();
  public static string Cancel => Get();
  public static string Save => Get();
  public static string EnableHeader => Get();
  public static string OptionsHeader => Get();
  public static string StripMetadata => Get();
  public static string StripMetadataHint => Get();
  public static string MaximumCompression => Get();
  public static string MaximumCompressionHint => Get();
  public static string AllowProgressiveJpeg => Get();
  public static string PreserveModifiedDate => Get();
  public static string CheckForUpdates => Get();
  public static string ExplorerMenu => Get();
  public static string ExplorerMenuHint => Get();
  public static string ExplorerMenuLabel => Get();
  public static string ExplorerMenuNotChanged(string error) => Format(error);
  public static string SafetyNote => Get();
  public static string PoweredBy => Get();
  public static string Version(string version) => Format(version);
  public static string GitHubLinkName => Get();
}
