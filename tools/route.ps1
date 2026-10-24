[CmdletBinding()]
param(
    # Steps run in order: "wait <seconds>", "key <name> [hold ms]", "shot <name>".
    # Keys go through keyboard controller emulation: Return = Start, Space = A,
    # Backspace = B, W/A/S/D = left stick.
    [Parameter(Mandatory)] [string[]]$Steps,
    [string]$Name = 'route',
    # A scripted gamepad file (src/scripted_input.h); it works without window
    # focus, unlike the "key" steps.
    [string]$InputScript,
    [string[]]$GameArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not ('Fm4StaticRecomp.Keys' -as [type])) {
    Add-Type -Namespace Fm4StaticRecomp -Name Keys -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr window, uint message, System.IntPtr wParam, System.IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern uint MapVirtualKey(uint code, uint mapType);
'@
}
$virtualKeys = @{ Return = 0x0D; Space = 0x20; Backspace = 0x08; Escape = 0x1B
                  W = 0x57; A = 0x41; S = 0x53; D = 0x44; X = 0x58 }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$shots = Join-Path $root ".local/state/shots/$Name"
if (Test-Path -LiteralPath $shots) { Remove-Item -LiteralPath $shots -Recurse -Force }
$env:FM4_STATIC_RECOMP_INPUT_SCRIPT = if ($InputScript) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($InputScript) } else { '' }
& (Join-Path $PSScriptRoot 'run.ps1') -GameArguments (@('--mnk_mode=true', '--audio_mute=true') + $GameArguments) | Out-Null
$env:FM4_STATIC_RECOMP_INPUT_SCRIPT = ''
$process = Get-Process fm4_static_recomp | Sort-Object StartTime | Select-Object -Last 1
try {
    foreach ($step in $Steps) {
        if ($process.HasExited) { "game exited with code $($process.ExitCode) before '$step'"; break }
        $parts = $step -split '\s+'
        switch ($parts[0]) {
            'wait' { Start-Sleep -Seconds ([int]$parts[1]) }
            'key' {
                $vk = $virtualKeys[$parts[1]]
                $hold = if ($parts.Count -gt 2) { [int]$parts[2] } else { 150 }
                $scan = [Fm4StaticRecomp.Keys]::MapVirtualKey($vk, 0)
                $window = $process.MainWindowHandle
                [void][Fm4StaticRecomp.Keys]::PostMessage($window, 0x100, [IntPtr]$vk, [IntPtr](($scan -shl 16) -bor 1))
                Start-Sleep -Milliseconds $hold
                [void][Fm4StaticRecomp.Keys]::PostMessage($window, 0x101, [IntPtr]$vk,
                    [IntPtr]([int64](($scan -shl 16) -bor 1) -bor 0xC0000000L))
            }
            'shot' {
                & (Join-Path $PSScriptRoot 'capture-window.ps1') -ProcessId $process.Id `
                    -Path (Join-Path $shots "$($parts[1]).png")
            }
            default { throw "Unknown step: $step" }
        }
    }
}
finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
}
