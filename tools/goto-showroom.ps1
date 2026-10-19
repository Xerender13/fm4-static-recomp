[CmdletBinding()]
param([string[]]$GameArguments = @('--audio_mute=true', '--fh1_native_gpu_profile=true'))

# Starts a test session and drives it to the Free Play car showroom (Abarth 500).
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$session = Join-Path $PSScriptRoot 'session.ps1'
$log = Join-Path $PSScriptRoot '../.local/state-test/logs/runtime.log'
& $session start -GameArguments (@('--fm4_skip_intro=true') + $GameArguments) | Out-Null
# START skips the intro movie and A leaves the title; the profile is read when it is left.
Start-Sleep -Seconds 2
$deadline = (Get-Date).AddSeconds(120)
$control = 'START'
while (-not (Select-String -LiteralPath $log -Pattern 'root="B13EBABEBABEBABE"' -Quiet)) {
    if ((Get-Date) -gt $deadline) { throw 'The title screen was not reached.' }
    & $session press $control -Hold 0.1 -Wait 0.6
    $control = if ($control -eq 'START') { 'A' } else { 'START' }
}
Start-Sleep -Seconds 20                      # main menu
1..3 | ForEach-Object { & $session press RIGHT -Hold 0.1 -Wait 0.8 }
& $session press A -Hold 0.1 -Wait 8         # Free Play
& $session press A -Hold 0.1 -Wait 10        # Quick Race -> manufacturer select
& $session press A -Hold 0.1 -Wait 7         # Abarth -> car select
& $session press RIGHT -Hold 0.1 -Wait 2     # the first car needs Disc 2
& $session press A -Hold 0.1 -Wait 9         # Abarth 500 -> showroom
