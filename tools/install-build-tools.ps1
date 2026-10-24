# Adapted from Pinyon Shift's tools/install-build-tools.ps1 (BSD-3, see THIRD_PARTY_NOTICES.md).
[CmdletBinding()]
param([Parameter(Mandatory)] [string]$Bootstrapper)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')
$config = Get-Fm4ReleaseToolchain

if (-not (Test-Path -LiteralPath $Bootstrapper -PathType Leaf)) {
    throw "Build Tools bootstrapper is missing: $Bootstrapper"
}

Write-Host 'Installing the Microsoft C++ Build Tools (a download of about 2 GB, often 10 minutes or more).'
Write-Host 'The installer shows its own progress window. Keep this window open; it closes when the installation ends.'
# --passive rather than --quiet: with no visible progress, players closed this window believing it hung.
# The pinned toolset side by side and the pinned Windows SDK: the default toolset moves on.
$arguments = @(
    '--passive', '--wait', '--norestart', '--nocache',
    '--add', $config.visual_studio.workload,
    '--includeRecommended'
)
foreach ($component in $config.visual_studio.components) { $arguments += @('--add', $component) }
$process = Start-Process -FilePath $Bootstrapper -ArgumentList $arguments -Wait -PassThru
if ($process.ExitCode -notin @(0, 3010)) {
    throw "Microsoft C++ Build Tools installation failed with exit code $($process.ExitCode)."
}
exit $process.ExitCode
