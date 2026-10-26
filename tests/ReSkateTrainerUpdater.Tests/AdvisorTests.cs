using ReSkateTrainerUpdater.Core;
using static ReSkateTrainerUpdater.Tests.TestKit;

namespace ReSkateTrainerUpdater.Tests;

internal static class AdvisorTests
{
    public static void Run(Workspace workspace)
    {
        Section("Status messages");

        var zip = workspace.NewZip("advisor", folder => FakeBinary.WriteRelease(folder));
        using var package = TrainerPackageLoader.Load(zip);

        InstallFolderInfo Folder(ReSkateVersion? installed, bool steam = false, bool special = false, bool exists = true) =>
            new(@"C:\Games\ReSkate", exists, IsEmpty: installed is null, HasLauncher: installed is not null,
                HasRuntime: installed is not null, installed, steam, special);

        Advice Advise(InstallFolderInfo? folder, string? chosenFolder = @"C:\Games\ReSkate", bool checkingFolder = false) =>
            UpdateAdvisor.Evaluate(new UpdateContext(false, zip, null, package, chosenFolder, checkingFolder, folder));

        var older = new ReSkateVersion("2.1.1", "ReSkate", "0.9.1", "aaaaaaa", "Tournament");
        var same = new ReSkateVersion("2.1.1", "ReSkate", "0.9.2", "578f597", "Tournament");
        var sameVersionOtherBuild = new ReSkateVersion("2.1.1", "ReSkate", "0.9.2", "bbbbbbb", "Tournament");
        var newer = new ReSkateVersion("2.1.1", "ReSkate", "0.10.0", "ccccccc", "Tournament");
        var plain = new ReSkateVersion("2.1.1", "ReSkate", null, null, null);

        var start = UpdateAdvisor.Evaluate(new UpdateContext(false, null, null, null, null, false, null));
        Check("With nothing chosen it asks for the zip", start is { CanInstall: false, Severity: Severity.Neutral } && start.Message.Contains("Drop"));
        Check("While checking the zip it says so", UpdateAdvisor.Evaluate(new UpdateContext(true, zip, null, null, null, false, null)).Severity == Severity.Working);
        Check("A zip that couldn't be read shows why",
            UpdateAdvisor.Evaluate(new UpdateContext(false, zip, "It's damaged.", null, null, false, null)) is { Severity: Severity.Error, Message: "It's damaged." });
        Check("With a good zip it asks for the folder", UpdateAdvisor.Evaluate(new UpdateContext(false, zip, null, package, null, false, null)).Message.Contains("choose your ReSkate folder", StringComparison.OrdinalIgnoreCase));
        Check("While checking the folder it says so", Advise(null, checkingFolder: true).Severity == Severity.Working);

        var fresh = Advise(Folder(null));
        Check("A fresh install is ready", fresh is { CanInstall: true, Severity: Severity.Good } && fresh.Message.StartsWith("Ready: install"), fresh.Message);
        Check("An upgrade shows both versions", Advise(Folder(older)).Message.Contains("0.9.1 (Tournament) → 0.9.2 (Tournament)"), Advise(Folder(older)).Message);

        var downgrade = Advise(Folder(newer));
        Check("A downgrade is a warning but allowed", downgrade is { CanInstall: true, Severity: Severity.Warning } && downgrade.Message.Contains("downgrade"), downgrade.Message);
        Check("Downgrades are recognised", UpdateAdvisor.IsDowngrade(newer, package.Version!) && !UpdateAdvisor.IsDowngrade(older, package.Version!));

        var sameBuild = Advise(Folder(same));
        Check("The same build says it only repairs", sameBuild is { CanInstall: true, Severity: Severity.Neutral } && sameBuild.Message.Contains("already have"), sameBuild.Message);
        Check("A different build of the same version is called out", Advise(Folder(sameVersionOtherBuild)).Message.Contains("different build"));
        Check("Adding the Trainer to plain ReSkate is ready", Advise(Folder(plain)).Message.Contains("add ReSkate Trainer"), Advise(Folder(plain)).Message);

        var steam = Advise(Folder(null, steam: true));
        Check("Steam's skate. folder blocks the install", steam is { CanInstall: false, Severity: Severity.Error });
        Check("A folder that vanished blocks the install", !Advise(Folder(null, exists: false)).CanInstall);

        var special = Advise(Folder(older, special: true));
        Check("Special characters warn but allow the install", special is { CanInstall: true, Severity: Severity.Warning } && special.Message.Contains("special characters"), special.Message);

        Section("Folder summary line");
        Check("An empty folder says fresh install", UpdateAdvisor.DescribeFolder(Folder(null)).Text.Contains("fresh install"));
        Check("An installed Trainer shows its version", UpdateAdvisor.DescribeFolder(Folder(same)).Text.Contains("0.9.2 (Tournament)"));
        Check("Plain ReSkate says no Trainer yet", UpdateAdvisor.DescribeFolder(Folder(plain)).Text.Contains("no Trainer yet"));

        Section("Result messages");
        InstallResult Result(InstallOutcome outcome, BlockReason reason = BlockReason.None) =>
            new(outcome, 0, 0, 0, Array.Empty<string>(), BlockedBy: reason, RescueFolder: @"C:\rescue");

        Check("A successful install is good news", UpdateAdvisor.DescribeResult(Result(InstallOutcome.Installed), same).Severity == Severity.Good);
        Check("Files in use ask the user to close the game", UpdateAdvisor.DescribeResult(Result(InstallOutcome.Blocked, BlockReason.FilesInUse), same).Message.Contains("still open"));
        Check("A read-only folder explains permissions", UpdateAdvisor.DescribeResult(Result(InstallOutcome.Blocked, BlockReason.FolderNotWritable), same).Message.Contains("allowed to write"));
        Check("A failed restore points at the rescue folder", UpdateAdvisor.DescribeResult(Result(InstallOutcome.RestoreFailed), same).Message.Contains(@"C:\rescue"));
    }
}
