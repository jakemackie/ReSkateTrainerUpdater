namespace ReSkateTrainerUpdater.Core;

/// <summary>
/// Decides what the window should say. Kept free of any UI code so every message can be tested.
/// </summary>
public static class UpdateAdvisor
{
    /// <summary>Works out the status message and whether installing is allowed right now.</summary>
    public static Advice Evaluate(UpdateContext context)
    {
        if (context.CheckingZip) return new Advice("Checking the zip...", Severity.Working);
        if (context.ZipPath is null) return new Advice("Drop the ReSkate Trainer zip to get started.", Severity.Neutral);
        if (context.Package is null) return new Advice(context.ZipProblem ?? "Couldn't read that zip.", Severity.Error);
        if (!context.Package.IsValid) return new Advice(context.Package.Errors[0], Severity.Error);
        if (context.Package.Version is not { } incoming) return new Advice("Couldn't read the version in that zip.", Severity.Error);

        if (context.Folder is null) return new Advice("Now choose your ReSkate folder.", Severity.Neutral);
        if (context.CheckingFolder) return new Advice("Checking your ReSkate folder...", Severity.Working);
        if (context.FolderInfo is not { } folder) return new Advice("Couldn't read that folder. Choose it again.", Severity.Error);

        if (!folder.Exists) return new Advice("That folder doesn't exist any more. Choose it again.", Severity.Error);
        if (folder.IsSteamSkateFolder)
        {
            return new Advice(
                "ReSkate can't run from Steam's skate. folder. Choose a folder of its own, for example C:\\Games\\ReSkate.",
                Severity.Error);
        }

        var (headline, severity) = Describe(incoming, folder);

        var notes = new List<string>(context.Package.Warnings);
        if (folder.HasSpecialCharacters)
            notes.Add("ReSkate may not run from a folder with special characters in its path. Plain letters work best.");
        if (notes.Count > 0)
        {
            headline += " Note: " + notes[0];
            if (severity is Severity.Good or Severity.Neutral) severity = Severity.Warning;
        }

        return new Advice(headline, severity, CanInstall: true);
    }

    // Compares what is installed with what the zip would install.
    private static (string Headline, Severity Severity) Describe(ReSkateVersion incoming, InstallFolderInfo folder)
    {
        var current = folder.Installed;

        if (!folder.HasRuntime || current is null)
            return ($"Ready: install {incoming.DisplayName}.", Severity.Good);

        if (!current.HasTrainer)
            return ($"Ready: add {incoming.DisplayName} to your ReSkate {current.BaseVersion}.", Severity.Good);

        if (!incoming.HasTrainer)
        {
            return ($"Careful: this zip has no Trainer, so it would replace your Trainer {current.TrainerText} with plain ReSkate.",
                Severity.Warning);
        }

        int comparison = VersionCompare.Compare(current.TrainerVersion, incoming.TrainerVersion);
        if (comparison < 0)
            return ($"Ready: update {current.TrainerText} → {incoming.TrainerText}.", Severity.Good);

        if (comparison > 0)
        {
            return ($"This zip ({incoming.TrainerText}) is older than what you have ({current.TrainerText}). Installing it will downgrade.",
                Severity.Warning);
        }

        if (current.TrainerBuild == incoming.TrainerBuild && current.Edition == incoming.Edition)
        {
            return ($"You already have {current.DisplayName}. Installing again only repairs files that have changed.",
                Severity.Neutral);
        }

        return ($"Ready: replace {current.TrainerText} with a different build of the same version ({incoming.TrainerText}).",
            Severity.Good);
    }

    /// <summary>True if installing <paramref name="incoming"/> over <paramref name="current"/> would go back a version.</summary>
    public static bool IsDowngrade(ReSkateVersion? current, ReSkateVersion incoming) =>
        current is { HasTrainer: true } && incoming.HasTrainer &&
        VersionCompare.Compare(current.TrainerVersion, incoming.TrainerVersion) > 0;

