namespace ReSkateTrainerUpdater.Core;

/// <summary>Looks at the folder the user picked and works out what, if anything, is installed there.</summary>
public static class InstallFolderInspector
{
    private const string SteamSkateFolderName = "skate.";

    public static InstallFolderInfo Inspect(string folder)
    {
        bool isSteamFolder = IsSteamSkateFolder(folder);
        bool hasSpecialCharacters = folder.Any(c => c > 127);

        if (!Directory.Exists(folder))
            return new InstallFolderInfo(folder, false, false, false, false, null, isSteamFolder, hasSpecialCharacters);

        bool isEmpty;
        try { isEmpty = !Directory.EnumerateFileSystemEntries(folder).Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { isEmpty = false; }

        var dllPath = Path.Combine(folder, ReSkateFiles.RuntimeDll);
        bool hasRuntime = File.Exists(dllPath);
        bool hasLauncher = File.Exists(Path.Combine(folder, ReSkateFiles.Launcher));

        ReSkateVersion? installed = null;
        if (hasRuntime)
        {
            try { installed = ReSkateScanner.Read(dllPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { installed = null; }
        }

        return new InstallFolderInfo(folder, true, isEmpty, hasLauncher, hasRuntime, installed, isSteamFolder, hasSpecialCharacters,
            TrainerInstaller.ListBackups(folder).FirstOrDefault());
    }

    /// <summary>The launcher refuses to run from Steam's own "skate." game folder.</summary>
    public static bool IsSteamSkateFolder(string folder)
    {
        var parts = folder.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => string.Equals(p, SteamSkateFolderName, StringComparison.OrdinalIgnoreCase));
    }
}
