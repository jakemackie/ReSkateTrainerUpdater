using System.IO.Compression;

namespace ReSkateTrainerUpdater.Core;

/// <summary>Unpacks a downloaded Trainer zip and checks it looks like a real ReSkate Trainer release.</summary>
public static class TrainerPackageLoader
{
    /// <summary>Prefix of the temporary folders this app creates.</summary>
    public const string TempPrefix = "ReSkateTrainerUpdater_";

    private const int MaxUnwrapDepth = 8;

    /// <summary>
    /// Unpacks the zip, including a zip inside the zip or a single wrapper folder, into a temporary folder
    /// and validates the result. Problems are reported on the returned package rather than thrown.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a valid zip.</exception>
    public static TrainerPackage Load(string zipPath)
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), TempPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        try
        {
            // ExtractToDirectory rejects entries that try to escape the folder (zip-slip).
            ZipFile.ExtractToDirectory(zipPath, tempFolder);

            var notes = new List<string>();
            var rootFolder = FindRootFolder(tempFolder, notes);

            var package = new TrainerPackage(zipPath, tempFolder, rootFolder, new FileInfo(zipPath).Length);
            foreach (var note in notes) package.AddNote(note);
            foreach (var file in Directory.EnumerateFiles(rootFolder, "*", SearchOption.AllDirectories))
                package.AddFile(Path.GetRelativePath(rootFolder, file));

            Validate(package);
            return package;
        }
        catch
        {
            TryDeleteFolder(tempFolder);
            throw;
        }
    }

    /// <summary>Deletes temporary folders left behind by an earlier run that was closed or crashed mid-way.</summary>
    public static void CleanUpStaleTempFolders()
    {
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(Path.GetTempPath(), TempPrefix + "*"))
            {
                if (Directory.GetLastWriteTimeUtc(folder) < DateTime.UtcNow.AddHours(-2))
                    TryDeleteFolder(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    // Finds the folder that holds ReSkate.dll:
    //   zip -> zip -> files          (a zip whose content is another zip)
    //   zip -> folder -> files       (a single wrapper folder)
    //   or any mix of those, a few levels deep.
    private static string FindRootFolder(string tempFolder, List<string> notes)
    {
        var current = tempFolder;

        for (int depth = 0; depth < MaxUnwrapDepth; depth++)
        {
            if (File.Exists(Path.Combine(current, ReSkateFiles.RuntimeDll))) return current;

            var zips = Directory.GetFiles(current, "*.zip");
            if (zips.Length == 1)
            {
                var innerFolder = Path.Combine(tempFolder, "inner_" + depth);
                Directory.CreateDirectory(innerFolder);
                notes.Add($"Found a zip inside the zip (\"{Path.GetFileName(zips[0])}\"), unpacked it too.");
                ZipFile.ExtractToDirectory(zips[0], innerFolder);
                current = innerFolder;
                continue;
            }

            var folders = Directory.GetDirectories(current);
            if (folders.Length == 1 && Directory.GetFiles(current).Length == 0)
            {
                notes.Add($"Skipped the top-level folder \"{Path.GetFileName(folders[0])}\".");
                current = folders[0];
                continue;
            }

            // Last resort: the nearest folder that contains ReSkate.dll, if there is one.
            var found = Directory.EnumerateFiles(current, ReSkateFiles.RuntimeDll, SearchOption.AllDirectories)
                                 .OrderBy(p => p.Length)
                                 .FirstOrDefault();
            return found is null ? current : Path.GetDirectoryName(found)!;
        }

        return current;
    }

    private static void Validate(TrainerPackage package)
    {
        var dllPath = Path.Combine(package.RootFolder, ReSkateFiles.RuntimeDll);
        var launcherPath = Path.Combine(package.RootFolder, ReSkateFiles.Launcher);
        bool hasDll = File.Exists(dllPath);
        bool hasLauncher = File.Exists(launcherPath);

        if (!hasDll && !hasLauncher)
            package.AddError($"This zip doesn't look like a ReSkate Trainer update: it has no {ReSkateFiles.RuntimeDll} or {ReSkateFiles.Launcher}.");
        else if (!hasDll)
            package.AddError($"{ReSkateFiles.RuntimeDll} is missing from this zip, so it can't be installed.");
        else if (!hasLauncher)
            package.AddError($"{ReSkateFiles.Launcher} is missing from this zip, so it can't be installed.");
        if (!package.IsValid) return;

        if (!ReSkateScanner.IsX64WindowsProgram(dllPath))
            package.AddError($"{ReSkateFiles.RuntimeDll} in this zip isn't a 64-bit Windows program. The download may be damaged.");
        if (!ReSkateScanner.IsX64WindowsProgram(launcherPath))
            package.AddError($"{ReSkateFiles.Launcher} in this zip isn't a 64-bit Windows program. The download may be damaged.");
        if (!package.IsValid) return;

        var runtime = ReSkateScanner.Read(dllPath);
        var launcher = ReSkateScanner.Read(launcherPath);
        package.Runtime = runtime;
        package.Launcher = launcher;

        if (!IsReSkate(runtime))
            package.AddError($"{ReSkateFiles.RuntimeDll} in this zip doesn't identify as ReSkate, so it may be the wrong download.");
        if (!IsReSkate(launcher))
            package.AddError($"{ReSkateFiles.Launcher} in this zip doesn't identify as ReSkate, so it may be the wrong download.");
        if (!package.IsValid) return;

        if (!runtime.HasTrainer)
            package.AddWarning("This zip doesn't contain a ReSkate Trainer version, so it may be plain ReSkate rather than the Trainer.");

        if (runtime.BaseVersion is not null && launcher.BaseVersion is not null && runtime.BaseVersion != launcher.BaseVersion)
            package.AddWarning($"{ReSkateFiles.RuntimeDll} ({runtime.BaseVersion}) and {ReSkateFiles.Launcher} ({launcher.BaseVersion}) are from different ReSkate versions.");
        else if (runtime.HasTrainer && launcher.HasTrainer && runtime.TrainerVersion != launcher.TrainerVersion)
            package.AddWarning($"{ReSkateFiles.RuntimeDll} ({runtime.TrainerVersion}) and {ReSkateFiles.Launcher} ({launcher.TrainerVersion}) are different Trainer versions.");
    }

    private static bool IsReSkate(ReSkateVersion version) =>
        string.Equals(version.ProductName, ReSkateFiles.ProductName, StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
