<#
.SYNOPSIS
  Launches Image Optimizer on a copy of some images and saves a screenshot of its window,
  or of the Settings window with -Settings. Used by CI so every build has a picture of the real UI.
#>
param(
  [Parameter(Mandatory)] [string]$App,
  [Parameter(Mandatory)] [string]$Images,
  [Parameter(Mandatory)] [string]$Output,
  [ValidateSet('Light', 'Dark')] [string]$Theme = 'Light',
  [switch]$Settings,
  [int]$WaitSeconds = 8
)

$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeWindow {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(int action, int param, string value, int flags);
  [DllImport("user32.dll")] public static extern bool SetSysColors(int count, int[] elements, int[] colors);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT rect, int size);
}
'@

# The app follows the Windows "app mode" setting, so flip it for this run.
$personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
if (-not (Test-Path $personalize)) { New-Item -Path $personalize -Force | Out-Null }
Set-ItemProperty -Path $personalize -Name AppsUseLightTheme -Value ([int]($Theme -eq 'Light')) -Type DWord

# Work on copies so the originals in the repo are never modified.
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("imageoptimizer-screenshot-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
Copy-Item (Join-Path $Images '*') $work

# The window border is slightly see-through, so put a plain grey desktop behind it.
(New-Object -ComObject Shell.Application).MinimizeAll()
[NativeWindow]::SystemParametersInfo(0x14, 0, '', 0) | Out-Null # no wallpaper
[NativeWindow]::SetSysColors(1, @(1), @(0x363636)) | Out-Null # COLOR_DESKTOP
Start-Sleep -Milliseconds 500

$process = Start-Process -FilePath (Resolve-Path $App) -ArgumentList "`"$work`"" -PassThru
try {
  $deadline = (Get-Date).AddSeconds(30)
  while ($process.MainWindowHandle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
    $process.Refresh()
  }
  if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'The app window never appeared.' }

  Start-Sleep -Seconds $WaitSeconds
  # Park the mouse in the corner so no row is hovered and no tooltip shows.
  Add-Type -AssemblyName System.Windows.Forms
  $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  [NativeWindow]::SetCursorPos($screen.Right - 1, $screen.Bottom - 1) | Out-Null
  [NativeWindow]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 500
  $window = $process.MainWindowHandle

  if ($Settings) {
    # Ctrl+, opens Settings; it is a modal window, so it becomes the foreground window.
    (New-Object -ComObject WScript.Shell).SendKeys('^,')
    $deadline = (Get-Date).AddSeconds(10)
    do {
      Start-Sleep -Milliseconds 250
      $window = [NativeWindow]::GetForegroundWindow()
    } while ($window -eq $process.MainWindowHandle -and (Get-Date) -lt $deadline)
    if ($window -eq $process.MainWindowHandle) { throw 'The Settings window never appeared.' }
    Start-Sleep -Milliseconds 500
  }

  # DWMWA_EXTENDED_FRAME_BOUNDS excludes the invisible resize borders.
  $rect = New-Object NativeWindow+RECT
  if ([NativeWindow]::DwmGetWindowAttribute($window, 9, [ref]$rect, 16) -ne 0) {
    [NativeWindow]::GetWindowRect($window, [ref]$rect) | Out-Null
  }
  $width = $rect.Right - $rect.Left
  $height = $rect.Bottom - $rect.Top

  $bitmap = New-Object System.Drawing.Bitmap $width, $height
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
  $bitmap.Save((Join-Path (Get-Location) $Output), [System.Drawing.Imaging.ImageFormat]::Png)
  $graphics.Dispose()
  $bitmap.Dispose()
  Write-Information "Saved ${width}x${height} screenshot to $Output"
}
finally {
  # Waits for it to be gone, since the app only allows one window and the next screenshot starts another.
  if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
  Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