    /// <summary>The line shown under the folder: what is installed in it.</summary>
    public static (string Text, Severity Severity) DescribeFolder(InstallFolderInfo folder)
    {
        if (!folder.Exists) return ("This folder doesn't exist any more.", Severity.Error);
        if (folder.IsSteamSkateFolder) return ("This is Steam's skate. folder, and ReSkate can't run from here.", Severity.Error);

        if (folder.HasRuntime && folder.Installed is { } installed)
        {
            return installed.HasTrainer
                ? ($"Installed: Trainer {installed.TrainerText} · ReSkate {installed.BaseVersion ?? "?"}", Severity.Good)
                : ($"Installed: ReSkate {installed.BaseVersion ?? "?"} (no Trainer yet)", Severity.Neutral);
        }

        if (folder.HasRuntime) return ($"{ReSkateFiles.RuntimeDll} is here but couldn't be read.", Severity.Warning);
        if (folder.HasLauncher) return ($"{ReSkateFiles.Launcher} is here but {ReSkateFiles.RuntimeDll} is missing.", Severity.Warning);
        if (folder.IsEmpty) return ("Empty folder: this will be a fresh install.", Severity.Neutral);
        return ("No ReSkate found in this folder yet.", Severity.Warning);
    }

    // ---------- Online updates ----------

    /// <summary>
    /// What the online check offers for this folder. Only offers what latest.json lists:
    /// an empty or new folder gets the fresh-install edition (Tournament), and an installed Trainer is only
    /// offered a newer version of its own edition. Plain ReSkate, an unreadable ReSkate.dll, and editions the
    /// manifest doesn't list get nothing, and the drop zone works as before.
    /// </summary>
    public static OnlineOffer DescribeOnlineOffer(TrainerManifest? manifest, InstallFolderInfo? folder)
    {
        if (manifest is null || folder is null) return OnlineOffer.Nothing;
        if (!folder.Exists || folder.IsSteamSkateFolder) return OnlineOffer.Nothing;

        if (!folder.HasRuntime)
        {
            return manifest.Find(OnlineSource.FreshInstallEdition) is { } fresh
                ? new OnlineOffer(OfferKind.FreshInstall, fresh)
                : OnlineOffer.Nothing;
        }

        // ReSkate.dll is there but couldn't be read, or it is plain ReSkate: don't guess.
        if (folder.Installed is not { HasTrainer: true } installed) return OnlineOffer.Nothing;
        if (manifest.Find(OnlineSource.EditionKey(installed.Edition)) is not { } latest) return OnlineOffer.Nothing;

        return new OnlineOffer(
            VersionCompare.Compare(installed.TrainerVersion, latest.Version) < 0 ? OfferKind.Update : OfferKind.UpToDate,
            latest);
    }

    /// <summary>
    /// Whether a newer version of this app is published. Null when it is up to date, when latest.json lists none, or when
    /// the app can't replace itself (for example when run through `dotnet`).
    /// </summary>
    public static AppOffer? DescribeAppOffer(TrainerManifest? manifest, string currentVersion, bool canUpdateInPlace) =>
        canUpdateInPlace && manifest?.App is { } app && VersionCompare.Compare(currentVersion, app.Version) < 0
            ? new AppOffer(app, currentVersion)
            : null;

    // ---------- The main window ----------

    private static ActionTile Idle(string title, string subtitle) =>
        new(title, subtitle, TileStyle.Muted, TileAction.None);

    private static HomeView Home(string pill, Severity severity, string headline, string subline, ActionTile tile,
        params StatusRow[] rows) => new(pill, severity, headline, subline, rows, tile);

    /// <summary>
    /// Works out everything the main window shows: the status card, its pill and rows, and the big action tile.
    /// The window draws this and does nothing else, so every case here is tested.
    /// </summary>
    public static HomeView DescribeHome(UpdateContext context)
    {
        if (context.CheckingZip)
        {
            return Home("CHECKING", Severity.Working, "Checking your zip...", Path.GetFileName(context.ZipPath ?? ""),
                Idle("INSTALL", "One moment"));
        }

        return context.ZipPath is null ? DescribeOnlineHome(context) : DescribeZipHome(context);
    }

