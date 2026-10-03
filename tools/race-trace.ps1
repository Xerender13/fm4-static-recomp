[CmdletBinding()]
param(
    # Names the files left in .local\perf: <Tag>-trace.log, <Tag>-race.png, <Tag>-dumps\.
    [Parameter(Mandatory)] [string]$Tag,
    [string[]]$GameArguments = @(),
    # Environments past the default in the list (17: Road Atlanta).
    [int]$EnvironmentSteps = 0
)

# Drives a test session into a race and dumps one frame: its resolves (guest memory, so
# only meaningful at 1x) and, in the log, every draw, surface transfer and texture load.
# Read the log with frame_timeline.py.
. (Join-Path $PSScriptRoot 'race.ps1')
$dumps = Join-Path $perf "$Tag-dumps"
[void](New-Item -ItemType Directory -Force -Path $dumps)
Start-Race (@("--fh1_resolve_dump_dir=$dumps", '--fh1_resolve_dump_frame=-1') + $GameArguments) -EnvironmentSteps $EnvironmentSteps
Hold-Control 'RT=1' 6
Start-Sleep -Seconds 3
Set-Content -LiteralPath (Join-Path $dumps 'trigger') -Value '' -Encoding ascii
Start-Sleep -Seconds 2
Save-RaceCapture (Join-Path $perf "$Tag-race.png") 1280
Copy-Item -LiteralPath $log -Destination (Join-Path $perf "$Tag-trace.log") -Force
& $session stop
"trace: $(Join-Path $perf "$Tag-trace.log")"
