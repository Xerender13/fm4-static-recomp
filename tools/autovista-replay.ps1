[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Tag,
    [string[]]$GameArguments = @(),
    [string]$Recording = '.local\perf\av-rec.input',
    # Replay only the recording's lines before this time (before the player's quit).
    [double]$Until = 29.7,
    [double]$FilmSeconds = 45,
    # The recording's time base the replay follows.
    [string]$Anchor = 'autovista_shown'
)
# Drives a test session into Autovista (main menu, AUTOVISTA, the car select's Ferrari 458),
# replays a recording made with run.ps1 -RecordInput from the moment the car fades in, films
# the picture into .local\perf\av-<tag>.gray with the log beside it, then stops the session.
# Compare films with film.py. The test state must hold a save that opens Autovista on the
# car select (a copy of the player's; the old test save skipped it for a tutorial).
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$session = Join-Path $root 'tools\session.ps1'
$live = Join-Path $root '.local\state-test\live.input'
$film = Join-Path $root ".local\perf\av-$Tag.gray"
if (Test-Path -LiteralPath $film) { Remove-Item -LiteralPath $film }

$lines = @("@$Anchor")
$anchored = $false
foreach ($line in Get-Content -LiteralPath (Join-Path $root $Recording)) {
    if ($line.StartsWith('@')) { $anchored = $line -eq "@$Anchor"; continue }
    if (-not $anchored -or $line -notmatch '^(\S+)\s') { continue }
    $start = [double]$Matches[1]
    if ($start -ge $Until) { continue }
    $lines += $line
}
$lines += '@'

& $session start -GameArguments (@('--audio_mute=true') + $GameArguments) | Out-Null
Add-Content -LiteralPath $live -Value ($lines -join "`n")
Start-Sleep 85
& $session press START -Hold 0.1 -Wait 12 | Out-Null
& $session press A -Hold 0.1 -Wait 30 | Out-Null
1..2 | ForEach-Object { & $session press RIGHT -Hold 0.1 -Wait 1 | Out-Null }
& $session press A -Hold 0.1 -Wait 6 | Out-Null    # AUTOVISTA -> car select (Ferrari 458)
Set-Content -LiteralPath "$live.film" -Value "$FilmSeconds $film" -Encoding ascii
& $session press A -Hold 0.1 | Out-Null
Start-Sleep ($FilmSeconds + 5)
$log = Join-Path $root '.local\state-test\logs\runtime.log'
Select-String -LiteralPath $log -Pattern 'Input event|Film:' | ForEach-Object { $_.Line }
Copy-Item -LiteralPath $log -Destination (Join-Path $root ".local\perf\av-$Tag.log")
& $session stop | Out-Null