    // A zip the user chose. It takes over from the online offer until it is cleared.
    private static HomeView DescribeZipHome(UpdateContext c)
    {
        var advice = Evaluate(c);

        if (c.Package is not { IsValid: true, Version: { } incoming })
        {
            return Home("PROBLEM", Severity.Error,
                c.Package is null ? "Couldn't read that zip" : "That isn't a Trainer update",
                advice.Message, Idle("INSTALL", "Choose another zip"));
        }

        var rows = new List<StatusRow>();
        if (!c.CheckingFolder && c.FolderInfo is { Exists: true, IsSteamSkateFolder: false } folder)
        {
            var (text, severity) = DescribeFolder(folder);
            rows.Add(new StatusRow(text, severity));
        }

        string zipName = incoming.HasTrainer ? "Trainer " + incoming.TrainerText : "ReSkate " + incoming.BaseVersion;
        rows.Add(new StatusRow($"Zip: {zipName}" + (incoming.HasTrainer && incoming.BaseVersion is not null ? $" · ReSkate {incoming.BaseVersion}" : ""),
            Severity.Highlight));

        var (pill, pillSeverity) = advice.Severity switch
        {
            Severity.Good => ("READY", Severity.Good),
            Severity.Warning => ("HEADS UP", Severity.Warning),
            Severity.Error => ("PROBLEM", Severity.Error),
            Severity.Working => ("CHECKING", Severity.Working),
            _ => (advice.CanInstall ? "READY" : "NEXT", Severity.Neutral),
        };

        var tile = advice.CanInstall
            ? new ActionTile("INSTALL", IsDowngrade(c.FolderInfo?.Installed, incoming) ? "Older than yours" : "From your zip",
                TileStyle.Primary, TileAction.InstallZip)
            : Idle("INSTALL", c.Folder is null ? "Choose a folder first" : advice.Severity == Severity.Working ? "One moment" : "See the note");

        // When something is wrong, say so in the headline instead of repeating the zip's name.
        return new HomeView(pill, pillSeverity, pillSeverity == Severity.Error ? "Can't install yet" : zipName, advice.Message, rows, tile);
    }

    // A newer version of the app itself comes before everything else: the new version may improve how Trainers are installed.
    private static HomeView DescribeAppUpdateHome(UpdateContext c, AppOffer offer)
    {
        var app = offer.Entry;
        var rows = new List<StatusRow>();
        if (c.Folder is not null && !c.CheckingFolder && c.FolderInfo is { Exists: true, IsSteamSkateFolder: false, HasRuntime: true } folder)
        {
            var (text, severity) = DescribeFolder(folder);
            rows.Add(new StatusRow(text, severity));
        }
        rows.Add(new StatusRow($"New updater: {app.Version} · {ByteSize.Format(app.SizeBytes)}", Severity.Highlight));

        return new HomeView("APP UPDATE", Severity.Highlight, "New version of the updater",
            $"{offer.CurrentVersion} → {app.Version}. The app restarts itself when it's done.", rows,
            new ActionTile("UPDATE APP", $"{offer.CurrentVersion} → {app.Version}", TileStyle.Primary, TileAction.UpdateApp));
    }

