<#
.SYNOPSIS
  Packs a published build into a Velopack installer (ImageOptimizer-win-Setup.exe) and update packages.
.DESCRIPTION
  Setup.exe installs per user (no admin prompt), adds Start menu and desktop shortcuts, and installs the
  .NET 10 Desktop Runtime first if it's missing. The app then updates itself from GitHub Releases.
  Keep the vpk version in .config/dotnet-tools.json in step with the Velopack package in the app.
#>
param(
  [Parameter(Mandatory)] [string]$Version,
  [string]$PublishDir = 'publish',
  [string]$OutputDir = 'releases',
  # Downloads the latest release first so the new one also gets a small delta update package.
  [switch]$WithDeltas
)

$ErrorActionPreference = 'Stop'
$repoUrl = 'https://github.com/coliff/image-optimizer-app'

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed' }

if ($WithDeltas) {
  dotnet vpk download github --repoUrl $repoUrl --channel win --outputDir $OutputDir --token $env:GITHUB_TOKEN
  if ($LASTEXITCODE -ne 0) { throw 'Downloading the previous release failed' }
}

dotnet vpk pack `
  --packId ImageOptimizer `
  --packVersion $Version `
  --packTitle 'Image Optimizer' `
  --packAuthors 'Christian Oliff' `
  --packDir $PublishDir `
  --mainExe ImageOptimizer.exe `
  --icon src/ImageOptimizer.App/Assets/ImageOptimizer.ico `
  --runtime win-x64 `
  --channel win `
  --framework net10.0-x64-desktop `
  --outputDir $OutputDir
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }
