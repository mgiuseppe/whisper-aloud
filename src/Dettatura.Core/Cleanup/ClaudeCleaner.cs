namespace Dettatura.Core.Cleanup;

/// <summary>
/// Ripulisce il testo dettato chiamando la CLI claude in modalità headless, quindi con l'abbonamento
/// Claude Code già autenticato sulla macchina e senza chiavi API.
/// </summary>
/// <remarks>
/// Avviare la CLI costa qualche secondo. Per non pagarlo a ogni dettatura il cleaner tiene un processo
/// "di riserva" già avviato in stream-json: alla dettatura gli manda il testo, prende la prima risposta,
/// lo congeda e ne avvia subito un altro. Ogni processo serve una sola dettatura, quindi il contesto
/// non si accumula e una dettatura non influenza la successiva.
/// </remarks>
public sealed class ClaudeCleaner : IAsyncDisposable
{
    private readonly ClaudeOptions _options;
    private readonly string _executable;
    private readonly IReadOnlyList<string> _arguments;
    private readonly Lock _lock = new();
    private readonly Timer? _recycleTimer;
    private ClaudeProcess? _spare;
    private bool _disposed;

    public ClaudeCleaner(ClaudeOptions options)
    {
        _options = options;
        _executable = ExecutableResolver.Resolve(options.Executable);
        _arguments = BuildArguments(options);

        if (options.UseWarmProcess)
        {
            _spare = StartProcess();
            _recycleTimer = new Timer(_ => RecycleStaleSpare(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }
    }

    public string Executable => _executable;

    /// <summary>Argomenti della CLI: niente strumenti, niente personalizzazioni utente, niente sessioni salvate.</summary>
    public static IReadOnlyList<string> BuildArguments(ClaudeOptions options) =>
    [
        "-p",
        "--model", options.Model,
        "--system-prompt", string.IsNullOrWhiteSpace(options.SystemPrompt) ? CleanupPrompt.System : options.SystemPrompt,
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

        // La riserva successiva si avvia in background, per non sommare il suo avvio a questa dettatura.
        if (_options.UseWarmProcess) _ = Task.Run(ReplenishSpare);

        if (process is null || process.HasExited)
        {
            process?.Retire();
            process = StartProcess();
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

    private void RecycleStaleSpare()
    {
        lock (_lock)
        {
            if (_disposed || _spare is null) return;
            var maxAge = TimeSpan.FromMinutes(_options.WarmProcessMaxAgeMinutes);
            if (_spare.HasExited || _spare.Age > maxAge)
            {
                _spare.Retire();
                _spare = StartProcess();
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

        if (_recycleTimer is not null) await _recycleTimer.DisposeAsync().ConfigureAwait(false);
        spare?.Kill();
    }
}
