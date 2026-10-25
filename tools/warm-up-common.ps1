# Dot-source this: helpers for the shader warm-up (warm-up.ps1), which plays a scripted route
# in a game nobody sees so that a new install starts with a filled shader cache.
Set-StrictMode -Version Latest

$script:Fm4ToolsRoot = $PSScriptRoot

# .NET resolves relative paths against the process folder, not the PowerShell location.
function Resolve-Fm4Path {
    param([Parameter(Mandatory)] [string]$Path)
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

# The run's own game, found by its state folder: the player may have theirs open.
function Get-Fm4StateGame {
    param([Parameter(Mandatory)] [string]$StateRoot)
    $marker = Resolve-Fm4Path (Join-Path $StateRoot 'user')
    Get-CimInstance Win32_Process -Filter "Name='fm4_static_recomp.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0 } |
        Sort-Object CreationDate | Select-Object -Last 1
}

# A job that ends its processes when its last handle closes: this PowerShell holds the handle, so
# the hidden game cannot outlive a console that is closed or killed mid-run.
if (-not ('Fm4StaticRecomp.KillOnCloseJob' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace Fm4StaticRecomp {
public static class KillOnCloseJob {
    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimits {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimits {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, int length);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    static IntPtr job;
    public static void Add(IntPtr process) {
        if (job == IntPtr.Zero) {
            IntPtr created = CreateJobObject(IntPtr.Zero, null);
            if (created == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            ExtendedLimits limits = new ExtendedLimits();
            limits.Basic.LimitFlags = 0x2000;  // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(created, 9, ref limits, Marshal.SizeOf(typeof(ExtendedLimits))))
                throw new System.ComponentModel.Win32Exception();
            job = created;
        }
        if (!AssignProcessToJobObject(job, process)) throw new System.ComponentModel.Win32Exception();
    }
}
}
'@
}

function Start-Fm4HiddenGame {
    param(
        [Parameter(Mandatory)] [string]$StateRoot,
        [Parameter(Mandatory)] [string]$GameRoot,
        [string]$InputScript,
        [string[]]$GameArguments = @()
    )
    $names = 'REX_WINDOW_HIDDEN', 'SDL_WINDOW_ACTIVATE_WHEN_SHOWN', 'FM4_STATIC_RECOMP_INPUT_SCRIPT'
    $saved = @{}
    foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
    try {
        $env:REX_WINDOW_HIDDEN = '1'
        $env:SDL_WINDOW_ACTIVATE_WHEN_SHOWN = '0'
        $env:FM4_STATIC_RECOMP_INPUT_SCRIPT = $InputScript
        & (Join-Path $script:Fm4ToolsRoot 'run.ps1') -StateRoot $StateRoot -GameRoot $GameRoot `
            -GameArguments (@('--audio_mute=true') + $GameArguments) | Out-Null
    }
    finally {
        foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
    $own = Get-Fm4StateGame -StateRoot $StateRoot
    if (-not $own) { throw 'The warm-up game did not start.' }
    $game = Get-Process -Id $own.ProcessId
    [Fm4StaticRecomp.KillOnCloseJob]::Add($game.Handle)
    $game
}

# A game a killed run left behind on its own work state: nothing else uses that folder.
function Stop-Fm4LeftoverGame {
    param([Parameter(Mandatory)] [string]$StateRoot)
    while ($left = Get-Fm4StateGame -StateRoot $StateRoot) {
        $process = Get-Process -Id $left.ProcessId -ErrorAction SilentlyContinue
        if ($process) { Stop-Fm4Game -Process $process }
    }
}

# The run's throwaway state, whose shader folder links to the player's: the link goes first, so
# that the recursive delete cannot reach the player's cache through it.
function Remove-Fm4WarmUpWork {
    param([Parameter(Mandatory)] [string]$Path)
    $link = Join-Path $Path 'cache\shaders'
    if ((Test-Path -LiteralPath $link) -and
        ((Get-Item -LiteralPath $link -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        [IO.Directory]::Delete($link)
    }
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
}

function Stop-Fm4Game {
    param([Parameter(Mandatory)] [Diagnostics.Process]$Process)
    if (-not $Process.HasExited) {
        try { $Process.Kill() } catch [InvalidOperationException] { }
    }
    $Process.WaitForExit()
}

function Get-Fm4RunLog {
    param([Parameter(Mandatory)] [string]$StateRoot)
    Join-Path $StateRoot 'logs\runtime.log'
}

function Test-Fm4LogLine {
    param(
        [Parameter(Mandatory)] [string]$StateRoot,
        [Parameter(Mandatory)] [string]$Pattern,
        [datetime]$Since = [datetime]::MinValue
    )
    $log = Resolve-Fm4Path (Get-Fm4RunLog -StateRoot $StateRoot)
    if (-not (Test-Path -LiteralPath $log)) { return $false }
    foreach ($match in @(Select-String -LiteralPath $log -Pattern $Pattern)) {
        if ($match.Line -match '^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\]' -and
            [datetime]::ParseExact($Matches[1], 'yyyy-MM-dd HH:mm:ss.fff', $null) -ge $Since) {
            return $true
        }
    }
    $false
}

function Wait-Fm4LogLine {
    param(
        [Parameter(Mandatory)] [string]$StateRoot,
        [Parameter(Mandatory)] [string]$Pattern,
        [Parameter(Mandatory)] [int]$TimeoutSeconds,
        [Parameter(Mandatory)] [Diagnostics.Process]$Game,
        [datetime]$Since = [datetime]::MinValue
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($Game.HasExited) { throw "The game exited (code $($Game.ExitCode))." }
        if (Test-Fm4LogLine -StateRoot $StateRoot -Pattern $Pattern -Since $Since) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out after $TimeoutSeconds s waiting for: $Pattern"
}

# The SDK's shader cache (vulkan\pipeline_cache.cpp): guest microcode behind an 8-byte header,
# pipeline descriptions behind a 12-byte one. The game discards a file whose header differs from
# the one it writes (an SDK version, or the EDRAM path a GPU takes), so only an equal header
# makes a cache worth keeping or adding to. The pipeline file is named after the EDRAM path
# (fbo or fsi, chosen per GPU), so its name comes from the probe.
$script:Fm4ShaderFile = '4D530910.xsh'
$script:Fm4PipelinePattern = '4D530910.*.vk.xpso'
$script:Fm4StoredPipelineBytes = 68

function Read-Fm4FileStart {
    param([string]$Path, [int]$Count)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $stream = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
    try {
        $bytes = New-Object byte[] $Count
        if ($stream.Read($bytes, 0, $Count) -ne $Count) { return $null }
        , $bytes
    }
    finally { $stream.Dispose() }
}

# The game opens its cache files exclusively, so a running game shows as a failed read.
function Test-Fm4FileLocked {
    param([Parameter(Mandatory)] [string]$Path)
    $full = Resolve-Fm4Path $Path
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { return $false }
    try { [IO.File]::Open($full, 'Open', 'Read', 'ReadWrite').Dispose(); $false }
    catch [IO.IOException], [UnauthorizedAccessException] { $true }
}

# CacheRoot is the folder holding shaders\ (a state's cache folder).
function Get-Fm4ShaderCacheHeaders {
    param(
        [Parameter(Mandatory)] [string]$CacheRoot,
        [Parameter(Mandatory)] [string]$PipelineFile
    )
    $folder = Resolve-Fm4Path (Join-Path $CacheRoot 'shaders\shareable')
    $pipelines = Join-Path $folder $PipelineFile
    $count = 0
    if (Test-Path -LiteralPath $pipelines -PathType Leaf) {
        $count = [int][Math]::Floor([Math]::Max(0, (Get-Item -LiteralPath $pipelines).Length - 12) /
            $script:Fm4StoredPipelineBytes)
    }
    [pscustomobject]@{
        Shaders       = Read-Fm4FileStart -Path (Join-Path $folder $script:Fm4ShaderFile) -Count 8
        Pipelines     = Read-Fm4FileStart -Path $pipelines -Count 12
        PipelineCount = $count
        PipelineFile  = $PipelineFile
    }
}

function Get-Fm4FileLength {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path -PathType Leaf) { (Get-Item -LiteralPath $Path).Length } else { 0 }
}

# The headers this build writes on this GPU, from a short hidden launch on an empty cache. The
# game holds both files open exclusively, so they are read once it has been stopped; their sizes
# show when the first records (and so the headers) have been flushed.
function Get-Fm4CurrentCacheHeaders {
    param([Parameter(Mandatory)] [string]$GameRoot)
    $state = Join-Path (Split-Path $script:Fm4ToolsRoot -Parent) '.local\warm-up-probe'
    Stop-Fm4LeftoverGame -StateRoot $state
    if (Test-Path -LiteralPath $state) { Remove-Item -LiteralPath $state -Recurse -Force }
    $folder = Join-Path $state 'cache\shaders\shareable'
    try {
        $game = Start-Fm4HiddenGame -StateRoot $state -GameRoot $GameRoot
        try {
            $deadline = (Get-Date).AddSeconds(120)
            while ($true) {
                $pipelines = Get-ChildItem -LiteralPath $folder -Filter $script:Fm4PipelinePattern -File -ErrorAction SilentlyContinue |
                    Select-Object -First 1
                if ((Get-Fm4FileLength (Join-Path $folder $script:Fm4ShaderFile)) -ge 20 -and $pipelines -and
                    $pipelines.Length -ge (12 + $script:Fm4StoredPipelineBytes)) { break }
                # Windows may report an open file's size as 0 until it is closed; by the executor's
                # first frame report (600 frames) the first records are written.
                if ($pipelines -and (Test-Fm4LogLine -StateRoot $state -Pattern 'native executor \(Vulkan\) frame=')) { break }
                if ($game.HasExited) { throw "The game exited (code $($game.ExitCode)) before writing its shader cache." }
                if ((Get-Date) -gt $deadline) { throw 'The game wrote no shader cache within 120 s.' }
                Start-Sleep -Milliseconds 500
            }
        }
        finally { Stop-Fm4Game -Process $game }
        Get-Fm4ShaderCacheHeaders -CacheRoot (Join-Path $state 'cache') -PipelineFile $pipelines.Name
    }
    finally { Remove-Item -LiteralPath $state -Recurse -Force -ErrorAction SilentlyContinue }
}

function Test-Fm4ShaderCacheUsable {
    param(
        [Parameter(Mandatory)] [string]$CacheRoot,
        [Parameter(Mandatory)] $Current
    )
    $headers = Get-Fm4ShaderCacheHeaders -CacheRoot $CacheRoot -PipelineFile $Current.PipelineFile
    $headers.Shaders -and $headers.Pipelines -and $headers.PipelineCount -gt 0 -and
        [Linq.Enumerable]::SequenceEqual([byte[]]$headers.Shaders, [byte[]]$Current.Shaders) -and
        [Linq.Enumerable]::SequenceEqual([byte[]]$headers.Pipelines, [byte[]]$Current.Pipelines)
}


# A press on the scripted gamepad (src\scripted_input.h): the game re-reads the input file and
# plays "<start> <hold> <control>" lines, times counted from its process start.
function Send-Fm4Press {
    param(
        [Parameter(Mandatory)] [Diagnostics.Process]$Game,
        [Parameter(Mandatory)] [string]$InputScript,
        [Parameter(Mandatory)] [string]$Control,
        [double]$Hold = 0.1,
        [double]$Wait = 0,
        # Returns at once, so that other presses can come during the hold.
        [switch]$NoWait
    )
    $start = ((Get-Date) - $Game.StartTime).TotalSeconds + 0.3
    $line = ('{0:F2} {1:F2} {2}' -f $start, $Hold, $Control).Replace(',', '.')
    Add-Content -LiteralPath (Resolve-Fm4Path $InputScript) -Value $line -Encoding ascii
    if (-not $NoWait) { Start-Sleep -Milliseconds ([int](($Hold + 0.3 + $Wait) * 1000)) }
}


# The last time the log names a pipeline being created, from its tail; MinValue when none.
function Get-Fm4LastPipelineCreation {
    param([Parameter(Mandatory)] [string]$StateRoot)
    $log = Resolve-Fm4Path (Get-Fm4RunLog -StateRoot $StateRoot)
    if (-not (Test-Path -LiteralPath $log)) { return [datetime]::MinValue }
    $stream = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    try {
        $count = [int][Math]::Min($stream.Length, 262144)
        [void]$stream.Seek(-$count, 'End')
        $bytes = New-Object byte[] $count
        $read = $stream.Read($bytes, 0, $count)
    }
    finally { $stream.Dispose() }
    $text = [Text.Encoding]::UTF8.GetString($bytes, 0, $read)
    $found = [regex]::Matches($text, '\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\][^\n]*Creating graphics pipeline')
    if ($found.Count -eq 0) { return [datetime]::MinValue }
    [datetime]::ParseExact($found[$found.Count - 1].Groups[1].Value, 'yyyy-MM-dd HH:mm:ss.fff', $null)
}

# On a cold cache every new screen compiles shaders first: waits at least $MinSeconds, then
# until no pipeline has been created for $QuietSeconds.
function Wait-Fm4Settled {
    param(
        [Parameter(Mandatory)] [string]$StateRoot,
        [Parameter(Mandatory)] [double]$MinSeconds,
        [Parameter(Mandatory)] [Diagnostics.Process]$Game,
        [double]$QuietSeconds = 1.5,
        [int]$TimeoutSeconds = 90
    )
    Start-Sleep -Milliseconds ([int]($MinSeconds * 1000))
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($true) {
        if ($Game.HasExited) { throw "The game exited (code $($Game.ExitCode))." }
        if (((Get-Date) - (Get-Fm4LastPipelineCreation -StateRoot $StateRoot)).TotalSeconds -ge $QuietSeconds) { return }
        if ((Get-Date) -gt $deadline) { throw "The game was still compiling shaders after $TimeoutSeconds s." }
        Start-Sleep -Milliseconds 250
    }
}
