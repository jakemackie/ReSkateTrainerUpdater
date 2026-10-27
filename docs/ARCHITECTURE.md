# Architecture

This explains how the updater works and why. `CLAUDE.md` has the rules and commands. `ONLINE-UPDATES.md` has the
`latest.json` format.

## Projects

| Project | Target | Role |
| --- | --- | --- |
| `src/ReSkateTrainerUpdater.Core` | `net8.0` | Everything that isn't drawing a window. No UI references, so it compiles and is tested on any OS. |
| `src/ReSkateTrainerUpdater` | `net8.0-windows` | The WinForms window. Calls into Core and shows what Core says. |
| `tests/ReSkateTrainerUpdater.Tests` | `net8.0` | A dependency-free console test runner for Core. |
| `tools/ReSkateTrainerUpdater.ManifestTool` | `net8.0` | The maintainer's tool for `latest.json`. Uses the same Core code as the app. |

The shipped app is one self-contained, single-file `ReSkateTrainerUpdater.exe` (win-x64), so players don't need
.NET installed.

## The real Trainer package

This is the layout of `ReSkate_Trainer_Tournament-0.9.2-on-v2.1.1.zip`, the 0.9.2 Tournament release:

```
ReSkate_Trainer_Tournament-0.9.2-on-v2.1.1.zip     17,319,112 bytes (the outer zip)
└── ReSkate_Trainer_Tournament-0.9.2.zip           a zip inside the zip
    ├── LICENSE.txt                                GPL-3.0
    ├── README.txt                                 Install notes and links to the source code
    ├── ReSkate.dll                                29,743,616 bytes, x64
    └── ReSkateLauncher.exe                        5,667,840 bytes, x64
```

Other builds have arrived with a single wrapper folder instead of an inner zip, so `TrainerPackageLoader`
handles both, nested up to 8 levels. If neither pattern fits, it uses the shallowest folder that contains
`ReSkate.dll`.

## Reading versions out of the binaries

There are three pieces of information, and they come from two places.

| Information | Example | Where it comes from |
| --- | --- | --- |
| Product name | `ReSkate` | Windows file properties (`FileVersionInfo.ProductName`) |
| ReSkate base version | `2.1.1` | Windows file properties (`ProductVersion`, or `FileVersion`) |
| Trainer version, build, edition | `0.9.2`, `578f597`, `Tournament` | Only inside the binary. See below. |

The Trainer doesn't set any file property of its own. The Trainer and plain ReSkate both say "ReSkate 2.1.1" in
Explorer. The Trainer does compile two strings into both binaries, close together:

```
offset 0x12AB660 in ReSkate.dll (0.9.2 Tournament):

52 65 53 6B 61 74 65 5F 54 72 61 69 6E 65 72 5F   ReSkate_Trainer_
54 6F 75 72 6E 61 6D 65 6E 74 00                  Tournament\0          <- the name, NUL-terminated
00 00 00 00 00                                    padding
90 C4 2A 81 01 00 00 00                           an 8-byte pointer
0D 00 00 00 00 00 00 00                           an 8-byte length (13)
30 2E 39 2E 32 2B 35 37 38 66 35 39 37 00         0.9.2+578f597\0      <- the version and build
```

`ReSkateScanner.FindTrainer` does this:

1. Finds each occurrence of the ASCII text `ReSkate_Trainer`.
2. Reads the rest of the name (letters, digits and `_`, at most 64 bytes). It must end with a NUL byte, or the
   match is skipped. This rules out the text appearing inside a longer string.
3. Searches the next 64 bytes for `major.minor.patch`, optionally followed by `+` and a 5 to 40 character hex
   build. The pattern refuses digits or letters directly around it, so it doesn't match part of a longer number.
4. Treats whatever follows `ReSkate_Trainer_` in the name as the edition, with `_` turned into spaces. `Tournament`
   comes from `ReSkate_Trainer_Tournament`. A bare `ReSkate_Trainer` is the standard edition, so the edition is null.

If a future Trainer stores this differently, the scanner finds nothing. The app then reports "no Trainer version"
and still lets the user install, with a warning. For online updates, `CheckPackageMatches` would refuse such a
zip, because it can't confirm the version. Publish such a build only once the scanner reads it.

The file is read with `FileShare.ReadWrite | FileShare.Delete`, so this works while the game has the DLL open.

## Validating a zip

`TrainerPackageLoader.Load` unpacks the zip to `%TEMP%\ReSkateTrainerUpdater_<guid>` and checks the following,
in order. It stops at the first group that fails.

1. Both `ReSkate.dll` and `ReSkateLauncher.exe` are present.
2. Both are real x64 Windows programs: an `MZ` header pointing to `PE\0\0` with machine type `0x8664`.
3. Both say "ReSkate" as their product name.
4. Warnings, which don't block the install: no Trainer version found, or the two files disagree on the base or
   Trainer version.

`ZipFile.ExtractToDirectory` refuses entries that would escape the folder ("zip slip"). Temp folders older than
2 hours that start with `ReSkateTrainerUpdater_` are deleted on each start-up.

## Installing

