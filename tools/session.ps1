[CmdletBinding()]
param(
    # start: launch the game with a live scripted gamepad. press: hold controls
    # (see src/scripted_input.h). shot: capture the window. stop: end the game.
    [Parameter(Mandatory, Position = 0)] [ValidateSet('start', 'press', 'shot', 'stop')] [string]$Action,
    [Parameter(Position = 1)] [string]$Value,
    [double]$Hold = 0.25,
    # Seconds to wait after a press or before a shot.
    [double]$Wait = 0,
    [string[]]$GameArguments = @('--audio_mute=true')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# A private copy of the player's state, so a session never writes to the real save.
$state = Join-Path $root '.local/state-test'
$script = Join-Path $state 'live.input'

function Get-Game {
    # Only the session's own game: the player may have theirs open on the real state.
    $own = Get-CimInstance Win32_Process -Filter "Name='fm4_native.exe'" |
        Where-Object { $_.CommandLine -like '*\.local\state-test\*' } | Sort-Object CreationDate | Select-Object -Last 1
    if (-not $own) { throw 'The session game is not running; use "session.ps1 start".' }
    Get-Process -Id $own.ProcessId
}

switch ($Action) {
    'start' {
        if (-not (Test-Path -LiteralPath $state)) {
            [void](New-Item -ItemType Directory -Force -Path (Join-Path $state 'cache'))
            $player = Join-Path $root '.local/state'
            Copy-Item -LiteralPath (Join-Path $player 'user') -Destination $state -Recurse
            Copy-Item -LiteralPath (Join-Path $player 'cache/shaders') -Destination (Join-Path $state 'cache') -Recurse
        }
        Set-Content -LiteralPath $script -Value '# live session' -Encoding ascii
        $env:FM4_NATIVE_INPUT_SCRIPT = $script
        $env:SDL_WINDOW_ACTIVATE_WHEN_SHOWN = '0'
        & (Join-Path $PSScriptRoot 'run.ps1') -StateRoot $state -GameArguments $GameArguments
        $env:FM4_NATIVE_INPUT_SCRIPT = ''
        $env:SDL_WINDOW_ACTIVATE_WHEN_SHOWN = ''
        # Minimised without activation, so the player's focus stays where it is.
        Add-Type -Namespace Fm4Native -Name Show -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool ShowWindowAsync(System.IntPtr window, int command);
'@
        $process = Get-Game
        $deadline = (Get-Date).AddSeconds(60)
        while ($process.MainWindowHandle -eq [IntPtr]::Zero) {
            if ((Get-Date) -gt $deadline) { throw 'The session game opened no window.' }
            Start-Sleep -Milliseconds 100
            $process.Refresh()
        }
        [void][Fm4Native.Show]::ShowWindowAsync($process.MainWindowHandle, 7)
    }
    'press' {
        $elapsed = ((Get-Date) - (Get-Game).StartTime).TotalSeconds
        $line = '{0:F2} {1:F2} {2}' -f ($elapsed + 0.3), $Hold, $Value
        Add-Content -LiteralPath $script -Value $line.Replace(',', '.') -Encoding ascii
        Start-Sleep -Milliseconds ([int](($Hold + 0.3 + $Wait) * 1000))
    }
    'shot' {
        if ($Wait -gt 0) { Start-Sleep -Milliseconds ([int]($Wait * 1000)) }
        [void](Get-Game)
        # The game writes its own guest output, so the window need not be visible.
        $png = Join-Path $state "shots/$Value.png"
        $bmp = Join-Path $state 'shots/capture.bmp'
        [void](New-Item -ItemType Directory -Force -Path (Split-Path $png -Parent))
        if (Test-Path -LiteralPath $bmp) { Remove-Item -LiteralPath $bmp -Force }
        Set-Content -LiteralPath "$script.shot.tmp" -Value $bmp -Encoding ascii
        Move-Item -LiteralPath "$script.shot.tmp" -Destination "$script.shot" -Force
        $deadline = (Get-Date).AddSeconds(8)
        while (-not (Test-Path -LiteralPath $bmp)) {
            if ((Get-Date) -gt $deadline) { throw 'The game did not write a capture.' }
            Start-Sleep -Milliseconds 100
        }
        Add-Type -AssemblyName System.Drawing
        $image = [System.Drawing.Image]::FromFile($bmp)
        $width = [Math]::Min(1280, $image.Width)
        $scaled = New-Object System.Drawing.Bitmap $image, $width, ([int]($image.Height * $width / $image.Width))
        $image.Dispose()
        $scaled.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
        $scaled.Dispose()
        Remove-Item -LiteralPath $bmp -Force
        $png
    }
    'stop' {
        $process = Get-Game
        $process.Kill()
        $process.WaitForExit()
    }
}
