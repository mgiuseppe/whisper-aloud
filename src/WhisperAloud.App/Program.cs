using WhisperAloud.Core;

namespace WhisperAloud.App;

internal static class Program
{
    /// <summary>Passato dall'istanza che si riavvia: si aspetta che chiuda invece di uscire subito.</summary>
    public const string RestartArgument = "--restart";

    /// <summary>Passato dalla chiave Run di Windows: all'accesso l'app parte in silenzio, senza avviso.</summary>
    public const string AutostartArgument = "--autostart";

    /// <summary>Segnale con cui una seconda istanza chiede a quella attiva di aprire le impostazioni.</summary>
    private const string ShowEventName = @"Local\WhisperAloud.Show";

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
        if (!owned)
        {
            // Già attiva (magari non si vede, è nel tray): invece di non fare niente, apre le sue impostazioni.
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var running))
            {
                using (running) running.Set();
            }
            return;
        }
        using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

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

        Application.Run(new TrayApp(settings, showRequest, quiet: args.Contains(AutostartArgument)));
    }
}
