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
    private MicRecorder _mic;
    private readonly System.Windows.Forms.Timer _releaseTimer = new() { Interval = 15 };
    private readonly Icon _idleIcon = TrayIcons.Circle(Color.FromArgb(120, 120, 120));
    private readonly Icon _recordingIcon = TrayIcons.Circle(Color.FromArgb(220, 40, 40));
    private readonly Icon _workingIcon = TrayIcons.Circle(Color.FromArgb(235, 160, 20));

    private readonly OverlayForm? _overlay;
    private readonly ToolStripMenuItem _copyLastItem;
    private string? _lastText;
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
        _overlay = settings.ShowOverlay ? new OverlayForm() : null;
        _copyLastItem = new ToolStripMenuItem("Copia ultima dettatura", null, (_, _) => CopyLast()) { Enabled = false };

        _statusItem = new ToolStripMenuItem("Avvio...") { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_copyLastItem);
        menu.Items.Add(BuildMicrophoneMenu());
        menu.Items.Add("Impostazioni", null, (_, _) => OpenFile(AppPaths.SettingsFile));
        menu.Items.Add("Registro latenze", null, (_, _) => OpenFile(AppPaths.LatencyLog));
        menu.Items.Add("Log errori", null, (_, _) => OpenFile(AppPaths.AppLog));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Esci", null, (_, _) => ExitThread());

        _tray = new NotifyIcon { Icon = _workingIcon, Text = Title, ContextMenuStrip = menu, Visible = true };
        _releaseTimer.Tick += OnReleaseTick;

        AppLog.Info($"avvio, microfono: {MicRecorder.DeviceName(settings.MicrophoneDevice)}");
        RegisterHotkeys();
        StartCleaner();
        _ = LoadTranscriberAsync();
    }

    /// <summary>Sottomenu per scegliere il microfono; la scelta viene salvata nelle impostazioni.</summary>
    private ToolStripMenuItem BuildMicrophoneMenu()
    {
        var root = new ToolStripMenuItem("Microfono");
        root.DropDownOpening += (_, _) =>
        {
            root.DropDownItems.Clear();
            var devices = new List<(int Index, string Name)> { (-1, "Predefinito di Windows") };
            devices.AddRange(MicRecorder.ListDevices());
            foreach (var (index, name) in devices)
            {
                var item = new ToolStripMenuItem(name) { Checked = index == _settings.MicrophoneDevice };
                item.Click += (_, _) => SelectMicrophone(index);
                root.DropDownItems.Add(item);
            }
        };
        root.DropDownItems.Add("..."); // segnaposto: senza voci il sottomenu non si apre
        return root;
    }

    private void SelectMicrophone(int index)
    {
        if (_mic.IsRecording) return;
        _mic.Dispose();
        _mic = new MicRecorder(index);
        _settings.MicrophoneDevice = index;
        _settings.Save();
        AppLog.Info($"microfono scelto: {MicRecorder.DeviceName(index)}");
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
                _settings.ResolveModelPath(), _settings.Language, _settings.WhisperPrompt, progress,
                _settings.GpuDevice, message => AppLog.Info($"whisper: {message}")));
            SetIdle();
            _tray.ShowBalloonTip(3000, Title,
                $"Pronta ({WhisperTranscriber.LoadedRuntime}). Tieni premuto {_settings.HotkeyDictate} per dettare.",
                ToolTipIcon.Info);
        }
        catch (HttpRequestException e)
        {
            SetStatus("Modello Whisper non scaricato");
            Report(new InvalidOperationException(
                $"Download del modello Whisper non riuscito (rete). Riavvia l'app per riprovare, oppure scarica " +
                $"ggml-large-v3-turbo-q5_0.bin da huggingface.co/ggerganov/whisper.cpp in {AppPaths.ModelsDir}.", e));
        }
        catch (Exception e)
        {
            SetStatus("Whisper non disponibile");
            Report(e);
        }
    }

    private void StartDictation(Hotkey hotkey, bool cleanup)
    {
        AppLog.Info($"{hotkey.Text} premuta (occupata: {_busy}, in registrazione: {_mic.IsRecording}, whisper pronto: {_transcriber is not null})");
        if (_busy || _mic.IsRecording) return;
        if (_transcriber is null)
        {
            Notify("Non ancora pronta", "Il modello Whisper si sta ancora caricando: riprova tra qualche secondo.");
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
        _overlay?.ShowRecording(cleanup ? "In ascolto · poi riscrive Claude" : "In ascolto",
            $"rilascia {hotkey.Text}", () => _mic.Level);
        _releaseTimer.Start();
    }

    /// <summary>RegisterHotKey segnala solo la pressione: il rilascio si rileva controllando il tasto.</summary>
    private void OnReleaseTick(object? sender, EventArgs e)
    {
        if (_heldHotkey is null || Native.IsKeyDown((int)_heldHotkey.Key)) return;
        _releaseTimer.Stop();
        var hotkey = _heldHotkey.Text;
        _heldHotkey = null;
        _ = FinishDictationAsync(_heldClean, hotkey);
    }

    private async Task FinishDictationAsync(bool cleanup, string hotkey)
    {
        var samples = _mic.Stop();
        _recording.Stop();
        var rms = WhisperTranscriber.Rms(samples);
        AppLog.Info($"rilasciata dopo {_recording.ElapsedMilliseconds} ms, {samples.Length} campioni, livello {rms:0.0000}");
        if (_recording.ElapsedMilliseconds < _settings.MinRecordingMs || _transcriber is null)
        {
            Notify("Pressione troppo breve", $"Tieni premuto {hotkey} mentre parli e rilascia quando hai finito.");
            SetIdle();
            return;
        }
        if (rms < WhisperTranscriber.SilenceRms)
        {
            Notify("Nessun suono dal microfono",
                $"Il microfono \"{MicRecorder.DeviceName(_settings.MicrophoneDevice)}\" non manda audio. " +
                "Scegline un altro dal menu Microfono dell'icona nel tray.");
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
            _overlay?.ShowWorking("Trascrizione…");
            var step = Stopwatch.StartNew();
            var text = await Task.Run(() => _transcriber.TranscribeAsync(samples));
            transcribeMs = step.ElapsedMilliseconds;
            AppLog.Info($"trascrizione in {transcribeMs} ms: {text.Length} caratteri");
            if (string.IsNullOrWhiteSpace(text))
            {
                Notify("Nessun testo riconosciuto", "Whisper non ha riconosciuto parole: riprova parlando più vicino al microfono.");
                return;
            }

            if (cleanup && _cleaner is not null)
            {
                SetStatus("Pulizia con Claude...");
                _overlay?.ShowWorking("Riscrittura con Claude…");
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
            Remember(text);
            _overlay?.ShowResult("Incollato", $"{total.Elapsed.TotalSeconds:0.0} s", text);

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
                Notify("Nessun testo selezionato", "Seleziona il testo da sistemare e poi premi la scorciatoia.");
                return;
            }

            SetStatus("Pulizia della selezione...");
            _overlay?.ShowWorking("Riscrittura della selezione…");
            var step = Stopwatch.StartNew();
            var cleaned = await _cleaner.CleanAsync(selection);
            var cleanMs = step.ElapsedMilliseconds;

            step.Restart();
            await TextInjector.PasteAsync(cleaned, _settings.RestoreClipboard);
            Remember(cleaned);
            _overlay?.ShowResult("Selezione riscritta", $"{total.Elapsed.TotalSeconds:0.0} s", cleaned);
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

    private void Remember(string text)
    {
        _lastText = text;
        _copyLastItem.Enabled = true;
    }

    private void CopyLast()
    {
        if (_lastText is null) return;
        Clipboard.SetText(_lastText);
        _overlay?.ShowResult("Ultima dettatura copiata", string.Empty, _lastText);
    }

    /// <summary>Avviso all'utente: nella finestrella se è attiva, altrimenti nel fumetto del tray.</summary>
    private void Notify(string title, string message)
    {
        if (_overlay is not null) _overlay.ShowMessage(title, message);
        else _tray.ShowBalloonTip(4000, Title, $"{title}. {message}", ToolTipIcon.Warning);
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
        if (_overlay is not null) _overlay.ShowMessage("Errore", e.Message);
        else _tray.ShowBalloonTip(5000, Title, e.Message, ToolTipIcon.Error);
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
        _overlay?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _cleaner?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _transcriber?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.ExitThreadCore();
    }
}