    // No zip chosen: show what the online check found for this folder.
    private static HomeView DescribeOnlineHome(UpdateContext c)
    {
        if (c.App is { } appOffer) return DescribeAppUpdateHome(c, appOffer);

        if (c.Folder is null)
        {
            return Home("SET UP", Severity.Neutral, "Choose your folder",
                "Pick the folder with ReSkateLauncher.exe, or an empty folder for a fresh install.",
                Idle("INSTALL", "Choose a folder first"));
        }
        if (c.CheckingFolder)
            return Home("CHECKING", Severity.Working, "Checking your folder...", "", Idle("INSTALL", "One moment"));
        if (c.FolderInfo is not { } folder)
        {
            return Home("PROBLEM", Severity.Error, "Couldn't read that folder", "Choose it again.",
                Idle("INSTALL", "Choose a folder first"));
        }
        if (!folder.Exists)
        {
            return Home("PROBLEM", Severity.Error, "Folder not found",
                "That folder doesn't exist any more. Choose it again.", Idle("INSTALL", "Choose a folder first"));
        }
        if (folder.IsSteamSkateFolder)
        {
            return Home("PROBLEM", Severity.Error, "Not this folder",
                "ReSkate can't run from Steam's skate. folder. Choose a folder of its own, for example C:\\Games\\ReSkate.",
                Idle("INSTALL", "Choose another folder"));
        }

        var (folderText, folderSeverity) = DescribeFolder(folder);
        var folderRow = new StatusRow(folderText, folderSeverity);

        if (c.Online == OnlineStatus.Checking)
        {
            return Home("CHECKING", Severity.Working, "Checking for updates...",
                "Looking on GitHub for the newest Trainer.", Idle("INSTALL", "One moment"), folderRow);
        }

        var offer = c.Offer ?? OnlineOffer.Nothing;
        var entry = offer.Entry;
        var installed = folder.Installed;

        switch (offer.Kind)
        {
            case OfferKind.FreshInstall when entry is not null:
                return Home("READY", Severity.Good, "Ready to install",
                    $"{entry.EditionName} Edition {entry.Version} is on GitHub.",
                    new ActionTile("INSTALL", $"{entry.EditionName} {entry.Version}", TileStyle.Primary, TileAction.DownloadAndInstall),
                    folderRow, LatestRow(entry, Severity.Highlight));

            case OfferKind.Update when entry is not null && installed is not null:
                return Home("UPDATE", Severity.Highlight, "Update available",
                    $"{installed.TrainerVersion} → {entry.TrainerText}",
                    new ActionTile("UPDATE", $"{installed.TrainerVersion} → {entry.Version}", TileStyle.Primary, TileAction.DownloadAndInstall),
                    folderRow, LatestRow(entry, Severity.Highlight));

            case OfferKind.UpToDate when entry is not null && installed is not null:
                bool ahead = VersionCompare.Compare(installed.TrainerVersion, entry.Version) > 0;
                return Home("READY", Severity.Good, "Ready to skate",
                    ahead
                        ? $"You have {installed.TrainerVersion}. The newest published is {entry.TrainerText}."
                        : "You're running the newest version.",
                    new ActionTile("UP TO DATE", "Nothing to install", TileStyle.Success, TileAction.None),
                    folderRow, LatestRow(entry, Severity.Good));
        }

        // Nothing to offer. Say why, so the screen is never a mystery.
        bool offline = c.Online == OnlineStatus.Unavailable;
        if (!folder.HasRuntime)
        {
            return Home("SET UP", Severity.Neutral,
                offline ? "Can't check GitHub" : "Nothing published yet",
                offline
                    ? "Couldn't reach GitHub. Drop a Trainer zip to install instead."
                    : "No Trainer is published online yet. Drop a Trainer zip to install instead.",
                Idle("INSTALL", "Use a zip"), folderRow);
        }
        if (installed is null)
        {
            return Home("HEADS UP", Severity.Warning, "Couldn't read ReSkate.dll",
                "The file is there but couldn't be read. Drop a Trainer zip to replace it.",
                Idle("INSTALL", "Use a zip"), folderRow);
        }
        if (!installed.HasTrainer)
        {
            return Home("READY", Severity.Neutral, "ReSkate without the Trainer",
                "Drop a Trainer zip to add the Trainer to this ReSkate.", Idle("INSTALL", "Use a zip"), folderRow);
        }
        return Home(offline ? "OFFLINE" : "READY", Severity.Neutral,
            offline ? "Couldn't check for updates" : $"Trainer {installed.TrainerText} installed",
            offline
                ? "Couldn't reach GitHub. You can still drop a Trainer zip."
                : "There is no online update for this edition. You can still drop a Trainer zip.",
            Idle("NO UPDATE", "Use a zip"), folderRow);
    }

