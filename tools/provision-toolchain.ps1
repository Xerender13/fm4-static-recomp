# Adapted from Pinyon Shift's tools/provision-toolchain.ps1 (BSD-3, see THIRD_PARTY_NOTICES.md).
[CmdletBinding()]
param([switch]$JsonEvents)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

. (Join-Path $PSScriptRoot 'release-common.ps1')
$config = Get-Fm4ReleaseToolchain
$downloads = Resolve-Fm4ToolchainPath -RelativePath 'downloads'
[void](New-Item -ItemType Directory -Force -Path $downloads)

Write-Fm4Event tools 18 'Checking the Windows build environment.' -JsonEvents:$JsonEvents
$vsRoot = Get-Fm4VisualStudioRoot -AllowMissing
if ([string]::IsNullOrWhiteSpace($vsRoot)) {
    Write-Fm4Event tools 20 'Microsoft C++ Build Tools are required. Windows may ask for administrator permission.' -JsonEvents:$JsonEvents
    $bootstrap = Join-Path $downloads 'vs_BuildTools.exe'
    if (-not (Test-Path -LiteralPath $bootstrap -PathType Leaf)) {
        $partial = "$bootstrap.partial"
        Invoke-WebRequest -Uri $config.visual_studio.bootstrap_url -OutFile $partial -UseBasicParsing
        Move-Item -LiteralPath $partial -Destination $bootstrap -Force
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $bootstrap
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.Subject -notlike "*$($config.visual_studio.bootstrap_signer)*") {
        Remove-Item -LiteralPath $bootstrap -Force
        throw 'The Microsoft Build Tools installer signature is invalid.'
    }
    $helper = Join-Path $PSScriptRoot 'install-build-tools.ps1'
    $arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$helper`" -Bootstrapper `"$bootstrap`""
    # The running PowerShell by its own path, since PATH may not hold it. Process.Start keeps
    # the Win32 error that Start-Process drops, so a declined permission prompt
    # (ERROR_CANCELLED) can be told apart from a failed installer.
    $startInfo = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path, $arguments)
    $startInfo.Verb = 'runas'
    $startInfo.UseShellExecute = $true
    try {
        $elevated = [Diagnostics.Process]::Start($startInfo)
    }
    catch {
        $win32 = $_.Exception.InnerException -as [ComponentModel.Win32Exception]
        if ($null -ne $win32 -and $win32.NativeErrorCode -eq 1223) {
            throw ('Windows administrator permission was declined, so the Microsoft C++ Build Tools ' +
                'were not installed. The build needs them: start the build again and choose Yes when ' +
                'Windows asks for permission.')
        }
        throw
    }
    $elevated.WaitForExit()
    if ($elevated.ExitCode -notin @(0, 3010)) {
        throw "Microsoft C++ Build Tools installation stopped with exit code $($elevated.ExitCode)."
    }
    $vsRoot = Get-Fm4VisualStudioRoot
}

Write-Fm4Event tools 23 'Preparing Git.' -JsonEvents:$JsonEvents
$gitRoot = Resolve-Fm4ToolchainPath -RelativePath $config.git.install_path
$gitExe = Join-Path $gitRoot $config.git.executable
if (-not (Test-Path -LiteralPath $gitExe -PathType Leaf)) {
    $archive = Join-Path $downloads "mingit-$($config.git.version).zip"
    Invoke-Fm4Download -Uri $config.git.url -Destination $archive -Sha256 $config.git.sha256
    [void](New-Item -ItemType Directory -Force -Path $gitRoot)
    Expand-Archive -LiteralPath $archive -DestinationPath $gitRoot -Force
}

Write-Fm4Event tools 26 'Preparing the pinned LLVM compiler.' -JsonEvents:$JsonEvents
$xzRoot = Resolve-Fm4ToolchainPath -RelativePath $config.xz.install_path
$xzExe = Join-Path $xzRoot $config.xz.executable
if (-not (Test-Path -LiteralPath $xzExe -PathType Leaf)) {
    $archive = Join-Path $downloads "xz-$($config.xz.version)-windows.zip"
    Invoke-Fm4Download -Uri $config.xz.url -Destination $archive -Sha256 $config.xz.sha256
    if (Test-Path -LiteralPath $xzRoot) { Remove-Item -LiteralPath $xzRoot -Recurse -Force }
    [void](New-Item -ItemType Directory -Force -Path $xzRoot)
    Expand-Archive -LiteralPath $archive -DestinationPath $xzRoot -Force
}

