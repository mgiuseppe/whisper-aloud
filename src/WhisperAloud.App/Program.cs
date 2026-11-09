using WhisperAloud.Core;

namespace WhisperAloud.App;

internal static class Program
{
    /// <summary>Passato dall'istanza che si riavvia: si aspetta che chiuda invece di uscire subito.</summary>
    public const string RestartArgument = "--restart";

    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new Mutex(initiallyOwned: false, @"Local\WhisperAloud.App");
        bool owned;
        try
        {
            owned = singleInstance.WaitOne(args.Contains(RestartArgument) ? TimeSpan.FromSeconds(15) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // l'istanza precedente è uscita senza rilasciarlo
        }
        if (!owned) return;

        ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // modalità scura di WinForms: ancora sperimentale, ma copre elenchi a discesa e campi numerici
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
        Application.ThreadException += (_, e) => AppLog.Write(e.Exception);

        WhisperAloudSettings settings;
        try
        {
            settings = WhisperAloudSettings.LoadOrCreate();
            Loc.Use(settings.UiLanguage);
        }
        catch (Exception e)
        {
            MessageBox.Show(Loc.T($"Impostazioni non valide in {AppPaths.SettingsFile}:\n{e.Message}", $"Invalid settings in {AppPaths.SettingsFile}:\n{e.Message}"), "Whisper Aloud",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new TrayApp(settings));
    }
}
