using System.Globalization;
using System.Security.Cryptography;

namespace ReSkateTrainerUpdater.Core;

/// <summary>A copy of the files an install replaced, kept inside the ReSkate folder.</summary>
/// <param name="Path">The backup folder.</param>
/// <param name="Created">When the install that made it ran.</param>
/// <param name="Version">What the backup's ReSkate.dll is, if the backup holds one that can be read.</param>
public sealed record BackupInfo(string Path, DateTime Created, ReSkateVersion? Version);

/// <summary>Copies the Trainer files into a ReSkate folder, safely.</summary>
public static class TrainerInstaller
{
    private const string BackupFolderPrefix = "_ReSkateTrainerUpdater_backup_";
    private const string BackupTimestampFormat = "yyyy-MM-dd_HH-mm-ss";
    private const string RescueFolderPrefix = "ReSkateTrainerRescue_";
    private const string PartialFileSuffix = ".updating";

    /// <summary>How many backups are kept. Older ones are deleted after a successful install, so they don't pile up.</summary>
    public const int BackupsToKeep = 3;

    /// <summary>
    /// The backups an earlier install left in this folder, newest first. Only folders with exactly the name this
    /// installer gives them, and at least one file inside, count.
    /// </summary>
    public static IReadOnlyList<BackupInfo> ListBackups(string destinationFolder)
    {
        var found = new List<BackupInfo>();
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(destinationFolder, BackupFolderPrefix + "*"))
            {
                var name = System.IO.Path.GetFileName(folder);
                if (!DateTime.TryParseExact(name[BackupFolderPrefix.Length..], BackupTimestampFormat,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var created))
                {
                    continue;
                }
                if (!Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any()) continue;

                ReSkateVersion? version = null;
                var dll = System.IO.Path.Combine(folder, ReSkateFiles.RuntimeDll);
                if (File.Exists(dll))
                {
                    try { version = ReSkateScanner.Read(dll); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { version = null; }
                }
                found.Add(new BackupInfo(folder, created, version));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder just has no backups to show.
        }

        return found.OrderByDescending(b => b.Created).ToList();
    }

    /// <summary>Deletes all but the newest <paramref name="keep"/> backups in this folder. Returns how many were deleted.</summary>
    public static int PruneBackups(string destinationFolder, int keep = BackupsToKeep)
    {
        int deleted = 0;
        foreach (var old in ListBackups(destinationFolder).Skip(Math.Max(0, keep)))
        {
            TryDeleteFolder(old.Path);
            if (!Directory.Exists(old.Path)) deleted++;
        }
        return deleted;
    }

    /// <summary>
    /// Puts the files of an earlier backup back, as an ordinary install: the same checks, the same rollback, and the files
    /// being replaced are backed up first, so a restore can itself be undone.
    /// </summary>
    public static InstallResult Restore(string destinationFolder, BackupInfo backup, IProgress<string>? progress = null) =>
        Install(backup.Path, destinationFolder, backup: true, progress);

