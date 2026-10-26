<#
.SYNOPSIS
    Builds the single-file ReSkateTrainerUpdater.exe and zips it, ready to upload.

.DESCRIPTION
    Needs the .NET 8 SDK on Windows. Output goes to .\release. The version comes from Directory.Build.props,
    so bump it there before each release.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
#>

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

Write-Host ''
Write-Host 'Done.'
Write-Host "  Exe : $exe ($exeSizeMb MB)"
Write-Host "  Zip : $zip"
Write-Host "  SHA256 of the zip: $sha256"
