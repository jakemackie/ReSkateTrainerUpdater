using System.Diagnostics;

namespace ReSkateTrainerUpdater.Core;

public enum LaunchFailure
{
    None,

    /// <summary>There is no ReSkateLauncher.exe in the folder.</summary>
    NotFound,

    /// <summary>Windows refused to start it.</summary>
    CouldNotStart,
}

/// <summary>Starts the ReSkate launcher from the chosen folder, as double-clicking it would.</summary>
public static class GameLauncher
{
    public static LaunchFailure Start(string folder, out string? detail)
    {
        detail = null;
        var exe = Path.Combine(folder, ReSkateFiles.Launcher);
        if (!File.Exists(exe)) return LaunchFailure.NotFound;

        try
        {
            // The shell starts it, so the launcher asks for administrator rights itself if it wants them.
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = folder })?.Dispose();
            return LaunchFailure.None;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            detail = ex.Message;
            return LaunchFailure.CouldNotStart;
        }
    }
}
