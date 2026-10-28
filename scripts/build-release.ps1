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

.PARAMETER Trim
    Publish the exe trimmed: about 30 MB instead of about 70 MB, which would make the self-update download much smaller.
    NOT used for releases yet. Trimming WinForms isn't supported by Microsoft, and the exe's self-test currently fails on a
    trimmed build (dropping a zip onto the window crashes it). Don't release a trimmed exe until that passes AND a zip
    dragged in from Explorer has been tried by hand. See docs/ARCHITECTURE.md.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
#>

param([switch]$SkipInstaller, [switch]$Trim)

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
# Not trimmed unless asked (-Trim): see the parameter's notes. The csproj explains what trimming involves.
$trimValue = if ($Trim) { 'true' } else { 'false' }
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=$trimValue `
    -p:DebugType=none -p:DebugSymbols=false `
    -o (Join-Path $output 'publish')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$exe = Join-Path $output 'publish\ReSkateTrainerUpdater.exe'

# Run the exe once, out of sight, to prove the window builds and the parts trimming can break still work. Nothing is
# sent over the network and no settings are written. A release must never go out if this fails.
$selfTestReport = Join-Path $output 'self-test.txt'
$selfTest = Start-Process -FilePath $exe -ArgumentList '--self-test', "`"$selfTestReport`"" -Wait -PassThru
if (Test-Path $selfTestReport) { Get-Content $selfTestReport | ForEach-Object { Write-Host "  self-test: $_" } }
if ($selfTest.ExitCode -ne 0) { throw "The published exe failed its self-test (exit code $($selfTest.ExitCode)). See above. If you used -Trim, that is probably the cause." }

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
