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
        if (context.ZipPath is null) return DescribeNoZip(context.Offer);
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

    /// <summary>The word shown in the coloured pill beside a message of this severity.</summary>
    public static string PillLabel(Severity severity) => severity switch
    {
        Severity.Working => "Working",
        Severity.Good => "All good",
        Severity.Warning => "Heads up",
        Severity.Error => "Problem",
        _ => "Info",
    };

    // The status line before any zip is chosen. The online banner, when shown, is the quicker route.
    private static Advice DescribeNoZip(OnlineOffer? offer) => offer?.Kind switch
    {
        OfferKind.Update or OfferKind.FreshInstall =>
            new Advice($"Click {offer.ButtonText} above to download it from GitHub, or drop a zip instead.", Severity.Neutral),
        OfferKind.UpToDate =>
            new Advice("You have the newest published version. Drop a zip only if you want a different one.", Severity.Neutral),
        _ => new Advice("Drop the ReSkate Trainer zip to get started.", Severity.Neutral),
    };

    // ---------- Online updates ----------

    /// <summary>
    /// What the online banner offers for this folder. Only offers what latest.json lists:
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
            if (manifest.Find(OnlineSource.FreshInstallEdition) is not { } fresh) return OnlineOffer.Nothing;
            return new OnlineOffer(
                OfferKind.FreshInstall,
                fresh,
                $"{fresh.EditionName} {fresh.Version} is available",
                DetailFor(fresh),
                $"Install {fresh.EditionName} Edition");
        }

        // ReSkate.dll is there but couldn't be read, or it is plain ReSkate: don't guess.
        if (folder.Installed is not { HasTrainer: true } installed) return OnlineOffer.Nothing;

        if (manifest.Find(OnlineSource.EditionKey(installed.Edition)) is not { } latest) return OnlineOffer.Nothing;

        if (VersionCompare.Compare(installed.TrainerVersion, latest.Version) < 0)
        {
            return new OnlineOffer(
                OfferKind.Update,
                latest,
                $"Update available: {installed.TrainerVersion} → {latest.TrainerText}",
                DetailFor(latest),
                "Update");
        }

        // Nothing to download. Say so, rather than hiding the banner, so the user gets a confirmation.
        bool newerThanPublished = VersionCompare.Compare(installed.TrainerVersion, latest.Version) > 0;
        return new OnlineOffer(
            OfferKind.UpToDate,
            latest,
            newerThanPublished
                ? "Your version is newer than the published one"
                : "You're running the newest version",
            newerThanPublished
                ? $"You have {installed.TrainerVersion}. The newest published is {latest.TrainerText}."
                : $"ReSkate Trainer {installed.TrainerText} is the latest published.",
            "Up to date");
    }

    private static string DetailFor(ManifestEntry entry)
    {
        var parts = new List<string>();
        if (entry.ReSkateVersion is not null) parts.Add("ReSkate " + entry.ReSkateVersion);
        parts.Add(ByteSize.Format(entry.SizeBytes));
        parts.Add("verified before install");
        return string.Join(" · ", parts);
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

    /// <summary>The status line while a zip downloads.</summary>
    public static Advice DescribeDownloadProgress(ManifestEntry entry, DownloadProgress progress) =>
        new($"Downloading ReSkate Trainer {entry.TrainerText}... {ByteSize.Format(progress.Received)} of {ByteSize.Format(progress.Total)}",
            Severity.Working);

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
