using Velopack;
using Velopack.Sources;

namespace ImageOptimizer.App;

/// <summary>Finds and downloads new releases from GitHub. Only the installed app updates itself.</summary>
public sealed class UpdateChecker
{
  private const string RepositoryUrl = "https://github.com/coliff/image-optimizer-app";

  private UpdateManager? _manager;
  private VelopackAsset? _downloaded;

  /// <summary>Downloads the latest release if it's newer, and returns its version (or null if there's nothing to install).</summary>
  public async Task<string?> DownloadLatestAsync()
  {
    try
    {
      var manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));

      // Running from a build or publish folder rather than the installer.
      if (!manager.IsInstalled)
        return null;

      var update = await manager.CheckForUpdatesAsync();
      if (update is null)
        return null;

      await manager.DownloadUpdatesAsync(update);
      _manager = manager;
      _downloaded = update.TargetFullRelease;
      return _downloaded.Version.ToString();
    }
    catch (Exception)
    {
      // Offline, rate limited or GitHub is down: try again next time the app starts.
      return null;
    }
  }

  /// <summary>Closes the app, installs the downloaded update and opens the new version.</summary>
  public void RestartToUpdate()
  {
    if (_manager is not null && _downloaded is not null)
      _manager.ApplyUpdatesAndRestart(_downloaded);
  }
}