    /// <summary>
    /// Copies every file under <paramref name="sourceFolder"/> into <paramref name="destinationFolder"/>,
    /// replacing files that differ. Before changing anything it checks nothing is locked. If anything fails
    /// part-way, the original files are restored, so an install is never left half-done.
    /// Meant to run on a worker thread; <paramref name="progress"/> receives one line per file.
    /// </summary>
    public static InstallResult Install(
        string sourceFolder,
        string destinationFolder,
        bool backup,
        IProgress<string>? progress = null)
    {
        // 1. Work out what each file needs.
        var toReplace = new List<string>();
        var toAdd = new List<string>();
        int unchanged = 0;

        var relativePaths = Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(sourceFolder, f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var relative in relativePaths)
        {
            var source = Path.Combine(sourceFolder, relative);
            var target = Path.Combine(destinationFolder, relative);

            if (!File.Exists(target))
            {
                toAdd.Add(relative);
            }
            else if (TryFilesAreIdentical(source, target))
            {
                unchanged++;
                progress?.Report($"Unchanged: {relative}");
            }
            else
            {
                toReplace.Add(relative);
            }
        }

        if (toReplace.Count == 0 && toAdd.Count == 0)
            return new InstallResult(InstallOutcome.AlreadyUpToDate, 0, 0, unchanged, Array.Empty<string>());

        // 2. Check up front that we can write, so we don't start and then fail.
        var blocked = CheckCanWrite(destinationFolder, toReplace, progress);
        if (blocked is not null)
        {
            return new InstallResult(InstallOutcome.Blocked, 0, 0, unchanged, blocked.Value.Problems,
                BlockedBy: blocked.Value.Reason);
        }

        // 3. Install, keeping a copy of every file we replace so it can be put back.
        string? backupFolder = backup ? NewBackupFolderPath(destinationFolder) : null;
        string rescueFolder = Path.Combine(Path.GetTempPath(), RescueFolderPrefix + Guid.NewGuid().ToString("N"));
        string savedCopiesFolder = backupFolder ?? rescueFolder;

        var savedCopies = new List<(string Target, string Copy)>();
        var createdFiles = new List<string>();
        string? partialFile = null;
        int added = 0, replaced = 0;

        try
        {
            foreach (var relative in toReplace)
            {
                var target = Path.Combine(destinationFolder, relative);
                var copy = Path.Combine(savedCopiesFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(target, copy, overwrite: true);
                savedCopies.Add((target, copy));
            }

            foreach (var relative in toReplace.Concat(toAdd))
            {
                var source = Path.Combine(sourceFolder, relative);
                var target = Path.Combine(destinationFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                bool existed = File.Exists(target);

                // Copy beside the target, then swap it in, so a file is never left half-written.
                partialFile = target + PartialFileSuffix;
                File.Copy(source, partialFile, overwrite: true);
                File.Move(partialFile, target, overwrite: true);
                partialFile = null;

                if (existed)
                {
                    replaced++;
                    progress?.Report($"Replaced: {relative}");
                }
                else
                {
                    createdFiles.Add(target);
                    added++;
                    progress?.Report($"Added: {relative}");
                }
            }

            TryDeleteFolder(rescueFolder);
            if (backup) PruneBackups(destinationFolder);
            // A backup folder only exists if some file was replaced. Don't point the user at one that isn't there.
            return new InstallResult(InstallOutcome.Installed, added, replaced, unchanged, Array.Empty<string>(),
                BackupFolder: replaced > 0 ? backupFolder : null);
        }
        catch (Exception ex)
        {
            // Any failure part-way must undo what was done, whatever kind of failure it is.
            progress?.Report($"Problem: {ex.Message}");
            progress?.Report("Putting your original files back...");

            if (partialFile is not null) TryDeleteFile(partialFile);

            if (RestoreOriginals(savedCopies, createdFiles, progress))
            {
                TryDeleteFolder(rescueFolder);
                if (backupFolder is not null) TryDeleteFolder(backupFolder);
                return new InstallResult(InstallOutcome.RolledBack, 0, 0, unchanged, new[] { ex.Message });
            }

            return new InstallResult(InstallOutcome.RestoreFailed, 0, 0, unchanged, new[] { ex.Message },
                BackupFolder: backupFolder, RescueFolder: backupFolder ?? rescueFolder);
        }
    }

    // Named by the second it was made. If that name is taken (a restore straight after an install), move on a second, so a
    // restore never writes its safety copies into the very backup it is restoring from.
    private static string NewBackupFolderPath(string destinationFolder)
    {
        var time = DateTime.Now;
        string path;
        do
        {
            path = Path.Combine(destinationFolder, BackupFolderPrefix + time.ToString(BackupTimestampFormat, CultureInfo.InvariantCulture));
            time = time.AddSeconds(1);
        }
        while (Directory.Exists(path));
        return path;
    }

    // Returns null when it's fine to go ahead.
    private static (BlockReason Reason, List<string> Problems)? CheckCanWrite(
        string destinationFolder,
        List<string> filesToReplace,
        IProgress<string>? progress)
    {
        try
        {
            var probe = Path.Combine(destinationFolder, ".write_test_" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { }
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (BlockReason.FolderNotWritable, new List<string> { "Can't write to this folder: " + ex.Message });
        }

        var inUse = new List<string>();
        foreach (var relative in filesToReplace)
        {
            var target = Path.Combine(destinationFolder, relative);
            try
            {
                // Opening with no sharing fails if the game or launcher has the file open.
                using (new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                inUse.Add($"{relative} is in use or protected");
                progress?.Report($"In use: {relative}");
            }
        }

        return inUse.Count > 0 ? (BlockReason.FilesInUse, inUse) : null;
    }

    private static bool RestoreOriginals(
        List<(string Target, string Copy)> savedCopies,
        List<string> createdFiles,
        IProgress<string>? progress)
    {
        bool restoredAll = true;

        foreach (var (target, copy) in savedCopies)
        {
            try
            {
                File.Copy(copy, target, overwrite: true);
            }
            catch (Exception ex)
            {
                restoredAll = false;
                progress?.Report($"Could not restore {target}: {ex.Message}");
            }
        }

        foreach (var file in createdFiles) TryDeleteFile(file);
        return restoredAll;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private static void TryDeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    // A file that can't even be read (the game has it locked) is not known to be identical, so it is treated as one that needs
    // replacing. The check that nothing is in use then reports it properly, instead of the install failing with a raw error.
    private static bool TryFilesAreIdentical(string pathA, string pathB)
    {
        try
        {
            return FilesAreIdentical(pathA, pathB);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool FilesAreIdentical(string pathA, string pathB)
    {
        var infoA = new FileInfo(pathA);
        var infoB = new FileInfo(pathB);
        if (infoA.Length != infoB.Length) return false;

        using var streamA = infoA.OpenRead();
        using var streamB = infoB.OpenRead();
        return SHA256.HashData(streamA).AsSpan().SequenceEqual(SHA256.HashData(streamB));
    }
}