`TrainerInstaller.Install(sourceFolder, destinationFolder, backup)` is the only code that writes to the user's
ReSkate folder.

1. **Plan.** It compares every file in the package with the folder, using size and then SHA-256. Each file is
   unchanged, to replace, or to add. If nothing differs, the result is `AlreadyUpToDate` and nothing is written.
2. **Check before touching anything.** It creates and deletes a probe file to prove the folder is writable. It
   opens every file it will replace with `FileShare.None`, which fails if the game or launcher has it open. If
   either check fails, the result is `Blocked` and the folder is untouched.
3. **Save originals.** It copies every file it will replace to `_ReSkateTrainerUpdater_backup_<yyyy-MM-dd_HH-mm-ss>`
   inside the folder. If the user turned backups off, it uses a rescue folder in `%TEMP%` instead, which is
   deleted after a successful install.
4. **Write each file safely.** It copies the new file to `<target>.updating`, then renames it over the target. A
   target is therefore always the whole old file or the whole new file, never half of one.
5. **Roll back on any failure.** If any exception happens during steps 3 and 4, it deletes the `.updating` file,
   copies every saved original back, deletes every file it added, and removes the backup folder. The result is
   `RolledBack`. If restoring also fails, the result is `RestoreFailed`, and the message names the folder that
   still holds the originals.

| Guarantee | Checked in `InstallerTests.cs` by |
| --- | --- |
| Nothing changes if any file is in use | "Invariant: a file in use blocks the install", "Invariant: nothing is touched if any file is locked" |
| A failure part-way leaves the folder as it was | "Invariant: a failure part-way is rolled back", "Invariant: the original file is put back after a failure", "Invariant: files added before a failure are removed" |
| No half-written files are ever left | "Invariant: no half-written files are left behind", "Invariant: no half-written files are left after a rollback" |

## Online updates

```
start-up ──► FetchManifestAsync ──► TrainerManifest.Parse ──► (manifest or nothing)
                 GET raw.githubusercontent.com/jakemackie/ReSkateTrainerUpdater/main/latest.json
                 6 s timeout, 64 KB limit, never throws

folder inspected or manifest arrives ──► UpdateAdvisor.DescribeOnlineOffer(manifest, folder)
                                              ──► OnlineOffer: None | FreshInstall | Update | UpToDate
                                              ──► UpdateAdvisor.DescribeHome(...) turns that into the screen:
                                                  FreshInstall -> blue INSTALL tile, Update -> blue UPDATE tile,
                                                  UpToDate -> dark green UP TO DATE tile, None -> dark tile and a reason

big tile clicked (INSTALL or UPDATE) ──► TrainerDownloader.DownloadAsync(entry)
                              1. IsTrustedDownloadUrl(entry.Url), or refuse without a request
                              2. GET, following GitHub's redirect to its download host (https only)
                              3. Content-Length, if sent, must equal sizeBytes
                              4. stream to %TEMP%\ReSkateTrainerUpdater_download_<guid>\<file>.zip,
                                 hashing as it goes, and stop if it grows past sizeBytes
                              5. size must equal sizeBytes and SHA-256 must equal sha256
                              any failure: delete the folder, return the reason
                      ──► TrainerPackageLoader.Load(zip)          the same validation as a dropped zip
                      ──► TrainerDownloader.CheckPackageMatches     version, edition and build match latest.json
                      ──► delete the downloaded zip (the package keeps its own unpacked copy)
                      ──► becomes the current package, shown in the zip tile as "Downloaded from GitHub"
                      ──► UpdaterForm.InstallAsync                 the same confirmations, backup and rollback
```

The online route adds checks in front of the normal install and replaces none of them.

## The installer

Players install the app with `ReSkateTrainerUpdater-Setup-<version>.exe`, built by Inno Setup from
`installer/ReSkateTrainerUpdater.iss`. It is a normal Windows installer, shaped by one constraint: the app updates itself by
renaming its exe, so the folder it lives in must be writable without administrator rights.

- **Per-user.** `PrivilegesRequired=lowest` installs into `%LocalAppData%\Programs\ReSkate Trainer Updater`. It never asks
  for administrator rights and can't be switched to an all-users install.
- **What it creates.** The exe and `THIRD-PARTY-NOTICES.md`, a Start Menu entry (and an optional desktop shortcut), an
  uninstaller, and an Add/Remove Programs entry under HKCU, filed under the app's permanent identity (`AppId`).
- **Upgrades.** Running a newer setup finds the old install by `AppId`, closes the app if it is running, and replaces it.
- **Self-update and the installer.** The app updates itself from the portable zip, in place, in the install folder. The
  setup's Add/Remove Programs entry would then show the old version, so on start-up `InstallRegistration.SyncVersion`
  rewrites its version, but only if the entry's `InstallLocation` is the folder the running exe is in. A portable copy
  elsewhere never touches it.
- **Uninstall.** Removes the files, shortcuts and the entry, and the `.old` and `.new` files a self-update leaves. A silent
  uninstall keeps the saved settings (`%AppData%\ReSkateTrainerUpdater`). An interactive one asks.
- **Not code-signed.** Windows shows "Unknown publisher" for the setup and the exe.

