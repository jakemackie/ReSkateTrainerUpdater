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
                ? ($"Installed: ReSkate Trainer {installed.TrainerText} · ReSkate {installed.BaseVersion ?? "?"}", Severity.Good)
                : ($"Installed: ReSkate {installed.BaseVersion ?? "?"} (no Trainer yet)", Severity.Neutral);
        }

        if (folder.HasRuntime) return ($"{ReSkateFiles.RuntimeDll} is here but couldn't be read.", Severity.Warning);
        if (folder.HasLauncher) return ($"{ReSkateFiles.Launcher} is here but {ReSkateFiles.RuntimeDll} is missing.", Severity.Warning);
        if (folder.IsEmpty) return ("Empty folder: this will be a fresh install.", Severity.Neutral);
        return ("No ReSkate found in this folder yet.", Severity.Warning);
    }

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
