using System.Net;
using System.Security.Cryptography;
using ReSkateTrainerUpdater.Core;
using static ReSkateTrainerUpdater.Tests.TestKit;

namespace ReSkateTrainerUpdater.Tests;

/// <summary>Online updates: the trusted-URL rule, latest.json, the banner offer, and downloads. No real network is used.</summary>
internal static class OnlineTests
{
    private const string GoodUrl =
        "https://github.com/jakemackie/ReSkateTrainerUpdater/releases/download/trainer-tournament-0.9.2/ReSkate_Trainer_Tournament-0.9.2-on-v2.1.1.zip";

    private static readonly string FakeSha = new('a', 64);

    public static void Run(Workspace workspace)
    {
        TrustedUrls();
        Manifest();
        Offers(workspace);
        DownloadsAsync(workspace).GetAwaiter().GetResult();
    }

    private static ManifestEntry Entry(string version = "0.9.2", string edition = "tournament", string url = GoodUrl,
        long size = 1000, string? sha = null, string? build = "578f597") =>
        new(edition, version, build, "2.1.1", url, size, sha ?? FakeSha);

    // ---------- Which URLs the app will download from ----------

    private static void TrustedUrls()
    {
        Section("Trusted download URLs");

        Check("A Release asset of this repo is trusted", OnlineSource.IsTrustedDownloadUrl(GoodUrl));
        Check("The owner and repo name are not case sensitive",
            OnlineSource.IsTrustedDownloadUrl(GoodUrl.Replace("jakemackie/ReSkateTrainerUpdater", "JakeMackie/reskatetrainerupdater")));

        string[] refused =
        {
            GoodUrl.Replace("https://", "http://"),
            GoodUrl.Replace("github.com", "evil.example"),
            GoodUrl.Replace("github.com", "github.com.evil.example"),
            GoodUrl.Replace("jakemackie/", "someoneelse/"),
            GoodUrl.Replace("ReSkateTrainerUpdater/releases", "OtherRepo/releases"),
            GoodUrl.Replace("/releases/download/", "/raw/main/"),
            GoodUrl.Replace("trainer-tournament-0.9.2/", "trainer-tournament-0.9.2/../../../other/"),
            GoodUrl.Replace("trainer-tournament-0.9.2/", "a/b/"),
            GoodUrl + "?x=1",
            GoodUrl + "#frag",
            GoodUrl.Replace(".zip", ".exe"),
            GoodUrl.Replace("https://github.com", "https://user:pass@github.com"),
            GoodUrl.Replace("https://github.com", "https://github.com:8443"),
            "https://raw.githubusercontent.com/jakemackie/ReSkateTrainerUpdater/main/x.zip",
            "",
            "not a url",
        };
        var accepted = refused.Where(OnlineSource.IsTrustedDownloadUrl).ToList();
        Check($"Every other kind of URL is refused ({refused.Length} cases)", accepted.Count == 0, string.Join(" | ", accepted));

        var built = OnlineSource.ReleaseAssetUrl("trainer-tournament-0.9.2", "ReSkate_Trainer_Tournament-0.9.2-on-v2.1.1.zip");
        Check("ReleaseAssetUrl builds the trusted URL", built == GoodUrl, built);
        Check("The manifest is read from this repo's main branch",
            OnlineSource.ManifestUrl.ToString() == "https://raw.githubusercontent.com/jakemackie/ReSkateTrainerUpdater/main/latest.json");

        Check("Edition keys are lower case with dashes",
            OnlineSource.EditionKey("Tournament") == "tournament" && OnlineSource.EditionKey("Big Air") == "big-air" && OnlineSource.EditionKey(null) == "standard");
        Check("Edition keys turn back into names", OnlineSource.EditionDisplayName("big-air") == "Big Air");
    }

    // ---------- latest.json ----------

    private static string Json(string editions, int schema = 1) => $$"""{ "schemaVersion": {{schema}}, "editions": { {{editions}} } }""";

    private static string EntryJson(string key = "tournament", string url = GoodUrl, string sha = "", long size = 1000, string version = "0.9.2") =>
        $$"""
        "{{key}}": { "version": "{{version}}", "build": "578f597", "reskateVersion": "2.1.1", "url": "{{url}}", "sizeBytes": {{size}}, "sha256": "{{(sha.Length > 0 ? sha : FakeSha)}}" }
        """;

