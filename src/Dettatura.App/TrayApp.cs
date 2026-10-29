using System.Diagnostics;
using Dettatura.Core;
using Dettatura.Core.Cleanup;
using Dettatura.Core.Transcription;

namespace Dettatura.App;

/// <summary>App nel system tray: scorciatoie push-to-talk, registrazione, trascrizione, pulizia e incolla.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string Title = "Dettatura";

    private readonly DettaturaSettings _settings;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly HotkeyWindow _hotkeys = new();
    private readonly MicRecorder _mic;
    private readonly System.Windows.Forms.Timer _releaseTimer = new() { Interval = 15 };
    private readonly Icon _idleIcon = TrayIcons.Circle(Color.FromArgb(120, 120, 120));
    private readonly Icon _recordingIcon = TrayIcons.Circle(Color.FromArgb(220, 40, 40));
    private readonly Icon _workingIcon = TrayIcons.Circle(Color.FromArgb(235, 160, 20));

    private ClaudeCleaner? _cleaner;
    private WhisperTranscriber? _transcriber;
    private Hotkey? _heldHotkey;
    private bool _heldClean;
    private readonly Stopwatch _recording = new();
    private bool _busy;

    public TrayApp(DettaturaSettings settings)
    {
        _settings = settings;
        _mic = new MicRecorder(settings.MicrophoneDevice);

        _statusItem = new ToolStripMenuItem("Avvio...") { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Impostazioni", null, (_, _) => OpenFile(AppPaths.SettingsFile));
        menu.Items.Add("Registro latenze", null, (_, _) => OpenFile(AppPaths.LatencyLog));
        menu.Items.Add("Log errori", null, (_, _) => OpenFile(AppPaths.AppLog));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Esci", null, (_, _) => ExitThread());

        _tray = new NotifyIcon { Icon = _workingIcon, Text = Title, ContextMenuStrip = menu, Visible = true };
        _releaseTimer.Tick += OnReleaseTick;

        RegisterHotkeys();
        StartCleaner();
        _ = LoadTranscriberAsync();
    }

    private void RegisterHotkeys()
    {
        // Ogni scorciatoia si registra per conto suo: se una è già occupata, le altre funzionano lo stesso.
        var errors = new List<string>();
        Register(_settings.HotkeyDictate, hotkey => () => StartDictation(hotkey, cleanup: false));
        Register(_settings.HotkeyDictateWithClaude, hotkey => () => StartDictation(hotkey, cleanup: true));
        Register(_settings.HotkeyCleanSelection, ignored => () => _ = CleanSelectionAsync());
        if (errors.Count > 0) Report(new InvalidOperationException(string.Join(Environment.NewLine, errors)));

        void Register(string text, Func<Hotkey, Action> handler)
        {
            try
            {
                var hotkey = Hotkey.Parse(text);
                _hotkeys.Register(hotkey, handler(hotkey));
            }
            catch (Exception e) when (e is FormatException or InvalidOperationException)
            {
                errors.Add(e.Message);
            }
        }
    }

    private void StartCleaner()
    {
        try
        {
            _cleaner = new ClaudeCleaner(_settings.Claude);
        }
        catch (Exception e)
        {
            Report(new InvalidOperationException($"Pulizia con Claude non disponibile, resta la sola trascrizione. {e.Message}", e));
        }
    }

    private async Task LoadTranscriberAsync()
    {
        var progress = new Progress<string>(SetStatus);
        try
        {
            _transcriber = await Task.Run(() => WhisperTranscriber.CreateAsync(
                _settings.ResolveModelPath(), _settings.Language, _settings.WhisperPrompt, progress));
            SetIdle();
            _tray.ShowBalloonTip(3000, Title,
                $"Pronta ({WhisperTranscriber.LoadedRuntime}). Tieni premuto {_settings.HotkeyDictate} per dettare.",
                ToolTipIcon.Info);
        }
        catch (Exception e)
        {
            SetStatus("Whisper non disponibile");
            Report(e);
        }
    }

    private void StartDictation(Hotkey hotkey, bool cleanup)
    {
        if (_busy || _mic.IsRecording) return;
        if (_transcriber is null)
        {
            _tray.ShowBalloonTip(2000, Title, "Il modello Whisper si sta ancora caricando.", ToolTipIcon.Warning);
            return;
        }

        try
        {
            _mic.Start();
        }
        catch (Exception e)
        {
            Report(new InvalidOperationException($"Microfono non disponibile: {e.Message}", e));
            return;
        }

        _heldHotkey = hotkey;
        _heldClean = cleanup && _cleaner is not null;
        _recording.Restart();
        _tray.Icon = _recordingIcon;
        SetStatus(cleanup ? "In ascolto (con riscrittura Claude)" : "In ascolto");
        _releaseTimer.Start();
    }

    /// <summary>RegisterHotKey segnala solo la pressione: il rilascio si rileva controllando il tasto.</summary>
    private void OnReleaseTick(object? sender, EventArgs e)
    {
        if (_heldHotkey is null || Native.IsKeyDown((int)_heldHotkey.Key)) return;
        _releaseTimer.Stop();
        _heldHotkey = null;
        _ = FinishDictationAsync(_heldClean);
    }

    private async Task FinishDictationAsync(bool cleanup)
    {
        var samples = _mic.Stop();
        _recording.Stop();
        if (_recording.ElapsedMilliseconds < _settings.MinRecordingMs || _transcriber is null)
        {
            SetIdle();
            return;
        }

        _busy = true;
        _tray.Icon = _workingIcon;
        var total = Stopwatch.StartNew();
        long transcribeMs = 0, cleanMs = 0, pasteMs = 0;
        try
        {
            SetStatus("Trascrizione...");
            var step = Stopwatch.StartNew();
            var text = await Task.Run(() => _transcriber.TranscribeAsync(samples));
            transcribeMs = step.ElapsedMilliseconds;
            if (string.IsNullOrWhiteSpace(text)) return;

            if (cleanup && _cleaner is not null)
            {
                SetStatus("Pulizia con Claude...");
                step.Restart();
                try
                {
                    text = await _cleaner.CleanAsync(text);
                }
                catch (Exception e) when (e is ClaudeCliException or OperationCanceledException or IOException)
                {
                    Report(new InvalidOperationException($"Pulizia non riuscita, incollo la trascrizione grezza. {e.Message}", e));
                }
                cleanMs = step.ElapsedMilliseconds;
            }

            step.Restart();
            await TextInjector.PasteAsync(_settings.AppendSpace ? text + " " : text, _settings.RestoreClipboard);
            pasteMs = step.ElapsedMilliseconds;

            LatencyLog.Append(new LatencySample(DateTimeOffset.Now, cleanup ? "clean" : "raw",
                samples.Length / (double)WhisperTranscriber.SampleRate, transcribeMs, cleanMs, pasteMs,
                total.ElapsedMilliseconds, text.Length));
        }
        catch (Exception e)
        {
            Report(e);
        }
        finally
        {
            _busy = false;
            SetIdle();
        }
    }

    private async Task CleanSelectionAsync()
    {
        if (_busy || _mic.IsRecording || _cleaner is null) return;

        _busy = true;
        _tray.Icon = _workingIcon;
        var total = Stopwatch.StartNew();
        try
        {
            var selection = await TextInjector.CopySelectionAsync();
            if (string.IsNullOrWhiteSpace(selection))
            {
                _tray.ShowBalloonTip(2000, Title, "Nessun testo selezionato.", ToolTipIcon.Info);
                return;
            }

            SetStatus("Pulizia della selezione...");
            var step = Stopwatch.StartNew();
            var cleaned = await _cleaner.CleanAsync(selection);
            var cleanMs = step.ElapsedMilliseconds;

            step.Restart();
            await TextInjector.PasteAsync(cleaned, _settings.RestoreClipboard);
            LatencyLog.Append(new LatencySample(DateTimeOffset.Now, "selection", 0, 0, cleanMs,
                step.ElapsedMilliseconds, total.ElapsedMilliseconds, cleaned.Length));
        }
        catch (Exception e)
        {
            Report(e);
        }
        finally
        {
            _busy = false;
            SetIdle();
        }
    }

    private void SetIdle()
    {
        _tray.Icon = _idleIcon;
        SetStatus(_transcriber is null ? "Whisper non pronto" : "Pronta");
    }

    private void SetStatus(string status)
    {
        _statusItem.Text = status;
        var tip = $"{Title}: {status}";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    private void Report(Exception e)
    {
        AppLog.Write(e);
        _tray.ShowBalloonTip(5000, Title, e.Message, ToolTipIcon.Error);
    }

    private static void OpenFile(string path)
    {
        if (!File.Exists(path)) File.WriteAllText(path, string.Empty);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    protected override void ExitThreadCore()
    {
        _releaseTimer.Dispose();
        _hotkeys.Dispose();
        _mic.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _cleaner?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _transcriber?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.ExitThreadCore();
    }
}
