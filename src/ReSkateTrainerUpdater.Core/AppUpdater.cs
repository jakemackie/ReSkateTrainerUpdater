using System.IO.Compression;

namespace ReSkateTrainerUpdater.Core;

/// <summary>Why replacing the app with a new version didn't happen. Every failure leaves the current exe untouched.</summary>
public enum AppUpdateFailure
{
    None,

    /// <summary>The folder the app runs from isn't writable, for example C:\Program Files.</summary>
    NotWritable,

    /// <summary>The downloaded zip isn't a genuine build of this app at the published version.</summary>
    BadPackage,

    /// <summary>Swapping the files failed. The current exe was put back.</summary>
    SwapFailed,
}

/// <summary>The outcome of <see cref="AppUpdater.Stage"/>.</summary>
/// <param name="ExePath">The new exe, saved next to the current one and fully checked, when <see cref="Failure"/> is None.</param>
public sealed record StagedUpdate(string? ExePath, AppUpdateFailure Failure, string? Detail)
{
    public bool Succeeded => Failure == AppUpdateFailure.None && ExePath is not null;
}

/// <summary>
/// Replaces the running exe with a newer one. A running exe can be renamed but not overwritten, so the swap is:
/// save the new exe as ReSkateTrainerUpdater.exe.new, rename the running exe to .old, rename .new into its place, and
/// start the new one. The new copy deletes the .old file when it starts. Nothing here talks to the network: the zip has
/// already been downloaded and checked against latest.json by <see cref="TrainerDownloader"/>.
/// </summary>
public static class AppUpdater
{
    public const string ExeName = "ReSkateTrainerUpdater.exe";
    public const string ProductName = "ReSkate Trainer Updater";
    public const string StagedSuffix = ".new";
    public const string OldSuffix = ".old";

    private const long MaxExeBytes = 400L * 1024 * 1024;

    /// <summary>
    /// True if the running program is an exe that can be swapped. False when it is run through `dotnet`, for example
    /// by a developer, where replacing the host would be wrong.
    /// </summary>
    public static bool CanUpdateInPlace(string? processPath) =>
        !string.IsNullOrEmpty(processPath)
        && processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase)
        && File.Exists(processPath);

    /// <summary>
    /// Takes ReSkateTrainerUpdater.exe out of the downloaded zip and saves it as <paramref name="currentExePath"/> plus
    /// ".new". It is only handed back if it is a 64-bit Windows program that says it is this app, at
    /// <paramref name="expectedVersion"/>. Anything else is deleted. Never throws.
    /// </summary>
    public static StagedUpdate Stage(string zipPath, string currentExePath, string expectedVersion)
    {
        string staged = currentExePath + StagedSuffix;

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            // Only an entry at the root of the zip with exactly this name. Nothing else in the zip is read.
            var entry = archive.Entries.FirstOrDefault(e => e.FullName == ExeName);
            if (entry is null) return Bad($"The zip has no {ExeName}.");
            if (entry.Length <= 0 || entry.Length > MaxExeBytes) return Bad($"{ExeName} in the zip has an impossible size ({entry.Length} bytes).");

            try
            {
                if (File.Exists(staged)) File.Delete(staged);
                using var source = entry.Open();
                using var target = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyTo(target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(staged);
                return new StagedUpdate(null, AppUpdateFailure.NotWritable, ex.Message);
            }
        }
        catch (InvalidDataException)
        {
            return Bad("The download isn't a valid zip.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Bad("The download couldn't be read: " + ex.Message);
        }

        if (!ReSkateScanner.IsX64WindowsProgram(staged)) return Reject($"{ExeName} in the zip isn't a 64-bit Windows program.");

        FileVersionDetails details;
        try { details = ReSkateScanner.FileVersionReader(staged); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return Reject("The new exe couldn't be read: " + ex.Message);
        }

        if (!string.Equals(details.ProductName, ProductName, StringComparison.Ordinal))
            return Reject($"{ExeName} in the zip doesn't identify as {ProductName}.");

        // The version has a "+commit" tail (1.2.0+abc123), which isn't part of the version number.
        string? version = details.ProductVersion?.Split('+')[0];
        if (!string.Equals(version, expectedVersion, StringComparison.Ordinal))
            return Reject($"{ExeName} in the zip is version {version ?? "unknown"}, but latest.json says {expectedVersion}.");

        return new StagedUpdate(staged, AppUpdateFailure.None, null);

        StagedUpdate Bad(string detail) => new(null, AppUpdateFailure.BadPackage, detail);

        StagedUpdate Reject(string detail)
        {
            TryDelete(staged);
            return Bad(detail);
        }
    }

    /// <summary>
    /// Renames the current exe to .old and the staged exe into its place. If the second rename fails, the first is
    /// undone, so there is always a working exe at <paramref name="currentExePath"/>.
    /// </summary>
    public static (AppUpdateFailure Failure, string? Detail) Swap(string currentExePath, string stagedExePath)
    {
        string old = currentExePath + OldSuffix;

        try
        {
            if (!File.Exists(stagedExePath)) return (AppUpdateFailure.SwapFailed, "The new version isn't there any more.");
            if (File.Exists(old)) File.Delete(old);
            File.Move(currentExePath, old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (AppUpdateFailure.SwapFailed, ex.Message);
        }

        try
        {
            File.Move(stagedExePath, currentExePath);
            return (AppUpdateFailure.None, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Move(old, currentExePath); }
            catch (Exception undoFailure) when (undoFailure is IOException or UnauthorizedAccessException) { }
            return (AppUpdateFailure.SwapFailed, ex.Message);
        }
    }

    /// <summary>Deletes a staged exe that won't be used. Best effort.</summary>
    public static void Discard(string stagedExePath) => TryDelete(stagedExePath);

    /// <summary>
    /// Deletes the .old and .new files an update leaves beside the exe. The previous version may still be shutting down
    /// when the new one starts, so a locked file is retried a few times. Best effort, blocking: run it on a worker thread.
    /// </summary>
    public static void CleanUpLeftovers(string exePath, int attempts = 10, TimeSpan? delay = null)
    {
        var pause = delay ?? TimeSpan.FromMilliseconds(500);
        foreach (var leftover in new[] { exePath + OldSuffix, exePath + StagedSuffix })
        {
            for (int attempt = 0; attempt < attempts && File.Exists(leftover); attempt++)
            {
                if (TryDelete(leftover)) break;
                Thread.Sleep(pause);
            }
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
