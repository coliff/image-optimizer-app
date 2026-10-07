<#
.SYNOPSIS
  Downloads the open-source optimizer binaries that Image Optimizer bundles into ./tools.

.DESCRIPTION
  All tools are official builds, pinned to the versions below:
   oxipng         PNG     https://github.com/oxipng/oxipng
   libjpeg-turbo  JPEG    https://github.com/libjpeg-turbo/libjpeg-turbo   (jpegtran)
   libwebp        WebP    https://developers.google.com/speed/webp         (cwebp, dwebp, webpmux)
   libavif        AVIF    https://github.com/AOMediaCodec/libavif          (avifenc, avifdec; statically linked with libaom and dav1d)
   libjxl         JPEG XL https://github.com/libjxl/libjxl                 (cjxl, djxl and their DLLs, in tools\jxl)
   Gifsicle       GIF     https://www.lcdf.org/gifsicle/ (Windows builds by eternallybored.org)
  SVGO           SVG     https://github.com/svg/svgo (browser build from npm, run by the app's JavaScript engine)
#>
param(
  [string]$Destination = (Join-Path $PSScriptRoot '..\tools')
)

$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$OxipngVersion = '10.2.1'
$LibjpegTurboVersion = '3.2.0'
$LibwebpVersion = '1.6.0'
$LibavifVersion = '1.4.2'
# The codec versions the libavif release links in (its ext/aom.cmd and ext/dav1d.cmd), for their license texts.
$LibaomVersion = '3.14.1'
$Dav1dVersion = '1.5.3'
$LibjxlVersion = '0.12.0'
$GifsicleVersion = '1.95'
$SvgoVersion = '4.1.0'

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("imageoptimizer-tools-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
$Destination = [System.IO.Path]::GetFullPath($Destination)
if (Test-Path $Destination) { Remove-Item -Recurse -Force $Destination }
$licenses = Join-Path $Destination 'licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null

$headers = @{ 'User-Agent' = 'image-optimizer-build' }

function Save-Url([string]$Url, [string]$OutFile) {
  Write-Information "Downloading $Url"
  Invoke-WebRequest -Uri $Url -OutFile $OutFile -Headers $headers
  $hash = (Get-FileHash $OutFile -Algorithm SHA256).Hash
  Write-Information "  SHA256 $hash"
}

function Save-GitHubAsset([string]$Repo, [string]$Tag, [string]$Pattern, [string]$OutFile) {
  $apiHeaders = $headers.Clone()
  if ($env:GITHUB_TOKEN) { $apiHeaders['Authorization'] = "Bearer $env:GITHUB_TOKEN" }
  $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag" -Headers $apiHeaders
  $asset = $release.assets | Where-Object { $_.name -match $Pattern } | Select-Object -First 1
  if (-not $asset) {
    throw "No asset matching '$Pattern' in $Repo $Tag. Available: $($release.assets.name -join ', ')"
  }
  Save-Url $asset.browser_download_url $OutFile
}

function Expand-To([string]$Archive, [string]$Name) {
  $out = Join-Path $work $Name
  if ($Archive.EndsWith('.zip')) {
    Expand-Archive -Path $Archive -DestinationPath $out -Force
  } else {
    & 7z x $Archive "-o$out" -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7-Zip failed to extract $Archive" }
  }
  return $out
}

function Copy-Tool([string]$From, [string]$FileName) {
  $file = Get-ChildItem -Path $From -Recurse -File -Filter $FileName | Select-Object -First 1
  if (-not $file) { throw "$FileName not found in $From" }
  Copy-Item $file.FullName $Destination -Force
  return $file.DirectoryName
}

function Copy-License([string]$From, [string]$Prefix) {
  Get-ChildItem -Path $From -Recurse -File |
    Where-Object { $_.Name -match '^(LICENSE|COPYING|README\.ijg)' } |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $licenses "$Prefix-$($_.Name)") -Force }
}

# oxipng
$archive = Join-Path $work 'oxipng.zip'
Save-GitHubAsset -Repo 'oxipng/oxipng' -Tag "v$OxipngVersion" -Pattern 'x86_64-pc-windows-msvc\.zip$' -OutFile $archive
$dir = Expand-To $archive 'oxipng'
Copy-Tool $dir 'oxipng.exe' | Out-Null
Copy-License $dir 'oxipng'

# libjpeg-turbo (the Visual C++ x64 installer is a 7-Zip readable NSIS package)
$archive = Join-Path $work 'libjpeg-turbo.exe'
Save-GitHubAsset -Repo 'libjpeg-turbo/libjpeg-turbo' -Tag $LibjpegTurboVersion -Pattern '-vc-?x?64\.exe$' -OutFile $archive
$dir = Expand-To $archive 'libjpeg-turbo'
$bin = Copy-Tool $dir 'jpegtran.exe'
Get-ChildItem -Path $bin -Filter 'jpeg*.dll' | ForEach-Object { Copy-Item $_.FullName $Destination -Force }
Copy-License $dir 'libjpeg-turbo'

# libwebp
$archive = Join-Path $work 'libwebp.zip'
Save-Url "https://storage.googleapis.com/downloads.webmproject.org/releases/webp/libwebp-$LibwebpVersion-windows-x64.zip" $archive
$dir = Expand-To $archive 'libwebp'
foreach ($tool in 'cwebp.exe', 'dwebp.exe', 'webpmux.exe') { Copy-Tool $dir $tool | Out-Null }
Copy-License $dir 'libwebp'
if (-not (Get-ChildItem $licenses -Filter 'libwebp-*')) {
  # The Windows zip doesn't include the license, so take it from the matching source tag.
  Save-Url "https://raw.githubusercontent.com/webmproject/libwebp/v$LibwebpVersion/COPYING" (Join-Path $licenses 'libwebp-COPYING')
}

