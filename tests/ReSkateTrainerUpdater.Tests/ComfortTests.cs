using System.Diagnostics;
using ReSkateTrainerUpdater.Core;
using static ReSkateTrainerUpdater.Tests.TestKit;

namespace ReSkateTrainerUpdater.Tests;

/// <summary>
/// The things that make the app kinder to use: finding the folder, PLAY after an install, closing the game, backups and
/// restoring, release details, retrying, and asking for help. No real network and no real game files are used.
/// </summary>
internal static class ComfortTests
{
    private const string TagUrl =
        "https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/trainer-tournament-0.9.2/ReSkate_Trainer_Tournament-0.9.2-on-v2.1.1.zip";

    private static readonly DateTimeOffset Today = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    public static void Run(Workspace workspace)
    {
        ReleaseDetails();
        Links();
        MainWindowExtras();
        Messages();
        Backups(workspace);
        FindingReSkate(workspace);
        GameInTheWay(workspace);
        Settings(workspace);
    }

    private static ManifestEntry Entry(string? published = null, string? notes = null, string version = "0.9.2") =>
        new("tournament", version, "578f597", "2.1.1", TagUrl.Replace("0.9.2/", version + "/"), 1000, new string('a', 64), published, notes);

    private static ReSkateVersion Tournament(string version, string build = "aaaaaaa") => new("2.1.1", "ReSkate", version, build, "Tournament");

    // ---------- When it came out, and what is new ----------

    private static void ReleaseDetails()
    {
        Section("Release details");

        string Ago(string day) => ReleaseDate.Describe(day, Today)!;
        Check("A release from today says today", Ago("2026-10-28") == "today");
        Check("A release from yesterday says yesterday", Ago("2026-10-27") == "yesterday");
        Check("A recent release counts days", Ago("2026-10-25") == "3 days ago");
        Check("A release from a few weeks back counts weeks", Ago("2026-10-07") == "3 weeks ago", Ago("2026-10-07"));
        Check("An older release counts months", Ago("2026-06-28") == "4 months ago", Ago("2026-06-28"));
        Check("A very old release says over a year", Ago("2024-01-01") == "over a year ago");
        Check("A date in the future counts as today", Ago("2026-12-31") == "today");
        Check("Something that isn't a date says nothing", ReleaseDate.Describe("soon", Today) is null && ReleaseDate.Describe(null, Today) is null);

        var json = new TrainerManifest().With(Entry("2026-10-25", "Smoother physics")).ToJson();
        var back = TrainerManifest.Parse(json).Manifest?.Find("tournament");
        Check("The date and the note survive writing and reading", back is { Published: "2026-10-25", Notes: "Smoother physics" }, json);
        Check("Without them nothing extra is written", !new TrainerManifest().With(Entry()).ToJson().Contains("published"));
        Check("They are written after the hash, so existing diffs stay small",
            json.IndexOf("\"sha256\"", StringComparison.Ordinal) < json.IndexOf("\"published\"", StringComparison.Ordinal));

        Check("A bad date drops the entry", TrainerManifest.CheckEntry(Entry("2026-13-45")).Count == 1 && TrainerManifest.CheckEntry(Entry("yesterday")).Count == 1);
        Check("A note must be one short line",
            TrainerManifest.CheckEntry(Entry(notes: "")).Count == 1
            && TrainerManifest.CheckEntry(Entry(notes: "two\nlines")).Count == 1
            && TrainerManifest.CheckEntry(Entry(notes: new string('x', TrainerManifest.MaxNotesLength + 1))).Count == 1
            && TrainerManifest.CheckEntry(Entry(notes: new string('x', TrainerManifest.MaxNotesLength))).Count == 0);

        var withApp = new TrainerManifest().WithApp(new AppEntry("1.4.0", OnlineSource.AppAssetUrl("1.4.0"), 1000, new string('b', 64), "2026-10-28", "Easier to use"));
        Check("The app's own entry carries them too", TrainerManifest.Parse(withApp.ToJson()).Manifest?.App is { Published: "2026-10-28", Notes: "Easier to use" });

        var older = TrainerManifest.Parse($$"""{ "schemaVersion": 1, "editions": { "tournament": { "version": "0.9.2", "url": "{{TagUrl}}", "sizeBytes": 1000, "sha256": "{{new string('a', 64)}}", "somethingNew": true } } }""");
        Check("Fields an older app doesn't know are ignored", older.Manifest?.Find("tournament") is { Published: null, Notes: null } && older.Problems.Count == 0);
    }

