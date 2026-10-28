<#
.SYNOPSIS
    Writes the winget manifests, and updates the Scoop manifest, for a published app release.

.DESCRIPTION
    Run it AFTER the release is published on GitHub. It downloads the release's two files (what people really get, so the
    hashes are right), then:
      - writes the winget manifest files to release\packaging\winget\<version>\ , ready to copy into a fork of
        microsoft/winget-pkgs under manifests\j\jakemackie\ReSkateTrainerUpdater\<version>\ and send as a pull request
        (or give to `wingetcreate submit`). Check them first with:  winget validate --manifest <that folder>
      - rewrites bucket\reskate-trainer-updater.json (version and hash). Commit that file.
    People then install with:
        winget install jakemackie.ReSkateTrainerUpdater
        scoop bucket add reskate https://github.com/jakemackie/ReSkateTrainerUpdater
        scoop install reskate/reskate-trainer-updater

.PARAMETER Version
    The app version. Defaults to <Version> in Directory.Build.props.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\make-package-manifests.ps1
#>

param([string]$Version)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    [xml]$propsXml = Get-Content (Join-Path $repoRoot 'Directory.Build.props')
    $Version = @($propsXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
}
if ($Version -notmatch '^\d{1,4}\.\d{1,4}\.\d{1,4}$') { throw "Not a version: $Version" }

$base     = "https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/v$Version"
$zipName  = "ReSkateTrainerUpdater-$Version.zip"
$setupName = "ReSkateTrainerUpdater-Setup-$Version.exe"
$scratch  = Join-Path ([IO.Path]::GetTempPath()) ("ReSkateTrainerUpdater_pkg_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null

try {
    $hashes = @{}
    foreach ($name in @($zipName, $setupName)) {
        $file = Join-Path $scratch $name
        Write-Host "Downloading $name ..."
        Invoke-WebRequest -Uri "$base/$name" -OutFile $file -UseBasicParsing
        $hashes[$name] = (Get-FileHash $file -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}
finally {
    Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
}

# ---------- Scoop ----------
$bucketFile = Join-Path $repoRoot 'bucket\reskate-trainer-updater.json'
$bucket = Get-Content $bucketFile -Raw | ConvertFrom-Json
$bucket.version = $Version
$bucket.architecture.'64bit'.url  = "$base/$zipName"
$bucket.architecture.'64bit'.hash = $hashes[$zipName].ToLowerInvariant()
$json = ($bucket | ConvertTo-Json -Depth 10) -replace "`r`n", "`n"
[IO.File]::WriteAllText($bucketFile, $json + "`n", (New-Object Text.UTF8Encoding($false)))
Write-Host "Updated $bucketFile"

# ---------- winget ----------
# The setup is an Inno Setup program; its Add/Remove Programs key is the AppId from the .iss with _is1 on the end.
$iss = Get-Content (Join-Path $repoRoot 'installer\ReSkateTrainerUpdater.iss') -Raw
if ($iss -notmatch '(?m)^AppId=\{\{([0-9A-Fa-f-]{36})\}') { throw 'Could not find AppId in the .iss file' }
$productCode = '{' + $Matches[1].ToUpperInvariant() + '}_is1'

$id  = 'jakemackie.ReSkateTrainerUpdater'
$out = Join-Path $repoRoot "release\packaging\winget\$Version"
New-Item -ItemType Directory -Path $out -Force | Out-Null
$schema = '1.6.0'

$files = @{
"$id.yaml" = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@
"$id.installer.yaml" = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: inno
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
Installers:
- Architecture: x64
  InstallerUrl: $base/$setupName
  InstallerSha256: $($hashes[$setupName])
  ProductCode: '$productCode'
ManifestType: installer
ManifestVersion: $schema
"@
"$id.locale.en-US.yaml" = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: Jake Mackie (os_uk)
PublisherUrl: https://github.com/jakemackie
PublisherSupportUrl: https://github.com/jakemackie/ReSkateTrainerUpdater/issues
PackageName: ReSkate Trainer Updater
PackageUrl: https://github.com/jakemackie/ReSkateTrainerUpdater
License: MIT
LicenseUrl: https://github.com/jakemackie/ReSkateTrainerUpdater/blob/main/LICENSE
ShortDescription: Installs and updates the ReSkate Trainer in your ReSkate folder.
Description: An unofficial helper that installs the ReSkate Trainer, a community mod for the game ReSkate, and keeps it up to date. Every download is checked against a signed list before anything is installed. Not made by or affiliated with the ReSkate or ReSkate Trainer authors.
Moniker: reskate-trainer-updater
Tags:
- reskate
- trainer
- mod
- updater
ReleaseNotesUrl: https://github.com/jakemackie/ReSkateTrainerUpdater/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@
}
foreach ($name in $files.Keys) {
    [IO.File]::WriteAllText((Join-Path $out $name), ($files[$name] -replace "`r`n", "`n") + "`n", (New-Object Text.UTF8Encoding($false)))
}
Write-Host "Wrote the winget manifest to $out"
Write-Host "Check it with:  winget validate --manifest `"$out`""
