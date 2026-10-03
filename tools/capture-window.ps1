[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int]$ProcessId,
    [Parameter(Mandatory)] [string]$Path,
    # Downscale wider captures to this width; 0 keeps the window's size.
    [int]$MaxWidth = 1280
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('Fm4Native.Capture' -as [type])) {
    Add-Type -Namespace Fm4Native -Name Capture -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool GetClientRect(System.IntPtr window, out RECT rect);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PrintWindow(System.IntPtr window, System.IntPtr dc, uint flags);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern System.IntPtr SetThreadDpiAwarenessContext(System.IntPtr context);
public struct RECT { public int Left, Top, Right, Bottom; }
'@
}

$window = (Get-Process -Id $ProcessId).MainWindowHandle
if ($window -eq [IntPtr]::Zero) { throw "Process $ProcessId has no main window." }
# Per-monitor aware, so the client rectangle is in physical pixels on a scaled display.
[void][Fm4Native.Capture]::SetThreadDpiAwarenessContext([IntPtr](-4))
$rect = New-Object Fm4Native.Capture+RECT
[void][Fm4Native.Capture]::GetClientRect($window, [ref]$rect)
$bitmap = New-Object System.Drawing.Bitmap ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$dc = $graphics.GetHdc()
# PW_CLIENTONLY | PW_RENDERFULLCONTENT, so a swapchain-presented window is captured.
$ok = [Fm4Native.Capture]::PrintWindow($window, $dc, 3)
$graphics.ReleaseHdc($dc)
$graphics.Dispose()
[void](New-Item -ItemType Directory -Force -Path (Split-Path $Path -Parent))
if ($MaxWidth -gt 0 -and $bitmap.Width -gt $MaxWidth) {
    $height = [int]($bitmap.Height * $MaxWidth / $bitmap.Width)
    $scaled = New-Object System.Drawing.Bitmap $bitmap, $MaxWidth, $height
    $bitmap.Dispose()
    $bitmap = $scaled
}
$bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
if (-not $ok) { throw 'PrintWindow failed.' }
$Path
