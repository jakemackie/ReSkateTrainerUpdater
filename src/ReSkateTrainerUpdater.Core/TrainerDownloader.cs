using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace ReSkateTrainerUpdater.Core;

/// <summary>Why a download was refused or failed. Nothing is ever installed from a failed download. Only a dropped connection or a timeout keeps the part that arrived, so the next attempt can carry on.</summary>
public enum DownloadFailure
{
    None,

    /// <summary>The URL is not a Release asset of the trusted repository. Nothing was requested.</summary>
    UntrustedUrl,

    /// <summary>GitHub couldn't be reached, or the connection dropped. What arrived is kept, and the next attempt continues from it.</summary>
    Network,

    /// <summary>GitHub answered with an error status, for example 404 when the Release asset doesn't exist.</summary>
    HttpError,

    /// <summary>The file was larger or smaller than latest.json says.</summary>
    SizeMismatch,

    /// <summary>The file's SHA-256 is not the one in latest.json.</summary>
    HashMismatch,

    /// <summary>The download took too long or was cancelled. What arrived is kept, and the next attempt continues from it.</summary>
    TimedOut,

    /// <summary>The file couldn't be written to the temp folder.</summary>
    DiskError,
}

/// <summary>The outcome of <see cref="TrainerDownloader.DownloadAsync"/>.</summary>
/// <param name="ZipPath">The verified zip, when <see cref="Failure"/> is None. The caller deletes its folder.</param>
/// <param name="Failure">What went wrong, or None.</param>
/// <param name="Detail">Technical detail for the activity log.</param>
/// <param name="KeptBytes">After a Network or TimedOut failure: how much was kept, so the next attempt continues from there.</param>
public sealed record DownloadResult(string? ZipPath, DownloadFailure Failure, string? Detail, long KeptBytes = 0)
{
    public bool Succeeded => Failure == DownloadFailure.None && ZipPath is not null;
}

/// <summary>How far a download has got, for the progress text.</summary>
public readonly record struct DownloadProgress(long Received, long Total);

/// <summary>
/// Talks to GitHub: reads latest.json and downloads Trainer zips. Nothing here touches the ReSkate folder.
/// A downloaded zip is only handed back once its size and SHA-256 match latest.json exactly.
/// </summary>
public static class TrainerDownloader
{
    /// <summary>How long the start-up check may take before it is silently skipped.</summary>
    public static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(6);

