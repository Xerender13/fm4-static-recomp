# Dot-source this: helpers that drive a test session (session.ps1, private state) into a
# race, waiting on what the game logs rather than on fixed delays. About 2 minutes from
# launch to green lights.
$ErrorActionPreference = 'Stop'
$raceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$session = Join-Path $PSScriptRoot 'session.ps1'
$state = Join-Path $raceRoot '.local\state-test'
$log = Join-Path $state 'logs\runtime.log'
$perf = Join-Path $raceRoot '.local\perf'
[void](New-Item -ItemType Directory -Force -Path $perf)

function Get-RaceGame {
    $own = Get-CimInstance Win32_Process -Filter "Name='fm4_static_recomp.exe'" |
        Where-Object { $_.CommandLine -like '*\.local\state-test\*' } | Sort-Object CreationDate | Select-Object -Last 1
    Get-Process -Id $own.ProcessId
}

# Holds controls without waiting for them to end (session.ps1 press waits).
function Hold-Control([string]$control, [double]$seconds) {
    $elapsed = ((Get-Date) - (Get-RaceGame).StartTime).TotalSeconds
    $line = '{0:F2} {1:F2} {2}' -f ($elapsed + 0.2), $seconds, $control
    Add-Content -LiteralPath "$state\live.input" -Value $line.Replace(',', '.') -Encoding ascii
}

function Press-Control([string]$control, [double]$wait) { & $session press $control -Hold 0.1 -Wait $wait }

# The guest output at full resolution, as a PNG (optionally resized to $width).
function Save-RaceCapture([string]$png, [int]$width = 0) {
    $bmp = [IO.Path]::ChangeExtension($png, '.bmp')
    if (Test-Path -LiteralPath $bmp) { Remove-Item -LiteralPath $bmp -Force }
    Set-Content -LiteralPath "$state\live.input.shot.tmp" -Value $bmp -Encoding ascii
    Move-Item -LiteralPath "$state\live.input.shot.tmp" -Destination "$state\live.input.shot" -Force
    $deadline = (Get-Date).AddSeconds(10)
    while (-not (Test-Path -LiteralPath $bmp)) {
        if ((Get-Date) -gt $deadline) { throw 'The game did not write a capture.' }
        Start-Sleep -Milliseconds 50
    }
    Start-Sleep -Milliseconds 300
    Add-Type -AssemblyName System.Drawing
    $image = [System.Drawing.Image]::FromFile($bmp)
    if ($width -le 0) { $width = $image.Width }
    $scaled = New-Object System.Drawing.Bitmap $image, $width, ([int]($image.Height * $width / $image.Width))
    $image.Dispose()
    $scaled.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $scaled.Dispose()
    Remove-Item -LiteralPath $bmp -Force
}

# Draws per frame over the executor's latest 600-frame stats window, 0 until one ends after $since.
function Get-DrawsPerFrame([datetime]$since) {
    $lines = @(Select-String -LiteralPath $log -Pattern '^\[\S+ (\S+)\].*\(Vulkan\) frame=(\d+) draws=(\d+)' |
        Select-Object -Last 2)
    if ($lines.Count -lt 2) { return 0 }
    $time = [datetime]::ParseExact($lines[1].Matches[0].Groups[1].Value, 'HH:mm:ss.fff', $null)
    if ($time -lt $since) { return 0 }
    $frames = [long]$lines[1].Matches[0].Groups[2].Value - [long]$lines[0].Matches[0].Groups[2].Value
    ([long]$lines[1].Matches[0].Groups[3].Value - [long]$lines[0].Matches[0].Groups[3].Value) / [Math]::Max(1, $frames)
}

# Launches the game and returns once the race has started (Free Play, Abarth 500; -HotLap: alone
# on the track instead of a Quick Race against eleven AI cars from a varying grid slot). The track is
# the default, Bernese Alps, or the environment $EnvironmentSteps further in the list (17: Road Atlanta).
# -Fullscreen keeps the game's own fullscreen window instead of a 1280x720 one.
function Start-Race([string[]]$GameArguments = @(), [switch]$Fullscreen, [int]$EnvironmentSteps = 0, [switch]$HotLap) {
    $window = if ($Fullscreen) { @() } else { '--fullscreen=false', '--window_width=1280', '--window_height=720' }
    & $session start -GameArguments (@('--audio_mute=true') + $window + $GameArguments) | Out-Null
    # START skips the intro movie and A leaves the title; the profile is read when it is left.
    Start-Sleep -Seconds 8
    $deadline = (Get-Date).AddSeconds(120)
    $control = 'START'
    while (-not (Select-String -LiteralPath $log -Pattern 'root="B13EBABEBABEBABE"' -Quiet)) {
        if ((Get-Date) -gt $deadline) { throw 'The title screen was not reached.' }
        Press-Control $control 0.6
        $control = if ($control -eq 'START') { 'A' } else { 'START' }
    }
    Start-Sleep -Seconds 17                              # main menu
    1..3 | ForEach-Object { Press-Control RIGHT 0.6 }
    Press-Control A 6                                    # Free Play
    if ($HotLap) { 1..2 | ForEach-Object { Press-Control RIGHT 0.6 } }
    Press-Control A 8                                    # Quick Race or Hot Lap -> manufacturer select
    Press-Control A 5.5                                  # Abarth -> car select
    Press-Control RIGHT 1.5                              # the first car needs Disc 2
    Press-Control A 7                                    # Abarth 500 -> showroom
    1..2 | ForEach-Object { Press-Control A 3 }          # assists, environment
    for ($step = 0; $step -lt $EnvironmentSteps; $step++) { Press-Control RIGHT 0.4 }
    Press-Control A 3                                    # track
    $go = Get-Date
    Press-Control A 20                                   # GO RACE
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-DrawsPerFrame $go.AddSeconds(8)) -lt 700) {
        if ((Get-Date) -gt $deadline) { throw 'The pre-race scene did not appear.' }
        Start-Sleep -Milliseconds 500
    }
    Press-Control A 2.5                                  # pre-race menu
    Press-Control A 13                                   # START RACE, fade and countdown
}