# libavif (the Windows release has no license files, so take them from the matching sources)
$archive = Join-Path $work 'libavif.zip'
Save-GitHubAsset -Repo 'AOMediaCodec/libavif' -Tag "v$LibavifVersion" -Pattern '^windows-artifacts\.zip$' -OutFile $archive
$dir = Expand-To $archive 'libavif'
foreach ($tool in 'avifenc.exe', 'avifdec.exe') { Copy-Tool $dir $tool | Out-Null }
Save-Url "https://raw.githubusercontent.com/AOMediaCodec/libavif/v$LibavifVersion/LICENSE" (Join-Path $licenses 'libavif-LICENSE')
Save-Url "https://raw.githubusercontent.com/videolan/dav1d/$Dav1dVersion/COPYING" (Join-Path $licenses 'dav1d-COPYING')
$archive = Join-Path $work 'libaom.tar.gz'
Save-Url "https://storage.googleapis.com/aom-releases/libaom-$LibaomVersion.tar.gz" $archive
tar -xzf $archive -C $work "libaom-$LibaomVersion/LICENSE" "libaom-$LibaomVersion/PATENTS"
if ($LASTEXITCODE -ne 0) { throw "Failed to extract $archive" }
foreach ($file in 'LICENSE', 'PATENTS') { Copy-Item (Join-Path $work "libaom-$LibaomVersion/$file") (Join-Path $licenses "libaom-$file") -Force }

# libjxl (the Windows release links its DLLs dynamically. They go in their own folder because libjxl and
# libjpeg-turbo both ship a jpeg62.dll, and the tools load DLLs from their own folder first.)
$archive = Join-Path $work 'libjxl.zip'
Save-GitHubAsset -Repo 'libjxl/libjxl' -Tag "v$LibjxlVersion" -Pattern '^jxl-x64-windows\.zip$' -OutFile $archive
$dir = Expand-To $archive 'libjxl'
$jxlDestination = Join-Path $Destination 'jxl'
New-Item -ItemType Directory -Force -Path $jxlDestination | Out-Null
$jxlFiles = 'cjxl.exe', 'djxl.exe', 'jxl.dll', 'jxl_cms.dll', 'jxl_threads.dll', 'brotlicommon.dll', 'brotlidec.dll',
  'brotlienc.dll', 'libpng16.dll', 'zlib1.dll', 'jpeg62.dll', 'gif.dll'
foreach ($file in $jxlFiles) {
  $found = Get-ChildItem -Path $dir -Recurse -File -Filter $file | Select-Object -First 1
  if (-not $found) { throw "$file not found in $dir" }
  Copy-Item $found.FullName $jxlDestination -Force
}
Copy-License $dir 'libjxl'
# They also need the Visual C++ runtime, which isn't part of Windows, so ship it next to them (app-local
# deployment, as the Visual C++ Redistributable license allows), taken from Visual Studio's redist folder.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$crt = if (Test-Path $vswhere) {
  & $vswhere -latest -products * -find 'VC\Redist\MSVC\*\x64\Microsoft.VC*.CRT\msvcp140.dll' |
    Sort-Object | Select-Object -Last 1 | Split-Path
}
if (-not $crt) { throw 'The Visual C++ runtime (VC\Redist\MSVC) was not found. Install Visual Studio or its Build Tools with the C++ workload.' }
Write-Information "Visual C++ runtime from $crt"
foreach ($file in 'msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll') {
  Copy-Item (Join-Path $crt $file) $jxlDestination -Force
}

# Gifsicle
$archive = Join-Path $work 'gifsicle.zip'
Save-Url "https://eternallybored.org/misc/gifsicle/releases/gifsicle-$GifsicleVersion-win64.zip" $archive
$dir = Expand-To $archive 'gifsicle'
Copy-Tool $dir 'gifsicle.exe' | Out-Null
Copy-License $dir 'gifsicle'

# SVGO (its self-contained browser build runs in the app's embedded JavaScript engine)
$archive = Join-Path $work 'svgo.tgz'
Save-Url "https://registry.npmjs.org/svgo/-/svgo-$SvgoVersion.tgz" $archive
$dir = Join-Path $work 'svgo'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
tar -xzf $archive -C $dir
if ($LASTEXITCODE -ne 0) { throw "Failed to extract $archive" }
Copy-Tool $dir 'svgo.browser.js' | Out-Null
Copy-License $dir 'svgo'

Remove-Item -Recurse -Force $work

# Smoke test: every tool must start.
$checks = @(
  @('oxipng.exe', '--version'),
  @('jpegtran.exe', '-version'),
  @('cwebp.exe', '-version'),
  @('dwebp.exe', '-version'),
  @('webpmux.exe', '-version'),
  @('avifenc.exe', '--version'),
  @('avifdec.exe', '--version'),
  @('jxl\cjxl.exe', '--version'),
  @('jxl\djxl.exe', '--version'),
  @('gifsicle.exe', '--version')
)
foreach ($check in $checks) {
  $exe = Join-Path $Destination $check[0]
  $output = & $exe $check[1] 2>&1 | Select-Object -First 1
  if ($LASTEXITCODE -ne 0) { throw "$($check[0]) failed to run (exit code $LASTEXITCODE): $output" }
  Write-Information ("{0,-14} {1}" -f $check[0], $output)
}

Write-Information "Tools ready in $Destination"
Get-ChildItem $Destination -Recurse -File | ForEach-Object { Write-Information ("  {0,-40} {1,10:N0}" -f $_.FullName.Substring($Destination.Length + 1), $_.Length) }