    /// <summary>How long a whole zip download may take.</summary>
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The HTTP client the app uses. It sends a User-Agent naming the app and its version, and nothing about the user.
    /// Redirects are followed (GitHub serves Release assets from another host), but never from https to http.
    /// </summary>
    public static HttpClient CreateClient(HttpMessageHandler? handler = null)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = Timeout.InfiniteTimeSpan; // each call sets its own deadline
        var version = typeof(TrainerDownloader).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ReSkateTrainerUpdater", version));
        return client;
    }

    /// <summary>
    /// Downloads and reads latest.json. Never throws: any failure, including no internet, comes back as a
    /// result with a null manifest and a problem for the activity log. The file's signature is checked first
    /// (<see cref="ManifestSignature"/>); a file that isn't signed by a trusted key is never read.
    /// <paramref name="trustedKeys"/> is for tests; the app always uses the keys built into it.
    /// </summary>
    public static async Task<ManifestReadResult> FetchManifestAsync(
        HttpClient client,
        Uri? manifestUrl = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        IEnumerable<string>? trustedKeys = null)
    {
        manifestUrl ??= OnlineSource.ManifestUrl;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? ManifestTimeout);

        try
        {
            using var response = await client.GetAsync(manifestUrl, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Problem($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase} for {manifestUrl}.");

            if (response.Content.Headers.ContentLength > TrainerManifest.MaxManifestBytes)
                return Problem("latest.json is too large to be a real manifest.");

            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > TrainerManifest.MaxManifestBytes)
                    return Problem("latest.json is too large to be a real manifest.");
            }

            var text = Encoding.UTF8.GetString(buffer.ToArray());
            var signature = ManifestSignature.Verify(text, trustedKeys);
            if (!signature.IsValid) return Problem(signature.Problem!);

            return TrainerManifest.Parse(text);
        }
        catch (OperationCanceledException)
        {
            return Problem("Checking GitHub took too long, so it was skipped.");
        }
        catch (HttpRequestException ex)
        {
            return Problem("Couldn't reach GitHub: " + ex.Message);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return Problem("Couldn't read latest.json: " + ex.Message);
        }

        static ManifestReadResult Problem(string message) => new(null, new[] { message });
    }

    /// <summary>
    /// Downloads the zip for <paramref name="entry"/> into a folder under <paramref name="parentFolder"/> (the system temp
    /// folder by default), checking its size and SHA-256 against the entry. Never throws.
    ///
    /// A download that is cut off by the network or the time limit keeps what it received, in a folder named after the
    /// file's hash. The next call for the same file asks the server for the rest with an HTTP Range request and carries
    /// on from there. A drop is also retried up to <see cref="MaxAutomaticResumes"/> times within one call, as long as
    /// each try got somewhere. The finished file is still checked as a whole, so a stale or damaged partial can never
    /// be installed: on any other failure everything is deleted, and nothing is returned unless size and hash match.
    /// </summary>
    public static async Task<DownloadResult> DownloadAsync(
        HttpClient client,
        IPublishedFile entry,
        IProgress<DownloadProgress>? progress = null,
        string? parentFolder = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        TimeSpan? retryDelay = null)
    {
        // Checked again here, so no code path can download from anywhere else.
        if (!entry.HasTrustedUrl)
            return Fail(DownloadFailure.UntrustedUrl, "Refused to download from " + entry.Url);
        if (entry.SizeBytes <= 0 || entry.SizeBytes > TrainerManifest.MaxZipBytes)
            return Fail(DownloadFailure.SizeMismatch, $"latest.json gives an impossible size ({entry.SizeBytes} bytes).");

        // The folder name starts with the same prefix as the unpack folders, so a crash leaves nothing that
        // TrainerPackageLoader.CleanUpStaleTempFolders won't eventually delete. It is the same every time for the same
        // file, which is how a later click finds the part that is already there.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Sha256 + "|" + entry.Url)))[..16].ToLowerInvariant();
        var folder = Path.Combine(parentFolder ?? Path.GetTempPath(), TrainerPackageLoader.TempPrefix + "download_" + key);
        var zipPath = Path.Combine(folder, SafeFileName(entry.FileName));
        var partPath = zipPath + ".part";

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? DownloadTimeout);

        bool restartedFresh = false;
        try
        {
            Directory.CreateDirectory(folder);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            int drops = 0;
            while (true)
            {
                long offset = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
                if (offset > entry.SizeBytes) { File.Delete(partPath); offset = 0; }

                try
                {
                    var step = await ReceiveAsync(client, entry, partPath, offset, progress, deadline.Token).ConfigureAwait(false);
                    if (step.RestartFresh)
                    {
                        // The server can't or won't continue from there. Start again once; a second time is a real failure.
                        if (restartedFresh) return Cleanup(DownloadFailure.HttpError, "The server would not send the file from the start.");
                        restartedFresh = true;
                        File.Delete(partPath);
                        continue;
                    }
                    if (step.Failure is { } problem) return Cleanup(problem.Failure, problem.Detail!);
                }
                catch (Exception ex) when (IsNetworkDrop(ex))
                {
                    long now = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
                    if (now > offset && ++drops <= MaxAutomaticResumes)
                    {
                        await Task.Delay(retryDelay ?? TimeSpan.FromSeconds(1.5), deadline.Token).ConfigureAwait(false);
                        continue;
                    }
                    return KeepPartial(DownloadFailure.Network, ex.Message);
                }

                // The whole file is here. Its size and hash decide everything.
                long total = new FileInfo(partPath).Length;
                if (total > entry.SizeBytes)
                {
                    return Cleanup(DownloadFailure.SizeMismatch,
                        $"The download is larger than the {entry.SizeBytes} bytes latest.json says, so it was stopped.");
                }
                if (total != entry.SizeBytes)
                {
                    return Cleanup(DownloadFailure.SizeMismatch,
                        $"Received {total} bytes, but latest.json says the file is {entry.SizeBytes} bytes.");
                }

                string actual;
                await using (var finished = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                {
                    actual = Convert.ToHexString(await SHA256.HashDataAsync(finished, deadline.Token).ConfigureAwait(false)).ToLowerInvariant();
                }
                if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    // A file finished from an old part is wrong if the part was (the published file may have been
                    // replaced since). Try once from the start before giving up.
                    if (offset > 0 && !restartedFresh)
                    {
                        restartedFresh = true;
                        File.Delete(partPath);
                        continue;
                    }
                    return Cleanup(DownloadFailure.HashMismatch,
                        $"SHA-256 of the download is {actual}, but latest.json says {entry.Sha256}.");
                }

                File.Move(partPath, zipPath, overwrite: true);
                return new DownloadResult(zipPath, DownloadFailure.None, $"SHA-256 matches latest.json ({actual}).");
            }
        }
        catch (OperationCanceledException)
        {
            return KeepPartial(DownloadFailure.TimedOut, "The download took too long and was stopped.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Cleanup(DownloadFailure.DiskError, ex.Message);
        }

        DownloadResult Cleanup(DownloadFailure failure, string detail)
        {
            DeleteDownloadFolder(zipPath);
            return Fail(failure, detail);
        }

        // Keeps what arrived so the next click can carry on. With nothing received there is nothing to keep.
        DownloadResult KeepPartial(DownloadFailure failure, string detail)
        {
            long kept = 0;
            try { kept = File.Exists(partPath) ? new FileInfo(partPath).Length : 0; }
            catch (IOException) { }
            if (kept <= 0) return Cleanup(failure, detail);
            return new DownloadResult(null, failure, detail, kept);
        }

        static DownloadResult Fail(DownloadFailure failure, string detail) => new(null, failure, detail);
    }

    /// <summary>How many times one call carries on by itself after the connection drops part-way.</summary>
    public const int MaxAutomaticResumes = 3;

    /// <summary>What one request did: nothing special (the body arrived), a failure to clean up after, or "start again from byte 0".</summary>
    private readonly record struct Received(bool RestartFresh = false, DownloadResult? Failure = null);

    // The connection dropping or the host being unreachable: worth carrying on. A full disk or a cancel is not.
    private static bool IsNetworkDrop(Exception ex) =>
        ex is HttpRequestException ||
        (ex is IOException io && (io is HttpIOException || io.InnerException is System.Net.Sockets.SocketException));

    // One request: appends to the part file from `offset` to the end. The caller checks size and hash afterwards.
    // Network trouble is thrown, with whatever arrived already saved in the part file.
    private static async Task<Received> ReceiveAsync(HttpClient client, IPublishedFile entry, string partPath, long offset,
        IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        if (offset == entry.SizeBytes) return default; // a whole part is already here; only its hash is left to check

        using var request = new HttpRequestMessage(HttpMethod.Get, entry.Url);
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

        if (offset > 0 && response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            return new Received(RestartFresh: true);
        if (!response.IsSuccessStatusCode)
        {
            return new Received(Failure: new DownloadResult(null, DownloadFailure.HttpError,
                $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase} for {entry.Url}"));
        }

        // 206 continues from the offset. 200 means the server sent the whole file, whatever was asked for.
        bool partial = offset > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (partial)
        {
            var range = response.Content.Headers.ContentRange;
            if (range?.From != offset) return new Received(RestartFresh: true);
            if (range.Length is long whole && whole != entry.SizeBytes)
            {
                return new Received(Failure: new DownloadResult(null, DownloadFailure.SizeMismatch,
                    $"GitHub says the file is {whole} bytes, but latest.json says {entry.SizeBytes}."));
            }
        }
        long start = partial ? offset : 0;

        if (response.Content.Headers.ContentLength is long announced && start + announced != entry.SizeBytes)
        {
            return new Received(Failure: new DownloadResult(null, DownloadFailure.SizeMismatch,
                $"GitHub says the file is {start + announced} bytes, but latest.json says {entry.SizeBytes}."));
        }

        long received = start;
        await using (var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
        await using (var target = new FileStream(partPath, start > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            progress?.Report(new DownloadProgress(received, entry.SizeBytes));
            var buffer = new byte[81920];
            int read;
            long lastReport = received;
            while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                received += read;
                await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                if (received > entry.SizeBytes) break; // too big: stop reading; the caller sees it is over the size

                // Report about every 256 KB, not on every chunk.
                if (received - lastReport >= 256 * 1024 || received == entry.SizeBytes)
                {
                    lastReport = received;
                    progress?.Report(new DownloadProgress(received, entry.SizeBytes));
                }
            }
        }
        return default;
    }

    /// <summary>Deletes the folder a downloaded zip was saved in. Best effort.</summary>
    public static void DeleteDownloadFolder(string zipPath)
    {
        try
        {
            var folder = Path.GetDirectoryName(zipPath);
            if (folder is not null &&
                Path.GetFileName(folder).StartsWith(TrainerPackageLoader.TempPrefix + "download_", StringComparison.Ordinal) &&
                Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; stale temp folders are removed on a later start-up.
        }
    }

    /// <summary>
    /// Checks the unpacked zip really is what latest.json promised: same Trainer version, edition and build.
    /// Returns null when it matches, or the reason it doesn't.
    /// </summary>
    public static string? CheckPackageMatches(ManifestEntry entry, TrainerPackage package)
    {
        if (!package.IsValid) return package.Errors.Count > 0 ? package.Errors[0] : "The downloaded zip isn't a valid Trainer.";
        if (package.Version is not { HasTrainer: true } version)
            return "The downloaded zip has no readable Trainer version.";

        if (version.TrainerVersion != entry.Version || OnlineSource.EditionKey(version.Edition) != entry.Edition)
            return $"The downloaded zip contains {version.TrainerText}, but latest.json says {entry.TrainerText}.";

        if (entry.Build is not null && version.TrainerBuild is not null && version.TrainerBuild != entry.Build)
            return $"The downloaded zip is build {version.TrainerBuild}, but latest.json says build {entry.Build}.";

        return null;
    }

    // The file name comes from the URL, which is already checked, but never let it pick a path.
    private static string SafeFileName(string name)
    {
        var cleaned = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return cleaned.Length == 0 ? "trainer.zip" : cleaned;
    }
}
