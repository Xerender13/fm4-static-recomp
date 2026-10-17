[CmdletBinding()]
param(
    [ValidateSet('Release', 'RelWithDebInfo')]
    [string]$Configuration = 'Release',
    [ValidateRange(0, 32)] [int]$Parallel = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

. (Join-Path $PSScriptRoot 'release-common.ps1')
$root = Get-Fm4RepoRoot

$environment = Enter-Fm4BuildEnvironment
$logs = Join-Path $root '.local/logs'
[void](New-Item -ItemType Directory -Force -Path $logs)
if ($Parallel -eq 0) { $Parallel = Get-Fm4BuildJobCount }
$preset = 'win-amd64-' + $Configuration.ToLowerInvariant()

# A running game keeps its executable and libraries open, and the linker cannot replace them.
# Windows still lets a loaded file be renamed: move each aside and leave a copy (same
# timestamp, so nothing relinks for it) under its name. The game goes on running the old
# build; what it leaves behind is removed by the first build after it has closed.
$output = [IO.Path]::GetFullPath((Join-Path $root "out/build/$preset"))
Get-ChildItem -LiteralPath $output -Filter '*.inuse-*' -File -ErrorAction SilentlyContinue |
    ForEach-Object { try { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop } catch {} }
foreach ($process in @(Get-Process fm4_static_recomp -ErrorAction SilentlyContinue)) {
    $loaded = @($process.Modules | ForEach-Object FileName |
        Where-Object { $_.StartsWith($output + '\', [StringComparison]::OrdinalIgnoreCase) })
    foreach ($file in $loaded) {
        $aside = "$file.inuse-$($process.Id)"
        if (Test-Path -LiteralPath $aside) { continue }
        Move-Item -LiteralPath $file -Destination $aside
        Copy-Item -LiteralPath $aside -Destination $file
    }
}

# A build folder copied from another install (an update seeds the new version's folder
# with the old one) keeps a cache CMake refuses.
[void](Reset-Fm4RelocatedCMakeCache -BuildDirectory $output)
Push-Location $root
try {
    # The Vulkan backend's glslang reads its version through Python.
    Invoke-Fm4BuildCommand $environment.CMake @('--preset', $preset,
        "-DPYTHON_EXECUTABLE=$(Get-Fm4Python)", "-DREXSDK_DIR=$(Resolve-Fm4SdkRoot)") `
        (Join-Path $logs 'configure.log') 'Configuration failed.'
    Invoke-Fm4BuildCommand $environment.CMake @('--build', '--preset', $preset, '--parallel', "$Parallel") `
        (Join-Path $logs 'build.log') 'Compilation failed.'
}
finally { Pop-Location }

$executable = Join-Path $root "out/build/$preset/fm4_static_recomp.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Compilation completed without producing fm4_static_recomp.exe.'
}
"built $executable"
