[CmdletBinding()]
param([string[]]$GameArguments = @('--audio_mute=true', '--fh1_native_gpu_profile=true'))

# Starts a test session and drives it to the Free Play car showroom (Abarth 500).
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$session = Join-Path $PSScriptRoot 'session.ps1'
& $session start -GameArguments $GameArguments | Out-Null
# The intro movie starts 60-75 s after launch; Start skips it.
Start-Sleep -Seconds 85
& $session press START -Hold 0.1 -Wait 7
& $session press A -Hold 0.1 -Wait 20        # title -> main menu
1..3 | ForEach-Object { & $session press RIGHT -Hold 0.1 -Wait 0.8 }
& $session press A -Hold 0.1 -Wait 8         # Free Play
& $session press A -Hold 0.1 -Wait 10        # Quick Race -> manufacturer select
& $session press A -Hold 0.1 -Wait 7         # Abarth -> car select
& $session press RIGHT -Hold 0.1 -Wait 2     # the first car needs Disc 2
& $session press A -Hold 0.1 -Wait 9         # Abarth 500 -> showroom
