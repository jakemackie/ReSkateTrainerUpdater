# CLAUDE.md

Instructions for AI coding agents (Claude Code, Codex, Copilot and others) working in this repository.
`AGENTS.md` points here. Read this whole file before changing anything. Where this file and the code disagree,
the code is the truth: fix this file in the same change.

## What this project is

ReSkate Trainer Updater is a Windows desktop app (C#, .NET 8, WinForms). It installs the ReSkate Trainer, a
community mod for the game ReSkate, into a player's ReSkate folder. The Trainer is two files, `ReSkate.dll` and
`ReSkateLauncher.exe`. The app is an unofficial helper. It is not made by or affiliated with the ReSkate or
ReSkate Trainer authors.

The app has two ways to get the Trainer:

1. **Online (the main route since 1.1.0).** On start-up the app reads `latest.json` from this repository's `main`
   branch. If it lists a newer version of the edition installed in the chosen folder, or the folder is empty, a
   banner offers a one-click **Update** or **Install Tournament Edition**. The zip is downloaded from a GitHub
   Release of this repository, its size and SHA-256 are checked against `latest.json`, and then it is installed
   exactly like a dropped zip.
2. **Offline (the fallback).** The user drops a Trainer zip on the window, or browses for it, and clicks
   **Install update**.

Both routes end in the same `UpdaterForm.InstallAsync`, which calls `TrainerInstaller.Install`.

## Commands

Run every command from the repository root. You need the .NET 8 SDK.

| What | Command | Runs on |
| --- | --- | --- |
| Build everything | `dotnet build ReSkateTrainerUpdater.sln -c Release` | Windows only (the UI targets `net8.0-windows`) |
| Build Core only | `dotnet build src/ReSkateTrainerUpdater.Core` | Any OS |
| Run all tests | `dotnet run --project tests/ReSkateTrainerUpdater.Tests -c Release` | Any OS |
| Run the app | `dotnet run --project src/ReSkateTrainerUpdater` | Windows only |
| Check `latest.json` (no network) | `dotnet run --project tools/ReSkateTrainerUpdater.ManifestTool -- check` | Any OS |
| Verify `latest.json` against GitHub | `dotnet run --project tools/ReSkateTrainerUpdater.ManifestTool -- verify` | Windows only, needs internet |
| Add a Trainer zip to `latest.json` | `dotnet run --project tools/ReSkateTrainerUpdater.ManifestTool -- add <zip>` | Windows only |
| Build the release exe and zip | `powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1` | Windows only |

The tests print one `pass` or `FAIL` line per check and end with `All N checks passed.` The exit code is non-zero
if any check fails. A change is not finished until the build has no warnings and every check passes.

**On Linux or macOS** you can build Core and run the tests, but you cannot build the WinForms project or run
`add` or `verify`. Those read the product name from Windows file properties (`FileVersionInfo`), which are
empty on other systems, so a real Trainer zip is rejected there as "doesn't identify as ReSkate". The tests
replace that reader with a fake (see `tests/ReSkateTrainerUpdater.Tests/Program.cs`), so they pass anywhere.
If you change UI code on a non-Windows machine, say clearly in your summary that it was not compiled.

## Rules

These are requirements, not suggestions. Each one exists because breaking it breaks a user's game folder,
weakens the download checks, or breaks the release process.

### Where code goes

1. **All logic goes in `src/ReSkateTrainerUpdater.Core`.** Core has no UI references and must keep compiling on
   any OS. `src/ReSkateTrainerUpdater` (the WinForms app) only lays out controls, wires events, and moves work to
   background threads. If you are writing an `if` about versions, files, URLs or messages inside `UpdaterForm`,
   it belongs in Core instead.
2. **Every new user-facing message comes from `UpdateAdvisor`** and gets at least one check in
   `tests/ReSkateTrainerUpdater.Tests/AdvisorTests.cs` or `OnlineTests.cs`. Some older text is still written
   directly in `UpdaterForm`: the confirmation dialogs, activity-log lines, and two fallback errors. Don't add
   to it. Move it into `UpdateAdvisor` when you touch it.
3. **Custom controls live in `src/ReSkateTrainerUpdater/Ui/Controls`**, derive from `BufferedControl`, take
   colours and fonts from `Theme`, and scale pixel sizes with `S(...)` for DPI.

### The ReSkate folder

4. **Only `TrainerInstaller.Install` writes to the user's ReSkate folder.** Nothing else may create, change or
   delete files there.
5. **Keep the install guarantees** (see `docs/ARCHITECTURE.md`): check write access and locked files before
   changing anything, write each file as `<name>.updating` and then move it into place, save every replaced file
   first, and put everything back if any step fails. Any change to `TrainerInstaller` must keep every
   `Invariant:` check in `InstallerTests.cs` passing.

### Online updates

6. **`OnlineSource` is the only place that names a URL, owner, repository or branch.** Do not write
   `github.com` or `raw.githubusercontent.com` anywhere else in `src/`.
7. **Never weaken `OnlineSource.IsTrustedDownloadUrl`.** It only accepts
   `https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/<tag>/<file>.zip`.
   `TrainerDownloader.DownloadAsync` checks it again before every request. Do not add other hosts, mirrors,
   http, or a setting that overrides it.
8. **A downloaded zip reaches `TrainerInstaller` only after all three checks pass**: the exact size from
   `latest.json`, the exact SHA-256 from `latest.json`, and `TrainerDownloader.CheckPackageMatches`, which
   confirms the version, edition and build inside the DLL. On any failure the download is deleted and the folder
   is not touched.
9. **Never install without a click.** The app may tell the user an update exists. It must never download or
   install a Trainer on its own.
10. **Network failures are silent.** If `latest.json` can't be fetched or read, the banner stays hidden and one
    line goes to the activity log. No dialogs and no error colours. The drop zone must keep working with no
    internet.
11. **No telemetry.** The app sends two kinds of request only: a GET for `latest.json` on start-up, and a GET
    for the zip when the user clicks the banner button. The only header added is the User-Agent
    `ReSkateTrainerUpdater/<version>`. Don't add analytics, IDs, crash reporting, or anything else.

### latest.json

12. **Change `latest.json` only with the manifest tool** (`add`). The tests fail if the committed file isn't
    valid or isn't byte-for-byte in the tool's canonical format. Never type a hash by hand.
13. **Adding an optional field** to a manifest entry doesn't need a schema change, because old apps ignore unknown
    fields. Update `ManifestEntry`, `TrainerManifest.Parse`, `CheckEntry` and `ToJson` together, and add tests.
    **A breaking change** (renaming or removing a field, or changing what one means) needs
    `TrainerManifest.SupportedSchemaVersion` raised. Older apps then ignore the file and show nothing, which is
    the safe failure. Publish the new app before you push a manifest in the new format.
14. **The format is documented in `docs/ONLINE-UPDATES.md`.** Keep that file in step with the code.

### Releases, versions and files

15. **The app version lives only in `Directory.Build.props`** (`<Version>`). `scripts/build-release.ps1` and
    the Release workflow read it from there.
16. **Tags starting with `v` are app releases only.** Pushing `v1.2.0` runs `.github/workflows/release.yml`,
    which fails unless the tag matches `<Version>`. Trainer zips use tags like `trainer-tournament-0.9.2`. The
    manifest tool refuses a Trainer tag that starts with `v`.
17. **Never commit zips, exes or DLLs.** Trainer zips are GitHub Release assets only (`docs/RELEASING.md`
    explains why). `.gitignore` already excludes `bin/`, `obj/`, `release/` and `publish/`.
18. **`TreatWarningsAsErrors` is on** in `Directory.Build.props`. Fix the warning. Don't suppress it with
    `#pragma`, `NoWarn`, or by turning the setting off.

### Tests

19. **The test project is a plain console runner with no packages.** Write a check as
    `Check("plain-English statement of what is true", condition, detailShownOnFailure)` inside a
    `Section(...)`. Prefix checks that guard a safety guarantee with `Invariant:`.
20. **Tests never use the real network or real game files.** Use `FakeServer` (in `OnlineTests.cs`) for HTTP,
    and `FakeBinary.Pe(...)` / `FakeBinary.WriteRelease(...)` (in `Fixtures.cs`) for ReSkate binaries.

## Where things are

| Path | What it holds |
| --- | --- |
| `src/ReSkateTrainerUpdater.Core/OnlineSource.cs` | The trusted repo, the manifest URL, the trusted-download-URL rule, and edition keys |
| `src/ReSkateTrainerUpdater.Core/TrainerManifest.cs` | `latest.json`: the model, the strict parser, the canonical writer |
| `src/ReSkateTrainerUpdater.Core/TrainerDownloader.cs` | The HTTP client, fetching `latest.json`, downloading and checking a zip, and `CheckPackageMatches` |
| `src/ReSkateTrainerUpdater.Core/TrainerPackageLoader.cs` | Unpacking a zip (nested zips, wrapper folders) and validating it |
| `src/ReSkateTrainerUpdater.Core/ReSkateScanner.cs` | Reading versions from `ReSkate.dll` / `ReSkateLauncher.exe` |
| `src/ReSkateTrainerUpdater.Core/InstallFolderInspector.cs` | What is installed in a folder |
| `src/ReSkateTrainerUpdater.Core/TrainerInstaller.cs` | The only code that writes to the ReSkate folder |
| `src/ReSkateTrainerUpdater.Core/UpdateAdvisor.cs` | Every status message, the banner offer, and the download messages |
| `src/ReSkateTrainerUpdater.Core/Advice.cs` | `Advice`, `Severity`, `UpdateContext`, `OnlineOffer`, `OfferKind` |
| `src/ReSkateTrainerUpdater.Core/UpdaterSettings.cs` | `%AppData%\ReSkateTrainerUpdater\settings.json` |
| `src/ReSkateTrainerUpdater/Ui/UpdaterForm.cs` | The window. Wiring only. |
| `src/ReSkateTrainerUpdater/Ui/Controls/` | Custom-painted controls, including `UpdateBanner` |
| `tests/ReSkateTrainerUpdater.Tests/` | The console test runner. `OnlineTests.cs` covers everything online. |
| `tools/ReSkateTrainerUpdater.ManifestTool/` | Maintainer tool: `add`, `check`, `verify` for `latest.json` |
| `latest.json` | The published manifest, read by every installed copy of the app |
| `docs/ARCHITECTURE.md` | How install, rollback, version reading and online updates work, and why |
| `docs/ONLINE-UPDATES.md` | The `latest.json` format, the trust model, and what the user sees in each case |
| `docs/RELEASING.md` | Step-by-step for releasing the app and for publishing a Trainer zip |

## Words used in this repo

| Word | Meaning |
| --- | --- |
| ReSkate | The game mod runtime. Its base version, for example `2.1.1`, is in the Windows file properties. |
| Trainer | The ReSkate Trainer mod. Its version, for example `0.9.2`, is only inside the binary. |
| Build | The Trainer's commit hash, for example `578f597`, stored as `0.9.2+578f597`. |
| Edition | A Trainer variant, for example `Tournament`. The standard edition has no name in the binary. |
| Edition key | An edition in `latest.json`: lower case, spaces as dashes (`tournament`), or `standard` for none. `OnlineSource.EditionKey` converts. |
| Manifest | `latest.json`. |
| Release asset | A file attached to a GitHub Release. Trainer zips are hosted only this way. |
| Package | A zip that `TrainerPackageLoader` has unpacked to a temp folder and checked. |

## Things that look wrong but are deliberate

- **The banner shows nothing for the standard edition or for plain ReSkate.** At first only Tournament is
  published. An installed edition is only offered a newer version of that same edition, never a switch.
- **"Up to date" shows no banner**, only a line in the activity log and a status hint.
- **`latest.json` is read from `raw.githubusercontent.com`, not the GitHub API.** Raw files have no 60-per-hour
  API limit, but GitHub may cache them for about 5 minutes after a push.
- **Version comparison ignores the build.** If `latest.json` lists the same version with a different build, the
  app does not offer it. Bump the version to ship a new build.
- **A zip dropped by hand is not checked against `latest.json`.** The offline route trusts the user's file and
  only runs the normal package validation.
- **`dotnet publish` leaves five `*_cor3.dll` files next to the exe** (D3DCompiler, PenImc, PresentationNative,
  vcruntime140, wpfgfx). They are WPF native files that this WinForms app never loads. `build-release.ps1`
  correctly zips only the exe, and the lone exe was confirmed to run. Don't add them to the zip.
- **Trainer Releases are never marked "Latest".** The README sends users to `releases/latest` for the app, so
  that must always be an app release.