    private static void Manifest()
    {
        Section("latest.json");

        var good = TrainerManifest.Parse(Json(EntryJson(sha: new string('A', 64))));
        var tournament = good.Manifest?.Find("tournament");
        Check("A valid manifest is read", tournament is { Version: "0.9.2", Build: "578f597", ReSkateVersion: "2.1.1", SizeBytes: 1000, Url: GoodUrl } && good.Problems.Count == 0,
            string.Join("; ", good.Problems));
        Check("The SHA-256 is stored in lower case", tournament?.Sha256 == new string('a', 64));
        Check("The entry knows its display text and file name",
            tournament is { TrainerText: "0.9.2 (Tournament)", FileName: "ReSkate_Trainer_Tournament-0.9.2-on-v2.1.1.zip" });

        var empty = TrainerManifest.Parse(Json(""));
        Check("A manifest with no editions is valid and offers nothing", empty.Manifest is { Editions.Count: 0 } && empty.Problems.Count == 0);

        Check("Broken JSON is rejected without throwing", TrainerManifest.Parse("{ not json").Manifest is null);
        Check("A JSON array is rejected", TrainerManifest.Parse("[]").Manifest is null);
        Check("A missing schemaVersion is rejected", TrainerManifest.Parse("""{ "editions": {} }""").Manifest is null);

        var future = TrainerManifest.Parse(Json(EntryJson(), schema: 2));
        Check("A newer format is ignored and says a newer app is needed",
            future.Manifest is null && future.Problems[0].Contains("newer version of the app"), string.Join("; ", future.Problems));

        var mixed = TrainerManifest.Parse(Json(EntryJson("tournament") + "," + EntryJson("standard", url: "https://evil.example/x.zip")));
        Check("An entry with an untrusted URL is dropped, others are kept",
            mixed.Manifest is { Editions.Count: 1 } && mixed.Manifest.Find("standard") is null && mixed.Problems.Any(p => p.Contains("\"url\"")),
            string.Join("; ", mixed.Problems));

        Check("A short SHA-256 is dropped", TrainerManifest.Parse(Json(EntryJson(sha: "abc123"))).Manifest is { Editions.Count: 0 });
        Check("A zero size is dropped", TrainerManifest.Parse(Json(EntryJson(size: 0))).Manifest is { Editions.Count: 0 });
        Check("A huge size is dropped", TrainerManifest.Parse(Json(EntryJson(size: TrainerManifest.MaxZipBytes + 1))).Manifest is { Editions.Count: 0 });
        Check("A malformed version is dropped", TrainerManifest.Parse(Json(EntryJson(version: "0.9"))).Manifest is { Editions.Count: 0 });
        Check("An upper-case edition key is dropped", TrainerManifest.Parse(Json(EntryJson(key: "Tournament"))).Manifest is { Editions.Count: 0 });

        var written = new TrainerManifest().With(Entry()).With(Entry(edition: "big-air", version: "1.0.0")).ToJson();
        var reread = TrainerManifest.Parse(written);
        Check("Writing then reading gives the same entries",
            reread.Manifest is { } m && m.Find("tournament") == Entry() && m.Find("big-air") == Entry(edition: "big-air", version: "1.0.0"), written);
        Check("Written files use LF line endings and end with a newline", !written.Contains('\r') && written.EndsWith("}\n"));
        Check("Editions are written in a stable order", written.IndexOf("\"big-air\"", StringComparison.Ordinal) < written.IndexOf("\"tournament\"", StringComparison.Ordinal));
        Check("Writing the same manifest twice gives identical text", written == reread.Manifest!.ToJson());

        Check("With() replaces the same edition", new TrainerManifest().With(Entry()).With(Entry(version: "0.9.3")).Find("tournament")?.Version == "0.9.3");
        bool threw = false;
        try { new TrainerManifest().With(Entry(url: "https://evil.example/x.zip")); }
        catch (ArgumentException) { threw = true; }
        Check("With() refuses an entry that fails a check", threw);

        // The latest.json committed at the repo root must always be one the app accepts.
        var repoManifest = FindRepoFile("latest.json");
        if (repoManifest is null)
        {
            Skip("The committed latest.json is valid", "not running from inside the repository");
        }
        else
        {
            var committed = TrainerManifest.Parse(File.ReadAllText(repoManifest));
            Check("The committed latest.json is valid", committed.Manifest is not null && committed.Problems.Count == 0,
                string.Join("; ", committed.Problems));
            Check("The committed latest.json is in the canonical format",
                committed.Manifest is not null && File.ReadAllText(repoManifest).Replace("\r\n", "\n") == committed.Manifest.ToJson(),
                "Run the manifest tool, or match its formatting exactly.");
        }
    }

