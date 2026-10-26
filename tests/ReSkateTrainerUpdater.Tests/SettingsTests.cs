using ReSkateTrainerUpdater.Core;
using static ReSkateTrainerUpdater.Tests.TestKit;

namespace ReSkateTrainerUpdater.Tests;

internal static class SettingsTests
{
    public static void Run(Workspace workspace)
    {
        Section("Remembered settings");

        var path = Path.Combine(workspace.Root, "settings", "settings.json");

        var defaults = UpdaterSettings.Load(path);
        Check("Defaults are used when there is no file", defaults is { LastFolder: null, BackupReplacedFiles: true });

        defaults.LastFolder = @"C:\Games\ReSkate";
        defaults.BackupReplacedFiles = false;
        defaults.Save();

        var reloaded = UpdaterSettings.Load(path);
        Check("Saved settings come back", reloaded is { LastFolder: @"C:\Games\ReSkate", BackupReplacedFiles: false });

        File.WriteAllText(path, "{ this is not json");
        Check("A corrupt file falls back to defaults instead of crashing", UpdaterSettings.Load(path) is { LastFolder: null, BackupReplacedFiles: true });
    }
}
