using System.Security.Cryptography;
using ReSkateTrainerUpdater.Core;
using static ReSkateTrainerUpdater.Tests.TestKit;

namespace ReSkateTrainerUpdater.Tests;

/// <summary>The app updating itself: the app entry in latest.json, the checks on a downloaded copy, the swap, and the screens.</summary>
internal static class AppUpdateTests
{
    private static readonly string FakeSha = new('b', 64);

    public static void Run(Workspace workspace)
    {
        ManifestEntryTests();
        ReplacingTheApp(workspace);
        Screens();
        DownloadsAsync(workspace).GetAwaiter().GetResult();
    }

    private static AppEntry App(string version = "1.2.0", long size = 5000, string? sha = null, string? url = null) =>
        new(version, url ?? OnlineSource.AppAssetUrl(version), size, sha ?? FakeSha);

    // ---------- latest.json ----------

    private static void ManifestEntryTests()
    {
        Section("The app's own entry in latest.json");

        Check("The app's release URL is built from its version", OnlineSource.AppAssetUrl("1.2.0") ==
            "https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/v1.2.0/ReSkateTrainerUpdater-1.2.0.zip", OnlineSource.AppAssetUrl("1.2.0"));
        Check("It is trusted for that version", OnlineSource.IsTrustedAppUrl(OnlineSource.AppAssetUrl("1.2.0"), "1.2.0"));

        string good = OnlineSource.AppAssetUrl("1.2.0");
        string[] refused =
        {
            OnlineSource.AppAssetUrl("1.2.1"),                                    // another version's file
            good.Replace("/v1.2.0/", "/trainer-tournament-0.9.2/"),               // a Trainer-style tag
            good.Replace("/v1.2.0/", "/1.2.0/"),                                  // no v
            good.Replace("ReSkateTrainerUpdater-1.2.0.zip", "Anything-1.2.0.zip"),// another file name
            good.Replace("ReSkateTrainerUpdater-1.2.0.zip", "ReSkateTrainerUpdater-1.2.0.exe"),
            good.Replace("jakemackie/", "someoneelse/"),
            good.Replace("github.com", "evil.example"),
            good.Replace("https://", "http://"),
            good + "?x=1",
        };
        var accepted = refused.Where(u => OnlineSource.IsTrustedAppUrl(u, "1.2.0")).ToList();
        Check($"Anything but this app's own release asset is refused ({refused.Length} cases)", accepted.Count == 0, string.Join(" | ", accepted));
        Check("A missing version or URL is refused", !OnlineSource.IsTrustedAppUrl(good, null) && !OnlineSource.IsTrustedAppUrl(null, "1.2.0"));

        string Json(string app, string editions = "") => $$"""{ "schemaVersion": 1, {{app}} "editions": { {{editions}} } }""";
        string AppJson(string version = "1.2.0", string? url = null, long size = 5000, string? sha = null) =>
            $$"""
            "app": { "version": "{{version}}", "url": "{{url ?? OnlineSource.AppAssetUrl(version)}}", "sizeBytes": {{size}}, "sha256": "{{sha ?? FakeSha}}" },
            """;

        var read = TrainerManifest.Parse(Json(AppJson(sha: new string('B', 64))));
        Check("A valid app entry is read", read.Manifest?.App == App() && read.Problems.Count == 0, string.Join("; ", read.Problems));
        Check("Its entry knows its file name and that its URL is trusted", read.Manifest?.App is { FileName: "ReSkateTrainerUpdater-1.2.0.zip", HasTrustedUrl: true });
        Check("A manifest with no app entry has none", TrainerManifest.Parse(Json("")).Manifest is { App: null });

        var untrusted = TrainerManifest.Parse(Json(AppJson(url: "https://evil.example/x.zip")));
        Check("An app entry with an untrusted URL is ignored and the file is otherwise kept",
            untrusted.Manifest is { App: null } && untrusted.Problems.Any(p => p.Contains("\"app\" entry was ignored")), string.Join("; ", untrusted.Problems));
        var trainerTag = TrainerManifest.Parse(Json(AppJson(url: "https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/trainer-tournament-0.9.2/ReSkateTrainerUpdater-1.2.0.zip")));
        Check("An app entry that points at a Trainer release is ignored", trainerTag.Manifest is { App: null });
        Check("A bad hash, size or version is ignored",
            TrainerManifest.Parse(Json(AppJson(sha: "abc"))).Manifest is { App: null } &&
            TrainerManifest.Parse(Json(AppJson(size: 0))).Manifest is { App: null } &&
            TrainerManifest.Parse(Json(AppJson(version: "1.2"))).Manifest is { App: null });
        Check("An app entry that isn't an object is ignored", TrainerManifest.Parse(Json("\"app\": 5,")).Manifest is { App: null });

        var manifest = new TrainerManifest().WithApp(App());
        var text = manifest.ToJson();
        Check("The app entry is written between the format version and the editions",
            text.IndexOf("\"schemaVersion\"", StringComparison.Ordinal) < text.IndexOf("\"app\"", StringComparison.Ordinal) &&
            text.IndexOf("\"app\"", StringComparison.Ordinal) < text.IndexOf("\"editions\"", StringComparison.Ordinal), text);
        Check("Writing then reading gives the same app entry", TrainerManifest.Parse(text).Manifest?.App == App());
        Check("A manifest without an app writes no app entry", !new TrainerManifest().ToJson().Contains("\"app\""));

        var trainer = new ManifestEntry("tournament", "0.9.2", "578f597", "2.1.1",
            "https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/trainer-tournament-0.9.2/x.zip", 1000, FakeSha);
        Check("Adding a Trainer keeps the app entry", manifest.With(trainer).App == App());
        Check("Removing a Trainer keeps the app entry", manifest.With(trainer).Without("tournament").App == App());
        Check("Setting the app keeps the Trainers", new TrainerManifest().With(trainer).WithApp(App()).Find("tournament") == trainer);
        Check("Setting the app replaces the old one", manifest.WithApp(App("1.3.0")).App?.Version == "1.3.0");
        bool threw = false;
        try { new TrainerManifest().WithApp(App(url: "https://evil.example/x.zip")); }
        catch (ArgumentException) { threw = true; }
        Check("WithApp refuses an entry that fails a check", threw);
    }

