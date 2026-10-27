# ReSkate Trainer Updater

[![CI](../../actions/workflows/ci.yml/badge.svg)](../../actions/workflows/ci.yml)

A small Windows app that installs the newest [ReSkate Trainer](https://github.com/andrewnakas/reskate-trainer) into your ReSkate folder. Pick your ReSkate folder and click **Update**. If you already have a Trainer zip, you can drop it on the window instead.

This is an unofficial helper. It isn't made by, or affiliated with, the ReSkate or ReSkate Trainer authors.

## Using it

1. Download `ReSkateTrainerUpdater-<version>.zip` from the [latest release](../../releases/latest) and unzip it. The exe needs no installation.
2. Close the game and the ReSkate launcher.
3. Choose your ReSkate folder, the one with `ReSkateLauncher.exe`. The app remembers it for next time.
4. Click the big blue tile. It says **UPDATE** when a newer Trainer is published, or **INSTALL** for an empty folder. When you're current it goes dark and says **UP TO DATE**.
5. Start `ReSkateLauncher.exe` as usual.

**The updater updates itself.** When a new version of this app is published, the big tile says **UPDATE APP**. One click downloads it, checks it, swaps it in and restarts. You only ever download the app by hand once. If it says it can't replace itself, the app is in a folder you can't write to (such as `Program Files`): move it to your Desktop or another folder you own.

**Offline, or a different version:** drop a ReSkate Trainer zip onto the window (or click the zip tile to browse), then click the **INSTALL** tile. A zip inside a zip, or one wrapped in a folder, is fine.

Windows may show an "Unknown publisher" SmartScreen warning because the exe isn't code-signed. Choose **More info**, then **Run anyway**.

## What it checks

**Downloads**
- It only downloads Trainer zips from this repository's GitHub Releases.
- The size and SHA-256 of every download must match the published [`latest.json`](latest.json) exactly, or the file is deleted and nothing is installed.
- The version, edition and build inside the downloaded DLL must match what was published.

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

## Privacy

The app contacts GitHub only, and only at two moments:

- **On start-up** it downloads [`latest.json`](latest.json) from this repository to see whether a newer Trainer is published.
- **When you click the big tile** it downloads one zip from this repository's Releases: a Trainer, or a new version of this app.

It sends nothing about you, your PC or your game: no IDs, folder paths, versions or usage data. GitHub sees your IP address, as with any visit to github.com. It never installs anything without your click. If you're offline, it skips the check silently and the drop zone works as before.

## Credits and licences

This updater was made by **os_uk**.

The ReSkate Trainer is GPL-3.0 software by its authors. The Trainer zips published here are redistributed unmodified, with their `LICENSE.txt` and `README.txt` included. Source code:

- ReSkate: https://github.com/Dingo-Shenanigans/ReSkate
- ReSkate Trainer: https://github.com/andrewnakas/reskate-trainer
- Dark Pop: https://github.com/Sivaes/darkpop
- Grab Fix: https://github.com/Sivaes/grabbugs

The window is styled after the ReSkate launcher and uses the same free fonts, Montserrat and Permanent Marker. Their licences are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), which is also inside every release zip.

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

**Maintainers:** releasing the app and publishing a new Trainer zip are described step by step in [docs/RELEASING.md](docs/RELEASING.md). AI coding agents should read [CLAUDE.md](CLAUDE.md) first.

## Project layout

| Path | What it holds |
|---|---|
| `src/ReSkateTrainerUpdater.Core` | All the logic, with no UI: reading versions, checking zips and folders, online updates, installing, the status messages |
| `src/ReSkateTrainerUpdater` | The WinForms app: the window and its custom dark controls |
| `tests/ReSkateTrainerUpdater.Tests` | Tests for the Core library, using small generated stand-ins for the ReSkate files and a fake GitHub |
| `tools/ReSkateTrainerUpdater.ManifestTool` | The maintainer's tool that writes and verifies `latest.json` |
| `latest.json` | The newest published Trainer for each edition, read by the app on start-up |
| `docs` | Architecture, the online update format, and the release steps |
| `THIRD-PARTY-NOTICES.md` | Licences for the embedded fonts |
| `scripts` | `build-release.ps1` |

## Notes

- If a future Trainer build stores its version differently, the app still installs it from a dropped zip but says it couldn't read the version.
- Settings (last folder, backup switch) are stored in `%AppData%\ReSkateTrainerUpdater\settings.json`.
