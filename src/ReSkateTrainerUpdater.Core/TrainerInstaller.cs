using System.Security.Cryptography;

namespace ReSkateTrainerUpdater.Core;

/// <summary>Copies the Trainer files into a ReSkate folder, safely.</summary>
public static class TrainerInstaller
{
    private const string BackupFolderPrefix = "_ReSkateTrainerUpdater_backup_";
    private const string RescueFolderPrefix = "ReSkateTrainerRescue_";
    private const string PartialFileSuffix = ".updating";

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
            else if (FilesAreIdentical(source, target))
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
        string? backupFolder = backup
            ? Path.Combine(destinationFolder, BackupFolderPrefix + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"))
            : null;
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
            return new InstallResult(InstallOutcome.Installed, added, replaced, unchanged, Array.Empty<string>(),
                BackupFolder: backupFolder);
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
