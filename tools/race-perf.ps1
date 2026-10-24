[CmdletBinding()]
param(
    # Names the files left in .local\perf: <Tag>-gpu.csv, -threads.txt, -race.png, -runtime.log.
    [Parameter(Mandatory)] [string]$Tag,
    [string[]]$GameArguments = @(),
    # Environments past the default in the list (17: Road Atlanta).
    [int]$EnvironmentSteps = 0,
    [int]$Seconds = 12
)

# Drives a test session into a race and, at full throttle for $Seconds, samples GPU
# utilisation, memory and power (nvidia-smi) and per-thread CPU time; then prints the
# summary (perf_summary.py). Add --fh1_native_gpu_profile=true for the GPU time per phase.
. (Join-Path $PSScriptRoot 'race.ps1')

function Get-ThreadTimes {
    $snapshot = @{}
    foreach ($thread in (Get-RaceGame).Threads) {
        try { $snapshot[$thread.Id] = $thread.TotalProcessorTime.TotalSeconds } catch {}
    }
    $snapshot
}

Start-Race $GameArguments -EnvironmentSteps $EnvironmentSteps
Hold-Control 'RT=1' ($Seconds + 3)
Start-Sleep -Seconds 2
$smi = Start-Process nvidia-smi -ArgumentList '--query-gpu=utilization.gpu,memory.used,power.draw',
    '--format=csv,noheader,nounits', '-lms', '500' -RedirectStandardOutput (Join-Path $perf "$Tag-gpu.csv") `
    -PassThru -WindowStyle Hidden
$before = Get-ThreadTimes
$start = Get-Date
Start-Sleep -Seconds $Seconds
$after = Get-ThreadTimes
$elapsed = ((Get-Date) - $start).TotalSeconds
# nvidia-smi counts every process's memory, a game the user has open included; this is the session's own.
$dedicated = (Get-Counter "\GPU Process Memory(pid_$((Get-RaceGame).Id)_*)\Dedicated Usage").CounterSamples |
    Measure-Object CookedValue -Sum
'session VRAM {0:N0} MB' -f ($dedicated.Sum / 1MB)
Stop-Process -Id $smi.Id -Force
$rows = foreach ($id in $after.Keys) {
    if ($before.ContainsKey($id)) {
        [pscustomobject]@{ Thread = $id; Percent = [Math]::Round(($after[$id] - $before[$id]) / $elapsed * 100, 1) }
    }
}
$threads = Join-Path $perf "$Tag-threads.txt"
$rows | Sort-Object Percent -Descending | Select-Object -First 14 |
    ForEach-Object { '{0,6} {1,6}' -f $_.Thread, $_.Percent } | Set-Content -LiteralPath $threads
Add-Content -LiteralPath $threads -Value ('window {0} to {1}' -f $start.ToString('HH:mm:ss.fff'), (Get-Date).ToString('HH:mm:ss.fff'))
Save-RaceCapture (Join-Path $perf "$Tag-race.png") 640
# The 600-frame stats window that covers the sample is logged a few seconds later.
Start-Sleep -Seconds 6
Copy-Item -LiteralPath $log -Destination (Join-Path $perf "$Tag-runtime.log") -Force
& $session stop
& "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe" (Join-Path $PSScriptRoot 'perf_summary.py') $perf $Tag
