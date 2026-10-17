# Fills the shader cache by playing a scripted route in a game nobody sees: title, menus, the
# showroom and a Quick Race on five tracks, in a throwaway state with its own profile. Only the
# shader cache is copied to the player's state, and it is kept when a later step fails.
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
    # After this many minutes the current track ends and the rest are skipped.
    [int]$TimeLimitMinutes = 25,
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

# Environment list positions (Free Play, Quick Race), in the order that adds the most new track
# shaders first, so a time limit drops the least.
$tracks = @(
    @{ Name = 'Bernese Alps'; Index = 0 },
    @{ Name = 'Le Mans Circuit de la Sarthe'; Index = 9 },
    @{ Name = 'Camino Viejo de Montserrat'; Index = 1 },
    @{ Name = 'Tsukuba Circuit'; Index = 24 },
    @{ Name = 'Indianapolis Motor Speedway'; Index = 6 }
)
$driveSeconds = 45

# Games of a run that ended before its job could stop them.
$work = Join-Path $root '.local\warm-up'
Stop-Fm4LeftoverGame -StateRoot $work
Stop-Fm4LeftoverGame -StateRoot (Join-Path $root '.local\warm-up-probe')

$playerFolder = Join-Path $CacheRoot 'shaders\shareable'
foreach ($file in @(Get-ChildItem -Path (Join-Path $playerFolder '4D530910.*') -ErrorAction SilentlyContinue)) {
    if (Test-Fm4FileLocked $file.FullName) {
        [Console]::Out.WriteLine('FM4 Native is running on this state. Close it and run the warm-up again.')
        exit 1
    }
}

$current = Get-Fm4CurrentCacheHeaders -GameRoot $GameRoot
$playerCurrent = Test-Fm4ShaderCacheUsable -CacheRoot $CacheRoot -Current $current
$cacheFiles = $script:Fm4ShaderFile, $current.PipelineFile
if ($IfNeeded -and $playerCurrent) {
    [Console]::Out.WriteLine('Shader cache is current; no warm-up needed.')
    exit 0
}

if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
[void](New-Item -ItemType Directory -Force -Path (Join-Path $work 'user'), (Join-Path $work 'config'),
    (Join-Path $work 'cache\shaders\shareable'))
Copy-Item -Path (Join-Path $root 'config\warm-up\user\*') -Destination (Join-Path $work 'user') -Recurse -Force
if (Test-Path -LiteralPath $ConfigPath -PathType Leaf) {
    Copy-Item -LiteralPath $ConfigPath -Destination (Join-Path $work 'config\graphics.toml')
}
# A cache with another header would be discarded by the game, new records with it.
if ($playerCurrent) {
    foreach ($name in $cacheFiles) {
        Copy-Item -LiteralPath (Join-Path $playerFolder $name) -Destination (Join-Path $work 'cache\shaders\shareable')
    }
}
$inputScript = Join-Path $work 'live.input'
Set-Content -LiteralPath $inputScript -Value '# shader warm-up' -Encoding ascii

