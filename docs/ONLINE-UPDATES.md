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
      "sha256": "737af3e3bdd83687c4bb32242794655aa46dac5f7574f0f7eeeacfbe4cc75619",
      "published": "2026-10-25",
      "notes": "Smoother physics"
    }
  }
}
```

These are the real values for the 0.9.2 Tournament zip, as the manifest tool wrote them (the last two lines are
optional, and are the tool's `--published` and `--notes`). If an edition isn't listed, the tile stays dark for it.

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
| `published` | no | The day it was published, as `yyyy-MM-dd`. The card shows it as "Published 3 days ago". |
| `notes` | no | One short line of 1 to 90 characters, no line breaks. The card shows it as "What's new: ...". |

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
| `published` | no | As for a Trainer. `set-app` writes today's date. |
| `notes` | no | As for a Trainer. The Release workflow passes the first line of `release-note.txt` in the tagged commit, if the file exists. |

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

On start-up the app makes one request for `latest.json`, with a 6-second limit. It makes the same request again only
when the user asks: the **Check again** button, or a click on the GitHub chip at the top. It never checks on a timer.
When the manifest arrives, and again every time a folder is inspected, `UpdateAdvisor.DescribeOnlineOffer` decides
what to show.

| The chosen folder has | `latest.json` has | The user sees |
| --- | --- | --- |
| No `ReSkate.dll` (empty or new folder) | `tournament` | Green READY pill, "Ready to install", and a blue **INSTALL** tile |
| No `ReSkate.dll` | no `tournament` | "Nothing published yet". The tile stays dark. |
| Tournament 0.9.1 | `tournament` 0.9.2 | Blue UPDATE pill and "Update available, 0.9.1 → 0.9.2 (Tournament)" on the card, a blue **PLAY** tile, and a small **Update available** button. The button opens a dialog saying the version is out of date and the new one is available, with **UPDATE** and **NOT NOW**. UPDATE starts the download below. (With no `ReSkateLauncher.exe` in the folder there is nothing to play, so the tile is a blue **UPDATE** instead.) |
| Tournament 0.9.2 | `tournament` 0.9.2, any build | Green READY pill, "Ready to skate. You're running the newest version.", and a blue **PLAY** tile ("Up to date. Start ReSkate"). With no `ReSkateLauncher.exe` in the folder it is a dark green **UP TO DATE** tile instead. |
| Tournament 0.9.5 | `tournament` 0.9.2 | Says your version is newer than the published one. The tile is **PLAY**. No downgrade is offered. |
| Standard edition, any version | `tournament` only | Blue SWITCH pill, "Another edition is available", and a blue **SWITCH** tile. It is only ever a deliberate switch: the download asks "This changes your Trainer from the Standard edition to the Tournament edition. Continue?" first, and the old files are backed up. It is never offered as an update. |
| Standard edition | `standard` too | Treated like any other edition: an **UPDATE** when it is newer. |
| Standard edition, any version | no `tournament` | "Trainer 0.9.1 installed. There is no online update for this edition." The tile is **PLAY**. Dropping a zip still works. |
| Plain ReSkate, no Trainer | `tournament` | Green READY pill, "Add the Trainer", and a blue **ADD TRAINER** tile |
| Plain ReSkate, no Trainer | no `tournament` | "ReSkate without the Trainer". The tile is dark. Dropping a zip still works. |
| A `ReSkate.dll` that can't be read | anything | Amber HEADS UP pill, "Couldn't read your ReSkate files", with what to do next. The tile is dark. |
| Steam's `skate.` folder | anything | Red PROBLEM pill. The app refuses this folder. |
| Anything | unreachable, invalid, or offline | Grey OFFLINE pill, "Couldn't check for updates", and a **Check again** button. No error colours, no dialog. One line in the activity log. |

While an update is shown, the card can also say "Published 3 days ago" and "What's new: ..." (from `published` and
`notes`), and the update dialog repeats the date and note.

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

**The manifest is signed.** The app ignores a `latest.json` that doesn't carry a valid signature from a key built into
the app (see "The signature" below). So changing the file on GitHub, or anywhere between GitHub and the user, isn't
enough to offer anyone a different download, because the file's hashes are what vouch for the zips. What the signature
does not stop: someone who also holds the signing key, or someone replaying an older signed file (which can only hide
a newer version, never offer a worse one, because the app only offers versions newer than the installed one).

**The trust anchor is your GitHub account and the signing key.** Anyone who can push to `main` and create Releases in
this repository can publish a release, but only a holder of the private key can make the app believe it. The key is a
repository secret (`MANIFEST_SIGNING_KEY`) so the Release workflow can sign, which means anyone who can change the
workflows can reach it. For more protection, keep the key off GitHub and sign by hand (docs/RELEASING.md). So:

- Turn on two-factor authentication for the account.
- Consider a branch protection rule on `main`, and don't give other people write access lightly.
- Keep the manifest and the zips in the same repository. A rule that only accepts your Releases means an attacker
  needs your account, not just a server somewhere.

## Privacy

The app makes two kinds of request, both to GitHub:

- A GET for `latest.json`, once per start-up, and again only when the user presses **Check again** or clicks the
  GitHub chip.
- A GET for one zip, only when the user clicks the big tile: a Trainer (**UPDATE**, **INSTALL**, **ADD TRAINER** or
  **SWITCH**) or a new version of the app itself (**UPDATE APP**). If the connection drops part-way, the app asks again
  for the rest of the same file (see "Interrupted downloads" below): still GETs for that one zip, and nothing else.

The only header it adds is `User-Agent: ReSkateTrainerUpdater/<version>`, plus `Range: bytes=<n>-` on a request that
carries on an interrupted download. It sends no identifiers, folder paths,
installed versions or usage data. GitHub sees the user's IP address, as with any visit to github.com.

**Report a problem** opens a page in the user's own browser: this repository's "new issue" page with the app and Windows
version filled in. The app makes no request for it and sends no log; the user reads the page and submits it themselves.
`OnlineSource.IsTrustedPageUrl` only lets the window open https pages of this repository.

On the first run, when no folder is remembered, `ReSkateLocator` also looks through a few local folders for
`ReSkateLauncher.exe` or `ReSkate.dll`. It only reads folder and file names, and nothing about it leaves the PC.

## Timing and caching

- `raw.githubusercontent.com` may serve the old `latest.json` for about 5 minutes after you push.
- Raw files are not the GitHub API, so the API's limit of 60 requests per hour per IP doesn't apply.
- An app that is already open checks at start-up, and again when the user presses **Check again** or clicks the GitHub
  chip. Otherwise users see a new release the next time they open it.

## Changing the format later

- **Adding an optional field:** no schema change is needed, because older apps ignore unknown fields. Update the
  parser, `CheckEntry`, `ToJson` and the tests together.
- **A breaking change:** raise `TrainerManifest.SupportedSchemaVersion`. Release the new app first, then push the
  new-format manifest. Older apps will quietly show "Couldn't check for updates" until the user updates the app.

## The signature

`latest.json` ends with one extra member, `signature`, written by the manifest tool:

```json
  },
  "signature": "<88 characters of base64>"
}
```

- **Algorithm:** ECDSA, curve P-256, SHA-256, signature in the IEEE P1363 layout (r then s, 64 bytes, base64).
- **What is signed:** the file's canonical text (what `TrainerManifest.ToJson` writes) exactly as it is *without* the
  `signature` member: LF line endings, final newline. The member is always last, written as `,\n  "signature": "..."` before
  the closing `}`. `ManifestSignature.Verify` rebuilds the signed text from the file and checks it, tolerating CRLF.
- **Which keys:** the public keys in `ManifestSignature.TrustedPublicKeys`. A signature from any one of them is enough, so a
  spare key kept offline can replace a lost or leaked one: ship an app that lists the new key, then sign with it.
- **Where it is checked:** `TrainerDownloader.FetchManifestAsync`, before the file is parsed. A missing or wrong signature
  gives a null manifest and a line in the activity log, like being offline: the status card says it couldn't check.
- **Old apps:** versions up to 1.4.1 ignore fields they don't know, so they read a signed file exactly as before.
- **Writing it:** `add`, `set-app`, `remove` and `sign` always sign, using `--key-file <path>` or the `RESKATE_MANIFEST_KEY`
  environment variable, and refuse to write anything if the key is missing or isn't a trusted one. `check` and the tests
  verify the committed file. `keygen <file>` makes a key pair; the private key never goes in the repository.

## Interrupted downloads

`TrainerDownloader.DownloadAsync` saves into a temp folder named after the file's hash, as `<name>.zip.part`. If the
connection drops or the time limit passes, what arrived is kept (`DownloadResult.KeptBytes`), and:

- within the same click it carries on by itself, up to 3 times, as long as each try got further;
- on the next click it sends `Range: bytes=<size of the part>-` and appends the answer, if the server says `206` with a
  matching `Content-Range`. A server that answers `200` (the whole file) or `416` just makes it start again from the start.

Nothing about the checks changes: the finished file must match the size and SHA-256 in `latest.json`, so a damaged,
stale or swapped part can never be installed. If a download finished from an old part fails its hash, it is deleted and
tried once from the start before it counts as a failure. Every other failure deletes the folder. Parts older than two
hours are removed at start-up (`CleanUpStaleTempFolders`). GitHub's release downloads support Range (checked: `206` with
`Content-Range: bytes 1000-1099/63779579`).