    private static StatusRow LatestRow(ManifestEntry entry, Severity severity) =>
        new($"Latest: {entry.TrainerText} · {ByteSize.Format(entry.SizeBytes)}", severity);

    /// <summary>The credit card in the top-left corner: who made this tool.</summary>
    public static ChipView DescribeCreator() =>
        new($"Made by {AppCredits.CreatorHandle}", "Creator of this tool", Severity.Neutral, ShowDot: false);

    /// <summary>The chip in the top-left corner: whether GitHub could be reached, and which version of the app this is.</summary>
    public static ChipView DescribeConnection(OnlineStatus status, string appVersion)
    {
        var (title, severity) = status switch
        {
            OnlineStatus.Reachable => ("Connected to GitHub", Severity.Good),
            OnlineStatus.Unavailable => ("Can't reach GitHub", Severity.Neutral),
            _ => ("Checking GitHub...", Severity.Working),
        };
        return new ChipView(title, $"v{appVersion} · Unofficial helper", severity);
    }

    /// <summary>The screen while something is running. <paramref name="what"/> names it, for example "ReSkate Trainer 0.9.2 (Tournament)".</summary>
    public static HomeView DescribeBusy(BusyStage stage, string what, DownloadProgress? progress = null)
    {
        switch (stage)
        {
            case BusyStage.Downloading:
                double? fraction = progress is { Total: > 0 } p ? Math.Clamp((double)p.Received / p.Total, 0, 1) : 0;
                string sizes = progress is { Total: > 0 } q ? $"{ByteSize.Format(q.Received)} of {ByteSize.Format(q.Total)}" : "Starting...";
                return Home("DOWNLOADING", Severity.Working, "Downloading...", what,
                    new ActionTile("DOWNLOADING", sizes, TileStyle.Primary, TileAction.None, fraction, Working: true),
                    new StatusRow("From GitHub, verified before install", Severity.Working));

            case BusyStage.Unpacking:
                return Home("VERIFYING", Severity.Working, "Checking the download...", what,
                    new ActionTile("VERIFYING", "Almost there", TileStyle.Primary, TileAction.None, Working: true));

            case BusyStage.Restarting:
                return Home("RESTARTING", Severity.Working, "Restarting...", "The new version opens in a moment.",
                    new ActionTile("RESTARTING", what, TileStyle.Primary, TileAction.None, Working: true));

            default:
                return Home("INSTALLING", Severity.Working, "Installing...", "Please don't close the app.",
                    new ActionTile("INSTALLING", what, TileStyle.Primary, TileAction.None, Working: true));
        }
    }

    /// <summary>Puts the outcome of an install or download on the status card, keeping its rows and tile.</summary>
    public static HomeView WithResult(HomeView view, Advice result)
    {
        var (pill, headline) = result.Severity switch
        {
            Severity.Good => ("DONE", "All set"),
            Severity.Warning => ("HEADS UP", "Nothing changed"),
            Severity.Error => ("PROBLEM", "Something went wrong"),
            _ => ("INFO", "Note"),
        };
        return view with { Pill = pill, Severity = result.Severity, Headline = headline, Subline = result.Message };
    }

    /// <summary>The activity-log line after the start-up check of latest.json.</summary>
    public static string DescribeManifestCheck(ManifestReadResult result)
    {
        if (result.Manifest is null)
        {
            var why = result.Problems.Count > 0 ? result.Problems[0] : "unknown problem";
            return $"Online check skipped ({why}). Dropping a zip still works.";
        }

        if (result.Manifest.Editions.Count == 0) return "Online check: no Trainer has been published online yet.";

        var latest = string.Join(", ", result.Manifest.Editions.Values.Select(e => $"{e.EditionName} {e.Version}"));
        return $"Online check: newest published Trainer is {latest}.";
    }

