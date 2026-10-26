using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace ReSkateTrainerUpdater.Core;

/// <summary>Why a download was refused or failed. Every failure leaves nothing behind on disk.</summary>
public enum DownloadFailure
{
    None,

    /// <summary>The URL is not a Release asset of the trusted repository. Nothing was requested.</summary>
    UntrustedUrl,

    /// <summary>GitHub couldn't be reached, or the connection dropped.</summary>
    Network,

    /// <summary>GitHub answered with an error status, for example 404 when the Release asset doesn't exist.</summary>
    HttpError,

    /// <summary>The file was larger or smaller than latest.json says.</summary>
    SizeMismatch,

    /// <summary>The file's SHA-256 is not the one in latest.json.</summary>
    HashMismatch,

    /// <summary>The download took too long or was cancelled.</summary>
    TimedOut,

    /// <summary>The file couldn't be written to the temp folder.</summary>
    DiskError,
}

/// <summary>The outcome of <see cref="TrainerDownloader.DownloadAsync"/>.</summary>
/// <param name="ZipPath">The verified zip, when <see cref="Failure"/> is None. The caller deletes its folder.</param>
/// <param name="Failure">What went wrong, or None.</param>
/// <param name="Detail">Technical detail for the activity log.</param>
public sealed record DownloadResult(string? ZipPath, DownloadFailure Failure, string? Detail)
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
    /// result with a null manifest and a problem for the activity log.
    /// </summary>
    public static async Task<ManifestReadResult> FetchManifestAsync(
        HttpClient client,
        Uri? manifestUrl = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
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

            return TrainerManifest.Parse(Encoding.UTF8.GetString(buffer.ToArray()));
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
    /// Downloads the zip for <paramref name="entry"/> into a new folder under <paramref name="parentFolder"/>
    /// (the system temp folder by default), checking its size and SHA-256 against the entry as it arrives.
    /// On any failure the partial file and its folder are deleted. Never throws.
    /// </summary>
    public static async Task<DownloadResult> DownloadAsync(
        HttpClient client,
        IPublishedFile entry,
        IProgress<DownloadProgress>? progress = null,
        string? parentFolder = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        // Checked again here, so no code path can download from anywhere else.
        if (!entry.HasTrustedUrl)
            return Fail(DownloadFailure.UntrustedUrl, "Refused to download from " + entry.Url);
        if (entry.SizeBytes <= 0 || entry.SizeBytes > TrainerManifest.MaxZipBytes)
            return Fail(DownloadFailure.SizeMismatch, $"latest.json gives an impossible size ({entry.SizeBytes} bytes).");

        // The folder name starts with the same prefix as the unpack folders, so a crash leaves nothing that
        // TrainerPackageLoader.CleanUpStaleTempFolders won't eventually delete.
        var folder = Path.Combine(parentFolder ?? Path.GetTempPath(), TrainerPackageLoader.TempPrefix + "download_" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(folder, SafeFileName(entry.FileName));

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? DownloadTimeout);

        try
        {
            Directory.CreateDirectory(folder);

            using var response = await client.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Cleanup(DownloadFailure.HttpError,
                    $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase} for {entry.Url}");
            }

            if (response.Content.Headers.ContentLength is long announced && announced != entry.SizeBytes)
            {
                return Cleanup(DownloadFailure.SizeMismatch,
                    $"GitHub says the file is {announced} bytes, but latest.json says {entry.SizeBytes}.");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;

            // The streams are closed at the end of this block, before anything is checked or deleted.
            await using (var source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false))
            await using (var target = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                long lastReport = -1;
                while ((read = await source.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
                {
                    received += read;
                    if (received > entry.SizeBytes) break; // too big: stop reading, reported below

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), deadline.Token).ConfigureAwait(false);

                    // Report about every 256 KB, not on every chunk.
                    if (received - lastReport >= 256 * 1024 || received == entry.SizeBytes)
                    {
                        lastReport = received;
                        progress?.Report(new DownloadProgress(received, entry.SizeBytes));
                    }
                }
            }

            if (received > entry.SizeBytes)
            {
                return Cleanup(DownloadFailure.SizeMismatch,
                    $"The download is larger than the {entry.SizeBytes} bytes latest.json says, so it was stopped.");
            }
            if (received != entry.SizeBytes)
            {
                return Cleanup(DownloadFailure.SizeMismatch,
                    $"Received {received} bytes, but latest.json says the file is {entry.SizeBytes} bytes.");
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Cleanup(DownloadFailure.HashMismatch,
                    $"SHA-256 of the download is {actual}, but latest.json says {entry.Sha256}.");
            }

            return new DownloadResult(zipPath, DownloadFailure.None, $"SHA-256 matches latest.json ({actual}).");
        }
        catch (OperationCanceledException)
        {
            return Cleanup(DownloadFailure.TimedOut, "The download took too long and was stopped.");
        }
        catch (HttpRequestException ex)
        {
            return Cleanup(DownloadFailure.Network, ex.Message);
        }
        catch (IOException ex) when (ex is HttpIOException || ex.InnerException is System.Net.Sockets.SocketException)
        {
            return Cleanup(DownloadFailure.Network, ex.Message);
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

        static DownloadResult Fail(DownloadFailure failure, string detail) => new(null, failure, detail);
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
