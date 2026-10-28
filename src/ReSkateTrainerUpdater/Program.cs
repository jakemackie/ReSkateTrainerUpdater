using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // The fonts must be loaded before any window (and so any control) is created.
        GameFonts.Load();

        // The build script runs this on the exe it just made. See SelfTest.
        int selfTest = Array.FindIndex(args, a => a.Equals(SelfTest.Flag, StringComparison.OrdinalIgnoreCase));
        if (selfTest >= 0) return SelfTest.Run(selfTest + 1 < args.Length ? args[selfTest + 1] : null);

        // The new copy is started with --updated after an app update, so it can say so.
        Application.Run(new UpdaterForm(justUpdated: args.Contains("--updated", StringComparer.OrdinalIgnoreCase)));
        return 0;
    }
}
