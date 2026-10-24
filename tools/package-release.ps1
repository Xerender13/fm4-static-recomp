# Builds the release assets into .artifacts\release: the launcher, the source payload, their
# checksums, and FM4StaticRecomp-Launcher.zip, the one file players download.
[CmdletBinding()]
param(
    [string]$Dotnet = (Join-Path $HOME '.dotnet\dotnet.exe'),
    # Extra MSBuild properties for the publish, e.g. '-p:PublishTrimmed=true'.
    [string[]]$PublishProperties = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$release = Get-Content -LiteralPath (Join-Path $root 'config/release.json') -Raw | ConvertFrom-Json
$out = Join-Path $root '.artifacts/release'
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
[void](New-Item -ItemType Directory -Force -Path $out)

& (Join-Path $PSScriptRoot 'package-source.ps1') | Out-Null
Copy-Item -LiteralPath (Join-Path $root '.artifacts/fm4-static-recomp-source.zip') -Destination $out

$publish = Join-Path $root '.artifacts/launcher-publish'
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
& $Dotnet publish (Join-Path $root 'launcher/Fm4StaticRecomp.Launcher') -c Release -r win-x64 -o $publish `
    "-p:Version=$($release.version)" @PublishProperties
if ($LASTEXITCODE -ne 0) { throw 'Publishing the launcher failed.' }
Copy-Item -LiteralPath (Join-Path $publish 'FM4StaticRecompLauncher.exe') -Destination $out

$sums = foreach ($name in 'FM4StaticRecompLauncher.exe', 'fm4-static-recomp-source.zip') {
    "$((Get-FileHash -LiteralPath (Join-Path $out $name) -Algorithm SHA256).Hash.ToUpperInvariant())  $name"
}
[IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS.txt'), ($sums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))

$zipFolder = Join-Path $root '.artifacts/launcher-zip'
if (Test-Path -LiteralPath $zipFolder) { Remove-Item -LiteralPath $zipFolder -Recurse -Force }
[void](New-Item -ItemType Directory -Force -Path $zipFolder)
Copy-Item -LiteralPath (Join-Path $out 'FM4StaticRecompLauncher.exe'), (Join-Path $out 'fm4-static-recomp-source.zip'),
    (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $zipFolder
# The long notice files the bundled native libraries and the .NET runtime come with, as their
# packages ship them (THIRD_PARTY_NOTICES.md names them).
$packages = Join-Path $HOME '.nuget/packages'
$notices = Join-Path $zipFolder 'notices'
[void](New-Item -ItemType Directory -Force -Path $notices)
$assets = Get-Content -LiteralPath (Join-Path $root 'launcher/Fm4StaticRecomp.Launcher/obj/project.assets.json') -Raw | ConvertFrom-Json
function Get-PackageFolder([string]$Id) {
    $key = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like "$Id/*" })[0]
    Join-Path $packages $assets.libraries.$key.path
}
Copy-Item -LiteralPath (Join-Path (Get-PackageFolder 'SkiaSharp.NativeAssets.Win32') 'THIRD-PARTY-NOTICES.txt') `
    -Destination (Join-Path $notices 'SkiaSharp-THIRD-PARTY-NOTICES.txt')
# The runtime pack is not a package reference; the publish's deps file names its version.
$deps = Get-Content -LiteralPath (Join-Path $root 'launcher/Fm4StaticRecomp.Launcher/bin/Release/net8.0/win-x64/FM4StaticRecompLauncher.deps.json') -Raw
if ($deps -notmatch 'runtimepack\.Microsoft\.NETCore\.App\.Runtime\.win-x64/(?<version>[0-9.]+)') { throw 'The .NET runtime pack version was not found.' }
Copy-Item -LiteralPath (Join-Path $packages "microsoft.netcore.app.runtime.win-x64/$($Matches.version)/THIRD-PARTY-NOTICES.TXT") `
    -Destination (Join-Path $notices 'dotnet-THIRD-PARTY-NOTICES.txt')
Compress-Archive -Path (Join-Path $zipFolder '*') -DestinationPath (Join-Path $out 'FM4StaticRecomp-Launcher.zip')
Remove-Item -LiteralPath $zipFolder -Recurse -Force
Get-ChildItem -LiteralPath $out | ForEach-Object { '{0,12:N0}  {1}' -f $_.Length, $_.Name }