    // ---------- Replacing the exe ----------

    private static byte[] AppExe(string salt = "app") => FakeBinary.Pe(null, null, salt);

    private static string AppZip(Workspace workspace, string name, byte[]? exe = null, string exeName = "ReSkateTrainerUpdater.exe", string? subfolder = null) =>
        workspace.NewZip(name, folder =>
        {
            var target = subfolder is null ? folder : Path.Combine(folder, subfolder);
            Directory.CreateDirectory(target);
            File.WriteAllBytes(Path.Combine(target, exeName), exe ?? AppExe());
            File.WriteAllText(Path.Combine(folder, "THIRD-PARTY-NOTICES.md"), "notices");
        });

    // Stands in for the Windows file properties of the exe in the zip.
    private static void WithFileProperties(string product, string version, Action run)
    {
        var before = ReSkateScanner.FileVersionReader;
        ReSkateScanner.FileVersionReader = _ => new FileVersionDetails(product, version);
        try { run(); }
        finally { ReSkateScanner.FileVersionReader = before; }
    }

    private static void ReplacingTheApp(Workspace workspace)
    {
        Section("Replacing the app");

        var folder = workspace.NewFolder("app_install");
        var current = Path.Combine(folder, "ReSkateTrainerUpdater.exe");
        var currentBytes = AppExe("current");
        File.WriteAllBytes(current, currentBytes);

        Check("An exe can be updated in place", AppUpdater.CanUpdateInPlace(current));
        Check("Nothing, a missing file or a dll can't", !AppUpdater.CanUpdateInPlace(null) && !AppUpdater.CanUpdateInPlace(Path.Combine(folder, "nope.exe")) && !AppUpdater.CanUpdateInPlace(Path.Combine(folder, "x.dll")));
        var dotnetHost = Path.Combine(workspace.NewFolder("host"), "dotnet.exe");
        File.WriteAllBytes(dotnetHost, AppExe("host"));
        Check("The dotnet host is never replaced", !AppUpdater.CanUpdateInPlace(dotnetHost));

        var goodZip = AppZip(workspace, "app_good");
        WithFileProperties("ReSkate Trainer Updater", "1.2.0+abc1234", () =>
        {
            var staged = AppUpdater.Stage(goodZip, current, "1.2.0");
            Check("A genuine zip is staged next to the current exe", staged.Succeeded && staged.ExePath == current + ".new" && File.Exists(staged.ExePath), staged.Detail);
            Check("The staged exe is the one from the zip", staged.ExePath is not null && File.ReadAllBytes(staged.ExePath).SequenceEqual(AppExe()));
            Check("Invariant: staging never touches the running exe", File.ReadAllBytes(current).SequenceEqual(currentBytes));
            AppUpdater.Discard(current + ".new");

            var wrongVersion = AppUpdater.Stage(goodZip, current, "1.2.1");
            Check("A zip with a different version than published is refused and deleted",
                wrongVersion is { Failure: AppUpdateFailure.BadPackage } && !File.Exists(current + ".new") && wrongVersion.Detail!.Contains("1.2.1"), wrongVersion.Detail);
        });

        WithFileProperties("ReSkate", "1.2.0", () =>
        {
            var wrongProduct = AppUpdater.Stage(goodZip, current, "1.2.0");
            Check("A zip whose exe isn't this app is refused and deleted", wrongProduct.Failure == AppUpdateFailure.BadPackage && !File.Exists(current + ".new"), wrongProduct.Detail);
        });

        WithFileProperties("ReSkate Trainer Updater", "1.2.0", () =>
        {
            var noExe = AppUpdater.Stage(AppZip(workspace, "app_noexe", exeName: "Other.exe"), current, "1.2.0");
            Check("A zip with no ReSkateTrainerUpdater.exe is refused", noExe.Failure == AppUpdateFailure.BadPackage && !File.Exists(current + ".new"), noExe.Detail);

            var nested = AppUpdater.Stage(AppZip(workspace, "app_nested", subfolder: "inner"), current, "1.2.0");
            Check("An exe hidden in a subfolder of the zip is not used", nested.Failure == AppUpdateFailure.BadPackage, nested.Detail);

            var notProgram = AppUpdater.Stage(AppZip(workspace, "app_text", exe: System.Text.Encoding.ASCII.GetBytes("not a program at all")), current, "1.2.0");
            Check("An exe that isn't a 64-bit Windows program is refused and deleted", notProgram.Failure == AppUpdateFailure.BadPackage && !File.Exists(current + ".new"), notProgram.Detail);

            var notZip = Path.Combine(workspace.Root, "not_a_zip.zip");
            File.WriteAllText(notZip, "this is not a zip");
            Check("A file that isn't a zip is refused", AppUpdater.Stage(notZip, current, "1.2.0").Failure == AppUpdateFailure.BadPackage);

            var missingFolder = AppUpdater.Stage(goodZip, Path.Combine(workspace.Root, "no_such_folder", "ReSkateTrainerUpdater.exe"), "1.2.0");
            Check("A folder that can't be written to is reported as such", missingFolder.Failure == AppUpdateFailure.NotWritable, missingFolder.Detail);
        });
        Check("Invariant: no refused update leaves its staged file behind", !File.Exists(current + ".new") && File.ReadAllBytes(current).SequenceEqual(currentBytes));

        // The swap.
        var stagedBytes = AppExe("next");
        File.WriteAllBytes(current + ".new", stagedBytes);
        var swap = AppUpdater.Swap(current, current + ".new");
        Check("Swapping puts the new exe in place", swap.Failure == AppUpdateFailure.None && File.ReadAllBytes(current).SequenceEqual(stagedBytes), swap.Detail);
        Check("The old exe is kept beside it as .old", File.Exists(current + ".old") && File.ReadAllBytes(current + ".old").SequenceEqual(currentBytes));
        Check("No staged file is left", !File.Exists(current + ".new"));

        var again = Path.Combine(workspace.NewFolder("app_again"), "ReSkateTrainerUpdater.exe");
        File.WriteAllBytes(again, currentBytes);
        File.WriteAllBytes(again + ".old", AppExe("older"));
        File.WriteAllBytes(again + ".new", stagedBytes);
        Check("An old leftover from an earlier update doesn't get in the way", AppUpdater.Swap(again, again + ".new").Failure == AppUpdateFailure.None);

        var intact = Path.Combine(workspace.NewFolder("app_missing_staged"), "ReSkateTrainerUpdater.exe");
        File.WriteAllBytes(intact, currentBytes);
        var missing = AppUpdater.Swap(intact, intact + ".new");
        Check("Invariant: swapping with no staged file changes nothing", missing.Failure == AppUpdateFailure.SwapFailed && File.ReadAllBytes(intact).SequenceEqual(currentBytes) && !File.Exists(intact + ".old"));

        if (OperatingSystem.IsWindows())
        {
            // A locked staged file makes the second rename fail, which has to put the first one back.
            var locked = Path.Combine(workspace.NewFolder("app_locked"), "ReSkateTrainerUpdater.exe");
            File.WriteAllBytes(locked, currentBytes);
            File.WriteAllBytes(locked + ".new", stagedBytes);
            AppUpdateFailure failure;
            using (new FileStream(locked + ".new", FileMode.Open, FileAccess.Read, FileShare.None))
                failure = AppUpdater.Swap(locked, locked + ".new").Failure;
            Check("Invariant: a failed swap puts the current exe back", failure == AppUpdateFailure.SwapFailed && File.Exists(locked) && File.ReadAllBytes(locked).SequenceEqual(currentBytes));
        }
        else
        {
            Skip("A failed swap puts the current exe back", "needs Windows file locking");
        }

        // Cleaning up after an update.
        File.WriteAllBytes(current + ".new", stagedBytes);
        AppUpdater.CleanUpLeftovers(current, attempts: 2, delay: TimeSpan.FromMilliseconds(1));
        Check("The new copy deletes the .old and .new leftovers", !File.Exists(current + ".old") && !File.Exists(current + ".new") && File.Exists(current));

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(current + ".old", currentBytes);
            using (new FileStream(current + ".old", FileMode.Open, FileAccess.Read, FileShare.None))
                AppUpdater.CleanUpLeftovers(current, attempts: 2, delay: TimeSpan.FromMilliseconds(5));
            Check("A leftover still in use is skipped quietly, then removed next time", File.Exists(current + ".old"));
            AppUpdater.CleanUpLeftovers(current, attempts: 2, delay: TimeSpan.FromMilliseconds(1));
            Check("It goes once nothing is using it", !File.Exists(current + ".old"));
        }
    }

    // ---------- Screens ----------

    private static void Screens()
    {
        Section("App update screens");

        var manifest = new TrainerManifest().WithApp(App());
        Check("A newer published version is offered", UpdateAdvisor.DescribeAppOffer(manifest, "1.1.3", true) is { CurrentVersion: "1.1.3", Entry.Version: "1.2.0" });
        Check("The same version is not", UpdateAdvisor.DescribeAppOffer(manifest, "1.2.0", true) is null);
        Check("A newer local version is not offered a downgrade", UpdateAdvisor.DescribeAppOffer(manifest, "1.3.0", true) is null);
        Check("Versions compare as numbers", UpdateAdvisor.DescribeAppOffer(new TrainerManifest().WithApp(App("1.10.0")), "1.9.0", true) is not null);
        Check("Nothing is offered when the app can't replace itself", UpdateAdvisor.DescribeAppOffer(manifest, "1.1.3", false) is null);
        Check("Nothing is offered without an app entry or without a manifest",
            UpdateAdvisor.DescribeAppOffer(new TrainerManifest(), "1.1.3", true) is null && UpdateAdvisor.DescribeAppOffer(null, "1.1.3", true) is null);

        var offer = UpdateAdvisor.DescribeAppOffer(manifest, "1.1.3", true);
        var views = new List<HomeView>();

        var noFolder = UpdateAdvisor.DescribeHome(new UpdateContext(false, null, null, null, null, false, null, null, OnlineStatus.Reachable, offer));
        views.Add(noFolder);
        Check("A new app version shows a blue UPDATE APP tile, even before a folder is chosen",
            noFolder is { Pill: "APP UPDATE", Severity: Severity.Highlight, Tile: { Title: "UPDATE APP", Action: TileAction.UpdateApp, Style: TileStyle.Primary, Enabled: true } }, noFolder.Pill);
        Check("It says from and to", noFolder.Tile.Subtitle == "1.1.3 → 1.2.0" && noFolder.Subline.Contains("1.1.3 → 1.2.0") && noFolder.Subline.Contains("restarts itself"), noFolder.Subline);
        Check("It lists the new version and its size", noFolder.Rows.Any(r => r.Text == "New updater: 1.2.0 · 4.9 KB"), string.Join(" | ", noFolder.Rows.Select(r => r.Text)));

        // The app comes before a Trainer update.
        var tournament = new ReSkateVersion("2.1.1", "ReSkate", "0.9.1", "aaaaaaa", "Tournament");
        var folder = new InstallFolderInfo(@"C:\Games\ReSkate", true, false, true, true, tournament, false, false);
        var trainerEntry = new ManifestEntry("tournament", "0.9.2", "578f597", "2.1.1",
            "https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/trainer-tournament-0.9.2/x.zip", 1000, FakeSha);
        var trainerOffer = UpdateAdvisor.DescribeOnlineOffer(new TrainerManifest().With(trainerEntry), folder);
        var both = UpdateAdvisor.DescribeHome(new UpdateContext(false, null, null, null, @"C:\Games\ReSkate", false, folder, trainerOffer, OnlineStatus.Reachable, offer));
        views.Add(both);
        Check("A new app version comes before a Trainer update", both.Tile.Action == TileAction.UpdateApp && both.Rows.Any(r => r.Text.Contains("Installed: Trainer 0.9.1")), both.Tile.Title);
        var trainerOnly = UpdateAdvisor.DescribeHome(new UpdateContext(false, null, null, null, @"C:\Games\ReSkate", false, folder, trainerOffer, OnlineStatus.Reachable, null));
        Check("Without an app update the Trainer update is offered as before", trainerOnly.Tile is { Title: "UPDATE", Action: TileAction.DownloadAndInstall });

        var zip = Path.Combine(Path.GetTempPath(), "x.zip");
        var withZip = UpdateAdvisor.DescribeHome(new UpdateContext(true, zip, null, null, @"C:\Games\ReSkate", false, folder, trainerOffer, OnlineStatus.Reachable, offer));
        Check("A zip being checked is not interrupted by the app update", withZip.Pill == "CHECKING");

        var restarting = UpdateAdvisor.DescribeBusy(BusyStage.Restarting, "ReSkate Trainer Updater 1.2.0");
        views.Add(restarting);
        Check("Restarting shows activity and no click", restarting is { Pill: "RESTARTING", Tile: { Title: "RESTARTING", Working: true, Enabled: false } });
        var downloading = UpdateAdvisor.DescribeBusy(BusyStage.Downloading, "ReSkate Trainer Updater 1.2.0", new DownloadProgress(1024, 4096));
        Check("Downloading the update fills the tile like any download", downloading.Tile is { Progress: 0.25, Working: true } && downloading.Subline == "ReSkate Trainer Updater 1.2.0");

        var notWritable = UpdateAdvisor.DescribeAppUpdateFailure(AppUpdateFailure.NotWritable);
        var badPackage = UpdateAdvisor.DescribeAppUpdateFailure(AppUpdateFailure.BadPackage);
        var swapFailed = UpdateAdvisor.DescribeAppUpdateFailure(AppUpdateFailure.SwapFailed);
        Check("A folder that can't be written to says where to move the app",
            notWritable is { Severity: Severity.Warning } && notWritable.Message.Contains("Move it to a folder you own") && notWritable.Message.Contains("Nothing was changed"), notWritable.Message);
        Check("A package that isn't the published one is an error that changed nothing", badPackage is { Severity: Severity.Error } && badPackage.Message.Contains("Nothing was changed"));
        Check("A failed swap says the current version is untouched", swapFailed.Message.Contains("untouched"));

        Check("If the new version can't open by itself, it says to reopen the app",
            UpdateAdvisor.DescribeAppRestartFailed("1.2.0") is { Severity: Severity.Warning } restartFailed && restartFailed.Message.Contains("open the updater again"));

        var updated = UpdateAdvisor.DescribeAppUpdated("1.2.0");
        Check("The new version says it updated", updated is { Severity: Severity.Good } && updated.Message.Contains("Updated to version 1.2.0"), updated.Message);
        Check("A failed update shows on the card without hiding the tile",
            UpdateAdvisor.WithResult(noFolder, notWritable) is { Pill: "HEADS UP", Tile.Action: TileAction.UpdateApp });

        Check("Every app screen has a short upper-case pill and tile title",
            views.All(v => v.Pill.Length is > 0 and <= 12 && v.Pill == v.Pill.ToUpperInvariant() && v.Tile.Title == v.Tile.Title.ToUpperInvariant()));
    }

    // ---------- Downloading the app ----------

    private static async Task DownloadsAsync(Workspace workspace)
    {
        Section("Downloading the app");

        var zip = AppZip(workspace, "app_download");
        var bytes = File.ReadAllBytes(zip);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var entry = App(size: bytes.Length, sha: sha);
        var parent = workspace.NewFolder("app_downloads");

        using (var client = TrainerDownloader.CreateClient(new FakeServer(_ => FakeServer.Ok(bytes))))
        {
            var ok = await TrainerDownloader.DownloadAsync(client, entry, null, parent);
            Check("The app zip downloads through the same checked path as a Trainer", ok.Succeeded && File.ReadAllBytes(ok.ZipPath!).SequenceEqual(bytes), ok.Detail);
            Check("It is named after the release asset", Path.GetFileName(ok.ZipPath) == "ReSkateTrainerUpdater-1.2.0.zip");
            TrainerDownloader.DeleteDownloadFolder(ok.ZipPath!);
        }

        var tampered = bytes.ToArray();
        tampered[^1] ^= 0xFF;
        using (var client = TrainerDownloader.CreateClient(new FakeServer(_ => FakeServer.Ok(tampered))))
        {
            var bad = await TrainerDownloader.DownloadAsync(client, entry, null, parent);
            Check("Invariant: an app zip with the wrong SHA-256 is refused and deleted",
                bad is { Failure: DownloadFailure.HashMismatch, ZipPath: null } && Directory.GetDirectories(parent).Length == 0, bad.Detail);
        }

        var server = new FakeServer(_ => FakeServer.Ok(bytes));
        using (var client = TrainerDownloader.CreateClient(server))
        {
            // A Trainer-style URL is a valid download URL, but not a valid app URL, so the app zip must not come from it.
            var trainerUrl = entry with { Url = "https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/trainer-tournament-0.9.2/ReSkateTrainerUpdater-1.2.0.zip" };
            var refused = await TrainerDownloader.DownloadAsync(client, trainerUrl, null, parent);
            Check("Invariant: an app update from anywhere but its own release is refused without any request",
                refused.Failure == DownloadFailure.UntrustedUrl && server.Requests == 0, refused.Detail);
        }
    }
}