$game = $null
$started = Get-Date
$skipped = @()
$failure = $null
try {
    Write-Fm4Event warmup 0 'Warming up the shader cache in the background (about 10 minutes).' -JsonEvents:$JsonEvents
    $game = Start-Fm4HiddenGame -StateRoot $work -GameRoot $GameRoot -InputScript $inputScript
    function Step([string]$Control, [double]$MinSeconds) {
        Send-Fm4Press -Game $game -InputScript $inputScript -Control $Control
        Wait-Fm4Settled -StateRoot $work -MinSeconds $MinSeconds -Game $game
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
    Step A 8                                                     # Quick Race -> manufacturer select
    Step A 5.5                                                   # Abarth -> car select
    Step RIGHT 1.5                                               # the first car needs Disc 2
    Step A 7                                                     # Abarth 500 -> showroom
    Step A 3                                                     # assists
    Step A 3                                                     # environment select, Bernese Alps
    $position = 0

    for ($i = 0; $i -lt $tracks.Count; $i++) {
        $track = $tracks[$i]
        if (((Get-Date) - $started).TotalMinutes -ge $TimeLimitMinutes) {
            $skipped = @($tracks[$i..($tracks.Count - 1)] | ForEach-Object { $_.Name })
            break
        }
        Write-Fm4Event warmup ([int](100 * $i / $tracks.Count)) "Warming up shaders: $($track.Name) ($($i + 1) of $($tracks.Count))." -JsonEvents:$JsonEvents
        $steps = $track.Index - $position
        $direction = if ($steps -lt 0) { 'LEFT' } else { 'RIGHT' }
        for ($s = 0; $s -lt [Math]::Abs($steps); $s++) { Step $direction 0.4 }
        $position = $track.Index
        Step A 3                                                 # track select
        Press-Until A 6 'fast vblank off after .* \(first car step\)' 240 'Race load: fast vblank on'  # GO RACE: load, pre-race scene
        Step A 3                                                 # pre-race menu
        Press-Until A 0 'Present interval: 1' 120 'Present interval: 1'  # START RACE
        # BACK cycles the race cameras (chase far and near, cockpit, hood, bumper), each with
        # shaders of its own.
        Send-Fm4Press -Game $game -InputScript $inputScript -Control 'RT=1' -Hold $driveSeconds -NoWait
        $view = $driveSeconds / 5
        Start-Sleep -Milliseconds ([int]($view * 1000))
        1..4 | ForEach-Object { Send-Fm4Press -Game $game -InputScript $inputScript -Control BACK -Wait ($view - 0.4) }
        Step START 2                                             # pause menu
        1..6 | ForEach-Object { Step DOWN 0.4 }                  # QUIT RACE
        Step A 2                                                 # confirm
        Step A 8                                                 # post-race menu over a replay
        Press-Until A 3 'fast vblank off after .* \(menu\)' 240  # CONTINUE -> track select
        Step B 3                                                 # environment select
    }
    if ($skipped.Count -gt 0) {
        Write-Fm4Event warmup 100 "Time limit of $TimeLimitMinutes minutes reached; skipped: $($skipped -join ', ')." -JsonEvents:$JsonEvents
    }
}
catch {
    $failure = $_.Exception.Message
}
finally {
    $locked = $false
    try {
        if ($game) { Stop-Fm4Game -Process $game }
        $workCache = Join-Path $work 'cache'
        $result = Get-Fm4ShaderCacheHeaders -CacheRoot $workCache -PipelineFile $current.PipelineFile
        $kept = Test-Fm4ShaderCacheUsable -CacheRoot $workCache -Current $current
        if (-not $kept -and -not $failure) { $failure = 'the game wrote no shader cache this build can load' }
        # The player may have started the game on this state since the run began.
        $locked = @($cacheFiles | Where-Object { Test-Fm4FileLocked (Join-Path $playerFolder $_) }).Count -gt 0
        if ($kept -and -not $locked) {
            [void](New-Item -ItemType Directory -Force -Path $playerFolder)
            foreach ($name in $cacheFiles) {
                Copy-Item -LiteralPath (Join-Path $workCache "shaders\shareable\$name") -Destination $playerFolder -Force
            }
        }
    }
    finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}

$minutes = ((Get-Date) - $started).TotalMinutes
if ($locked) {
    [Console]::Out.WriteLine('FM4 Native was started on this state during the warm-up, so its shader cache was left as it was. Run the warm-up again with the game closed.')
    exit 1
}
if ($failure) {
    $saved = if ($kept) { "; $($result.PipelineCount) pipelines compiled so far are kept" } else { '' }
    [Console]::Out.WriteLine(("The shader warm-up stopped after {0:N1} min: {1}{2}" -f $minutes, $failure, $saved))
    exit 1
}
Write-Fm4Event warmup 100 ("Shader warm-up done in {0:N1} min: {1} pipelines." -f $minutes, $result.PipelineCount) -JsonEvents:$JsonEvents
exit 0
