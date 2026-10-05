# AGENTS.md

Image Optimizer is a lossless image optimizer for Windows: a WPF app on .NET 10, styled after ImageOptim.

## Layout

- `src/ImageOptimizer.Core`: cross-platform engine (format detection, optimizers, verification).
- `src/ImageOptimizer.App`: the WPF app (Windows only).
- `tests/`: xUnit tests. `ImageOptimizer.App.Tests` only runs on Windows.
- `scripts/fetch-tools.ps1`: downloads the bundled optimizer binaries into `tools/`.

## Rules

- Never replace an image unless the result is strictly smaller and (for bitmaps) pixel-identical. Otherwise leave the original untouched.
- Keep the main window free of options; settings belong in the Settings panel.
- Text people see lives in `Strings.resx` (English) with translations in `Strings.de.resx`, `Strings.es.resx`, `Strings.fr.resx`, `Strings.it.resx` and `Strings.ja.resx`, in both `src` projects. Add every new string to all of them, and read it through the project's `Strings` class.
- Follow `.editorconfig` (2-space indent) and keep `dotnet format whitespace --verify-no-changes` clean.

## Build and test

```powershell
./scripts/fetch-tools.ps1
dotnet build ImageOptimizer.sln
dotnet test ImageOptimizer.sln
```

## Git

- Squash each pull request to a single commit.
- Use meaningful branch names that describe the change, such as `add-avif-support` or `fix-jpeg-orientation`.
- Pin GitHub Actions to full commit SHAs, with the version in a trailing comment.
