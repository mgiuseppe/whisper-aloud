using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhisperAloud.Core;

/// <summary>Impostazioni utente, salvate in %APPDATA%\WhisperAloud\settings.json.</summary>
public sealed class WhisperAloudSettings
{
    /// <summary>Tenere premuta la scorciatoia mentre si parla, oppure premerla una volta per iniziare e una per finire.</summary>
    public DictationMode Mode { get; set; } = DictationMode.PushToTalk;

    /// <summary>Tieni premuto per dettare; al rilascio incolla la trascrizione di Whisper così com'è.</summary>
    public string HotkeyDictate { get; set; } = "Ctrl+Alt+D";

    /// <summary>Tieni premuto per dettare; al rilascio il testo viene ripulito da Claude e poi incollato.</summary>
    public string HotkeyDictateWithClaude { get; set; } = "Ctrl+Alt+Shift+D";

    /// <summary>Ripulisce con Claude il testo selezionato nella finestra attiva e lo sostituisce.</summary>
    public string HotkeyCleanSelection { get; set; } = "Ctrl+Alt+R";

    public string Language { get; set; } = "it";

    /// <summary>File del modello ggml; se è un nome semplice viene cercato in %LOCALAPPDATA%\WhisperAloud\models.</summary>
    public string WhisperModel { get; set; } = "ggml-large-v3-turbo-q5_0.bin";

    /// <summary>Contesto per Whisper: aiuta punteggiatura e parole ricorrenti (nomi, termini tecnici).</summary>
    public string WhisperPrompt { get; set; } = "Trascrizione di un messaggio dettato in italiano, con punteggiatura.";

    /// <summary>GPU Vulkan per Whisper (indice come lo elenca Vulkan); -1 sceglie da sola la prima GPU dedicata.</summary>
    public int GpuDevice { get; set; } = -1;

    /// <summary>Mostra in primo piano la finestrella di stato (registrazione, trascrizione, testo incollato).</summary>
    public bool ShowOverlay { get; set; } = true;

    /// <summary>Posizione della finestrella scelta trascinandola (angolo in alto a sinistra); vuota = in basso al centro.</summary>
    public int? OverlayLeft { get; set; }

    public int? OverlayTop { get; set; }

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
        // Il file si modifica a mano: il "+" delle scorciatoie resta leggibile invece di diventare una sequenza \u.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ResolveModelPath() =>
        Path.IsPathRooted(WhisperModel) ? WhisperModel : Path.Combine(AppPaths.ModelsDir, WhisperModel);

    /// <summary>Copia indipendente, da modificare nella finestra Impostazioni senza toccare quella in uso.</summary>
    public WhisperAloudSettings Clone() =>
        JsonSerializer.Deserialize<WhisperAloudSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    public void Save(string? path = null) =>
        File.WriteAllText(path ?? AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>Legge le impostazioni; al primo avvio scrive il file con i valori di default.</summary>
    public static WhisperAloudSettings LoadOrCreate(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        if (File.Exists(path))
        {
            return JsonSerializer.Deserialize<WhisperAloudSettings>(File.ReadAllText(path), JsonOptions)
                   ?? new WhisperAloudSettings();
        }

        var settings = new WhisperAloudSettings();
        settings.Save(path);
        return settings;
    }
}

public enum DictationMode
{
    /// <summary>Si registra finché la scorciatoia resta premuta.</summary>
    PushToTalk,

    /// <summary>Una pressione avvia la registrazione, la successiva la termina.</summary>
    Toggle,
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

    /// <summary>Prompt tra cui scegliere: nelle impostazioni il predefinito, dalla finestrella uno al volo.</summary>
    public List<PromptPreset> Prompts { get; set; } = DefaultPrompts();

    /// <summary>Nome del prompt predefinito, usato da "detta e riscrivi" e da "riscrivi la selezione".</summary>
    public string SelectedPrompt { get; set; } = "Correzione";

    /// <summary>
    /// Prompt di sistema alternativo (solo da file, per esperti); vuoto usa <see cref="Cleanup.CleanupPrompt.System"/>.
    /// Deve spiegare a claude i tag &lt;istruzione&gt; e &lt;dettato&gt; del messaggio.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Il prompt con quel nome; se non c'è, il predefinito o il primo della lista.</summary>
    public PromptPreset FindPrompt(string? name) =>
        Prompts.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Prompts.FirstOrDefault(p => string.Equals(p.Name, SelectedPrompt, StringComparison.OrdinalIgnoreCase))
        ?? Prompts.FirstOrDefault()
        ?? new PromptPreset { Name = "Correzione", Instructions = Cleanup.CleanupPrompt.Correction };

    public static List<PromptPreset> DefaultPrompts() =>
    [
        new() { Name = "Correzione", Instructions = Cleanup.CleanupPrompt.Correction },
        new()
        {
            Name = "Minuta",
            Instructions = "Fai una minuta della registrazione, predisponi un riassunto breve, una lista di argomenti, una o più cta. " +
                           "Devi essere super conciso e non fare domande all'utente perché il risultato verrà mostrato in una casella di sola lettura.",
            Paste = false,
        },
        new() { Name = "Traduzione in inglese", Instructions = "Traduci in inglese la registrazione." },
        new() { Name = "Pirata ubriaco", Instructions = "Riscrivi la registrazione come la direbbe un pirata ubriaco." },
    ];
}

/// <summary>Un prompt per claude: cosa fare del testo dettato.</summary>
public sealed class PromptPreset
{
    public string Name { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    /// <summary>Incolla il risultato nella finestra attiva; se falso resta nella finestrella, da copiare.</summary>
    public bool Paste { get; set; } = true;
}
