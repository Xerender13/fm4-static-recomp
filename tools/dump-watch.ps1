[CmdletBinding()]
param(
    # Seconds between dumps.
    [int]$Interval = 20,
    # Give up when no game with resolve dumps has started after this many minutes.
    [int]$WaitMinutes = 120
)

# While a game started with "--fh1_resolve_dump_dir=<.local\dumps> --fh1_resolve_dump_frame=-1"
# runs, asks it for one frame's resolves every $Interval seconds and files each set under
# .local\dumps\<HHmmss>. Ends when that game exits.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dumps = Join-Path $root '.local\dumps'
[void](New-Item -ItemType Directory -Force -Path $dumps)

function Get-DumpGame {
    Get-CimInstance Win32_Process -Filter "Name='fm4_static_recomp.exe'" |
        Where-Object { $_.CommandLine -like "*fh1_resolve_dump_dir=$dumps*" } | Select-Object -First 1
}

$deadline = (Get-Date).AddMinutes($WaitMinutes)
while (-not ($game = Get-DumpGame)) {
    if ((Get-Date) -gt $deadline) { 'no game with resolve dumps was started'; return }
    Start-Sleep -Seconds 3
}
"watching PID $($game.ProcessId)"
$trigger = Join-Path $dumps 'trigger'
$sets = 0
while (Get-Process -Id $game.ProcessId -ErrorAction SilentlyContinue) {
    Set-Content -LiteralPath $trigger -Value '' -Encoding ascii
    $taken = (Get-Date).AddSeconds(5)
    while ((Test-Path -LiteralPath $trigger) -and (Get-Date) -lt $taken) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path -LiteralPath $trigger)) {
        Start-Sleep -Milliseconds 1500
        $files = @(Get-ChildItem -LiteralPath $dumps -File -Filter '*.bin')
        if ($files.Count) {
            $set = Join-Path $dumps (Get-Date -Format 'HHmmss')
            [void](New-Item -ItemType Directory -Force -Path $set)
            $files | Move-Item -Destination $set
            $sets++
        }
    }
    Start-Sleep -Seconds $Interval
}
if (Test-Path -LiteralPath $trigger) { Remove-Item -LiteralPath $trigger -Force }
"game exited; $sets dump sets in $dumps"