`scripts/build-release.ps1` builds the exe, the zip and the setup from one version number, and the CI and Release workflows
both run it, so a broken installer fails CI long before a release. The wizard pictures are drawn by
`installer/make-wizard-images.ps1` from the app's own icon and fonts.

## Updating the app itself

`AppUpdater` replaces the running exe with a newer one. A running exe can be renamed but not overwritten, which
shapes the whole design.

```
start-up ──► manifest read ──► UpdateAdvisor.DescribeAppOffer(manifest, runningVersion, canUpdateInPlace)
                               newer app.version, and the exe isn't being run through `dotnet`  ──►  an AppOffer
UpdateAdvisor.DescribeHome shows the blue UPDATE APP tile, ahead of any Trainer update

UPDATE APP clicked ──► TrainerDownloader.DownloadAsync(app entry)     the same checked download as a Trainer:
                                                                      trusted URL, exact size, exact SHA-256
                   ──► AppUpdater.Stage(zip, currentExe, version)     takes ReSkateTrainerUpdater.exe from the zip root,
                                                                      saves it as <exe>.new, and checks that it is a
                                                                      64-bit program, is "ReSkate Trainer Updater", and is
                                                                      exactly the published version. Else it is deleted.
                   ──► AppUpdater.Swap(currentExe, staged)            <exe> -> <exe>.old, then <exe>.new -> <exe>.
                                                                      If the second rename fails the first is undone.
                   ──► Process.Start(<exe>, "--updated") and close    the new copy shows "Updated to version X" and
                                                                      calls AppUpdater.CleanUpLeftovers, which retries
                                                                      deleting <exe>.old while the old process exits
```

The current exe is untouched until the swap, which is two renames on one volume. Every earlier failure leaves nothing
behind except, at worst, a temp download that the start-up cleanup removes. If the app is in a folder it can't write to,
`Stage` fails with `NotWritable` before anything is changed.

`ProductVersion` of the built exe is `1.2.0+<commit>`. The part after the `+` is not the version, so `Stage` compares
only what comes before it.

On release, the workflow runs `ManifestTool set-app`, which runs the same `Stage` against the zip it just built. A zip
that wouldn't pass the app's own checks is never recorded in latest.json.

## The window

The window is built to look like the ReSkate launcher, using measurements taken from the launcher at its default
size of 1440 by 840 (or the screen's usable area, if that is smaller). It has no title bar, and minimise and close buttons
are drawn in the top-right corner. Top left are two small cards, the creator's credit with their profile picture and
GitHub's status, and under them the name in tilted marker lettering. Then a compact STATUS card on the left, and on the
right, stacked up from the bottom, the folder, an optional zip, and one big tile.

**Layout.** There are no panels or table layouts. `UpdaterForm.LayoutAll` places every control directly on the form, in
96-dpi units scaled by the display's DPI: 60 in from the left, 64 from the right, 56 up from the bottom, and 16 between
tiles. It runs on every render because the status card is only as tall as its content.

**See-through cards.** The launcher lets its background photo show faintly through its cards. Here the form paints a
backdrop (`Backdrop`: the launcher's photo, fitted and darkened the way the launcher does it, drawn once and cached) and
implements `IBackdropHost`. Each card's
`BufferedControl.Prepare` copies the part of the backdrop under it, then fills itself with a `Theme` colour that has an
alpha, so the backdrop shows through. This only works because the cards are direct children of the form. The photo is
game artwork that belongs to Electronic Arts and Full Circle, credited in `THIRD-PARTY-NOTICES.md`. If it is removed or
can't be read, `Backdrop` paints a dusk landscape in code instead.

**Instant clicks.** `BufferedControl` turns off `ControlStyles.StandardDoubleClick`. Otherwise Windows delivers the
second of two quick clicks as a double-click, which no control handles, and every other click of a fast series would be
lost. There are no animations on the response to a click.

**Window frame.** The window is borderless. `NativeMethods.ApplyWindowFrame` asks Windows 11 for rounded corners and a
thin border, and gets a shadow by extending the frame one pixel into the window. On Windows 10 the corners stay square.
Dragging any empty area moves the window by sending the same message a title bar would. The form keeps the minimise and
system-menu window styles, which is what lets the taskbar button minimise and restore it.

**State.** `UpdaterForm` keeps a small amount of state: the chosen zip and its package, the chosen folder and what was
found in it, the manifest and whether it could be read, the current offer, and whether something is running. Every change
ends by calling `Render`, which asks `UpdateAdvisor.DescribeHome` (or `DescribeBusy` while something runs, then
`WithResult` for the outcome) for a `HomeView`, and hands its parts to `StatusCard` and `ActionTileControl`. The form
makes no decisions about what to say.

**Text.** Text is drawn through `TextPaint` with GDI+, because the embedded fonts are only visible to GDI+.
`GameFonts.Load()` reads them from the exe at start-up, before any control exists, and falls back to Windows fonts if one
fails to load.

Background work (unpacking, inspecting the folder, downloading, installing) runs on the thread pool. A generation counter
throws away a result if the user picked something else while it was running. While an install or download runs,
`_installing` blocks drops, browsing, and every button.
