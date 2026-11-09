using WhisperAloud.Core;

namespace WhisperAloud.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\WhisperAloud.App", out var isFirstInstance);
        if (!isFirstInstance) return;

        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // modalità scura di WinForms: ancora sperimentale, ma copre elenchi a discesa e campi numerici
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
        Application.ThreadException += (_, e) => AppLog.Write(e.Exception);

        WhisperAloudSettings settings;
        try
        {
            settings = WhisperAloudSettings.LoadOrCreate();
        }
        catch (Exception e)
        {
            MessageBox.Show($"Impostazioni non valide in {AppPaths.SettingsFile}:\n{e.Message}", "Whisper Aloud",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new TrayApp(settings));
    }
}
