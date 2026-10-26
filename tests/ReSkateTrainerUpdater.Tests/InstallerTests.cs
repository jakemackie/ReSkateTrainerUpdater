using ReSkateTrainerUpdater.Core;
using static ReSkateTrainerUpdater.Tests.TestKit;

namespace ReSkateTrainerUpdater.Tests;

internal static class InstallerTests
{
    public static void Run(Workspace workspace)
    {
        var release = workspace.NewFolder("release");
        FakeBinary.WriteRelease(release);
        var releaseDll = Path.Combine(release, "ReSkate.dll");

        Section("Inspecting a ReSkate folder");

        var target = workspace.NewFolder("game");
        var empty = InstallFolderInspector.Inspect(target);
        Check("An empty folder is reported as empty", empty is { Exists: true, IsEmpty: true, HasRuntime: false, HasLauncher: false, Installed: null });
        Check("A missing folder is reported as missing", !InstallFolderInspector.Inspect(Path.Combine(workspace.Root, "nowhere")).Exists);
        Check("Steam's skate. folder is detected", InstallFolderInspector.IsSteamSkateFolder(@"C:\Program Files (x86)\Steam\steamapps\common\skate."));
        Check("Ordinary folders are not flagged",
            !InstallFolderInspector.IsSteamSkateFolder(@"C:\Games\ReSkate") && !InstallFolderInspector.IsSteamSkateFolder(@"C:\skate stuff"));
        Check("Special characters are flagged", InstallFolderInspector.Inspect(@"C:\Jeux\Réskate").HasSpecialCharacters);

        Section("Installing");

        var fresh = TrainerInstaller.Install(release, target, backup: true);
        Check("A fresh install adds every file", fresh is { Outcome: InstallOutcome.Installed, Added: 3, Replaced: 0 }, $"{fresh.Outcome} +{fresh.Added} ~{fresh.Replaced}");
        Check("The installed file matches the package", Hashing.Of(Path.Combine(target, "ReSkate.dll")) == Hashing.Of(releaseDll));
        Check("A fresh install makes no backup and doesn't claim one",
            fresh.BackupFolder is null && Directory.GetDirectories(target, "_ReSkateTrainerUpdater_backup_*").Length == 0);
        Check("Invariant: no half-written files are left behind", !Directory.EnumerateFiles(target, "*.updating", SearchOption.AllDirectories).Any());
        Check("The folder's version is now readable",
            InstallFolderInspector.Inspect(target).Installed is { TrainerVersion: "0.9.2", Edition: "Tournament" });

        var again = TrainerInstaller.Install(release, target, backup: true);
        Check("Installing the same files again changes nothing", again is { Outcome: InstallOutcome.AlreadyUpToDate, Unchanged: 3 }, $"{again.Outcome}");

        var dll = Path.Combine(target, "ReSkate.dll");
        var edited = File.ReadAllBytes(dll).Concat(new byte[] { 1, 2, 3 }).ToArray();

        File.WriteAllBytes(dll, edited);
        var replace = TrainerInstaller.Install(release, target, backup: true);
        Check("A changed file is replaced", replace is { Outcome: InstallOutcome.Installed, Replaced: 1, Added: 0 }, $"{replace.Outcome} ~{replace.Replaced}");
        Check("The replaced file matches the package again", Hashing.Of(dll) == Hashing.Of(releaseDll));
        Check("A replacing install reports a backup folder that exists", replace.BackupFolder is not null && Directory.Exists(replace.BackupFolder));
        Check("The backup holds the file that was replaced",
            replace.BackupFolder is not null && File.ReadAllBytes(Path.Combine(replace.BackupFolder, "ReSkate.dll")).SequenceEqual(edited));

        File.WriteAllBytes(dll, edited);
        var rescueFoldersBefore = Directory.GetDirectories(Path.GetTempPath(), "ReSkateTrainerRescue_*").Length;
        var noBackup = TrainerInstaller.Install(release, target, backup: false);
        Check("Installing without a backup works", noBackup is { Outcome: InstallOutcome.Installed, Replaced: 1, BackupFolder: null });
        Check("It leaves no rescue folder behind", Directory.GetDirectories(Path.GetTempPath(), "ReSkateTrainerRescue_*").Length == rescueFoldersBefore);
        Check("It makes no extra backup folders", Directory.GetDirectories(target, "_ReSkateTrainerUpdater_backup_*").Length == 1);

        Section("Staying safe");

        File.WriteAllBytes(dll, edited);
        var launcher = Path.Combine(target, "ReSkateLauncher.exe");
        File.WriteAllBytes(launcher, File.ReadAllBytes(launcher).Concat(new byte[] { 9 }).ToArray());
        var launcherBefore = Hashing.Of(launcher);

        InstallResult locked;
        using (new FileStream(dll, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            locked = TrainerInstaller.Install(release, target, backup: true);
        }
        Check("Invariant: a file in use blocks the install", locked is { Outcome: InstallOutcome.Blocked, BlockedBy: BlockReason.FilesInUse }, locked.Outcome.ToString());
        Check("The blocked file is named", locked.Problems.Any(p => p.Contains("ReSkate.dll")), string.Join("; ", locked.Problems));
        Check("Invariant: nothing is touched if any file is locked", Hashing.Of(launcher) == launcherBefore && File.ReadAllBytes(dll).SequenceEqual(edited));

        var broken = workspace.NewFolder("broken");
        File.WriteAllBytes(Path.Combine(broken, "ReSkate.dll"), edited);
        // A folder where the launcher should go: copying the launcher fails part-way through.
        Directory.CreateDirectory(Path.Combine(broken, "ReSkateLauncher.exe"));
        var rolledBack = TrainerInstaller.Install(release, broken, backup: true);
        Check("Invariant: a failure part-way is rolled back", rolledBack.Outcome == InstallOutcome.RolledBack, rolledBack.Outcome.ToString());
        Check("Invariant: the original file is put back after a failure", File.ReadAllBytes(Path.Combine(broken, "ReSkate.dll")).SequenceEqual(edited));
        Check("Invariant: files added before a failure are removed", !File.Exists(Path.Combine(broken, "README.txt")));
        Check("Invariant: no half-written files are left after a rollback", !Directory.EnumerateFiles(broken, "*.updating", SearchOption.AllDirectories).Any());
        Check("The unused backup folder is cleaned up", Directory.GetDirectories(broken, "_ReSkateTrainerUpdater_backup_*").Length == 0);
    }
}