$llvmRoot = Resolve-Fm4ToolchainPath -RelativePath $config.llvm.install_path
$llvmExe = Join-Path $llvmRoot $config.llvm.executable
if (-not (Test-Path -LiteralPath $llvmExe -PathType Leaf)) {
    $archive = Join-Path $downloads "llvm-$($config.llvm.version).tar.xz"
    Invoke-Fm4Download -Uri $config.llvm.url -Destination $archive -Sha256 $config.llvm.sha256
    $staging = Resolve-Fm4ToolchainPath -RelativePath "llvm-$($config.llvm.version)-staging"
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    [void](New-Item -ItemType Directory -Force -Path $staging)
    Expand-Fm4TarXz -Archive $archive -Destination $staging -XzPath $xzExe
    $children = @(Get-ChildItem -LiteralPath $staging -Directory)
    if ($children.Count -ne 1) { throw 'The LLVM archive layout is not recognized.' }
    if (Test-Path -LiteralPath $llvmRoot) { Remove-Item -LiteralPath $llvmRoot -Recurse -Force }
    Move-Item -LiteralPath $children[0].FullName -Destination $llvmRoot
    Remove-Item -LiteralPath $staging -Recurse -Force
}

Write-Fm4Event tools 30 'Preparing the local shader tooling runtime.' -JsonEvents:$JsonEvents
$pythonRoot = Resolve-Fm4ToolchainPath -RelativePath $config.python.install_path
$pythonExe = Join-Path $pythonRoot $config.python.executable
$pythonCheck = "import hashlib, json, struct, subprocess, tomllib, zipfile; assert hasattr(hashlib, 'file_digest')"
$pythonReady = $false
if (Test-Path -LiteralPath $pythonExe -PathType Leaf) {
    & $pythonExe -I -c $pythonCheck
    $pythonReady = $LASTEXITCODE -eq 0
}
if (-not $pythonReady) {
    $archive = Join-Path $downloads "python-$($config.python.version)-embed-amd64.zip"
    Invoke-Fm4Download -Uri $config.python.url -Destination $archive -Sha256 $config.python.sha256
    [void](New-Item -ItemType Directory -Force -Path $pythonRoot)
    Expand-Archive -LiteralPath $archive -DestinationPath $pythonRoot -Force
}
& $pythonExe -I -c $pythonCheck
if ($LASTEXITCODE -ne 0) { throw 'The local Python runtime failed its shader tooling check.' }

Write-Fm4Event tools 31 'Preparing CMake with support for the build presets.' -JsonEvents:$JsonEvents
$cmakeRoot = Resolve-Fm4ToolchainPath -RelativePath $config.cmake.install_path
if (-not (Test-Path -LiteralPath (Join-Path $cmakeRoot $config.cmake.executable) -PathType Leaf)) {
    $archive = Join-Path $downloads "cmake-$($config.cmake.version).zip"
    Invoke-Fm4Download -Uri $config.cmake.url -Destination $archive -Sha256 $config.cmake.sha256
    Expand-Archive -LiteralPath $archive -DestinationPath (Split-Path $cmakeRoot -Parent) -Force
}
$environment = Enter-Fm4BuildEnvironment
$git = Get-Fm4Git
foreach ($required in @(
    @{ Name = 'Git'; Path = $git },
    @{ Name = 'XZ'; Path = $xzExe },
    @{ Name = 'CMake'; Path = $environment.CMake },
    @{ Name = 'Ninja'; Path = $environment.Ninja },
    @{ Name = 'LLVM'; Path = $llvmExe },
    @{ Name = 'Python'; Path = $pythonExe }
)) {
    if (-not (Test-Path -LiteralPath $required.Path -PathType Leaf)) {
        throw "$($required.Name) was not provisioned correctly: $($required.Path)"
    }
}
Write-Fm4Event tools 32 'Windows build tools are ready.' -JsonEvents:$JsonEvents

[pscustomobject]@{
    result = 'pass'
    git = $git
    cmake = $environment.CMake
    ninja = $environment.Ninja
    llvm = $llvmExe
    python = $pythonExe
}
