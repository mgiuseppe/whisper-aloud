namespace WhisperAloud.Core.Cleanup;

/// <summary>
/// Ripulisce il testo dettato chiamando la CLI claude in modalità headless, quindi con l'abbonamento
/// Claude Code già autenticato sulla macchina e senza chiavi API.
/// </summary>
/// <remarks>
/// Avviare la CLI costa qualche secondo. Per non pagarlo a ogni dettatura il cleaner avvia un processo
/// "di riserva" in stream-json quando gli si chiede di prepararsi (<see cref="Prepare"/>, alla pressione della
/// scorciatoia): la CLI si avvia mentre si parla. Alla riscrittura gli manda il testo, prende la prima risposta,
/// lo congeda e ne avvia un altro, che resta pronto per qualche minuto e poi si chiude se non serve.
/// Ogni processo serve una sola dettatura, quindi il contesto non si accumula.
/// </remarks>
public sealed class ClaudeCleaner : IAsyncDisposable
{
    private readonly ClaudeOptions _options;
    private readonly string _executable;
    private readonly IReadOnlyList<string> _arguments;
    private readonly Lock _lock = new();
    private readonly Timer? _expiryTimer;
    private ClaudeProcess? _spare;
    private long _lastUseTicks = Environment.TickCount64;
    private bool _disposed;

    public ClaudeCleaner(ClaudeOptions options)
    {
        _options = options;
        _executable = ExecutableResolver.Resolve(options.Executable);
        _arguments = BuildArguments(options);

        // Nessun processo all'avvio: parte solo quando serve (Prepare o la prima riscrittura).
        if (options.UseWarmProcess)
        {
            _expiryTimer = new Timer(_ => ExpireIdleSpare(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>
    /// Avvia il processo di riserva se non c'è, ad esempio appena si preme la scorciatoia di Claude:
    /// l'avvio della CLI (qualche secondo) si sovrappone al tempo in cui si parla.
    /// </summary>
    public void Prepare()
    {
        if (!_options.UseWarmProcess) return;
        lock (_lock)
        {
            if (_disposed) return;
            Interlocked.Exchange(ref _lastUseTicks, Environment.TickCount64);
            if (_spare is { HasExited: false }) return;
            _spare?.Retire();
            _spare = StartProcess();
        }
    }

    /// <summary>Vero se c'è un processo di riserva acceso (per il log e per i test).</summary>
    public bool HasSpare
    {
        get
        {
            lock (_lock) return _spare is { HasExited: false };
        }
    }

    public string Executable => _executable;

    /// <summary>Per il log: come è stata servita l'ultima riscrittura (processo di riserva e sua età, oppure avvio a freddo).</summary>
    public string LastProcessInfo { get; private set; } = string.Empty;

    /// <summary>Argomenti della CLI: niente strumenti, niente personalizzazioni utente, niente sessioni salvate.</summary>
    public static IReadOnlyList<string> BuildArguments(ClaudeOptions options) =>
    [
        "-p",
        "--model", options.Model,
        "--system-prompt", CleanupPrompt.EffectiveSystem(options.SystemPrompt),
        "--tools", "",
        "--safe-mode",
        "--strict-mcp-config",
        "--no-session-persistence",
        "--input-format", "stream-json",
        "--output-format", "stream-json",
        "--verbose",
    ];

    /// <summary>Correzione leggera, il prompt di base.</summary>
    public Task<string> CleanAsync(string transcript, CancellationToken cancellationToken = default) =>
        CleanAsync(transcript, CleanupPrompt.Correction, cancellationToken);

    /// <param name="instructions">Cosa fare del testo: l'istruzione di un <see cref="PromptPreset"/>.</param>
    public async Task<string> CleanAsync(string transcript, string instructions, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(transcript)) return transcript;

        var process = TakeProcess();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            var response = await process.AskAsync(CleanupPrompt.Build(instructions, transcript), timeout.Token).ConfigureAwait(false);
            return CleanupPrompt.Unwrap(response);
        }
        finally
        {
            process.Retire();
        }
    }

    private ClaudeProcess TakeProcess()
    {
        ClaudeProcess? process;
        lock (_lock)
        {
            process = _spare;
            _spare = null;
        }

        // La riserva successiva si avvia in background, per non sommare il suo avvio a questa dettatura;
        // resta pronta per KeepWarmMinutes, poi si chiude se nel frattempo non si riscrive più.
        Interlocked.Exchange(ref _lastUseTicks, Environment.TickCount64);
        if (_options.UseWarmProcess) _ = Task.Run(ReplenishSpare);

        if (process is null || process.HasExited)
        {
            LastProcessInfo = process is null ? "avvio a freddo (nessuna riserva)" : "avvio a freddo (riserva terminata)";
            process?.Retire();
            process = StartProcess();
        }
        else
        {
            LastProcessInfo = $"riserva avviata da {process.Age.TotalSeconds:0.0} s";
        }
        return process;
    }

    private void ReplenishSpare()
    {
        lock (_lock)
        {
            if (!_disposed && _spare is null) _spare = StartProcess();
        }
    }

    /// <summary>Chiude la riserva dopo KeepWarmMinutes senza riscritture: da ferma non deve occupare memoria.</summary>
    private void ExpireIdleSpare()
    {
        lock (_lock)
        {
            if (_disposed || _spare is null) return;
            var idle = TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastUseTicks));
            if (_spare.HasExited || idle > TimeSpan.FromMinutes(Math.Max(1, _options.KeepWarmMinutes)))
            {
                _spare.Retire();
                _spare = null;
            }
        }
    }

    private ClaudeProcess StartProcess() => ClaudeProcess.Start(_executable, _arguments, AppPaths.ClaudeWorkDir);

    public async ValueTask DisposeAsync()
    {
        ClaudeProcess? spare;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            spare = _spare;
            _spare = null;
        }

        if (_expiryTimer is not null) await _expiryTimer.DisposeAsync().ConfigureAwait(false);
        spare?.Kill();
    }
}
