# Releasing

This repository publishes two different things. Keep them apart.

| What | Tag | How it's built | Marked "Latest"? |
| --- | --- | --- | --- |
| **The app** (`ReSkateTrainerUpdater-<version>.zip`) | `v1.1.0` | Automatically, by `.github/workflows/release.yml` | Yes, always |
| **A Trainer zip** (`ReSkate_Trainer_Tournament-0.9.2-on-v2.1.1.zip`) | `trainer-tournament-0.9.2` | By hand: you upload the zip you received | No, never |

A tag starting with `v` always starts the app's Release workflow. Never use one for a Trainer zip. The manifest
tool refuses it.

## One-time setup

1. **Turn on two-factor authentication** for the GitHub account that owns the repository. Anyone who can push to
   `main` and create Releases can ship code to every user.
2. **Check the Actions token can write.** Go to Settings → Actions → General → Workflow permissions. Either
   setting works, because `release.yml` asks for `contents: write` itself. But if an organisation policy
   forces read-only, the Release workflow will fail at its last step.
3. **Optional: protect `main`.** Go to Settings → Branches and add a rule that requires the CI check to pass
   before merging. CI includes `verify`, which downloads every published zip and checks it.

## Releasing a new version of the app

1. Set `<Version>` in `Directory.Build.props`, for example `1.1.0`. This is the only place the version lives.
2. Commit and push to `main`. Wait for the **CI** workflow to pass.
3. Tag the same commit and push the tag:

   ```
   git tag v1.1.0
   git push origin v1.1.0
   ```

4. Watch the **Release** workflow in the Actions tab. It checks that the tag matches `<Version>`, runs the
   tests, builds the single-file exe, zips it, and creates the GitHub Release with that zip attached, marked
   Latest.
5. Open the Release, download the zip, and run the exe once on a Windows PC to confirm it starts.

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

CI runs `verify` again. Running copies of the app see the update the next time they start. Allow about 5
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

Don't delete a Release that `latest.json` still points to. Users would get a download error, and CI fails.

## Never

- Commit a zip, exe or DLL to the repository. A zip about 17 MB in size stays in git history forever, every
  clone downloads it, and GitHub blocks files over 100 MB. Release assets cost nothing.
- Edit `latest.json` by hand. The tests fail if it isn't in the tool's exact format.
- Replace the zip on an existing Release with a different file. The hash in `latest.json` would no longer
  match, and every download would be refused. Publish a new version with a new tag instead.
