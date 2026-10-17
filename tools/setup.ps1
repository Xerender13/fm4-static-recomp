# Builds FM4 Native from this source folder for a verified, extracted Disc 1 (the launcher's
# build step). The launcher reads the ::fm4:: event lines.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$GameRoot,
    # The player's state whose shader cache the warm-up fills, as run.ps1 takes it.
    [string]$StateRoot,
    [switch]$SkipWarmUp,
    # Parallel compile jobs; 0 picks them from the processor count and memory.
    [ValidateRange(0, 32)] [int]$Parallel = 0,
    [switch]$JsonEvents
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}
. (Join-Path $PSScriptRoot 'release-common.ps1')
$root = Get-Fm4RepoRoot
$logs = Resolve-Fm4LocalPath -RelativePath '.local/logs'
[void](New-Item -ItemType Directory -Force -Path $logs)
$errorPath = Join-Path $logs 'setup-error.json'
if (Test-Path -LiteralPath $errorPath) { Remove-Item -LiteralPath $errorPath -Force }
try {
    # Only a game started from this folder's build locks its files: an update builds a new
    # version's folder while the player keeps playing the previous one.
    $buildTree = [IO.Path]::GetFullPath((Join-Path $root 'out/build')).TrimEnd('\', '/') +
        [IO.Path]::DirectorySeparatorChar
    $locking = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $path = $null
        try { $path = $_.Path } catch { }
        $path -and $path.StartsWith($buildTree, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($locking.Count -gt 0) {
        throw 'Close FM4 Native before building this version. Windows locks its files while it runs.'
    }
    & (Join-Path $PSScriptRoot 'provision-toolchain.ps1') -JsonEvents:$JsonEvents | Out-Host
    Write-Fm4Event sdk 35 'Preparing the ShiftGlue SDK.' -JsonEvents:$JsonEvents
    & (Join-Path $PSScriptRoot 'prepare-sdk.ps1') -JsonEvents:$JsonEvents | Out-Host
    Write-Fm4Event codegen 45 'Translating the game code. This takes several minutes.' -JsonEvents:$JsonEvents
    & (Join-Path $PSScriptRoot 'codegen.ps1') -GameRoot $GameRoot -Parallel $Parallel | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw (New-Fm4CommandFailure -FailureMessage 'Translating the game code failed.' -Step 'codegen' `
            -LogPath (Join-Path $logs 'codegen.log') -ExitCode $LASTEXITCODE)
    }
    Write-Fm4Event build 70 'Compiling FM4 Native.' -JsonEvents:$JsonEvents
    & (Join-Path $PSScriptRoot 'build.ps1') -Parallel $Parallel | Out-Host
    Write-Fm4Event schema 97 'Reading the graphics settings.' -JsonEvents:$JsonEvents
    Write-Fm4GraphicsSchema -ExecutableDirectory (Join-Path $root 'out/build/win-amd64-release') `
        -GameRoot $GameRoot -LogPath (Join-Path $logs 'schema.log')
    Write-Fm4Event schema 100 'FM4 Native is built.' -JsonEvents:$JsonEvents
}
catch {
    # Plain text and an exit code: Write-Error under 'Stop' would raise a second error whose
    # position hides the real step (Pinyon's setup-preview.ps1 learnt this).
    $failure = $_
    try {
        $record = New-Fm4FailureRecord -ErrorRecord $failure
        $record.system = Get-Fm4SystemSummary -Root $root
        [IO.File]::WriteAllText($errorPath, ($record | ConvertTo-Json -Depth 4) + [Environment]::NewLine,
            [Text.UTF8Encoding]::new($false))
        foreach ($line in (Format-Fm4FailureRecord -Record $record)) { [Console]::Out.WriteLine($line) }
        [Console]::Out.WriteLine("Failure details: $errorPath")
    }
    catch {
        [Console]::Out.WriteLine("Setup failed: $($failure.Exception.Message)")
        [Console]::Out.WriteLine("The failure report could not be written: $($_.Exception.Message)")
    }
    [Console]::Out.Flush()
    [Console]::Error.WriteLine("Setup failed: $($failure.Exception.Message)")
    exit 1
}

# The build is done whatever the warm-up does: without it the game compiles shaders in play.
if (-not $SkipWarmUp) {
    $warmUp = @{ GameRoot = $GameRoot; IfNeeded = $true; JsonEvents = $JsonEvents }
    if ($StateRoot) { $warmUp.StateRoot = $StateRoot }
    $warmedUp = $false
    try {
        & (Join-Path $PSScriptRoot 'warm-up.ps1') @warmUp | Out-Host
        $warmedUp = $LASTEXITCODE -eq 0
    }
    catch { [Console]::Out.WriteLine("Shader warm-up: $($_.Exception.Message)") }
    if (-not $warmedUp) {
        [Console]::Out.WriteLine('The shader warm-up did not finish; the game will compile shaders while you play.')
    }
}
exit 0
