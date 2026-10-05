# Image Optimizer

[![Build](https://github.com/coliff/image-optimizer-app/actions/workflows/build.yml/badge.svg)](https://github.com/coliff/image-optimizer-app/actions/workflows/build.yml)
[![Lint](https://github.com/coliff/image-optimizer-app/actions/workflows/lint.yml/badge.svg)](https://github.com/coliff/image-optimizer-app/actions/workflows/lint.yml)
[![Latest release](https://img.shields.io/github/v/release/coliff/image-optimizer-app)](https://github.com/coliff/image-optimizer-app/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/coliff/image-optimizer-app/total)](https://github.com/coliff/image-optimizer-app/releases)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/coliff/image-optimizer-app/badge)](https://scorecard.dev/viewer/?uri=github.com/coliff/image-optimizer-app)
[![License: MIT](https://img.shields.io/github/license/coliff/image-optimizer-app)](LICENSE)

A lightweight, fast, lossless image optimizer for Windows, inspired by [ImageOptim](https://imageoptim.com/) on the Mac.

Drop AVIF, GIF, JPEG, JPEG XL, PNG, SVG and WebP files (or whole folders) onto the window. Each file is optimized and
**replaced in place**. There's nothing to configure: the defaults are safe, and a small Settings panel
(gear button) is there if you want it.

You can also right-click images or folders in File Explorer and choose **Optimize with Image Optimizer**
(on Windows 11 it's under **Show more options**). Selecting many files at once opens them all in one window.

Click the File, Size or Savings header to sort the list (click again to reverse it).
The app follows the Windows light or dark app mode automatically, and uses your High Contrast colors when a contrast theme is on.
It's in English, French, German, Italian, Japanese and Spanish, and follows the Windows display language.

| Light                                                       | Dark                                                      |
| ----------------------------------------------------------- | --------------------------------------------------------- |
| ![Image Optimizer in light mode](docs/screenshot-light.png) | ![Image Optimizer in dark mode](docs/screenshot-dark.png) |

## How it stays lossless and safe

1. The original is copied to a private temporary folder; the optimizers only ever see that copy.
2. The format's optimizer writes one or more candidates (for JPEG, both baseline and progressive).
3. Candidates that aren't **strictly smaller** than the original are thrown away.
4. The smallest candidate is decoded and compared **pixel by pixel** with the original
   (Windows imaging for PNG/JPEG, `dwebp` for WebP, `avifdec` for AVIF, `djxl` for JPEG XL). Any difference and it's discarded.
5. If the file changed on disk in the meantime, nothing is written.
6. Only then is the original swapped for the candidate in a single `File.Replace` step.

If anything fails along the way, the original file is left exactly as it was.

| Format  | Tool                                                                          | What it does                                                                                                                                                                                                                                                  |
| ------- | ----------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AVIF    | `avifenc` / `avifdec` from [libavif](https://github.com/AOMediaCodec/libavif) | Lossless AVIFs are re-encoded losslessly at a higher effort (typically a few percent smaller). Lossy AVIFs are left untouched.                                                                                                                                |
| GIF     | [Gifsicle](https://www.lcdf.org/gifsicle/)                                    | `-O3` lossless frame and LZW optimization.                                                                                                                                                                                                                    |
| JPEG    | `jpegtran` from [libjpeg-turbo](https://libjpeg-turbo.org/)                   | Optimized Huffman tables and progressive encoding, without touching the image data.                                                                                                                                                                           |
| JPEG XL | `cjxl` / `djxl` from [libjxl](https://github.com/libjxl/libjxl)               | Lossless JPEG XLs are re-encoded losslessly at a higher effort (typically a few percent smaller). Recompressed JPEGs are rebuilt and recompressed, and still turn back into the exact original JPEG. Lossy, animated and rotated JPEG XLs are left untouched. |
| PNG     | [oxipng](https://github.com/oxipng/oxipng)                                    | Lossless filter, bit depth, palette and deflate optimization. Animated PNGs are left untouched.                                                                                                                                                               |
| SVG     | [SVGO](https://github.com/svg/svgo)                                           | SVGO's default preset, run in the built-in [Jint](https://github.com/sebastienros/jint) engine (no Node.js needed). SVGs are text, so they aren't pixel-compared.                                                                                             |
| WebP    | `cwebp` / `webpmux` from [libwebp](https://developers.google.com/speed/webp)  | Lossless WebPs are re-encoded losslessly. Lossy WebPs are never re-encoded, only stripped of metadata.                                                                                                                                                        |

By default metadata (EXIF, comments, camera info) is removed, but color profiles and JPEG
orientation are always kept so images look exactly the same.

## Settings

- Formats to optimize (AVIF, GIF, JPEG, JPEG XL, PNG, SVG, WebP)
- Remove metadata (on by default)
- Maximum compression: Zopfli for PNG, maximum effort for WebP, AVIF and JPEG XL, multipass SVGO (off by default; much slower)
- Use progressive JPEG when it's smaller (on by default)
- Keep each file's original "Date modified" (on by default)
- Check for updates when the app starts (on by default)
- Add "Optimize with Image Optimizer" to File Explorer's right-click menu (on by default when installed)

Settings are stored in `%AppData%\ImageOptimizer\settings.json`.

## Install

Download `ImageOptimizer-win-Setup.exe` from the [latest release](https://github.com/coliff/image-optimizer-app/releases/latest)
and run it. It installs for your user account only (no admin prompt), adds Start menu and desktop shortcuts,
and installs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) first if it's missing.
It also adds the File Explorer right-click entry for your account. Uninstall it from Windows Settings > Apps, which
removes the entry too.

When the app starts it quietly checks for a newer release and downloads it in the background. Once it's ready,
a **Restart to update** button appears in the footer; if you'd rather carry on, the update installs the next
time you open the app. You can turn this off in Settings.

Requires Windows 10 or 11 (x64).

### If your antivirus blocks it

The installer and app aren't code-signed yet, and the app rewrites many image files in quick succession and runs
bundled command-line tools, which some antivirus behavior checks mistake for ransomware. If yours blocks or closes it,
add the install folder (`%LocalAppData%\ImageOptimizer`) to its exceptions and report the false positive to the vendor
(for Trend Micro, [submit the file for analysis](https://helpcenter.trendlife.com/en-us/article/tmka-14388); on a work
PC, ask your IT team).

## Releasing

Bump `<Version>` in `Directory.Build.props`, then open **Actions > Publish > Run workflow**. It uses that version
(or the one you type in), tags the commit `vX.Y.Z`, builds and tests it, packs the installer and update packages with
[Velopack](https://velopack.io/), and publishes them as a GitHub Release. Installed copies pick it up on their next launch.
Pushing a `vX.Y.Z` tag yourself does the same.

After publishing, the **VirusTotal** workflow uploads the installer and the app's own exe and DLL to
[VirusTotal](https://www.virustotal.com/) and lists any antivirus engines that flag them in the run summary, so
false positives show up before people hit them. It needs a free VirusTotal API key saved as the `VIRUSTOTAL_API_KEY`
repository secret (without one it skips the scan), and it never fails a release. To scan an earlier release, open
**Actions > VirusTotal > Run workflow**.

## Building

```powershell
./scripts/fetch-tools.ps1          # downloads the pinned optimizer binaries into ./tools
dotnet build ImageOptimizer.sln
dotnet test ImageOptimizer.sln
dotnet publish src/ImageOptimizer.App -c Release -r win-x64 --self-contained false -o publish
```

Every push is built and tested on Windows by GitHub Actions, and the ready-to-run app and its installer
are attached to the workflow run as the `ImageOptimizer-win-x64` and `ImageOptimizer-win-Setup` artifacts.

The engine (`src/ImageOptimizer.Core`) is cross-platform, so its tests also run on Linux or macOS
with `oxipng`, `jpegtran`, `cwebp`/`dwebp`/`webpmux`, `avifenc`/`avifdec`, `cjxl`/`djxl` and `gifsicle` on the `PATH`.

The tests include [FsCheck](https://github.com/fscheck/FsCheck) property tests (`PropertyTests.cs`) that fuzz the
engine with random and damaged images, checking that nothing crashes and that a file is only ever replaced by a
smaller, verified copy.

## License

Image Optimizer is released under the [MIT License](LICENSE). The bundled tools keep their own
licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
