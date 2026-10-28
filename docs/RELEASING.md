# Releasing

This repository publishes two different things. Keep them apart.

| What | Tag | How it's built | Marked "Latest"? |
| --- | --- | --- | --- |
| **The app** (`ReSkateTrainerUpdater-<version>.zip`) | `v1.1.0` | Automatically, by `.github/workflows/release.yml` | Yes, always |
| **A Trainer zip** (`ReSkate_Trainer_Tournament-0.9.2-on-v2.1.1.zip`) | `trainer-tournament-0.9.2` | By hand: you upload the zip you received | No, never |

A tag starting with `v` always starts the app's Release workflow. Never use one for a Trainer zip. The manifest
tool refuses it.

## What a release contains

| File | For | Notes |
| --- | --- | --- |
| `ReSkateTrainerUpdater-Setup-<version>.exe` | People installing the app | Installs for the current user (no administrator), adds a Start Menu entry and an uninstaller |
| `ReSkateTrainerUpdater-<version>.zip` | The app's own self-update, and anyone who wants the portable exe | `latest.json` points at this one. Don't remove it |

The Release workflow builds both with `scripts/build-release.ps1` and puts a "download the Setup" line at the top of the
release notes. Building the setup needs Inno Setup 6, which GitHub's Windows runners already have. To build locally:
`winget install JRSoftware.InnoSetup`, then run the script (or add `-SkipInstaller` to skip the setup).

## One-time setup

1. **Turn on two-factor authentication** for the GitHub account that owns the repository. Anyone who can push to
   `main` and create Releases can ship code to every user.
2. **Check the Actions token can write.** Go to Settings → Actions → General → Workflow permissions. Either
   setting works, because `release.yml` asks for `contents: write` itself. But if an organisation policy
   forces read-only, the Release workflow will fail at its last step.
3. **Create the manifest signing key and store it as a secret.** The app ignores a `latest.json` that isn't signed, and
   the Release workflow stops at the start if it can't sign. Once, on your PC:
   `dotnet run --project tools/ReSkateTrainerUpdater.ManifestTool -- keygen C:\Users\<you>\.reskate-signing\manifest-signing.key`
   (a path outside the repository), paste the printed public key into `ManifestSignature.TrustedPublicKeys`, then add
   the key file's text as the repository secret `MANIFEST_SIGNING_KEY` (Settings → Secrets and variables → Actions).
   **Back the key file up somewhere safe.** Lose it and you can't publish; leak it and anyone can. Recommended: make a
   second pair the same way, list its public key too, and keep that private key offline as a spare. To sign a manifest
   change yourself, pass `--key-file <path>` to `add`, `set-app`, `remove` or `sign`.
4. **Optional: protect `main`.** Go to Settings → Branches and add a rule that requires the CI check to pass
   before merging. CI has three jobs that run side by side (`test`, `package` and `linux-tests`). If you
   require checks, require all three. The `Verify latest.json` workflow (`verify-manifest.yml`) downloads every
   published zip and checks it; it runs when `latest.json` changes and every Monday, not on every push.

## Releasing a new version of the app

1. Set `<Version>` in `Directory.Build.props`, for example `1.1.0`. This is the only place the version lives.
   Also put one short line (at most 90 characters) in `release-note.txt` at the repository root. The app shows it as
   "What's new" next to the **UPDATE APP** tile. If the file is missing or empty, no line is shown.
2. Commit and push to `main`. Wait for the **CI** workflow to pass.
3. Tag the same commit and push the tag:

   ```
   git tag v1.1.0
   git push origin v1.1.0
   ```

4. Watch the **Release** workflow in the Actions tab. It checks that the tag matches `<Version>`, runs the
   tests, builds the single-file exe, zips it together with `THIRD-PARTY-NOTICES.md` (the font licences), builds the
   setup program, and creates the GitHub Release with the setup and the zip attached, marked Latest.
5. The same workflow then **records the release in `latest.json`** so that installed copies can update themselves.
   It takes main's `latest.json`, runs `set-app` (which refuses a zip that isn't this version's genuine exe), runs
   `verify` (which downloads the published zip and checks it), and commits the result to `main` as
   "Publish app v1.2.0 for self-update". You don't touch `latest.json` for an app release.
