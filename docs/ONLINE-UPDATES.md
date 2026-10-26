# Online updates

Since version 1.1.0 the app can download the newest ReSkate Trainer itself. This file describes `latest.json`,
what the app does with it, and what it does and doesn't protect against. The step-by-step for publishing is in
`RELEASING.md`.

## Where everything lives

| Thing | Location |
| --- | --- |
| The manifest | `latest.json` at the root of `main` in `github.com/jakemackie/ReSkateTrainerUpdater` |
| What the app downloads it from | `https://raw.githubusercontent.com/jakemackie/ReSkateTrainerUpdater/main/latest.json` |
| The Trainer zips | Release assets of the same repository, under tags such as `trainer-tournament-0.9.2` |
| Accepted download URLs | Only `https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/<tag>/<file>.zip` |

These are set in `src/ReSkateTrainerUpdater.Core/OnlineSource.cs` and compiled into the app. Moving or renaming
the repository strands every copy of the app already installed, because they keep reading the old address.
GitHub redirects renamed repositories for a while, but don't rely on it.

## latest.json format (schemaVersion 1)

```json
{
  "schemaVersion": 1,
  "editions": {
    "tournament": {
      "version": "0.9.2",
      "build": "578f597",
      "reskateVersion": "2.1.1",
      "url": "https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/trainer-tournament-0.9.2/ReSkate_Trainer_Tournament-0.9.2-on-v2.1.1.zip",
      "sizeBytes": 17319112,
      "sha256": "737af3e3bdd83687c4bb32242794655aa46dac5f7574f0f7eeeacfbe4cc75619"
    }
  }
}
```

These are the real values for the 0.9.2 Tournament zip, as the manifest tool wrote them. The committed file
starts with `"editions": {}`, which means nothing is published and the tile stays dark.

| Field | Required | Rule |
| --- | --- | --- |
| `schemaVersion` | yes | Whole number. The app reads `1`. A higher number makes older apps ignore the whole file. |
| `editions` | yes | Object. Each key is an edition key: lower-case letters, digits and dashes. `tournament`, or `standard` for a Trainer with no edition name. |
| `version` | yes | `major.minor.patch`, for example `0.9.2`. Compared as numbers, so `0.10.0` is newer than `0.9.2`. |
| `build` | no | 5 to 40 lower-case hex characters. When present it must match the build inside the DLL. |
| `reskateVersion` | no | `major.minor.patch`. Not used for any decision. |
| `url` | yes | Must pass the accepted-URL rule above. The file name may use only letters, digits, `.`, `-` and `_`. |
| `sizeBytes` | yes | The exact size of the zip, from 1 byte to 500 MB. |
| `sha256` | yes | The SHA-256 of the zip, 64 hex characters. |

If an entry breaks any rule, the app ignores that entry and keeps the others. If the file isn't valid JSON, has no
`schemaVersion`, or has a higher `schemaVersion`, the app ignores all of it. Either way the user sees no error,
only a line in the activity log.

Don't edit this file by hand. `ManifestTool add` writes it, and the tests fail if it isn't in exactly the
format the tool writes.

## The app's own entry (self-update)

latest.json can also say which version of the updater itself is the newest. The Release workflow adds this entry for you
when you publish an app release (see `RELEASING.md`). It is optional, and older apps ignore it.

```json
{
  "schemaVersion": 1,
  "app": {
    "version": "1.2.0",
    "url": "https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/v1.2.0/ReSkateTrainerUpdater-1.2.0.zip",
    "sizeBytes": 63088693,
    "sha256": "0045cecea3920f37f20150e92b2073e8684b81b67efecddaacb254f9e5eb842d"
  },
  "editions": { }
}
```

| Field | Required | Rule |
| --- | --- | --- |
| `version` | yes | `major.minor.patch`. The app offers the update when this is higher than its own version, compared as numbers. |
| `url` | yes | Exactly `https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/v<version>/ReSkateTrainerUpdater-<version>.zip`. Stricter than a Trainer URL: the tag must be `v<version>` and the file name must match. |
| `sizeBytes` | yes | The exact size of the zip. |
| `sha256` | yes | The SHA-256 of the zip. |

A self-update installs code, so it is checked in more places than a Trainer:

1. The URL must be this app's own release asset for that exact version (`OnlineSource.IsTrustedAppUrl`).
2. The downloaded zip must match the size and SHA-256 in latest.json exactly, or it is deleted.
3. The zip must hold `ReSkateTrainerUpdater.exe` at its top level, which must be a 64-bit Windows program that says
   it is "ReSkate Trainer Updater" and is exactly the published version.
