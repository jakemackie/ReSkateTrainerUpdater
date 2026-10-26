# ReSkate Trainer Updater

[![CI](../../actions/workflows/ci.yml/badge.svg)](../../actions/workflows/ci.yml)

A small Windows app that installs the newest [ReSkate Trainer](https://github.com/andrewnakas/reskate-trainer) into your ReSkate folder. Drop the zip you downloaded, pick your ReSkate folder, click **Install update**.

This is an unofficial helper. It isn't made by, or affiliated with, the ReSkate or ReSkate Trainer authors.

## Using it

1. Download `ReSkateTrainerUpdater-<version>.zip` from the [Releases](../../releases) page and unzip it. The exe needs no installation.
2. Close the game and the ReSkate launcher.
3. Drop the ReSkate Trainer zip onto the window (or click it to browse). A zip inside a zip, or one wrapped in a folder, is fine.
4. Choose your ReSkate folder, the one with `ReSkateLauncher.exe`. The app remembers it for next time.
5. Click **Install update**, then start `ReSkateLauncher.exe` as usual.

Windows may show an "Unknown publisher" SmartScreen warning because the exe isn't code-signed. Choose **More info**, then **Run anyway**.

## What it checks

**The zip**
- It contains `ReSkate.dll` and `ReSkateLauncher.exe`, both real 64-bit Windows programs that identify as ReSkate.
- It reads the Trainer version out of the DLL (for example `0.9.2`, Tournament edition, built on ReSkate 2.1.1).
- It warns if the two files don't match each other, or if the zip has no Trainer in it.

**Your ReSkate folder**
- It shows which Trainer version is installed there now and compares it with the zip: update, already up to date, different build, or downgrade (it asks first).
- It refuses Steam's own `skate.` folder, because the launcher can't run from there, and warns about special characters in the path.

**While installing**
- Before changing anything it checks that no file is in use or protected, so an open game leaves your files untouched.
- Every replaced file is copied to `_ReSkateTrainerUpdater_backup_<date>` inside your folder. You can switch this off.
- If something fails part-way, your original files are put back, so you never end up half-updated.

## Building from source

You need Windows 10/11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet build
dotnet run --project src/ReSkateTrainerUpdater
```

Run the tests (they run on any OS, no packages needed):

```
dotnet run --project tests/ReSkateTrainerUpdater.Tests
```

Build the single-file exe and zip it for sharing:

```
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

The exe is self-contained, so players don't need to install .NET. The result is in `release/`.

To publish a version on GitHub, set `<Version>` in `Directory.Build.props`, commit, then push a matching tag such as `v1.0.0`. The Release workflow builds the exe and attaches it to a GitHub release.

## Project layout

| Path | What it holds |
|---|---|
| `src/ReSkateTrainerUpdater.Core` | All the logic, with no UI: reading versions, checking zips and folders, installing, the status messages |
| `src/ReSkateTrainerUpdater` | The WinForms app: the window and its custom dark controls |
| `tests/ReSkateTrainerUpdater.Tests` | Tests for the Core library, using small generated stand-ins for the ReSkate files |
| `scripts` | `build-release.ps1` |

## Notes

- If a future Trainer build stores its version differently, the app still installs it but says it couldn't read the version.
- Settings (last folder, backup switch) are stored in `%AppData%\ReSkateTrainerUpdater\settings.json`.