    // ---------- Links that open somewhere ----------

    private static void Links()
    {
        Section("Links");

        Check("A release's page comes from its download address",
            OnlineSource.ReleasePageUrl(TagUrl) == "https://github.com/jakemackie/ReSkateTrainerUpdater/releases/tag/trainer-tournament-0.9.2");
        Check("An address that isn't a trusted download has no page", OnlineSource.ReleasePageUrl("https://evil.example/x.zip") is null && OnlineSource.ReleasePageUrl(null) is null);

        Check("Pages of this repository may be opened", OnlineSource.IsTrustedPageUrl("https://github.com/jakemackie/ReSkateTrainerUpdater/issues/new"));
        string[] refused =
        {
            "http://github.com/jakemackie/ReSkateTrainerUpdater/issues",
            "https://github.com.evil.example/jakemackie/ReSkateTrainerUpdater/",
            "https://github.com/someoneelse/ReSkateTrainerUpdater/issues",
            "https://github.com/jakemackie/OtherRepo/issues",
            "https://user:pass@github.com/jakemackie/ReSkateTrainerUpdater/",
            "https://github.com/jakemackie/ReSkateTrainerUpdater/../../evil",
            "file:///C:/Windows/System32/calc.exe",
            "C:\\Windows\\System32\\calc.exe",
            "",
        };
        Check($"Nothing else is opened ({refused.Length} cases)", refused.All(u => !OnlineSource.IsTrustedPageUrl(u)), string.Join(" | ", refused.Where(OnlineSource.IsTrustedPageUrl)));

        var report = UpdateAdvisor.DescribeProblemReportUrl("1.4.0", "Windows 11 (10.0.26200)");
        Check("Reporting a problem opens this repository's new-issue page", OnlineSource.IsTrustedPageUrl(report) && report.Contains("/issues/new?"), report);
        Check("It already holds the version and Windows", Uri.UnescapeDataString(report).Contains("**Updater version:** 1.4.0") && Uri.UnescapeDataString(report).Contains("Windows 11"), report);
        Check("It asks for the log and never includes it", Uri.UnescapeDataString(report).Contains("COPY LOG") && report.Length < 2000, report.Length.ToString());
        Check("A copied log starts with the version and Windows", UpdateAdvisor.DescribeLogHeader("1.4.0", "Windows 11").StartsWith("ReSkate Trainer Updater v1.4.0"));
    }

    // ---------- The main window ----------

    private static InstallFolderInfo Folder(ReSkateVersion? installed, BackupInfo? backup = null, bool launcher = true) =>
        new(@"C:\Games\ReSkate", true, IsEmpty: installed is null, HasLauncher: launcher, HasRuntime: installed is not null, installed, false, false, backup);

    private static HomeView Home(InstallFolderInfo? folder, ManifestEntry entry, OnlineStatus online = OnlineStatus.Reachable, string? chosen = @"C:\Games\ReSkate",
        string? suggested = null)
    {
        var manifest = new TrainerManifest().With(entry);
        var offer = online == OnlineStatus.Reachable ? UpdateAdvisor.DescribeOnlineOffer(manifest, folder) : OnlineOffer.Nothing;
        return UpdateAdvisor.DescribeHome(new UpdateContext(false, null, null, null, chosen, false, folder, offer, online, null, suggested, Today));
    }