4. Only then is it swapped in. The running exe is renamed to `.old`, the new one is renamed into its place, and if
   the second rename fails the first is undone. The new copy starts with `--updated`, shows "Updated to version X",
   and deletes the `.old` file.

What the user sees: a blue **UPDATE APP** tile, ahead of any Trainer update. One click downloads, checks, swaps and
restarts, and the new version opens on a green **DONE** card. If the app is in a folder it can't write to, for example
`C:\Program Files`, nothing changes and the card says to move it to a folder the user owns. The app never updates
itself without a click, and a copy started through `dotnet` (a developer's) never offers it.

## What the app does

On start-up the app makes one request for `latest.json`, with a 6-second limit. When the manifest arrives, and
again every time a folder is inspected, `UpdateAdvisor.DescribeOnlineOffer` decides what to show.

| The chosen folder has | `latest.json` has | The user sees |
| --- | --- | --- |
| No `ReSkate.dll` (empty or new folder) | `tournament` | Green READY pill, "Ready to install", and a blue **INSTALL** tile |
| No `ReSkate.dll` | no `tournament` | "Nothing published yet". The tile stays dark. |
| Tournament 0.9.1 | `tournament` 0.9.2 | Blue UPDATE pill, "Update available, 0.9.1 → 0.9.2 (Tournament)", and a blue **UPDATE** tile |
| Tournament 0.9.2 | `tournament` 0.9.2, any build | Green READY pill, "Ready to skate. You're running the newest version.", and a dark green **UP TO DATE** tile |
| Tournament 0.9.5 | `tournament` 0.9.2 | Says your version is newer than the published one. The tile is dark. No downgrade is offered. |
| Standard edition, any version | only `tournament` | "Trainer 0.9.1 installed. There is no online update for this edition." The tile is dark. Dropping a zip still works. |
| Plain ReSkate, no Trainer | anything | "ReSkate without the Trainer". The tile is dark. |
| A `ReSkate.dll` that can't be read | anything | Amber HEADS UP pill. The tile is dark. |
| Steam's `skate.` folder | anything | Red PROBLEM pill. The app refuses this folder. |
| Anything | unreachable, invalid, or offline | Grey OFFLINE pill, "Couldn't check for updates". No error colours, no dialog. One line in the activity log. |

Clicking the button:

1. Downloads the zip to `%TEMP%\ReSkateTrainerUpdater_download_<guid>\`, with a 15-minute limit.
2. Refuses it if the size or SHA-256 differs from `latest.json`, or if it is larger than promised (the download
   stops early). The file is deleted and the user sees "The downloaded file doesn't match the published one,
   so it was deleted and not installed. Nothing was changed."
3. Unpacks it and checks the Trainer version, edition and build inside `ReSkate.dll` match `latest.json`. This
   catches a publishing mistake, such as attaching the wrong zip to a Release.
4. Hands the unpacked package to the normal install: the same confirmations, backup and rollback as a dropped zip.

The app never downloads or installs anything without a click.

## What "verified" means

**It does mean** the file installed is byte-for-byte the file you published. The size and hash come over HTTPS
from your repository, and the zip must come from your repository's Releases.

**It does not mean** the Trainer has been audited. You are vouching for where the file came from, not for the
Trainer author's code.

**The trust anchor is your GitHub account.** Anyone who can push to `main` and create Releases in this repository
can publish anything to every user. So:

- Turn on two-factor authentication for the account.
- Consider a branch protection rule on `main`, and don't give other people write access lightly.
- Keep the manifest and the zips in the same repository. A rule that only accepts your Releases means an attacker
  needs your account, not just a server somewhere.

## Privacy

The app makes two kinds of request, both to GitHub:

- A GET for `latest.json`, once per start-up.
- A GET for one zip, only when the user clicks the big tile: a Trainer (**UPDATE** or **INSTALL**) or a new version
  of the app itself (**UPDATE APP**).

The only header it adds is `User-Agent: ReSkateTrainerUpdater/<version>`. It sends no identifiers, folder paths,
installed versions or usage data. GitHub sees the user's IP address, as with any visit to github.com.

## Timing and caching

- `raw.githubusercontent.com` may serve the old `latest.json` for about 5 minutes after you push.
- Raw files are not the GitHub API, so the API's limit of 60 requests per hour per IP doesn't apply.
- An app that is already open only checks once, at start-up. Users see a new release the next time they open it.

## Changing the format later

- **Adding an optional field:** no schema change is needed, because older apps ignore unknown fields. Update the
  parser, `CheckEntry`, `ToJson` and the tests together.
- **A breaking change:** raise `TrainerManifest.SupportedSchemaVersion`. Release the new app first, then push the
  new-format manifest. Older apps will quietly show "Couldn't check for updates" until the user updates the app.
