namespace ReSkateTrainerUpdater.Core;

/// <summary>
/// Looks in the places people usually keep ReSkate for a folder holding ReSkateLauncher.exe or ReSkate.dll, so a first-time
/// user doesn't have to find it themselves. It only reads folder names and file names; it never opens a file.
/// </summary>
public static class ReSkateLocator
{
    /// <summary>A place to look, and how many folder levels below it to look.</summary>
    public sealed record Root(string Path, int Depth);

    // Folders that are never a ReSkate install, and are slow or pointless to walk.
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "$Recycle.Bin", "System Volume Information", "ProgramData", "AppData", "node_modules", ".git",
    };

    /// <summary>Each fixed drive and its Games folder, then Documents, Desktop and Downloads (where an unpacked zip would be).</summary>
    public static IReadOnlyList<Root> DefaultRoots()
    {
        var roots = new List<Root>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                roots.Add(new Root(Path.Combine(drive.RootDirectory.FullName, "Games"), 3));
                roots.Add(new Root(drive.RootDirectory.FullName, 2));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No drive list: the user's own folders below still work.
        }

        foreach (var folder in new[] { Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Desktop })
        {
            var path = Environment.GetFolderPath(folder);
            if (path.Length > 0) roots.Add(new Root(path, 3));
        }
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0) roots.Add(new Root(Path.Combine(profile, "Downloads"), 3));

        return roots;
    }

    /// <summary>
    /// The folder that looks most like a ReSkate install: one with both the launcher and ReSkate.dll beats one with a single
    /// file, and an earlier place in <paramref name="roots"/> beats a later one. Steam's own skate. folder is never offered,
    /// because ReSkate can't run from it. Null if nothing was found.
    /// </summary>
    public static string? Find(IEnumerable<Root> roots)
    {
        string? best = null;
        int bestScore = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            foreach (var folder in Walk(root.Path, root.Depth))
            {
                if (!seen.Add(folder)) continue;

                int score = (File.Exists(Path.Combine(folder, ReSkateFiles.Launcher)) ? 1 : 0)
                          + (File.Exists(Path.Combine(folder, ReSkateFiles.RuntimeDll)) ? 1 : 0);
                if (score == 0 || InstallFolderInspector.IsSteamSkateFolder(folder)) continue;

                if (score == 2) return folder; // can't beat that, and earlier places win
                if (score > bestScore)
                {
                    best = folder;
                    bestScore = score;
                }
            }
        }

        return best;
    }

    // The folder itself, then its subfolders down to the given depth (0 is the folder only).
    private static IEnumerable<string> Walk(string path, int depth)
    {
        if (!Directory.Exists(path)) yield break;
        yield return path;
        if (depth <= 0) yield break;

        foreach (var child in SubFolders(path))
        {
            var name = Path.GetFileName(child);
            if (Skipped.Contains(name) || name.StartsWith("_ReSkateTrainerUpdater_backup_", StringComparison.Ordinal)) continue;
            foreach (var found in Walk(child, depth - 1)) yield return found;
        }
    }

    private static List<string> SubFolders(string path)
    {
        try { return Directory.EnumerateDirectories(path).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new List<string>(); }
    }
}