    private static void MainWindowExtras()
    {
        Section("Main window extras");

        var found = Home(null, Entry(), chosen: null, suggested: @"D:\Games\ReSkate");
        Check("A folder found on this PC is offered with one click",
            found is { Pill: "FOUND", Tile: { Title: "USE IT", Action: TileAction.UseSuggestedFolder, Enabled: true } } && found.Subline.Contains(@"D:\Games\ReSkate"), found.Subline);
        Check("Once a folder is chosen, the suggestion is gone", Home(Folder(Tournament("0.9.1")), Entry(), suggested: @"D:\Games\ReSkate").Pill == "UPDATE");
        Check("With nothing found it still asks for a folder", Home(null, Entry(), chosen: null).Pill == "SET UP");

        var update = Home(Folder(Tournament("0.9.1")), Entry("2026-10-25", "Smoother physics"));
        Check("An update says when it was published", update.Rows.Any(r => r.Text == "Published 3 days ago"), string.Join(" | ", update.Rows.Select(r => r.Text)));
        Check("An update says what is new", update.Rows.Any(r => r.Text == "What's new: Smoother physics"), string.Join(" | ", update.Rows.Select(r => r.Text)));
        Check("An update links to the release notes",
            update.Links?.Any(l => l is { Text: "Release notes", Action: LinkAction.OpenUrl } && OnlineSource.IsTrustedPageUrl(l.Target)) == true);
        Check("Without a date or note there are no extra lines", Home(Folder(Tournament("0.9.1")), Entry()).Rows.Count == 2);

        var switchView = Home(Folder(new ReSkateVersion("2.1.1", "ReSkate", "0.9.1", "aaaaaaa", null)), Entry());
        Check("Every kind of download offer links to its release notes", switchView.Links?.Any(l => l.Action == LinkAction.OpenUrl) == true);

        var offline = Home(Folder(Tournament("0.9.2")), Entry(), OnlineStatus.Unavailable);
        Check("Offline offers a way to try again", offline.Links?.Any(l => l is { Text: "Check again", Action: LinkAction.CheckAgain }) == true);
        Check("Online doesn't show it", Home(Folder(Tournament("0.9.2")), Entry()).Links?.Any(l => l.Action == LinkAction.CheckAgain) != true);

        var backup = new BackupInfo(@"C:\Games\ReSkate\_ReSkateTrainerUpdater_backup_2026-10-27_10-00-00", new DateTime(2026, 10, 27, 10, 0, 0), Tournament("0.9.1"));
        var restorable = Home(Folder(Tournament("0.9.2"), backup), Entry());
        Check("A folder with an older backup offers to go back to it",
            restorable.Links?.Any(l => l is { Text: "Restore 0.9.1", Action: LinkAction.RestorePrevious }) == true, string.Join(" | ", restorable.Links?.Select(l => l.Text) ?? Array.Empty<string>()));
        var samePast = new BackupInfo(backup.Path, backup.Created, Tournament("0.9.2"));
        Check("It doesn't offer to go back to the same release", Home(Folder(Tournament("0.9.2"), samePast), Entry()).Links?.Any(l => l.Action == LinkAction.RestorePrevious) != true);
        Check("An unreadable backup is still offered, in plain words",
            Home(Folder(Tournament("0.9.2"), new BackupInfo(backup.Path, backup.Created, null)), Entry()).Links?.Any(l => l.Text == "Restore backup") == true);
        var backupOfOlder = new BackupInfo(backup.Path, backup.Created, Tournament("0.9.0"));
        var updateWithBackup = Home(Folder(Tournament("0.9.1"), backupOfOlder), Entry());
        Check("When an update is on offer, the big blue UPDATE tile is the way forward, not a small restore button",
            updateWithBackup.Tile is { Title: "UPDATE", Enabled: true } && updateWithBackup.Links?.Any(l => l.Action == LinkAction.RestorePrevious) != true);
        Check("No backup, no restore button", Home(Folder(Tournament("0.9.2")), Entry()).Links?.Any(l => l.Action == LinkAction.RestorePrevious) != true);

        var broken = Home(new InstallFolderInfo(@"C:\Games\ReSkate", false, false, false, false, null, false, false), Entry());
        Check("A problem screen offers to report it", broken.Links?.Any(l => l.Action == LinkAction.ReportProblem) == true);

        var views = new[] { found, update, switchView, offline, restorable, broken };
        Check("A screen never has more buttons than the card has room for", views.All(v => (v.Links?.Count ?? 0) <= HomeView.MaxLinks));
        Check("Every button has a few words of text", views.SelectMany(v => v.Links ?? Array.Empty<CardLink>()).All(l => l.Text.Length is > 0 and <= 20), "keep them short");

        Section("After an install");
        var ready = Home(Folder(Tournament("0.9.1")), Entry()); // an update is on offer, so the tile is the blue UPDATE
        var installed = new InstallResult(InstallOutcome.Installed, 0, 2, 1, Array.Empty<string>(), BackupFolder: @"C:\Games\ReSkate\_backup");
        var advice = UpdateAdvisor.DescribeResult(installed, Tournament("0.9.2"));
        Check("A good install says to press PLAY", advice is { Severity: Severity.Good, Next: FollowUp.Launch } && advice.Message.Contains("PLAY"), advice.Message);
        Check("It offers to open the backup folder", advice.Links?.Single() is { Text: "Open backup folder", Action: LinkAction.OpenFolder, Target: @"C:\Games\ReSkate\_backup" });
        Check("No backup folder, no link", UpdateAdvisor.DescribeResult(installed with { BackupFolder = null }, Tournament("0.9.2")).Links is null);

        var done = UpdateAdvisor.WithResult(ready, advice, Folder(Tournament("0.9.2")));
        Check("The big tile becomes PLAY", done.Tile is { Title: "PLAY", Action: TileAction.Launch, Style: TileStyle.Primary, Enabled: true });
        Check("The backup link comes first", done.Links?[0].Action == LinkAction.OpenFolder);
        Check("Without a launcher in the folder the tile stays as it was", UpdateAdvisor.WithResult(ready, advice, Folder(Tournament("0.9.2"), launcher: false)).Tile == ready.Tile);
        Check("Without knowing the folder the tile stays as it was", UpdateAdvisor.WithResult(ready, advice).Tile == ready.Tile);

        var crowded = ready with { Links = new[] { new CardLink("A", LinkAction.CheckAgain), new CardLink("B", LinkAction.RestorePrevious), new CardLink("C", LinkAction.ReportProblem) } };
        var merged = UpdateAdvisor.WithResult(crowded, advice, Folder(Tournament("0.9.2")));
        Check("The result's button goes first and the total stays within the limit", merged.Links?.Count == HomeView.MaxLinks && merged.Links[0].Action == LinkAction.OpenFolder);
        Check("An error result adds a way to report it",
            UpdateAdvisor.WithResult(ready, new Advice("It broke.", Severity.Error)).Links?.Any(l => l.Action == LinkAction.ReportProblem) == true);

        var already = UpdateAdvisor.DescribeResult(new InstallResult(InstallOutcome.AlreadyUpToDate, 0, 0, 3, Array.Empty<string>()), Tournament("0.9.2"));
        Check("Already having it also leads to PLAY", already.Next == FollowUp.Launch);
        Check("A failed install does not", UpdateAdvisor.DescribeResult(new InstallResult(InstallOutcome.RolledBack, 0, 0, 0, Array.Empty<string>()), Tournament("0.9.2")).Next == FollowUp.None);
    }