6. Open the Release, download the zip, and run the exe once on a Windows PC to confirm it starts.

**Installed copies update themselves** the next time they start: the big tile says **UPDATE APP**. Allow about
5 minutes for GitHub's cache of `latest.json`. Copies older than 1.2.0 have no update code, so they need one last
manual download of a version from 1.2.0 on. After that they never do.

**If the last step of the workflow fails** (the release is published, but "Publish this version for self-update"
is red), the most likely cause is a rule on `main` that stops the workflow pushing. Either allow
`github-actions[bot]` to push, or record the release by hand from the repository root:

```
dotnet run --project tools/ReSkateTrainerUpdater.ManifestTool -- set-app C:\path\to\ReSkateTrainerUpdater-1.2.0.zip
dotnet run --project tools/ReSkateTrainerUpdater.ManifestTool -- verify
git add latest.json
git commit -m "Publish app v1.2.0 for self-update"
git push
```

Use the zip attached to the Release (download it), not a rebuilt one: its hash has to match.

If the workflow fails at "Check the tag matches the version", delete the tag (`git push --delete origin v1.1.0`
and `git tag -d v1.1.0`), fix `<Version>`, and start again from step 2.

## Publishing a new Trainer zip

Do these steps in this order. If `latest.json` goes live before the zip is uploaded, users who click
**Update** get "GitHub couldn't provide the update file". Nothing breaks, but it looks bad.

### 1. Check you may redistribute it

The Trainer is GPL-3.0. Redistributing the binaries is allowed, but the GPL requires you to make the matching
source code available to anyone you give them to. To meet that:

- Publish the zip **unmodified**, so its `LICENSE.txt` and `README.txt`, with its source links, stay inside.
- Put the source links in the Release notes (see the template below).
- Make sure the source for that exact version stays available. The safest way is to link to the exact tag or
  commit in the upstream repositories, for example the build hash `578f597`. Better still, attach a source
  archive of that commit to the Release.

This is general guidance, not legal advice. If in doubt, ask the Trainer authors. They may also simply be happy to
be asked.

### 2. Give the file a safe name

The file name may use only letters, digits, `.`, `-` and `_`. GitHub renames other characters when you upload,
which would break the download link. For example, rename
`ReSkate_Trainer_Tournament-0.8.3-on-v2.1.1 2 By Jaq.zip` to
`ReSkate_Trainer_Tournament-0.8.3-on-v2.1.1.zip`. Renaming doesn't change the file's hash.

### 3. Add it to latest.json

From the repository root, on Windows:

```
dotnet run --project tools/ReSkateTrainerUpdater.ManifestTool -- add C:\path\to\ReSkate_Trainer_Tournament-0.9.2-on-v2.1.1.zip
```

The tool unpacks the zip, reads the version, edition and build from the DLL itself, computes the size and SHA-256,
and writes the entry into `latest.json`. Other editions are kept. It prints the exact tag to use, by default
`trainer-<edition>-<version>`. Don't commit yet.

It also records today's date as `published` (override with `--published 2026-10-25`), which the app shows as
"Published 3 days ago". To add a "What's new" line, add `--notes "One short line, up to 90 characters"`.

### 4. Create the GitHub Release

**On the website:** go to Releases → **Draft a new release**.

- **Tag:** type exactly the tag the tool printed, for example `trainer-tournament-0.9.2`, and choose
  **Create new tag on publish**. Target: `main`.
- **Title:** `ReSkate Trainer 0.9.2 (Tournament)`.
- **Attach** the zip, exactly the file you gave the tool. Don't re-zip it.
- **Untick "Set as the latest release".** This step matters: the README's download link points to the latest
  release, which must stay the app.
- **Publish release.**

**Or with the GitHub CLI:**

```
gh release create trainer-tournament-0.9.2 ReSkate_Trainer_Tournament-0.9.2-on-v2.1.1.zip --title "ReSkate Trainer 0.9.2 (Tournament)" --notes-file notes.md --latest=false
```

