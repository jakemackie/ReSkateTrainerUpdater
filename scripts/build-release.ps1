<#
.SYNOPSIS
    Builds the single-file ReSkateTrainerUpdater.exe, zips it, and builds the setup program, ready to upload.

.DESCRIPTION
    Needs the .NET 8 SDK and Inno Setup 6 on Windows. Output goes to .\release:
      ReSkateTrainerUpdater-<version>.zip         the portable exe (the app's self-update downloads this one)
      ReSkateTrainerUpdater-Setup-<version>.exe   the installer people are told to download
    The version comes from Directory.Build.props, so bump it there before each release.
    Inno Setup is free: https://jrsoftware.org/isinfo.php (winget install JRSoftware.InnoSetup). GitHub's Windows
    runners already have it.

.PARAMETER SkipInstaller
    Only build the exe and the zip. For quick local builds; a release always builds the setup too.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
#>

param([switch]$SkipInstaller)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project  = Join-Path $repoRoot 'src\ReSkateTrainerUpdater\ReSkateTrainerUpdater.csproj'
$props    = Join-Path $repoRoot 'Directory.Build.props'
$output   = Join-Path $repoRoot 'release'

[xml]$propsXml = Get-Content $props
$version = @($propsXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw "No <Version> found in $props" }

if (Test-Path $output) { Remove-Item $output -Recurse -Force }

Write-Host "Building ReSkate Trainer Updater $version ..."
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false `
    -o (Join-Path $output 'publish')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$exe = Join-Path $output 'publish\ReSkateTrainerUpdater.exe'
$zip = Join-Path $output "ReSkateTrainerUpdater-$version.zip"
# The font licences must travel with the exe, so the notices file goes in the zip too.
$notices = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'
if (-not (Test-Path $notices)) { throw "Missing $notices" }
Compress-Archive -Path $exe, $notices -DestinationPath $zip

$exeSizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
$sha256    = (Get-FileHash $zip -Algorithm SHA256).Hash

# The setup program. It installs the same exe for the current user, with a Start Menu entry and an uninstaller.
$setup = $null
if (-not $SkipInstaller) {
    $candidates = @(
        $env:ISCC,
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }
    $iscc = @($candidates)[0]
    if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
    if (-not $iscc) {
        throw 'Inno Setup 6 was not found, so the setup program can''t be built. Install it with: winget install JRSoftware.InnoSetup (or use -SkipInstaller).'
    }

    Write-Host "Building the setup program with $iscc ..."
    $script = Join-Path $repoRoot 'installer\ReSkateTrainerUpdater.iss'
    & $iscc "/DAppVersion=$version" "/DSourceExe=$exe" /Qp $script
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }

    $setup = Join-Path $output "ReSkateTrainerUpdater-Setup-$version.exe"
    if (-not (Test-Path $setup)) { throw "Inno Setup didn't produce $setup" }
}

Write-Host ''
Write-Host 'Done.'
Write-Host "  Exe   : $exe ($exeSizeMb MB)"
Write-Host "  Zip   : $zip"
Write-Host "  SHA256 of the zip: $sha256"
if ($setup) { Write-Host "  Setup : $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)" }
