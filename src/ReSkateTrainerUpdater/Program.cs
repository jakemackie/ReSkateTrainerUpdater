using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new UpdaterForm());
    }
}
