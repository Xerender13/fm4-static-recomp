[CmdletBinding()]
param(
    # The replay's place in Replay Theater, 0 = the one it opens on (the newest).
    [int]$Index = 0,
    [string[]]$GameArguments = @('--audio_mute=true'),
    # Shows the window without focus: a temporal upscaler only runs on a shown window.
    [switch]$Show
)

# Starts a test session with the player's saved replays and drives it into one: PLAY CAREER,
# MY PROFILE, MEDIA, MY REPLAYS, then LOAD. Returns once the replay's scene is pushed.
# The replays are copied from the player's state (read only) into the private test copy.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$session = Join-Path $PSScriptRoot 'session.ps1'
$player = Join-Path $root '.local/state/user'
$test = Join-Path $root '.local/state-test/user'
$log = Join-Path $root '.local/state-test/logs/runtime.log'

Get-ChildItem -LiteralPath $player -Directory -Recurse -Filter 'replay' | ForEach-Object {
    $destination = Join-Path $test $_.FullName.Substring($player.Length)
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
    Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse
}

function Test-Red([string]$png, [int]$x, [int]$y0, [int]$y1) {
    $bitmap = [System.Drawing.Bitmap]::FromFile($png)
    try {
        for ($y = $y0; $y -lt $y1; $y++) {
            $p = $bitmap.GetPixel($x, $y)
            if ($p.R -gt 150 -and $p.G -lt 90 -and $p.B -lt 90) { return $y }
        }
        return -1
    } finally { $bitmap.Dispose() }
}

# The side menu's highlighted row: a red bar above it, rows 40 px apart from y 52.
function Get-MenuRow {
    $y = Test-Red (& $session shot replay-nav) 230 40 640
    if ($y -lt 0) { return -1 }
    [int][Math]::Round(($y - 52) / 40)
}

# Menus drop presses while they animate, so each step repeats until the capture shows it taken.
function Set-MenuRow([int]$row) {
    for ($i = 0; $i -lt 20; $i++) {
        $now = Get-MenuRow
        if ($now -eq $row) { return }
        if ($now -lt 0) { Start-Sleep -Milliseconds 700; continue }
        & $session press $(if ($now -lt $row) { 'DOWN' } else { 'UP' }) -Hold 0.1 -Wait 0.7
    }
    throw "Menu row $row was not reached."
}

function Invoke-Until([string]$control, [scriptblock]$done, [double]$wait = 2.5) {
    for ($i = 0; $i -lt 8; $i++) {
        & $session press $control -Hold 0.1 -Wait $wait
        # A press taken late must not be repeated into the next screen.
        for ($check = 0; $check -lt 3; $check++) {
            if (& $done) { return }
            Start-Sleep -Milliseconds 1500
        }
    }
    throw "$control was not taken."
}

& $session start -GameArguments (@('--fm4_skip_intro=true') + $GameArguments) | Out-Null
if ($Show) {
    Add-Type -Namespace Fm4StaticRecomp -Name ReplayWindow -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool ShowWindowAsync(System.IntPtr window, int command);
'@
    $game = Get-CimInstance Win32_Process -Filter "Name='fm4_static_recomp.exe'" |
        Where-Object { $_.CommandLine -like '*\.local\state-test\*' } | Sort-Object CreationDate | Select-Object -Last 1
    [void][Fm4StaticRecomp.ReplayWindow]::ShowWindowAsync((Get-Process -Id $game.ProcessId).MainWindowHandle, 4)
}
# START skips the intro movie and A leaves the title; the profile is read when it is left.
$deadline = (Get-Date).AddSeconds(120)
$control = 'A'
while (-not (Select-String -LiteralPath $log -Pattern 'root="B13EBABEBABEBABE"' -Quiet)) {
    if ((Get-Date) -gt $deadline) { throw 'The title screen was not reached.' }
    & $session press $control -Hold 0.1 -Wait 0.6
    $control = if ($control -eq 'START') { 'A' } else { 'START' }
}
Start-Sleep -Seconds 18                                    # main menu, PLAY CAREER selected
Invoke-Until A { (Get-MenuRow) -eq 0 } 4                   # PLAY CAREER: GO RACE
Set-MenuRow 4                                              # MY PROFILE
Invoke-Until A { (Get-MenuRow) -eq 1 } 3                   # EDIT PLAYERCARD under the header
Set-MenuRow 5                                              # MEDIA
Invoke-Until A { (Get-MenuRow) -eq 2 } 2.5                 # MY PHOTOS under two headers
Set-MenuRow 3                                              # MY REPLAYS
& $session press A -Hold 0.1 -Wait 4                       # Replay Theater
for ($i = 0; $i -lt $Index; $i++) { & $session press RIGHT -Hold 0.1 -Wait 1 }
& $session shot replay-theater | Out-Null
# FILE OPTIONS, LOAD highlighted: its red bar at y 277.
Invoke-Until A { (Test-Red (& $session shot replay-nav) 400 270 285) -ge 0 } 2.5
$pushes = @(Select-String -LiteralPath $log -Pattern 'Render scenario .* pushed').Count
& $session press A -Hold 0.1
$deadline = (Get-Date).AddSeconds(90)
while (@(Select-String -LiteralPath $log -Pattern 'Render scenario .* pushed').Count -le $pushes) {
    if ((Get-Date) -gt $deadline) { throw 'The replay did not load.' }
    Start-Sleep -Milliseconds 500
}
Write-Output "Replay $Index loaded; the theater's capture is .local\state-test\shots\replay-theater.png"