    // ---------- What the messages say ----------

    private static void Messages()
    {
        Section("Plainer messages");

        var variants = new[]
        {
            Folder(null), // empty folder
            new InstallFolderInfo(@"C:\x", true, false, false, true, null, false, false), // main file unreadable
            new InstallFolderInfo(@"C:\x", true, false, true, false, null, false, false), // main file missing
            new InstallFolderInfo(@"C:\x", true, false, false, false, null, false, false), // something else
            Folder(Tournament("0.9.2")),
        };
        var lines = variants.Select(v => UpdateAdvisor.DescribeFolder(v).Text).ToList();
        Check("The folder lines don't talk about DLLs or EXEs", lines.All(l => !l.Contains(".dll", StringComparison.OrdinalIgnoreCase) && !l.Contains(".exe", StringComparison.OrdinalIgnoreCase)),
            string.Join(" | ", lines));
        Check("A file that can't be read is described in words", lines[1].Contains("can't be read"), lines[1]);

        var unreadable = UpdateAdvisor.DescribeHome(new UpdateContext(false, null, null, null, @"C:\x", false, variants[1], OnlineOffer.Nothing, OnlineStatus.Reachable));
        Check("It says what to do next", unreadable.Subline.Contains("Reinstall ReSkate") && !unreadable.Headline.Contains("dll", StringComparison.OrdinalIgnoreCase), unreadable.Headline + " / " + unreadable.Subline);

        Section("Switching editions and closing the game");
        var standard = new ReSkateVersion("2.1.1", "ReSkate", "0.9.1", "aaaaaaa", null);
        var prompt = UpdateAdvisor.DescribeEditionSwitchPrompt(standard, Tournament("0.9.2"));
        Check("Moving to another edition asks first and names both", prompt is not null && prompt.Contains("Standard edition (0.9.1)") && prompt.Contains("Tournament edition (0.9.2)"), prompt);
        Check("The same edition doesn't ask", UpdateAdvisor.DescribeEditionSwitchPrompt(Tournament("0.9.1"), Tournament("0.9.2")) is null);
        Check("Adding a Trainer to plain ReSkate doesn't ask", UpdateAdvisor.DescribeEditionSwitchPrompt(new ReSkateVersion("2.1.1", "ReSkate", null, null, null), Tournament("0.9.2")) is null);
        Check("A first install doesn't ask", UpdateAdvisor.DescribeEditionSwitchPrompt(null, Tournament("0.9.2")) is null);

        var running = new[] { new RunningProcess(10, "ReSkateLauncher.exe"), new RunningProcess(11, "skate.exe") };
        var blocked = new InstallResult(InstallOutcome.Blocked, 0, 0, 0, new[] { "ReSkate.dll is in use or protected" }, BlockedBy: BlockReason.FilesInUse);
        var named = UpdateAdvisor.DescribeResult(blocked, Tournament("0.9.2"), running);
        Check("A blocked install names the programs in the way", named.Message.Contains("ReSkateLauncher.exe and skate.exe are still open"), named.Message);
        Check("It offers to close them and carry on", named is { Severity: Severity.Warning, Next: FollowUp.CloseGameAndRetry } && named.Message.Contains("Nothing was changed"));
        Check("One program reads naturally", UpdateAdvisor.DescribeResult(blocked, Tournament("0.9.2"), running.Take(1).ToList()).Message.Contains("ReSkateLauncher.exe is still open"));
        Check("If nothing can be found, it falls back to asking the user to close it",
            UpdateAdvisor.DescribeResult(blocked, Tournament("0.9.2"), Array.Empty<RunningProcess>()) is { Next: FollowUp.None } plain && plain.Message.Contains("still open"));

        var closeTile = UpdateAdvisor.WithResult(Home(Folder(Tournament("0.9.1")), Entry()), named, Folder(Tournament("0.9.1")));
        Check("The big tile becomes CLOSE GAME", closeTile.Tile is { Title: "CLOSE GAME", Action: TileAction.CloseGameAndRetry, Enabled: true });
        Check("The question warns that unsaved work is lost", UpdateAdvisor.DescribeCloseGamePrompt(running).Contains("unsaved", StringComparison.OrdinalIgnoreCase) || UpdateAdvisor.DescribeCloseGamePrompt(running).Contains("haven't saved"));
        Check("If it won't close, the message says so and keeps the button", UpdateAdvisor.DescribeCloseGameFailed(running.Take(1).ToList()) is { Next: FollowUp.CloseGameAndRetry } failed && failed.Message.Contains("Couldn't close"));

        var closing = UpdateAdvisor.DescribeBusy(BusyStage.Closing, "ReSkateLauncher.exe");
        Check("While the game is being closed the screen says so and waits", closing is { Pill: "CLOSING", Tile: { Working: true, Enabled: false } } && closing.Subline == "ReSkateLauncher.exe");
        Check("Pressing PLAY says the launcher was started and keeps the PLAY tile",
            UpdateAdvisor.DescribeLaunched() is { Severity: Severity.Good, Next: FollowUp.Launch } started && started.Message.Contains("ReSkateLauncher.exe"));

        Check("A missing launcher is explained", UpdateAdvisor.DescribeLaunchFailure(LaunchFailure.NotFound, null).Message.Contains("isn't in that folder"));
        Check("A launcher that won't start is explained", UpdateAdvisor.DescribeLaunchFailure(LaunchFailure.CouldNotStart, "Access denied").Message.Contains("Access denied"));
    }