    private static string? FindRepoFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ReSkateTrainerUpdater.sln")))
            {
                var path = Path.Combine(dir.FullName, name);
                return File.Exists(path) ? path : null;
            }
        }
        return null;
    }

    // ---------- What the banner offers ----------

    private static void Offers(Workspace workspace)
    {
        Section("Online offer");

        var manifest = new TrainerManifest().With(Entry());
        InstallFolderInfo Folder(ReSkateVersion? installed, bool hasRuntime = true, bool steam = false) =>
            new(@"C:\Games\ReSkate", true, IsEmpty: !hasRuntime, HasLauncher: hasRuntime, HasRuntime: hasRuntime, installed, steam, false);

        ReSkateVersion Tournament(string version) => new("2.1.1", "ReSkate", version, "aaaaaaa", "Tournament");

        Check("No manifest means no offer", UpdateAdvisor.DescribeOnlineOffer(null, Folder(null, hasRuntime: false)).Kind == OfferKind.None);
        Check("No folder means no offer", UpdateAdvisor.DescribeOnlineOffer(manifest, null).Kind == OfferKind.None);

        var fresh = UpdateAdvisor.DescribeOnlineOffer(manifest, Folder(null, hasRuntime: false));
        Check("An empty folder is offered the Tournament Edition",
            fresh is { Kind: OfferKind.FreshInstall, CanDownload: true, ButtonText: "Install Tournament Edition" } && fresh.Title.Contains("0.9.2 (Tournament)"),
            fresh.Title);
        Check("The offer names the size and that it is checked", fresh.Detail.Contains("checked before installing"), fresh.Detail);
        Check("An empty folder gets nothing if Tournament isn't published",
            UpdateAdvisor.DescribeOnlineOffer(new TrainerManifest().With(Entry(edition: "big-air")), Folder(null, hasRuntime: false)).Kind == OfferKind.None);

        var update = UpdateAdvisor.DescribeOnlineOffer(manifest, Folder(Tournament("0.9.1")));
        Check("An older Tournament gets an update offer",
            update is { Kind: OfferKind.Update, CanDownload: true, ButtonText: "Update" } && update.Title == "Update available: 0.9.1 → 0.9.2 (Tournament)",
            update.Title);
        Check("Versions compare as numbers, not text",
            UpdateAdvisor.DescribeOnlineOffer(new TrainerManifest().With(Entry(version: "0.10.0")), Folder(Tournament("0.9.2"))).Kind == OfferKind.Update);

        var same = UpdateAdvisor.DescribeOnlineOffer(manifest, Folder(Tournament("0.9.2")));
        Check("The newest Tournament is up to date with no button", same is { Kind: OfferKind.UpToDate, CanDownload: false });
        Check("A newer local Tournament is not offered a downgrade",
            UpdateAdvisor.DescribeOnlineOffer(manifest, Folder(Tournament("0.9.5"))) is { Kind: OfferKind.UpToDate, CanDownload: false });

        var standard = new ReSkateVersion("2.1.1", "ReSkate", "0.9.1", "aaaaaaa", null);
        Check("The standard edition gets no Tournament offer", UpdateAdvisor.DescribeOnlineOffer(manifest, Folder(standard)).Kind == OfferKind.None);
        Check("Plain ReSkate gets no offer",
            UpdateAdvisor.DescribeOnlineOffer(manifest, Folder(new ReSkateVersion("2.1.1", "ReSkate", null, null, null))).Kind == OfferKind.None);
        Check("An unreadable ReSkate.dll gets no offer", UpdateAdvisor.DescribeOnlineOffer(manifest, Folder(null, hasRuntime: true)).Kind == OfferKind.None);
        Check("Steam's skate. folder gets no offer",
            UpdateAdvisor.DescribeOnlineOffer(manifest, Folder(null, hasRuntime: false, steam: true)).Kind == OfferKind.None);

        var status = UpdateAdvisor.Evaluate(new UpdateContext(false, null, null, null, @"C:\Games\ReSkate", false, Folder(Tournament("0.9.1")), update));
        Check("With an offer and no zip, the status points at the button", status.Message.Contains("Click Update above") && !status.CanInstall, status.Message);

        Section("Online messages");
        Check("A failed check is logged quietly",
            UpdateAdvisor.DescribeManifestCheck(new ManifestReadResult(null, new[] { "no internet" })).Contains("Dropping a zip still works"));
        Check("A successful check names the newest version",
            UpdateAdvisor.DescribeManifestCheck(new ManifestReadResult(manifest, Array.Empty<string>())).Contains("Tournament 0.9.2"));
        Check("Download progress shows sizes",
            UpdateAdvisor.DescribeDownloadProgress(Entry(), new DownloadProgress(512 * 1024, 2 * 1024 * 1024)).Message.Contains("of 2 MB"));
        var everyFailure = Enum.GetValues<DownloadFailure>().Where(f => f != DownloadFailure.None).Select(UpdateAdvisor.DescribeDownloadFailure).ToList();
        Check("Every download failure says nothing was changed", everyFailure.All(a => a.Message.Contains("Nothing was changed")));
        Check("A hash mismatch is an error", UpdateAdvisor.DescribeDownloadFailure(DownloadFailure.HashMismatch).Severity == Severity.Error);
    }

    // ---------- Downloading ----------

    private static async Task DownloadsAsync(Workspace workspace)
    {
        Section("Downloads");

        var zip = workspace.NewZip("download", folder => FakeBinary.WriteRelease(folder));
        var bytes = File.ReadAllBytes(zip);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var entry = Entry(size: bytes.Length, sha: sha);
        var parent = workspace.NewFolder("downloads");

        bool NothingLeft() => Directory.GetDirectories(parent).Length == 0;

        // Success.
        var server = new FakeServer(_ => FakeServer.Ok(bytes));
        using (var client = TrainerDownloader.CreateClient(server))
        {
            var progressCalls = 0;
            var ok = await TrainerDownloader.DownloadAsync(client, entry, new SyncProgress<DownloadProgress>(_ => progressCalls++), parent);
            Check("A matching download succeeds", ok.Succeeded, ok.Detail);
            Check("The downloaded file is byte-for-byte the published one", ok.ZipPath is not null && File.ReadAllBytes(ok.ZipPath).SequenceEqual(bytes));
            Check("It is named after the Release asset", Path.GetFileName(ok.ZipPath) == "ReSkate_Trainer_Tournament-0.9.2-on-v2.1.1.zip");
            Check("Progress is reported", progressCalls > 0);
            Check("The request named the app in its User-Agent", server.LastUserAgent?.Contains("ReSkateTrainerUpdater") == true, server.LastUserAgent);

            using (var package = TrainerPackageLoader.Load(ok.ZipPath!))
            {
                Check("The downloaded zip matches latest.json", TrainerDownloader.CheckPackageMatches(entry, package) is null);
                Check("A different version is caught", TrainerDownloader.CheckPackageMatches(entry with { Version = "0.9.3" }, package)?.Contains("0.9.3 (Tournament)") == true);
                Check("A different edition is caught", TrainerDownloader.CheckPackageMatches(entry with { Edition = "big-air" }, package) is not null);
                Check("A different build is caught", TrainerDownloader.CheckPackageMatches(entry with { Build = "1234567" }, package)?.Contains("build") == true);

                var target = workspace.NewFolder("online_install_target");
                var installed = TrainerInstaller.Install(package.RootFolder, target, backup: true);
                Check("A downloaded zip installs exactly like a dropped one",
                    installed.Outcome == InstallOutcome.Installed && ReSkateScanner.Read(Path.Combine(target, "ReSkate.dll")).TrainerVersion == "0.9.2");
            }

            TrainerDownloader.DeleteDownloadFolder(ok.ZipPath!);
            Check("The download folder can be cleaned up", NothingLeft());
        }

        // Invariant: every failure deletes what was downloaded and never returns a file.
        async Task<DownloadResult> Try(Func<HttpRequestMessage, HttpResponseMessage> respond, ManifestEntry e, TimeSpan? timeout = null)
        {
            using var client = TrainerDownloader.CreateClient(new FakeServer(respond));
            return await TrainerDownloader.DownloadAsync(client, e, null, parent, timeout);
        }

        var tampered = bytes.ToArray();
        tampered[^1] ^= 0xFF;
        var hashFail = await Try(_ => FakeServer.Ok(tampered), entry);
        Check("Invariant: a file with the wrong SHA-256 is refused and deleted",
            hashFail is { Failure: DownloadFailure.HashMismatch, ZipPath: null } && NothingLeft(), hashFail.Detail);

        var longer = bytes.Concat(new byte[10]).ToArray();
        var tooBig = await Try(_ => FakeServer.Ok(longer, sendLength: false), entry);
        Check("Invariant: a file larger than latest.json says is stopped and deleted",
            tooBig is { Failure: DownloadFailure.SizeMismatch, ZipPath: null } && NothingLeft(), tooBig.Detail);

        var tooSmall = await Try(_ => FakeServer.Ok(bytes[..^5], sendLength: false), entry);
        Check("Invariant: a file smaller than latest.json says is refused and deleted",
            tooSmall is { Failure: DownloadFailure.SizeMismatch, ZipPath: null } && NothingLeft(), tooSmall.Detail);

        var wrongLength = await Try(_ => FakeServer.Ok(longer), entry);
        Check("A wrong Content-Length is refused before downloading", wrongLength.Failure == DownloadFailure.SizeMismatch && NothingLeft());

        var missing = await Try(_ => new HttpResponseMessage(HttpStatusCode.NotFound), entry);
        Check("A missing Release asset is an HTTP error", missing.Failure == DownloadFailure.HttpError && NothingLeft(), missing.Detail);

        var offline = await Try(_ => throw new HttpRequestException("No such host is known."), entry);
        Check("No internet is a network error", offline.Failure == DownloadFailure.Network && NothingLeft(), offline.Detail);

        var slow = await Try(_ => FakeServer.Ok(bytes, delay: TimeSpan.FromSeconds(5)), entry, TimeSpan.FromMilliseconds(100));
        Check("A download that takes too long is stopped", slow.Failure == DownloadFailure.TimedOut && NothingLeft(), slow.Detail);

        var neverCalled = new FakeServer(_ => FakeServer.Ok(bytes));
        using (var client = TrainerDownloader.CreateClient(neverCalled))
        {
            var untrusted = await TrainerDownloader.DownloadAsync(client, entry with { Url = "https://evil.example/x.zip" }, null, parent);
            Check("Invariant: an untrusted URL is refused without any request", untrusted.Failure == DownloadFailure.UntrustedUrl && neverCalled.Requests == 0);
        }

        Section("Reading latest.json online");
        var manifestJson = new TrainerManifest().With(entry).ToJson();
        async Task<ManifestReadResult> Fetch(Func<HttpRequestMessage, HttpResponseMessage> respond, TimeSpan? timeout = null)
        {
            using var client = TrainerDownloader.CreateClient(new FakeServer(respond));
            return await TrainerDownloader.FetchManifestAsync(client, null, timeout);
        }

        var fetched = await Fetch(_ => FakeServer.Ok(System.Text.Encoding.UTF8.GetBytes(manifestJson)));
        Check("latest.json is fetched and read", fetched.Manifest?.Find("tournament") == entry, string.Join("; ", fetched.Problems));
        Check("A server error is skipped quietly", (await Fetch(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))).Manifest is null);
        Check("No internet is skipped quietly", (await Fetch(_ => throw new HttpRequestException("offline"))).Manifest is null);
        Check("A huge response is refused",
            (await Fetch(_ => FakeServer.Ok(new byte[TrainerManifest.MaxManifestBytes + 1], sendLength: false))).Manifest is null);
        Check("A slow response is skipped",
            (await Fetch(_ => FakeServer.Ok(System.Text.Encoding.UTF8.GetBytes(manifestJson), delay: TimeSpan.FromSeconds(5)), TimeSpan.FromMilliseconds(100))).Manifest is null);
    }
}

