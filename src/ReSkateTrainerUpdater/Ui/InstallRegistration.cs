using Microsoft.Win32;

namespace ReSkateTrainerUpdater.Ui;

/// <summary>
/// The Add/Remove Programs entry that the setup creates. The app updates itself by swapping its exe, which the setup
/// doesn't know about, so after an update the entry would still show the old version. This keeps it right.
/// It only touches the entry when the running copy really is the installed one, never for a portable copy elsewhere.
/// </summary>
internal static class InstallRegistration
{
    private static string KeyPath => $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppInfo.InstallerAppId}_is1";

    /// <summary>Makes the entry show <paramref name="version"/>. Does nothing if the app wasn't installed by the setup. Never throws.</summary>
    public static void SyncVersion(string version)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is null) return;

            // The setup records where it installed to. Only update the entry for the copy that lives there.
            var location = key.GetValue("InstallLocation") as string;
            var running = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrEmpty(location) || string.IsNullOrEmpty(running)) return;
            if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)),
                               Path.TrimEndingDirectorySeparator(Path.GetFullPath(running)), StringComparison.OrdinalIgnoreCase))
                return;

            if (key.GetValue("DisplayVersion") as string != version) key.SetValue("DisplayVersion", version);

            // The setup writes the name as "<app name> <version>" (its AppVerName), so that carries the version too.
            var name = $"{AppInfo.Name} {version}";
            if (key.GetValue("DisplayName") as string != name) key.SetValue("DisplayName", name);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            // The entry is cosmetic. A failure here must never affect the app.
        }
    }
}
