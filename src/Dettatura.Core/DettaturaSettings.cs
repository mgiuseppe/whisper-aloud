using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dettatura.Core;

/// <summary>Impostazioni utente, salvate in %APPDATA%\Dettatura\settings.json.</summary>
public sealed class DettaturaSettings
{
    /// <summary>Tieni premuto per dettare; al rilascio incolla la trascrizione di Whisper così com'è.</summary>
    public string HotkeyDictate { get; set; } = "Ctrl+Alt+D";

    /// <summary>Tieni premuto per dettare; al rilascio il testo viene ripulito da Claude e poi incollato.</summary>
    public string HotkeyDictateWithClaude { get; set; } = "Ctrl+Alt+Shift+D";

    /// <summary>Ripulisce con Claude il testo selezionato nella finestra attiva e lo sostituisce.</summary>
    public string HotkeyCleanSelection { get; set; } = "Ctrl+Alt+R";

    public string Language { get; set; } = "it";

    /// <summary>File del modello ggml; se è un nome semplice viene cercato in %LOCALAPPDATA%\Dettatura\models.</summary>
    public string WhisperModel { get; set; } = "ggml-large-v3-turbo-q5_0.bin";

    /// <summary>Contesto per Whisper: aiuta punteggiatura e parole ricorrenti (nomi, termini tecnici).</summary>
    public string WhisperPrompt { get; set; } = "Trascrizione di un messaggio dettato in italiano, con punteggiatura.";

    /// <summary>Indice del microfono per WaveIn; -1 è il dispositivo predefinito di Windows.</summary>
    public int MicrophoneDevice { get; set; } = -1;

    public ClaudeOptions Claude { get; set; } = new();

    /// <summary>Aggiunge uno spazio dopo il testo incollato, così le dettature consecutive non si attaccano.</summary>
    public bool AppendSpace { get; set; } = true;

    /// <summary>Ripristina gli appunti com'erano prima dell'incolla.</summary>
    public bool RestoreClipboard { get; set; } = true;

    /// <summary>Le pressioni più brevi di così sono considerate accidentali.</summary>
    public int MinRecordingMs { get; set; } = 300;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Il file si modifica a mano: "Ctrl+Alt+D" resta leggibile invece di diventare "Ctrl+Alt+D".
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ResolveModelPath() =>
        Path.IsPathRooted(WhisperModel) ? WhisperModel : Path.Combine(AppPaths.ModelsDir, WhisperModel);

    /// <summary>Legge le impostazioni; al primo avvio scrive il file con i valori di default.</summary>
    public static DettaturaSettings LoadOrCreate(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        if (File.Exists(path))
        {
            return JsonSerializer.Deserialize<DettaturaSettings>(File.ReadAllText(path), JsonOptions)
                   ?? new DettaturaSettings();
        }

        var settings = new DettaturaSettings();
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
        return settings;
    }
}

public sealed class ClaudeOptions
{
    /// <summary>Percorso della CLI; "claude" la cerca nel PATH e in %USERPROFILE%\.local\bin.</summary>
    public string Executable { get; set; } = "claude";

    /// <summary>Sonnet: nei test è il più fedele al testo dettato; haiku è più veloce ma a volte riformula.</summary>
    public string Model { get; set; } = "sonnet";

    /// <summary>
    /// Tiene un processo claude già avviato in attesa della prossima dettatura,
    /// così il costo di avvio della CLI (diversi secondi) non pesa sulla latenza.
    /// </summary>
    public bool UseWarmProcess { get; set; } = true;

    /// <summary>Dopo questo tempo il processo di riserva viene sostituito da uno nuovo.</summary>
    public int WarmProcessMaxAgeMinutes { get; set; } = 20;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Prompt di sistema alternativo; vuoto usa <see cref="Cleanup.CleanupPrompt.Default"/>.</summary>
    public string? SystemPrompt { get; set; }
}
