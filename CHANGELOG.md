# Changelog

All notable changes to Image Optimizer Plus are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-02

First stable release, available from the [Microsoft Store](https://apps.microsoft.com/detail/9pg9rcdcgp9x).

### Changed

- Updated gifsicle to 1.97 ([#69](https://github.com/coliff/image-optimizer-app/pull/69)).
- Updated the CodeQL and cspell GitHub Actions ([#68](https://github.com/coliff/image-optimizer-app/pull/68)), and Dependabot now groups updates and limits its open pull requests.

## [0.9.5] - 2026-10-30

### Added

- A setting to ignore `node_modules` folders when a folder is added, on by default ([#62](https://github.com/coliff/image-optimizer-app/pull/62)).

### Changed

- Updated libjpeg-turbo to 3.2.1, which includes a jpegtran fix plus security and hardening fixes ([#63](https://github.com/coliff/image-optimizer-app/pull/63)).
- Moved the Building and Releasing docs to CONTRIBUTING.md ([#65](https://github.com/coliff/image-optimizer-app/pull/65)).

## [0.9.4] - 2026-10-27

### Changed

- Renamed the app to Image Optimizer Plus, the name reserved in Partner Center ([#59](https://github.com/coliff/image-optimizer-app/pull/59)).
- Linked to the Microsoft Store listing and refreshed the docs screenshots ([#60](https://github.com/coliff/image-optimizer-app/pull/60), [#61](https://github.com/coliff/image-optimizer-app/pull/61)).

## [0.9.3] - 2026-10-27

### Fixed

- A failed Publish run can be run again, and the Store upload is retried ([#57](https://github.com/coliff/image-optimizer-app/pull/57)).

## [0.9.2] - 2026-10-26

### Fixed

- The MSIX package uses the identity reserved in Partner Center so the Store accepts it ([#55](https://github.com/coliff/image-optimizer-app/pull/55)).

## [0.9.1] - 2026-10-26

### Fixed

- The package's publisher display name matches Partner Center ([#53](https://github.com/coliff/image-optimizer-app/pull/53)).

## [0.9.0] - 2026-10-26

### Added

- The app ships through the Microsoft Store as an MSIX package signed by Microsoft, replacing the unsigned installer ([#51](https://github.com/coliff/image-optimizer-app/pull/51)).
- The File Explorer menu entry is now a native command, so it also shows in the Windows 11 compact menu ([#51](https://github.com/coliff/image-optimizer-app/pull/51)).

### Changed

- The title bar shows the app's Store name ([#52](https://github.com/coliff/image-optimizer-app/pull/52)).
- Updated GitHub Actions and added an HTMLHint rule for the docs site ([#50](https://github.com/coliff/image-optimizer-app/pull/50)).

## [0.8.0] - 2026-10-08

### Added

- GIFs are checked frame by frame (transparency, disposal, interlacing, delays and loop count) before an optimized copy is kept ([#47](https://github.com/coliff/image-optimizer-app/pull/47)).
- A docs page languages card, a dark-mode gradient background, a fediverse creator tag, an og:image and an emoji favicon ([#43](https://github.com/coliff/image-optimizer-app/pull/43), [#44](https://github.com/coliff/image-optimizer-app/pull/44), [#49](https://github.com/coliff/image-optimizer-app/pull/49)).

### Changed

- Removing metadata from GIFs now also drops other programs' data such as XMP, unless the GIF carries a color profile ([#47](https://github.com/coliff/image-optimizer-app/pull/47)).
- Updated libjpeg-turbo to 3.2.0, Jint to 4.17.0 and the test packages ([#46](https://github.com/coliff/image-optimizer-app/pull/46)).
- Pinned the `package.json` version to 0.0.0; the app version lives in `Directory.Build.props` ([#48](https://github.com/coliff/image-optimizer-app/pull/48)).

### Fixed

- Clicking the GitHub link in Settings with no default browser no longer closes the app ([#47](https://github.com/coliff/image-optimizer-app/pull/47)).

## [0.7.8] - 2026-10-05

### Added

- FsCheck property tests that fuzz format detection, the container inspectors, the engine and SVGO ([#42](https://github.com/coliff/image-optimizer-app/pull/42)).

### Fixed

- A damaged Brotli-compressed box in a JPEG XL file is treated as invalid instead of showing a decoder error ([#42](https://github.com/coliff/image-optimizer-app/pull/42)).

## [0.7.7] - 2026-10-05

### Fixed

- Deeply nested SVGs are left untouched instead of crashing the app ([#41](https://github.com/coliff/image-optimizer-app/pull/41)).
- OneDrive and other cloud folders are no longer skipped when a folder is dropped ([#41](https://github.com/coliff/image-optimizer-app/pull/41)).
- WebP files keep EXIF data that rotates the image ([#41](https://github.com/coliff/image-optimizer-app/pull/41)).
- File sizes use Ko, Mo, Go and To in French, as Explorer does ([#41](https://github.com/coliff/image-optimizer-app/pull/41)).

## [0.7.6] - 2026-10-05

### Changed

- Settings are grouped into cards ([#40](https://github.com/coliff/image-optimizer-app/pull/40)).

### Fixed

- The Save button stays readable on hover and when pressed ([#40](https://github.com/coliff/image-optimizer-app/pull/40)).

## [0.7.5] - 2026-10-05

### Changed

- Release files are uploaded to VirusTotal with curl ([#39](https://github.com/coliff/image-optimizer-app/pull/39)).

## [0.7.4] - 2026-10-05

### Added

- Each release is scanned with VirusTotal ([#36](https://github.com/coliff/image-optimizer-app/pull/36)).

## [0.7.3] - 2026-10-05

### Fixed

- Optimizers are stopped when the app exits or crashes, so no tools are left running ([#35](https://github.com/coliff/image-optimizer-app/pull/35)).
- Files in progress are cancelled and cleaned up on close ([#35](https://github.com/coliff/image-optimizer-app/pull/35)).
- Selecting many files in Explorer during a slow first launch no longer opens a second window ([#35](https://github.com/coliff/image-optimizer-app/pull/35)).

## [0.7.2] - 2026-10-05

### Fixed

- Optimized files are staged under a name antivirus software won't mistake for ransomware encryption ([#34](https://github.com/coliff/image-optimizer-app/pull/34)).

## [0.7.1] - 2026-10-05

### Added

- Company, product and copyright details in every exe and DLL, and docs on antivirus false positives ([#33](https://github.com/coliff/image-optimizer-app/pull/33)).

## [0.7.0] - 2026-10-05

### Added

- French, Spanish, Italian and German translations ([#30](https://github.com/coliff/image-optimizer-app/pull/30)).
- Status badges in the README ([#32](https://github.com/coliff/image-optimizer-app/pull/32)).
- `private`, `funding` and `engines` fields in `package.json` ([#31](https://github.com/coliff/image-optimizer-app/pull/31)).

## [0.6.1] - 2026-10-05

### Fixed

- JPEGs with data after the end-of-image marker (motion photos, gain maps, stereo pairs) are skipped instead of losing that data ([#29](https://github.com/coliff/image-optimizer-app/pull/29)).
- PNGs keep EXIF data that rotates the image when metadata is stripped ([#29](https://github.com/coliff/image-optimizer-app/pull/29)).
- SVGs that aren't valid UTF-8 are skipped instead of being rewritten with replacement characters ([#29](https://github.com/coliff/image-optimizer-app/pull/29)).

## [0.6.0] - 2026-10-05

### Added

- Japanese localization ([#28](https://github.com/coliff/image-optimizer-app/pull/28)).
- A security policy ([#25](https://github.com/coliff/image-optimizer-app/pull/25)) and CODEOWNERS ([#26](https://github.com/coliff/image-optimizer-app/pull/26)).

### Fixed

- Access key underscores no longer show on flat buttons, and Alt+T restores defaults ([#27](https://github.com/coliff/image-optimizer-app/pull/27)).

## [0.5.0] - 2026-10-05

### Added

- A GitHub link in Settings ([#24](https://github.com/coliff/image-optimizer-app/pull/24)).
- ACCESSIBILITY.md ([#23](https://github.com/coliff/image-optimizer-app/pull/23)).

### Fixed

- The remaining accessibility gaps ([#23](https://github.com/coliff/image-optimizer-app/pull/23)).

## [0.4.0] - 2026-10-04

### Added

- Scroll bars follow the light, dark and High Contrast themes ([#22](https://github.com/coliff/image-optimizer-app/pull/22)).
- An OpenSSF Scorecard workflow ([#21](https://github.com/coliff/image-optimizer-app/pull/21)).

### Changed

- Clearer "Enable" labels for the format toggles in Settings ([#21](https://github.com/coliff/image-optimizer-app/pull/21)).
- Refreshed the screenshots ([#20](https://github.com/coliff/image-optimizer-app/pull/20)).

## [0.3.0] - 2026-10-04

### Added

- A docs homepage ([#16](https://github.com/coliff/image-optimizer-app/pull/16), [#18](https://github.com/coliff/image-optimizer-app/pull/18)).
- A VS Code extension recommendation for EditorConfig ([#19](https://github.com/coliff/image-optimizer-app/pull/19)).

### Changed

- The format checkboxes in Settings are a vertical list beside the options ([#17](https://github.com/coliff/image-optimizer-app/pull/17)).

## [0.2.0] - 2026-10-04

### Added

- Lossless JPEG XL optimization ([#12](https://github.com/coliff/image-optimizer-app/pull/12)).
- Lossless AVIF optimization ([#5](https://github.com/coliff/image-optimizer-app/pull/5)).
- A per-user installer with an update check on launch ([#6](https://github.com/coliff/image-optimizer-app/pull/6)).
- "Optimize with Image Optimizer" in File Explorer's right-click menu ([#7](https://github.com/coliff/image-optimizer-app/pull/7)).
- The app version at the bottom of Settings ([#8](https://github.com/coliff/image-optimizer-app/pull/8)).
- A one-click Publish workflow ([#9](https://github.com/coliff/image-optimizer-app/pull/9)).
- Windows High Contrast support and better screen reader and keyboard access ([#13](https://github.com/coliff/image-optimizer-app/pull/13), [#14](https://github.com/coliff/image-optimizer-app/pull/14)).
- Prettier and cspell scripts and a spell check workflow ([#2](https://github.com/coliff/image-optimizer-app/pull/2), [#3](https://github.com/coliff/image-optimizer-app/pull/3), [#15](https://github.com/coliff/image-optimizer-app/pull/15)).

### Changed

- Each file keeps its original date modified by default ([#4](https://github.com/coliff/image-optimizer-app/pull/4)).
- Supported formats are sorted alphabetically ([#11](https://github.com/coliff/image-optimizer-app/pull/11)).

### Removed

- The Again button ([#10](https://github.com/coliff/image-optimizer-app/pull/10)).

## [0.1.0] - 2026-10-04

### Added

- First version: a lightweight, lossless image optimizer for Windows. Drop PNG, JPEG, WebP, GIF or SVG files or folders on the window and each file is optimized in place, kept only if it is smaller and its pixels match the original ([#1](https://github.com/coliff/image-optimizer-app/pull/1)).