Release notes template:

```
ReSkate Trainer 0.9.2 Tournament Edition (build 578f597), built on ReSkate 2.1.1.
Redistributed unmodified for ReSkate Trainer Updater. Install it with the updater, or copy
ReSkate.dll and ReSkateLauncher.exe over your ReSkate folder.

Licence: GPL-3.0 (LICENSE.txt inside the zip).
Source: https://github.com/Dingo-Shenanigans/ReSkate, https://github.com/andrewnakas/reskate-trainer,
https://github.com/Sivaes/darkpop and https://github.com/Sivaes/grabbugs
```

### 5. Verify before pushing

```
dotnet run --project tools/ReSkateTrainerUpdater.ManifestTool -- verify
```

This downloads the zip from the Release exactly as the app will, then checks its size, SHA-256, version, edition
and build. It must end with `Every published zip downloads and matches latest.json.` If it reports a 404, check
the tag and file name on the Release match the URL it printed.

### 6. Push latest.json

```
git add latest.json
git commit -m "Publish ReSkate Trainer 0.9.2 (Tournament)"
git push
```

The `Verify latest.json` workflow runs again. Running copies of the app see the update the next time they start. Allow about 5
minutes for GitHub's cache.

## Pulling a bad Trainer release

To stop the app offering a release, either go back to the previous zip:

```
dotnet run --project tools/ReSkateTrainerUpdater.ManifestTool -- add C:\path\to\previous.zip --tag trainer-tournament-0.9.1
```

or stop offering that edition entirely:

```
dotnet run --project tools/ReSkateTrainerUpdater.ManifestTool -- remove tournament
```

Then commit and push `latest.json`. Users who already installed the bad version keep it. The app never
downgrades anyone automatically. Tell them on Discord to drop the older zip onto the app.

Don't delete a Release that `latest.json` still points to. Users would get a download error, and the weekly `Verify latest.json` run fails.

## Pulling a bad app release

Installed copies only update forward, so the fix for a bad app version is a newer good one: bump `<Version>`, tag, and
release again. The workflow points `latest.json` at it, and everyone who took the bad one is offered it.

To stop offering a bad version **right away**, before the fix is ready, remove the `app` entry from `latest.json` by
hand (delete the `"app": { ... }` block, then run `check`) and push. Copies that already updated keep the bad
version until the fixed one is published. Don't delete a Release that `latest.json` still points to.

## Never

- Commit a zip, exe or DLL to the repository. A zip about 17 MB in size stays in git history forever, every
  clone downloads it, and GitHub blocks files over 100 MB. Release assets cost nothing.
- Edit `latest.json` by hand. Any change breaks its signature, so every installed copy ignores it, and the tests fail.
  Use the manifest tool (`remove` pulls an entry; `sign` re-signs).
- Replace the zip on an existing Release with a different file. The hash in `latest.json` would no longer
  match, and every download would be refused. Publish a new version with a new tag instead.

## Package managers

After an app release is published, run `powershell -ExecutionPolicy Bypass -File .\scripts\make-package-manifests.ps1`.
It downloads the release's zip and setup (so the hashes are of what people really get), rewrites
`bucket\reskate-trainer-updater.json` (the Scoop manifest, which lives in this repository, so commit it) and writes the winget
manifest to `release\packaging\winget\<version>\`.

- **Scoop:** `scoop bucket add reskate https://github.com/jakemackie/ReSkateTrainerUpdater`, then
  `scoop install reskate/reskate-trainer-updater`. The manifest has `checkver`/`autoupdate`, so Scoop's own tooling can also
  bump it.
- **winget:** check with `winget validate --manifest release\packaging\winget\<version>`, then copy the folder into a fork of
  `microsoft/winget-pkgs` as `manifests/j/jakemackie/ReSkateTrainerUpdater/<version>/` and open a pull request (or run
  `wingetcreate submit`). The first one is reviewed by hand; later ones are mostly automatic. An unsigned setup may be flagged
  by their checks. The pull request is a public submission under your name, so do it yourself.

The app also updates itself, so a package manager may list an older version than the one running. That is harmless.