    // ---------- Backups ----------

    private static void Backups(Workspace workspace)
    {
        Section("Backups");

        string Dll(string folder) => Path.Combine(folder, "ReSkate.dll");
        string BackupName(string stamp) => "_ReSkateTrainerUpdater_backup_" + stamp;

        var oldRelease = workspace.NewFolder("comfort_release_091");
        FakeBinary.WriteRelease(oldRelease, trainerVersion: "0.9.1+aaaaaaa", salt: "old");
        var newRelease = workspace.NewFolder("comfort_release_092");
        FakeBinary.WriteRelease(newRelease, trainerVersion: "0.9.2+578f597", salt: "new");

        var game = workspace.NewFolder("comfort_game");
        TrainerInstaller.Install(oldRelease, game, backup: true);
        Check("Nothing is backed up before anything is replaced", TrainerInstaller.ListBackups(game).Count == 0);

        var update = TrainerInstaller.Install(newRelease, game, backup: true);
        var backups = TrainerInstaller.ListBackups(game);
        Check("An update leaves one backup that can be listed", backups.Count == 1 && backups[0].Path == update.BackupFolder);
        Check("The backup knows which version it holds", backups[0].Version is { TrainerVersion: "0.9.1", Edition: "Tournament" });
        Check("The inspector reports the newest backup", InstallFolderInspector.Inspect(game).PreviousBackup?.Path == update.BackupFolder);

        // Going back, straight after the update (the same second): the backup being restored must survive intact.
        var oldDllHash = Hashing.Of(Dll(oldRelease));
        var restore = TrainerInstaller.Restore(game, backups[0]);
        Check("Restoring puts the old files back", restore.Outcome == InstallOutcome.Installed && Hashing.Of(Dll(game)) == oldDllHash, restore.Outcome.ToString());
        Check("Invariant: the backup that was restored from is still intact", File.Exists(Dll(backups[0].Path)) && Hashing.Of(Dll(backups[0].Path)) == oldDllHash);
        Check("Restoring saves what it replaced, so it can be undone",
            restore.BackupFolder is not null && restore.BackupFolder != backups[0].Path && Hashing.Of(Dll(restore.BackupFolder)) == Hashing.Of(Dll(newRelease)));
        Check("The folder now reads as the old version", InstallFolderInspector.Inspect(game).Installed is { TrainerVersion: "0.9.1" });
        Check("The newest backup is now the version that was replaced", TrainerInstaller.ListBackups(game)[0].Version is { TrainerVersion: "0.9.2" });
        Check("Invariant: no half-written files are left after a restore", !Directory.EnumerateFiles(game, "*.updating", SearchOption.AllDirectories).Any());

        var restoreAdvice = UpdateAdvisor.DescribeRestoreResult(restore, backups[0]);
        Check("After a restore the screen's own tile stays, so a newer version is still one blue UPDATE click away", restoreAdvice.Next == FollowUp.None);
        Check("The restore message names the version", restoreAdvice.Severity == Severity.Good && restoreAdvice.Message.Contains("Trainer 0.9.1 (Tournament)"), restoreAdvice.Message);
        Check("The restore question names the version and says nothing is lost", UpdateAdvisor.DescribeRestorePrompt(backups[0]) is { } q && q.Contains("0.9.1") && q.Contains("saved first"));

        Check("A restore that is blocked says why, like an install would",
            UpdateAdvisor.DescribeRestoreResult(new InstallResult(InstallOutcome.Blocked, 0, 0, 0, Array.Empty<string>(), BlockedBy: BlockReason.FilesInUse), backups[0]).Message.Contains("still open"));

        // Old backups don't pile up.
        var crowded = workspace.NewFolder("comfort_crowded");
        TrainerInstaller.Install(oldRelease, crowded, backup: false);
        foreach (var stamp in new[] { "2020-01-01_00-00-01", "2020-01-02_00-00-01", "2020-01-03_00-00-01", "2020-01-04_00-00-01" })
        {
            var folder = Path.Combine(crowded, BackupName(stamp));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "ReSkate.dll"), stamp);
        }
        var notABackup = Path.Combine(crowded, BackupName("not-a-date"));
        var emptyBackup = Path.Combine(crowded, BackupName("2019-01-01_00-00-01"));
        Directory.CreateDirectory(notABackup);
        File.WriteAllText(Path.Combine(notABackup, "keep.txt"), "mine");
        Directory.CreateDirectory(emptyBackup);

        Check("Only real backups are listed", TrainerInstaller.ListBackups(crowded).Count == 4);
        TrainerInstaller.Install(newRelease, crowded, backup: true);
        var left = TrainerInstaller.ListBackups(crowded);
        Check($"After an install only the newest {TrainerInstaller.BackupsToKeep} backups are kept", left.Count == TrainerInstaller.BackupsToKeep, left.Count.ToString());
        Check("The oldest ones are the ones deleted", !Directory.Exists(Path.Combine(crowded, BackupName("2020-01-01_00-00-01"))) && !Directory.Exists(Path.Combine(crowded, BackupName("2020-01-02_00-00-01"))));
        Check("Invariant: a folder that only looks like a backup is never deleted", File.Exists(Path.Combine(notABackup, "keep.txt")) && Directory.Exists(emptyBackup));

        var noBackupGame = workspace.NewFolder("comfort_nobackup");
        TrainerInstaller.Install(oldRelease, noBackupGame, backup: false);
        var preexisting = Path.Combine(noBackupGame, BackupName("2020-01-01_00-00-01"));
        Directory.CreateDirectory(preexisting);
        File.WriteAllText(Path.Combine(preexisting, "ReSkate.dll"), "x");
        TrainerInstaller.PruneBackups(noBackupGame, keep: 5);
        Check("Pruning with room to spare deletes nothing", Directory.Exists(preexisting));
    }

    // ---------- Finding the folder ----------

    private static void FindingReSkate(Workspace workspace)
    {
        Section("Finding the ReSkate folder");

        void Both(string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "ReSkate.dll"), "x");
            File.WriteAllText(Path.Combine(folder, "ReSkateLauncher.exe"), "x");
        }

        var rootA = workspace.NewFolder("find_a");
        Both(Path.Combine(rootA, "Games", "ReSkate"));
        Check("A folder with both files is found a couple of levels down",
            ReSkateLocator.Find(new[] { new ReSkateLocator.Root(rootA, 2) }) == Path.Combine(rootA, "Games", "ReSkate"));
        Check("It isn't found beyond the depth it was told to look", ReSkateLocator.Find(new[] { new ReSkateLocator.Root(rootA, 1) }) is null);

        var rootB = workspace.NewFolder("find_b");
        Directory.CreateDirectory(Path.Combine(rootB, "Partial"));
        File.WriteAllText(Path.Combine(rootB, "Partial", "ReSkateLauncher.exe"), "x");
        Check("A folder with only one of the files is found if nothing better exists",
            ReSkateLocator.Find(new[] { new ReSkateLocator.Root(rootB, 1) }) == Path.Combine(rootB, "Partial"));
        Check("A complete folder beats a partial one found earlier",
            ReSkateLocator.Find(new[] { new ReSkateLocator.Root(rootB, 1), new ReSkateLocator.Root(rootA, 2) }) == Path.Combine(rootA, "Games", "ReSkate"));

        var rootC = workspace.NewFolder("find_c");
        Both(Path.Combine(rootC, "_ReSkateTrainerUpdater_backup_2026-01-01_00-00-00"));
        Check("The updater's own backup folders are never suggested", ReSkateLocator.Find(new[] { new ReSkateLocator.Root(rootC, 2) }) is null);

        Check("A place that doesn't exist is skipped", ReSkateLocator.Find(new[] { new ReSkateLocator.Root(Path.Combine(workspace.Root, "nowhere"), 3) }) is null);
        Check("With nothing to look in there is nothing found", ReSkateLocator.Find(Array.Empty<ReSkateLocator.Root>()) is null);

        var defaults = ReSkateLocator.DefaultRoots();
        Check("The usual places are searched without trouble", defaults.All(r => r.Path.Length > 0 && r.Depth is > 0 and <= 3));
        Check("Finding on this PC's usual places finishes without throwing", Run(() => ReSkateLocator.Find(defaults)));
    }

    private static bool Run(Action action)
    {
        try { action(); return true; }
        catch (Exception) { return false; }
    }

    // ---------- Programs in the way ----------

    private static void GameInTheWay(Workspace workspace)
    {
        Section("Programs in the way");

        var self = Process.GetCurrentProcess();
        string? exe = self.MainModule?.FileName;
        if (exe is null)
        {
            Skip("The programs running from a folder are found", "this system hides the test program's own path");
        }
        else
        {
            string folder = Path.GetDirectoryName(exe)!;
            Check("A program running from the folder is found", GameProcesses.FindUsing(folder, ignoreProcessId: -1).Any(p => p.Id == self.Id));
            Check("The updater itself is never counted", GameProcesses.FindUsing(folder).All(p => p.Id != Environment.ProcessId));
        }
        Check("An empty folder has nothing running from it", GameProcesses.FindUsing(workspace.NewFolder("comfort_idle")).Count == 0);

        // A program started from inside a folder (as the game is from the ReSkate folder), like a copy of a system tool.
        var home = workspace.NewFolder("comfort_game_home");
        using var sleeper = StartSleeper(home);
        if (sleeper is null)
        {
            Skip("A program running from the folder is named, then closed", "no sleep program to start on this system");
        }
        else
        {
            var named = GameProcesses.FindUsing(home);
            // On a PC whose temp folder is written with 8.3 short names (as here), this also covers matching those to Windows' long names.
            Check("A program started from the folder is found and named", named.Any(p => p.Id == sleeper.Id && p.Name.StartsWith("skate", StringComparison.OrdinalIgnoreCase)),
                string.Join(", ", named.Select(p => p.Name)));

            // Windows reports a program under the spelling it was started with. Start one through the folder's short (8.3) name
            // and look for it by the long name, which is how a folder reaches the app from the folder picker.
            if (OperatingSystem.IsWindows() && ShortPath(home) is { } shortHome && !string.Equals(shortHome, home, StringComparison.OrdinalIgnoreCase))
            {
                using var viaShortName = Process.Start(new ProcessStartInfo(Path.Combine(shortHome, "skate.exe"), "-n 60 127.0.0.1")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
                var seen = GameProcesses.FindUsing(Path.GetFullPath(home));
                Check("A program started through a short folder name is found by the long one", viaShortName is not null && seen.Any(p => p.Id == viaShortName.Id),
                    string.Join(", ", seen.Select(p => p.Id)));
                viaShortName?.Kill();
            }
            else
            {
                Skip("A program started through a short folder name is found by the long one", "this folder has no short name here");
            }

            var target = new RunningProcess(sleeper.Id, sleeper.ProcessName);
            var left = GameProcesses.Close(new[] { target }, TimeSpan.FromMilliseconds(300));
            Check("A program that ignores the request is ended after the grace period", left.Count == 0 && sleeper.WaitForExit(5000));
            Check("Closing something that has already gone is fine", GameProcesses.Close(new[] { target }, TimeSpan.FromMilliseconds(100)).Count == 0);
        }
        Check("A recycled id for a different program is left alone",
            GameProcesses.Close(new[] { new RunningProcess(Environment.ProcessId, "definitely-not-this-test") }, TimeSpan.FromMilliseconds(100)).Count == 0 && !Environment.HasShutdownStarted);

        Check("Starting a game that isn't there is reported, not thrown",
            GameLauncher.Start(workspace.NewFolder("comfort_nolauncher"), out _) == LaunchFailure.NotFound);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "GetShortPathNameW")]
    private static extern uint GetShortPathName(string longPath, System.Text.StringBuilder shortPath, uint bufferSize);

    // The 8.3 spelling of a folder (C:\Users\JAKEMA~1\...), or null if it has none.
    private static string? ShortPath(string folder)
    {
        var buffer = new System.Text.StringBuilder(1024);
        uint length = GetShortPathName(folder, buffer, (uint)buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : null;
    }

    // Starts a harmless long-running program from inside <paramref name="folder"/>: a copy of ping on Windows, sleep elsewhere.
    private static Process? StartSleeper(string folder)
    {
        try
        {
            ProcessStartInfo info;
            if (OperatingSystem.IsWindows())
            {
                var copy = Path.Combine(folder, "skate.exe");
                File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), copy, overwrite: true);
                info = new ProcessStartInfo(copy, "-n 60 127.0.0.1");
            }
            else
            {
                var copy = Path.Combine(folder, "skate");
                File.Copy("/bin/sleep", copy, overwrite: true);
                info = new ProcessStartInfo(copy, "60");
            }
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            return Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ---------- Settings ----------

    private static void Settings(Workspace workspace)
    {
        Section("Remembering the window");

        var path = Path.Combine(workspace.NewFolder("comfort_settings"), "settings.json");
        var settings = UpdaterSettings.Load(path);
        Check("A new install has no remembered position", settings is { WindowLeft: null, WindowTop: null });

        settings.WindowLeft = 120;
        settings.WindowTop = 80;
        settings.Save();
        Check("The position is remembered", UpdaterSettings.Load(path) is { WindowLeft: 120, WindowTop: 80 });
    }
}
