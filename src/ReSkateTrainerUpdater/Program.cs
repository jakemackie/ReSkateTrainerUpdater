using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // The fonts must be loaded before any window (and so any control) is created.
        GameFonts.Load();

        // The new copy is started with --updated after an app update, so it can say so.
        Application.Run(new UpdaterForm(justUpdated: args.Contains("--updated", StringComparer.OrdinalIgnoreCase)));
    }
}