/// <summary>Stands in for GitHub. Each request is answered by a function, optionally after a delay.</summary>
internal sealed class FakeServer : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public FakeServer(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public int Requests { get; private set; }
    public string? LastUserAgent { get; private set; }

    public static HttpResponseMessage Ok(byte[] body, bool sendLength = true, TimeSpan? delay = null)
    {
        HttpContent content = sendLength
            ? new ByteArrayContent(body)
            : new StreamContent(new UnknownLengthStream(body));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        if (delay is { } d) response.Headers.Add("X-Test-Delay", ((int)d.TotalMilliseconds).ToString());
        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        LastUserAgent = request.Headers.UserAgent.ToString();
        var response = _respond(request);
        if (response.Headers.TryGetValues("X-Test-Delay", out var values))
            await Task.Delay(int.Parse(values.First()), cancellationToken);
        return response;
    }

    // A stream that can't report its length, so no Content-Length header is sent.
    private sealed class UnknownLengthStream : MemoryStream
    {
        public UnknownLengthStream(byte[] data) : base(data) { }
        public override bool CanSeek => false;
    }
}

/// <summary>Like Progress&lt;T&gt; but calls back immediately on the same thread, so tests can count calls.</summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;
    public SyncProgress(Action<T> report) => _report = report;
    public void Report(T value) => _report(value);
}
