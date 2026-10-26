# AGENTS.md

All instructions for AI coding agents are in [CLAUDE.md](CLAUDE.md). Read it in full before making changes.
It applies to every agent, not only Claude.

The short version:

- Build: `dotnet build ReSkateTrainerUpdater.sln -c Release` (Windows only). Test: `dotnet run --project tests/ReSkateTrainerUpdater.Tests -c Release` (any OS).
- Logic goes in `src/ReSkateTrainerUpdater.Core`. The WinForms project only wires up the window.
- New user-facing text goes through `UpdateAdvisor`, with a test.
- Only `TrainerInstaller.Install` writes to the user's ReSkate folder.
- Never weaken the download checks: trusted URL, exact size, exact SHA-256, and a package that matches `latest.json`.
- Change `latest.json` only with `tools/ReSkateTrainerUpdater.ManifestTool`.
- `v*` tags are app releases. Trainer zips use `trainer-<edition>-<version>` tags.
