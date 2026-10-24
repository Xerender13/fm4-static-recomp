# Builds FM4 Static Recomp from this source folder for a verified, extracted Disc 1 (the launcher's
# build step). The launcher reads the ::fm4:: event lines.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$GameRoot,
    # The player's state whose shader cache setup fills, as run.ps1 takes it.
    [string]$StateRoot,
    # The launcher's per-version cache folder instead: the shader cache, the UI fonts and the
    # tool runs' scratch user folder go there, and -StateRoot is not used.
    [string]$CacheRoot,
    [switch]$SkipShaderCache,
    # Parallel compile jobs; 0 picks them from the processor count and memory.
    [ValidateRange(0, 32)] [int]$Parallel = 0,
    # The player accepted the licences of the Microsoft files the build downloads
    # (tools\microsoft-licences.ps1 names them).
    [switch]$AcceptMicrosoftLicense,
    # VisualStudio: Microsoft's installer instead (asks for administrator permission once).
    [ValidateSet('Feed', 'VisualStudio')] [string]$ToolchainSource = 'Feed',
    [switch]$JsonEvents
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}
. (Join-Path $PSScriptRoot 'release-common.ps1')
$root = Get-Fm4RepoRoot
if (-not $StateRoot) { $StateRoot = Join-Path $root '.local/state' }
$StateRoot = [IO.Path]::GetFullPath($StateRoot)
if ($CacheRoot) {
    $CacheRoot = [IO.Path]::GetFullPath($CacheRoot)
    $toolUserRoot = Join-Path $CacheRoot 'tool-user'
}
else {
    $CacheRoot = Join-Path $StateRoot 'cache'
    $toolUserRoot = Join-Path $StateRoot 'user'
}
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
        throw 'Close FM4 Static Recomp before building this version. Windows locks its files while it runs.'
    }
    & (Join-Path $PSScriptRoot 'provision-toolchain.ps1') -AcceptMicrosoftLicense:$AcceptMicrosoftLicense `
        -ToolchainSource $ToolchainSource -JsonEvents:$JsonEvents | Out-Host
    Write-Fm4Event sdk 35 'Preparing the ShiftGlue SDK.' -JsonEvents:$JsonEvents
    & (Join-Path $PSScriptRoot 'prepare-sdk.ps1') -JsonEvents:$JsonEvents | Out-Host
    Write-Fm4Event codegen 45 'Translating the game code. This takes several minutes.' -JsonEvents:$JsonEvents
    & (Join-Path $PSScriptRoot 'codegen.ps1') -GameRoot $GameRoot -Parallel $Parallel | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw (New-Fm4CommandFailure -FailureMessage 'Translating the game code failed.' -Step 'codegen' `
            -LogPath (Join-Path $logs 'codegen.log') -ExitCode $LASTEXITCODE)
    }
    & (Join-Path $PSScriptRoot 'build.ps1') -Parallel $Parallel -JsonEvents:$JsonEvents | Out-Host
    Write-Fm4Event schema 97 'Reading the graphics settings.' -JsonEvents:$JsonEvents
    Write-Fm4GraphicsSchema -ExecutableDirectory (Join-Path $root 'out/build/win-amd64-release') `
        -GameRoot $GameRoot -LogPath (Join-Path $logs 'schema.log')
    Write-Fm4Event schema 100 'FM4 Static Recomp is built.' -JsonEvents:$JsonEvents
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

# The build is done whatever the fonts and shader cache steps do: without the fonts the launcher
# keeps its own, without the cache the game compiles shaders in play.
$executableDirectory = Join-Path $root 'out/build/win-amd64-release'
try {
    $line = Write-Fm4UiFonts -ExecutableDirectory $executableDirectory -GameRoot $GameRoot `
        -OutDirectory (Join-Path $CacheRoot 'ui-fonts') -LogPath (Join-Path $logs 'fonts.log')
}
catch {
    $line = "$($_.Exception.Message) The launcher uses its own font."
}
Write-Fm4Event fonts 100 $line -JsonEvents:$JsonEvents

if (-not $SkipShaderCache) {
    Write-Fm4Event shaders 0 'Building the shader cache from the disc.' -JsonEvents:$JsonEvents
    try {
        $line = Write-Fm4ShaderCache -ExecutableDirectory $executableDirectory -GameRoot $GameRoot `
            -CacheRoot $CacheRoot -UserRoot $toolUserRoot -LogPath (Join-Path $logs 'shaders.log')
    }
    catch {
        $line = "$($_.Exception.Message) The game will compile shaders while you play."
    }
    Write-Fm4Event shaders 100 $line -JsonEvents:$JsonEvents
}
exit 0
