using Dettatura.Core;

namespace Dettatura.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\Dettatura.App", out var isFirstInstance);
        if (!isFirstInstance) return;

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => AppLog.Write(e.Exception);

        DettaturaSettings settings;
        try
        {
            settings = DettaturaSettings.LoadOrCreate();
        }
        catch (Exception e)
        {
            MessageBox.Show($"Impostazioni non valide in {AppPaths.SettingsFile}:\n{e.Message}", "Dettatura",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new TrayApp(settings));
    }
}
