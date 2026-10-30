# Contributing

## Building

```powershell
./scripts/fetch-tools.ps1          # downloads the pinned optimizer binaries into ./tools
dotnet build ImageOptimizer.sln
dotnet test --solution ImageOptimizer.sln
dotnet publish src/ImageOptimizer.App -c Release -r win-x64 --self-contained true -o publish
./scripts/pack-msix.ps1 -Version 1.2.3 -Register  # optional: packs and installs it (needs Developer Mode)
```

Every push is built and tested on Windows by GitHub Actions, and the ready-to-run app and its Store package
are attached to the workflow run as the `ImageOptimizer-win-x64` and `ImageOptimizer-msix` artifacts.

The File Explorer entry (`src/ImageOptimizer.ShellMenu`) is a small C++ COM DLL that only works inside the
MSIX package; `pack-msix.ps1` builds it with CMake and Visual Studio's C++ tools.

The engine (`src/ImageOptimizer.Core`) is cross-platform, so its tests also run on Linux or macOS
with `oxipng`, `jpegtran`, `cwebp`/`dwebp`/`webpmux`, `avifenc`/`avifdec`, `cjxl`/`djxl` and `gifsicle` on the `PATH`.

The tests include [FsCheck](https://github.com/fscheck/FsCheck) property tests (`PropertyTests.cs`) that fuzz the
engine with random and damaged images, checking that nothing crashes and that a file is only ever replaced by a
smaller, verified copy.

## Releasing

Bump `<Version>` in `Directory.Build.props`, then open **Actions > Publish > Run workflow**. It uses that version
(or the one you type in), tags the commit `vX.Y.Z`, builds and tests it, packs the Microsoft Store package
(`ImageOptimizer-X.Y.Z-x64.msix`, made by `scripts/pack-msix.ps1`) and attaches it to a GitHub Release.
Pushing a `vX.Y.Z` tag yourself does the same.

Then, in [Partner Center](https://partner.microsoft.com/dashboard/apps-and-games/overview), start a new submission
for Image Optimizer, upload the `.msix` under **Packages** and submit it. The Store signs it, and once it passes
certification everyone's copy updates itself.

After publishing, the **VirusTotal** workflow uploads the package and the app's own exe and DLLs to
[VirusTotal](https://www.virustotal.com/) and lists any antivirus engines that flag them in the run summary, so
false positives show up before people hit them. It needs a free VirusTotal API key saved as the `VIRUSTOTAL_API_KEY`
repository secret (without one it skips the scan), and it never fails a release. To scan an earlier release, open
**Actions > VirusTotal > Run workflow**.
