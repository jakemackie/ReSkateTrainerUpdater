using System.Diagnostics;

namespace ReSkateTrainerUpdater.Core;

/// <summary>A program that is running and has files of the ReSkate folder open.</summary>
/// <param name="Id">The process id.</param>
/// <param name="Name">What to call it, for example ReSkateLauncher.exe.</param>
public sealed record RunningProcess(int Id, string Name);

/// <summary>Finds, and on request closes, the programs that stop an install because they are using the ReSkate folder's files.</summary>
public static class GameProcesses
{
    /// <summary>
    /// The programs running from <paramref name="folder"/>, or with a file from it loaded (such as the game with ReSkate.dll
    /// inside it). This updater is never included unless <paramref name="ignoreProcessId"/> says otherwise. Slow enough to
    /// belong on a worker thread, so it is only used once an install has been blocked.
    /// </summary>
    public static IReadOnlyList<RunningProcess> FindUsing(string folder, int? ignoreProcessId = null)
    {
        // GetFullPath turns short (8.3) names such as JAKEMA~1 into the long ones. Windows can report a running program under
        // either spelling, so the folder and every program's path are put the same way before they are compared.
        string root = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        bool InFolder(string path) => LongForm(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        int ignore = ignoreProcessId ?? Environment.ProcessId;
        var found = new Dictionary<int, RunningProcess>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == ignore) continue;

                    string? exe = process.MainModule?.FileName;
                    bool uses = exe is not null && InFolder(exe);
                    if (!uses)
                    {
                        foreach (ProcessModule module in process.Modules)
                        {
                            if (InFolder(module.FileName))
                            {
                                uses = true;
                                break;
                            }
                        }
                    }

                    if (uses) found[process.Id] = new RunningProcess(process.Id, exe is null ? process.ProcessName : Path.GetFileName(exe));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Protected or already gone: not something that could be using the game's files for this user.
                }
            }
        }

        return found.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Only paths with a ~ can be short names, so only those are looked up.
    private static string LongForm(string path)
    {
        if (!path.Contains('~')) return path;
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    /// <summary>
    /// Asks each program to close like a person clicking its close button, waits up to <paramref name="grace"/> for it, and
    /// ends it if it still hasn't. Returns the ones that are still running afterwards. Only call this after the user said yes.
    /// </summary>
    public static IReadOnlyList<RunningProcess> Close(IEnumerable<RunningProcess> processes, TimeSpan grace)
    {
        var stillRunning = new List<RunningProcess>();
        foreach (var target in processes)
        {
            try
            {
                using var process = Process.GetProcessById(target.Id);
                // The id could have been given to a different program since it was found.
                if (!string.Equals(Path.ChangeExtension(process.ProcessName, null), Path.ChangeExtension(target.Name, null), StringComparison.OrdinalIgnoreCase))
                    continue;

                process.CloseMainWindow();
                if (!process.WaitForExit(grace))
                {
                    process.Kill();
                    process.WaitForExit(TimeSpan.FromSeconds(3));
                }
                if (!process.HasExited) stillRunning.Add(target);
            }
            catch (ArgumentException)
            {
                // Already closed.
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                stillRunning.Add(target);
            }
        }
        return stillRunning;
    }
}
