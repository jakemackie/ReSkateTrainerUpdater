using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // The fonts must be loaded before any window (and so any control) is created.
        GameFonts.Load();

        Application.Run(new UpdaterForm());
    }
}
