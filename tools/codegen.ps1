[CmdletBinding()]
param(
    [ValidateRange(0, 32)] [int]$Parallel = 0,
    [switch]$SkipSdkBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

# The toolchain and the ShiftGlue checkout are borrowed from the sibling pinyon-shift tree.
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$pinyonTools = [IO.Path]::GetFullPath((Join-Path $root '../pinyon-shift/tools'))
. (Join-Path $pinyonTools 'release-common.ps1')

$environment = Enter-PinyonBuildEnvironment
$sdkRoot = Resolve-PinyonRexGlueRoot
$rexglueExe = Join-Path $sdkRoot 'out/win-amd64/Release/rexglue.exe'
$manifest = Join-Path $root 'config/rexglue/fm4_manifest.toml'
$logs = Join-Path $root '.local/logs'
[void](New-Item -ItemType Directory -Force -Path $logs)
if ($Parallel -eq 0) { $Parallel = Get-PinyonBuildJobCount }

if (-not $SkipSdkBuild) {
    Push-Location $sdkRoot
    try {
        Invoke-PinyonBuildCommand $environment.CMake @('--preset', 'win-amd64',
            '-DREXGLUE_ENABLE_TRACY=OFF', '-DSDL_HIDAPI_LIBUSB=OFF') `
            (Join-Path $logs 'rexglue-configure.log') 'ReXGlue configuration failed.'
        Invoke-PinyonBuildCommand $environment.CMake @('--build', '--preset', 'win-amd64-release',
            '--target', 'rexglue', '--parallel', "$Parallel") `
            (Join-Path $logs 'rexglue-build.log') 'ReXGlue code-generator build failed.'
    }
    finally { Pop-Location }
}
if (-not (Test-Path -LiteralPath $rexglueExe -PathType Leaf)) {
    throw "The ReXGlue code generator was not produced: $rexglueExe"
}

$codegenLog = Join-Path $logs 'codegen.log'
if (Test-Path -LiteralPath $codegenLog) { Remove-Item -LiteralPath $codegenLog -Force }
& $rexglueExe --log-level info --log-file $codegenLog codegen $manifest
"codegen exit code: $LASTEXITCODE"
exit $LASTEXITCODE
