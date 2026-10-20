# Fills the shader cache by playing a scripted route in a game nobody sees: title, menus and ten
# Quick Races, each with another car and track, in a throwaway state with its own profile. The
# throwaway state's shader folder is a link to the player's, so the game writes every shader it
# compiles straight into the player's cache, and stopping the run at any point keeps them.
[CmdletBinding()]
param(
    # The player's state, as run.ps1 takes it.
    [string]$StateRoot,
    # The folder holding shaders\; default <StateRoot>\cache.
    [string]$CacheRoot,
    # The GRAPHICS settings the pipelines are compiled for; default <StateRoot>\config\graphics.toml.
    [string]$ConfigPath,
    # The extracted Disc 1, as run.ps1 takes it.
    [string]$GameRoot,
    # Only when the cache is missing or this build would discard it (setup's case).
    [switch]$IfNeeded,
    [switch]$JsonEvents
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')
. (Join-Path $PSScriptRoot 'warm-up-common.ps1')

$root = Get-Fm4RepoRoot
if (-not $StateRoot) { $StateRoot = Join-Path $root '.local/state' }
if (-not $GameRoot) { $GameRoot = Join-Path $root '../extracted/fm4' }
$StateRoot = Resolve-Fm4Path $StateRoot
$GameRoot = Resolve-Fm4Path $GameRoot
$CacheRoot = if ($CacheRoot) { Resolve-Fm4Path $CacheRoot } else { Join-Path $StateRoot 'cache' }
$ConfigPath = if ($ConfigPath) { Resolve-Fm4Path $ConfigPath } else { Join-Path $StateRoot 'config\graphics.toml' }

# Disc 1 cars and tracks that together reference the most shaders (the car shaders a car's models
# name, the shader files in a track's bin.zip). Tracks come in the order that adds the most first,
# so a stopped run has the most; any car brings most of the car shaders, so cars come in menu order,
# which keeps the presses between races few. Menu positions as the throwaway state shows them, the
# same on every install since it has no Disc 2 or DLC: manufacturers alphabetical after View
# Garage (0); a manufacturer's cars by performance index, Disc 2 cars included (choosing one asks
# for the disc), unicorns left out; environments alphabetical from 0, where the list starts again
# after every car change.
$races = @(
    @{ Car = 'Aston Martin DBR1'; Make = 5; Model = 2; Track = 'Bernese Alps'; Environment = 0 },
    @{ Car = 'Cadillac CTS-V'; Make = 12; Model = 1; Track = 'Le Mans Circuit de la Sarthe'; Environment = 9 },
    @{ Car = 'Chevrolet Corvette Z06'; Make = 13; Model = 18; Track = 'Camino Viejo de Montserrat'; Environment = 1 },
    @{ Car = 'Ferrari F430'; Make = 21; Model = 14; Track = 'Tsukuba Circuit'; Environment = 24 },
    @{ Car = 'Ferrari 599 GTB Fiorano'; Make = 21; Model = 17; Track = 'Indianapolis Motor Speedway'; Environment = 6 },
    @{ Car = 'Ferrari 599 GTO'; Make = 21; Model = 20; Track = 'Rally di Positano'; Environment = 15 },
    @{ Car = 'Ford Mustang Mach 1'; Make = 23; Model = 7; Track = 'Twin Ring Motegi'; Environment = 25 },
    @{ Car = 'Lotus Evora Type 124'; Make = 40; Model = 9; Track = 'Sunset Peninsula Raceway'; Environment = 21 },
    @{ Car = 'Nissan Versa SL'; Make = 50; Model = 4; Track = 'Top Gear Test Track'; Environment = 23 },
    @{ Car = 'Pontiac GTO'; Make = 57; Model = 7; Track = 'Nurburgring Grand Prix Circuit'; Environment = 13 }
)
# Each race camera shows for this long; a new view's shaders come with its first frames.
$viewSeconds = 4

# Games of a run that ended before its job could stop them.
$work = Join-Path $root '.local\warm-up'
Stop-Fm4LeftoverGame -StateRoot $work
Stop-Fm4LeftoverGame -StateRoot (Join-Path $root '.local\warm-up-probe')

$playerShaders = Join-Path $CacheRoot 'shaders'
$playerFolder = Join-Path $playerShaders 'shareable'
foreach ($file in @(Get-ChildItem -Path (Join-Path $playerFolder '4D530910.*') -ErrorAction SilentlyContinue)) {
    if (Test-Fm4FileLocked $file.FullName) {
        [Console]::Out.WriteLine('FM4 Static Recomp is running on this state. Close it and run the warm-up again.')
        exit 1
    }
}

$current = Get-Fm4CurrentCacheHeaders -GameRoot $GameRoot
if ($IfNeeded -and (Test-Fm4ShaderCacheUsable -CacheRoot $CacheRoot -Current $current)) {
    [Console]::Out.WriteLine('Shader cache is current; no warm-up needed.')
    exit 0
}

Remove-Fm4WarmUpWork -Path $work
[void](New-Item -ItemType Directory -Force -Path (Join-Path $work 'user'), (Join-Path $work 'config'),
    (Join-Path $work 'cache'), $playerShaders)
# A cache with another header is rewritten by the game, as at any launch of this build.
[void](New-Item -ItemType Junction -Path (Join-Path $work 'cache\shaders') -Target $playerShaders)
Copy-Item -Path (Join-Path $root 'config\warm-up\user\*') -Destination (Join-Path $work 'user') -Recurse -Force
if (Test-Path -LiteralPath $ConfigPath -PathType Leaf) {
    Copy-Item -LiteralPath $ConfigPath -Destination (Join-Path $work 'config\graphics.toml')
}
$inputScript = Join-Path $work 'live.input'
Set-Content -LiteralPath $inputScript -Value '# shader warm-up' -Encoding ascii

$game = $null
$started = Get-Date
$failure = $null
$finished = $false
try {
    Write-Fm4Event warmup 0 ("Warming up the shader cache in the background: $($races.Count) races, about 20 minutes. " +
        'Press Ctrl+C or close this window to stop at any time; the shaders compiled so far are kept.') -JsonEvents:$JsonEvents
    $game = Start-Fm4HiddenGame -StateRoot $work -GameRoot $GameRoot -InputScript $inputScript
    function Step([string]$Control, [double]$MinSeconds) {
        Send-Fm4Press -Game $game -InputScript $inputScript -Control $Control
        Wait-Fm4Settled -StateRoot $work -MinSeconds $MinSeconds -Game $game
    }
    # Presses that move through a list on one screen need no wait for shaders in between.
    function Move-Selection([int]$From, [int]$To, [string]$Back = 'LEFT', [string]$On = 'RIGHT') {
        $control = if ($To -lt $From) { $Back } else { $On }
        for ($i = 0; $i -lt [Math]::Abs($To - $From); $i++) {
            Send-Fm4Press -Game $game -InputScript $inputScript -Control $control -Wait 0.2
        }
        Wait-Fm4Settled -StateRoot $work -MinSeconds 0.4 -Game $game
    }
    # A screen still coming up can ignore a press (pre-race at 3x with MSAA runs at 20 fps), so
    # with $Ack the press repeats every 20 s until the log shows it was taken. A taken START RACE
    # shows after up to 10.4 s at those settings.
    function Press-Until([string]$Control, [double]$MinSeconds, [string]$Pattern, [int]$TimeoutSeconds, [string]$Ack) {
        $since = (Get-Date).AddSeconds(-1)
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        Send-Fm4Press -Game $game -InputScript $inputScript -Control $Control
        $again = (Get-Date).AddSeconds(20)
        while ($Ack -and -not (Test-Fm4LogLine -StateRoot $work -Pattern $Ack -Since $since)) {
            if ($game.HasExited) { throw "The game exited (code $($game.ExitCode))." }
            if ((Get-Date) -gt $deadline) { throw "Timed out after $TimeoutSeconds s waiting for: $Ack" }
            if ((Get-Date) -gt $again) {
                Send-Fm4Press -Game $game -InputScript $inputScript -Control $Control
                $again = (Get-Date).AddSeconds(20)
            }
            Start-Sleep -Milliseconds 500
        }
        Wait-Fm4LogLine -StateRoot $work -Pattern $Pattern -TimeoutSeconds $TimeoutSeconds -Since $since -Game $game
        Wait-Fm4Settled -StateRoot $work -MinSeconds $MinSeconds -Game $game
    }

    # START skips the intro movie and A leaves the title; the profile is read when it is left.
    Start-Sleep -Seconds 8
    $deadline = (Get-Date).AddSeconds(180)
    $control = 'START'
    while (-not (Select-String -LiteralPath (Get-Fm4RunLog -StateRoot $work) -Pattern 'root="B13EBABEBABEBABE"' -Quiet)) {
        if ($game.HasExited) { throw "The game exited (code $($game.ExitCode))." }
        if ((Get-Date) -gt $deadline) { throw 'The title screen was not left within 180 s.' }
        Send-Fm4Press -Game $game -InputScript $inputScript -Control $control -Wait 0.6
        $control = if ($control -eq 'START') { 'A' } else { 'START' }
    }
    Wait-Fm4Settled -StateRoot $work -MinSeconds 15 -Game $game # main menu
    1..3 | ForEach-Object { Step RIGHT 0.6 }
    Step A 6                                                     # Free Play
    Step A 8                                                     # Quick Race -> manufacturer select, Abarth
    $make = 1
    $model = $null

    for ($i = 0; $i -lt $races.Count; $i++) {
        $race = $races[$i]
        Write-Fm4Event warmup ([int](100 * $i / $races.Count)) "Warming up shaders: race $($i + 1) of $($races.Count), $($race.Car) at $($race.Track)." -JsonEvents:$JsonEvents
        if ($i -gt 0) {
            Step B 3                                             # environment select
            Step B 3                                             # car select
        }
        if ($race.Make -ne $make) {
            if ($null -ne $model) { Step B 3 }                   # manufacturer select
            Move-Selection $make $race.Make
            Step A 5.5                                           # car select, first car
            $make = $race.Make
            $model = 0
        }
        Move-Selection $model $race.Model
        $model = $race.Model
        Step A 7                                                 # showroom
        Step A 3                                                 # assists
        Step A 3                                                 # environment select, first environment
        Move-Selection 0 $race.Environment
        Step A 3                                                 # track select
        Press-Until A 6 'fast vblank off after .* \(first car step\)' 240 'Race load: fast vblank on'  # GO RACE: load, pre-race scene
        Step A 3                                                 # pre-race menu
        Press-Until A 0 'Present interval: 1' 120 'Present interval: 1'  # START RACE
        # BACK cycles the race cameras (chase far and near, cockpit, hood, bumper), each with
        # shaders of its own.
        Send-Fm4Press -Game $game -InputScript $inputScript -Control 'RT=1' -Hold (5 * $viewSeconds) -NoWait
        Start-Sleep -Milliseconds ([int]($viewSeconds * 1000))
        1..4 | ForEach-Object { Send-Fm4Press -Game $game -InputScript $inputScript -Control BACK -Wait ($viewSeconds - 0.4) }
        Step START 2                                             # pause menu
        Move-Selection 0 6 UP DOWN                               # QUIT RACE
        Step A 2                                                 # confirm
        Step A 8                                                 # post-race menu over a replay
        Press-Until A 3 'fast vblank off after .* \(menu\)' 240  # CONTINUE -> track select
    }
    $finished = $true
}
catch {
    $failure = $_.Exception.Message
}
finally {
    if ($game) { Stop-Fm4Game -Process $game }
    Remove-Fm4WarmUpWork -Path $work
    $result = Get-Fm4ShaderCacheHeaders -CacheRoot $CacheRoot -PipelineFile $current.PipelineFile
    $kept = Test-Fm4ShaderCacheUsable -CacheRoot $CacheRoot -Current $current
    $minutes = ((Get-Date) - $started).TotalMinutes
    # Ctrl+C ends the script after this block, with neither a failure nor a finished route.
    if (-not $finished -and -not $failure) {
        [Console]::Out.WriteLine(("Shader warm-up stopped after {0:N1} min; {1} pipelines are in the cache." -f $minutes, $result.PipelineCount))
    }
}

if (-not $kept -and -not $failure) { $failure = 'the game wrote no shader cache this build can load' }
if ($failure) {
    $saved = if ($kept) { "; $($result.PipelineCount) pipelines are in the cache" } else { '' }
    [Console]::Out.WriteLine(("The shader warm-up stopped after {0:N1} min: {1}{2}" -f $minutes, $failure, $saved))
    exit 1
}
Write-Fm4Event warmup 100 ("Shader warm-up done in {0:N1} min: {1} pipelines." -f $minutes, $result.PipelineCount) -JsonEvents:$JsonEvents
exit 0
