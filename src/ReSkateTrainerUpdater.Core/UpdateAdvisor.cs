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

        if (folder.HasRuntime) return ("ReSkate is here, but its main file can't be read.", Severity.Warning);
        if (folder.HasLauncher) return ("ReSkate looks incomplete: its main file is missing.", Severity.Warning);
        if (folder.IsEmpty) return ("Empty folder: this will be a fresh install.", Severity.Neutral);
        return ("No ReSkate in this folder yet.", Severity.Warning);
    }

    /// <summary>"Standard" for the edition with no name, otherwise the edition's own name.</summary>
    private static string EditionLabel(string? edition) => string.IsNullOrWhiteSpace(edition) ? "Standard" : edition;

    // ---------- Online updates ----------

    /// <summary>
    /// What the online check offers for this folder. Only offers what latest.json lists:
    /// an empty or new folder gets the fresh-install edition (Tournament), plain ReSkate is offered that same edition
    /// to add, and an installed Trainer is offered a newer version of its own edition. A Trainer whose edition isn't
    /// listed (such as the standard one) is told the fresh-install edition exists and may switch to it on purpose,
    /// but is never moved over automatically. An unreadable ReSkate.dll gets nothing, and the drop zone works as before.
    /// </summary>
    public static OnlineOffer DescribeOnlineOffer(TrainerManifest? manifest, InstallFolderInfo? folder)
    {
        if (manifest is null || folder is null) return OnlineOffer.Nothing;
        if (!folder.Exists || folder.IsSteamSkateFolder) return OnlineOffer.Nothing;

        var fresh = manifest.Find(OnlineSource.FreshInstallEdition);

        if (!folder.HasRuntime)
            return fresh is not null ? new OnlineOffer(OfferKind.FreshInstall, fresh) : OnlineOffer.Nothing;

        // ReSkate.dll is there but couldn't be read: don't guess.
        if (folder.Installed is not { } installed) return OnlineOffer.Nothing;

        if (!installed.HasTrainer)
            return fresh is not null ? new OnlineOffer(OfferKind.AddTrainer, fresh) : OnlineOffer.Nothing;

        if (manifest.Find(OnlineSource.EditionKey(installed.Edition)) is { } latest)
        {
            return new OnlineOffer(
                VersionCompare.Compare(installed.TrainerVersion, latest.Version) < 0 ? OfferKind.Update : OfferKind.UpToDate,
                latest);
        }

        return fresh is not null ? new OnlineOffer(OfferKind.SwitchEdition, fresh) : OnlineOffer.Nothing;
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
        rows.AddRange(DescriptionRows(app, c.Now));

        // The download is big (it carries .NET inside it), so the tile says how big before anyone clicks.
        return new HomeView("APP UPDATE", Severity.Highlight, "New version of the updater",
            $"{offer.CurrentVersion} → {app.Version}. It's a {ByteSize.Format(app.SizeBytes)} download, and the app restarts itself when it's done.", rows,
            new ActionTile("UPDATE APP", $"{offer.CurrentVersion} → {app.Version} · {ByteSize.Format(app.SizeBytes)}", TileStyle.Primary, TileAction.UpdateApp));
    }

    // "Published 3 days ago" and "What's new: ...", for whatever latest.json says about a release.
    private static IEnumerable<StatusRow> DescriptionRows(IPublishedFile file, DateTimeOffset? now)
    {
        if (ReleaseDate.Describe(file.Published, now ?? DateTimeOffset.UtcNow) is { } ago)
            yield return new StatusRow($"Published {ago}", Severity.Neutral);
        if (file.Notes is { Length: > 0 } notes)
            yield return new StatusRow($"What's new: {notes}", Severity.Neutral);
    }

    // No zip chosen: show what the online check found for this folder, with the small buttons that go with it.
    private static HomeView DescribeOnlineHome(UpdateContext c)
    {
        var view = DescribeOnlineHomeCore(c);

        var links = new List<CardLink>();
        if (c.Online == OnlineStatus.Unavailable) links.Add(new CardLink("Check again", LinkAction.CheckAgain));

        // A newer Trainer doesn't take over the big tile: that stays PLAY. A small button opens the dialog that explains it.
        if (c.App is null && c.Offer is { Kind: OfferKind.Update, Entry: not null } && c.FolderInfo is { HasLauncher: true })
            links.Add(UpdateLink);

        if (c.App is null && RestoreLink(c.FolderInfo) is { } restore) links.Add(restore);
        if (view.Severity == Severity.Error) links.Add(ReportLink);

        return links.Count == 0 ? view : view with { Links = links.Take(HomeView.MaxLinks).ToList() };
    }

    private static readonly CardLink ReportLink = new("Report a problem", LinkAction.ReportProblem);
    private static readonly CardLink UpdateLink = new("Update available", LinkAction.ShowUpdateDialog);

    // Offered when an earlier install left a backup whose files differ from what is installed now.
    private static CardLink? RestoreLink(InstallFolderInfo? folder)
    {
        if (folder is not { Exists: true, HasRuntime: true, PreviousBackup: { } backup }) return null;

        var then = backup.Version;
        var now = folder.Installed;
        if (then is { HasTrainer: true } && now is { HasTrainer: true } &&
            then.TrainerVersion == now.TrainerVersion && then.TrainerBuild == now.TrainerBuild && then.Edition == now.Edition)
        {
            return null; // the same release: there's nothing to go back to
        }

        return new CardLink(then is { HasTrainer: true } ? $"Restore {then.TrainerVersion}" : "Restore backup", LinkAction.RestorePrevious);
    }

    private static HomeView DescribeOnlineHomeCore(UpdateContext c)
    {
        if (c.App is { } appOffer) return DescribeAppUpdateHome(c, appOffer);

        if (c.Folder is null && c.SuggestedFolder is { } suggested)
        {
            return Home("FOUND", Severity.Highlight, "Found ReSkate on this PC",
                $"{suggested}\nUse it, or click the folder box on the right to choose a different one.",
                new ActionTile("USE IT", "Use the folder found", TileStyle.Primary, TileAction.UseSuggestedFolder));
        }

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
                    Rows(folderRow, entry, Severity.Highlight, c.Now));

            case OfferKind.AddTrainer when entry is not null && installed is not null:
                return Home("READY", Severity.Good, "Add the Trainer",
                    $"You have ReSkate {installed.BaseVersion ?? "?"} without the Trainer. {entry.EditionName} Edition {entry.Version} is on GitHub.",
                    new ActionTile("ADD TRAINER", $"{entry.EditionName} {entry.Version}", TileStyle.Primary, TileAction.DownloadAndInstall),
                    Rows(folderRow, entry, Severity.Highlight, c.Now));

            case OfferKind.Update when entry is not null && installed is not null:
                return Home("UPDATE", Severity.Highlight, "Update available",
                    $"{installed.TrainerVersion} → {entry.TrainerText}",
                    // PLAY stays the big tile. The update is behind the "Update available" button. Without a launcher there is
                    // nothing to play, and the blue UPDATE tile is the only way forward.
                    PlayOrIdle(folder, "Start ReSkate",
                        new ActionTile("UPDATE", $"{installed.TrainerVersion} → {entry.Version}", TileStyle.Primary, TileAction.DownloadAndInstall)),
                    Rows(folderRow, entry, Severity.Highlight, c.Now));

            case OfferKind.SwitchEdition when entry is not null && installed is not null:
                return Home("SWITCH", Severity.Highlight, "Another edition is available",
                    $"You have the {EditionLabel(installed.Edition)} edition ({installed.TrainerVersion}). The {entry.EditionName} Edition {entry.Version} is on GitHub too, if you want to switch. Your files are backed up first.",
                    new ActionTile("SWITCH", $"To {entry.EditionName} {entry.Version}", TileStyle.Primary, TileAction.DownloadAndInstall),
                    Rows(folderRow, entry, Severity.Highlight, c.Now));

            case OfferKind.UpToDate when entry is not null && installed is not null:
                bool ahead = VersionCompare.Compare(installed.TrainerVersion, entry.Version) > 0;
                return Home("READY", Severity.Good, "Ready to skate",
                    ahead
                        ? $"You have {installed.TrainerVersion}. The newest published is {entry.TrainerText}."
                        : "You're running the newest version.",
                    PlayOrIdle(folder, "Up to date. Start ReSkate", new ActionTile("UP TO DATE", "Nothing to install", TileStyle.Success, TileAction.None)),
                    Rows(folderRow, entry, Severity.Good, c.Now));
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
            return Home("HEADS UP", Severity.Warning, "Couldn't read your ReSkate files",
                "ReSkate's main file is there but can't be read, so it may be damaged. Reinstall ReSkate, or drop a Trainer zip to replace it.",
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
            PlayOrIdle(folder, "Start ReSkate", Idle("NO UPDATE", "Use a zip")), folderRow);
    }

    // With a Trainer installed and nothing to do, the tile that serves most is PLAY. The status card still says whether
    // the Trainer is current. Without a launcher in the folder there is nothing to start, so the usual tile stays.
    private static ActionTile PlayOrIdle(InstallFolderInfo folder, string subtitle, ActionTile otherwise) =>
        folder.HasLauncher
            ? new ActionTile("PLAY", subtitle, TileStyle.Primary, TileAction.Launch)
            : otherwise;

    // The folder, the newest published Trainer, and whatever latest.json says about it (when it came out, what is new).
    private static StatusRow[] Rows(StatusRow folderRow, ManifestEntry entry, Severity severity, DateTimeOffset? now) =>
        new[] { folderRow, new StatusRow($"Latest: {entry.TrainerText} · {ByteSize.Format(entry.SizeBytes)}", severity) }
            .Concat(DescriptionRows(entry, now))
            .ToArray();

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

            case BusyStage.Closing:
                return Home("CLOSING", Severity.Working, "Closing the game...", what,
                    new ActionTile("CLOSING", "One moment", TileStyle.Primary, TileAction.None, Working: true));

            default:
                return Home("INSTALLING", Severity.Working, "Installing...", "Please don't close the app.",
                    new ActionTile("INSTALLING", what, TileStyle.Primary, TileAction.None, Working: true));
        }
    }

    /// <summary>
    /// Puts the outcome of an install or download on the status card, keeping its rows. The tile stays as it was, unless the
    /// outcome says what comes next: PLAY once the Trainer is in place (and the folder has a launcher), or CLOSE GAME when the
    /// game is in the way. The outcome's own buttons come first, then the ones the screen already had.
    /// </summary>
    public static HomeView WithResult(HomeView view, Advice result, InstallFolderInfo? folder = null)
    {
        var (pill, headline) = result.Severity switch
        {
            Severity.Good => ("DONE", "All set"),
            Severity.Warning => ("HEADS UP", "Nothing changed"),
            Severity.Error => ("PROBLEM", "Something went wrong"),
            _ => ("INFO", "Note"),
        };

        var tile = result.Next switch
        {
            FollowUp.Launch when folder is { HasLauncher: true } =>
                new ActionTile("PLAY", "Start the ReSkate launcher", TileStyle.Primary, TileAction.Launch),
            FollowUp.CloseGameAndRetry =>
                new ActionTile("CLOSE GAME", "Then install again", TileStyle.Primary, TileAction.CloseGameAndRetry),
            _ => view.Tile,
        };

        var links = new List<CardLink>(result.Links ?? Array.Empty<CardLink>());
        if (result.Severity == Severity.Error) links.Add(ReportLink);
        foreach (var existing in view.Links ?? Array.Empty<CardLink>())
        {
            if (!links.Contains(existing) && links.All(l => l.Action != existing.Action))
                links.Add(existing);
        }

        return view with
        {
            Pill = pill,
            Severity = result.Severity,
            Headline = headline,
            Subline = result.Message,
            Tile = tile,
            Links = links.Count == 0 ? view.Links : links.Take(HomeView.MaxLinks).ToList(),
        };
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

    /// <summary>
    /// The message shown once an install has finished (or failed). <paramref name="running"/> names the programs found using
    /// the game's files when the install was blocked, so the message can say which ones to close.
    /// </summary>
    public static Advice DescribeResult(InstallResult result, ReSkateVersion incoming, IReadOnlyList<RunningProcess>? running = null)
    {
        switch (result.Outcome)
        {
            case InstallOutcome.Installed:
                return new Advice($"Installed {incoming.DisplayName}. Press PLAY to start ReSkate.", Severity.Good,
                    Next: FollowUp.Launch, Links: BackupLinks(result));

            case InstallOutcome.AlreadyUpToDate:
                return new Advice("You already have this version. Nothing needed changing.", Severity.Good, Next: FollowUp.Launch);

            case InstallOutcome.Blocked when result.BlockedBy == BlockReason.FolderNotWritable:
                return new Advice(
                    "The updater isn't allowed to write to that folder. Choose a folder you own, for example C:\\Games\\ReSkate.",
                    Severity.Warning);

            case InstallOutcome.Blocked when running is { Count: > 0 }:
                return new Advice(
                    $"{NameList(running)} {(running.Count == 1 ? "is" : "are")} still open and using ReSkate's files. " +
                    "Close it yourself, or press CLOSE GAME and the updater will close it and carry on. Nothing was changed.",
                    Severity.Warning, Next: FollowUp.CloseGameAndRetry);

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

    // "Open backup folder", when the install saved the files it replaced.
    private static IReadOnlyList<CardLink>? BackupLinks(InstallResult result) =>
        result.BackupFolder is { Length: > 0 } folder
            ? new[] { new CardLink("Open backup folder", LinkAction.OpenFolder, folder) }
            : null;

    // "ReSkateLauncher.exe", "skate.exe and ReSkateLauncher.exe", "a, b and c".
    private static string NameList(IReadOnlyList<RunningProcess> processes)
    {
        var names = processes.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }

    // ---------- Restoring, switching, closing and playing ----------

    /// <summary>
    /// The dialog behind the "Update available" button: your Trainer is out of date, this newer one is available, and you can
    /// keep playing or update now.
    /// </summary>
    public static ModalView DescribeUpdateDialog(ReSkateVersion installed, ManifestEntry latest, bool backupsOn, DateTimeOffset? now = null)
    {
        var paragraphs = new List<string>
        {
            $"Your Trainer {installed.TrainerText} is out of date. A newer version is available: {latest.TrainerText}, " +
            $"{ByteSize.Format(latest.SizeBytes)}" +
            (ReleaseDate.Describe(latest.Published, now ?? DateTimeOffset.UtcNow) is { } ago ? $", published {ago}." : "."),
        };
        if (latest.Notes is { Length: > 0 } notes) paragraphs.Add($"What's new: {notes}");
        paragraphs.Add(backupsOn
            ? "You can keep playing on your current version. If you update, your current files are backed up first, so you can go back."
            : "You can keep playing on your current version. Backups are turned off, so if you update your current files can't be restored afterwards.");
        return new ModalView("UPDATE AVAILABLE", paragraphs, "UPDATE", "NOT NOW");
    }

    /// <summary>The question before putting an earlier backup back.</summary>
    public static string DescribeRestorePrompt(BackupInfo backup)
    {
        string what = backup.Version is { HasTrainer: true } v ? "Trainer " + v.TrainerText : "your previous files";
        return $"Go back to {what}, saved on {backup.Created:g}?\r\n\r\nYour current files are saved first, so you can come back to them.";
    }

    /// <summary>The message once an earlier backup has been put back (or couldn't be).</summary>
    public static Advice DescribeRestoreResult(InstallResult result, BackupInfo backup, IReadOnlyList<RunningProcess>? running = null)
    {
        switch (result.Outcome)
        {
            case InstallOutcome.Installed:
                string what = backup.Version is { HasTrainer: true } v ? "Trainer " + v.TrainerText : "your previous files";
                // No follow-up tile: the screen's own big tile stays (PLAY).
                return new Advice($"Went back to {what}. The files it replaced are saved too, in case you change your mind.", Severity.Good,
                    Links: BackupLinks(result));

            case InstallOutcome.AlreadyUpToDate:
                return new Advice("Your files already match that backup. Nothing needed changing.", Severity.Good);

            default:
                // Blocked, rolled back or failed: say it the way an install would.
                return DescribeResult(result, backup.Version ?? new ReSkateVersion(null, null, null, null, null), running);
        }
    }

    /// <summary>
    /// The question before moving a Trainer to a different edition, or null if the two are the same edition (or either is not a Trainer).
    /// </summary>
    public static string? DescribeEditionSwitchPrompt(ReSkateVersion? installed, ReSkateVersion incoming)
    {
        if (installed is not { HasTrainer: true } || !incoming.HasTrainer) return null;
        if (string.Equals(installed.Edition ?? "", incoming.Edition ?? "", StringComparison.OrdinalIgnoreCase)) return null;

        return $"This changes your Trainer from the {EditionLabel(installed.Edition)} edition ({installed.TrainerVersion}) " +
               $"to the {EditionLabel(incoming.Edition)} edition ({incoming.TrainerVersion}).\r\n\r\nContinue?";
    }

    /// <summary>The question before closing the game for the user.</summary>
    public static string DescribeCloseGamePrompt(IReadOnlyList<RunningProcess> running) =>
        $"Close {NameList(running)}?\r\n\r\nAnything you haven't saved in it will be lost. The install carries on once it has closed.";

    /// <summary>When the game was asked to close and didn't.</summary>
    public static Advice DescribeCloseGameFailed(IReadOnlyList<RunningProcess> stillRunning) =>
        new($"Couldn't close {NameList(stillRunning)}. Close it yourself, then press the button again. Nothing was changed.",
            Severity.Warning, Next: FollowUp.CloseGameAndRetry);

    /// <summary>Shown after PLAY is pressed. The tile stays PLAY, in case the launcher is closed and wanted again.</summary>
    public static Advice DescribeLaunched() =>
        new($"Started {ReSkateFiles.Launcher}. Have fun!", Severity.Good, Next: FollowUp.Launch);

    /// <summary>When the launcher couldn't be started.</summary>
    public static Advice DescribeLaunchFailure(LaunchFailure failure, string? detail) => failure switch
    {
        LaunchFailure.NotFound => new Advice(
            $"{ReSkateFiles.Launcher} isn't in that folder, so there's nothing to start. Check the folder.", Severity.Warning),
        _ => new Advice(
            $"Windows couldn't start {ReSkateFiles.Launcher}{(string.IsNullOrWhiteSpace(detail) ? "" : ": " + detail)}. Try opening it from the folder yourself.",
            Severity.Warning),
    };

    // ---------- Asking for help ----------

    /// <summary>The first line of a copied activity log: which app and which Windows it came from.</summary>
    public static string DescribeLogHeader(string appVersion, string windows) =>
        $"ReSkate Trainer Updater v{appVersion} · {windows}";

    /// <summary>The address of a GitHub "new issue" page, with the version already filled in. The log is pasted by the user, not sent.</summary>
    public static string DescribeProblemReportUrl(string appVersion, string windows) =>
        OnlineSource.NewIssueUrl(
            "Problem with the updater",
            "**What happened?**\n\n\n\n**What did you expect?**\n\n\n\n" +
            $"**Updater version:** {appVersion}\n**Windows:** {windows}\n\n" +
            "Please paste the activity log below (open ACTIVITY LOG in the app, then press COPY LOG):\n\n```\n\n```\n");
}