    /// <summary>The message when a download fails. Every case says nothing was changed, because nothing was.</summary>
    public static Advice DescribeDownloadFailure(DownloadFailure failure) => failure switch
    {
        DownloadFailure.UntrustedUrl => new Advice(
            "The update link doesn't point to this app's GitHub releases, so it was refused. Nothing was changed.",
            Severity.Error),
        DownloadFailure.Network => new Advice(
            "Couldn't download the update. Check your internet connection and try again. Nothing was changed.",
            Severity.Warning),
        DownloadFailure.HttpError => new Advice(
            "GitHub couldn't provide the update file right now. Try again later, or drop a zip instead. Nothing was changed.",
            Severity.Warning),
        DownloadFailure.SizeMismatch or DownloadFailure.HashMismatch => new Advice(
            "The downloaded file doesn't match the published one, so it was deleted and not installed. Nothing was changed.",
            Severity.Error),
        DownloadFailure.TimedOut => new Advice(
            "The download took too long and was stopped. Try again, or drop a zip instead. Nothing was changed.",
            Severity.Warning),
        _ => new Advice(
            "Couldn't save the download to your temp folder. Nothing was changed.",
            Severity.Error),
    };

    /// <summary>The message when the new version of the app couldn't be put in place. The current version is always left working.</summary>
    public static Advice DescribeAppUpdateFailure(AppUpdateFailure failure) => failure switch
    {
        AppUpdateFailure.NotWritable => new Advice(
            "The updater can't replace itself in this folder. Move it to a folder you own, such as your Desktop, or download the new version from GitHub. Nothing was changed.",
            Severity.Warning),
        AppUpdateFailure.BadPackage => new Advice(
            "The downloaded update isn't the version that was published, so it wasn't used. Nothing was changed.",
            Severity.Error),
        _ => new Advice(
            "Couldn't swap in the new version, so your current one is untouched. Close and reopen the app, or download the new version from GitHub.",
            Severity.Warning),
    };

    /// <summary>When the new version is in place but couldn't be started, so the old one is still showing.</summary>
    public static Advice DescribeAppRestartFailed(string version) =>
        new($"Version {version} is installed, but it couldn't open by itself. Close this window and open the updater again.",
            Severity.Warning);

    /// <summary>Shown by the new version the first time it opens after an update.</summary>
    public static Advice DescribeAppUpdated(string version) =>
        new($"Updated to version {version}. You have the newest updater.", Severity.Good);

    /// <summary>The message when the downloaded zip isn't what latest.json promised.</summary>
    public static Advice DescribePackageMismatch(string reason) =>
        new($"The downloaded update isn't what was published, so it was not installed. Nothing was changed. {reason}",
            Severity.Error);

    /// <summary>The message shown once an install has finished (or failed).</summary>
    public static Advice DescribeResult(InstallResult result, ReSkateVersion incoming)
    {
        switch (result.Outcome)
        {
            case InstallOutcome.Installed:
                return new Advice($"Installed {incoming.DisplayName}. Start {ReSkateFiles.Launcher} as usual.", Severity.Good);

            case InstallOutcome.AlreadyUpToDate:
                return new Advice("You already have this version. Nothing needed changing.", Severity.Good);

            case InstallOutcome.Blocked when result.BlockedBy == BlockReason.FolderNotWritable:
                return new Advice(
                    "The updater isn't allowed to write to that folder. Choose a folder you own, for example C:\\Games\\ReSkate.",
                    Severity.Warning);

            case InstallOutcome.Blocked:
                return new Advice(
                    "ReSkate or its launcher is still open. Close the game and the ReSkate launcher, then try again. Nothing was changed.",
                    Severity.Warning);

            case InstallOutcome.RolledBack:
                return new Advice(
                    "The install failed part-way, so your original files were put back. Nothing has changed. See the activity log.",
                    Severity.Warning);

            default:
                return new Advice(
                    $"The install failed and some files couldn't be restored. Your original files are saved in: {result.RescueFolder}",
                    Severity.Error);
        }
    }
}
