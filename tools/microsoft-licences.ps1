# Prints the licences of the Microsoft files the build downloads, as one JSON line, for the
# launcher's licence step: the Build Tools licence read from Microsoft's own channel, and the
# Windows SDK licence. Runs before anything is provisioned.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'release-common.ps1')
$config = Get-Fm4ReleaseToolchain
[ordered]@{
    build_tools = Get-Fm4BuildToolsLicence -ChannelUrl $config.msvc.channel_url
    windows_sdk = $config.windows_sdk.license_url
} | ConvertTo-Json -Compress
