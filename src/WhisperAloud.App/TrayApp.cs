using System.Diagnostics;
using System.Text.Json;
using WhisperAloud.Core;
using WhisperAloud.Core.Cleanup;
using WhisperAloud.Core.Transcription;
using static WhisperAloud.Core.Loc;

namespace WhisperAloud.App;

/// <summary>App nel system tray: scorciatoie push-to-talk, registrazione, trascrizione, pulizia e incolla.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string Title = "Whisper Aloud";

    private WhisperAloudSettings _settings;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly HotkeyWindow _hotkeys = new();
    private MicRecorder _mic;
    private readonly System.Windows.Forms.Timer _releaseTimer = new() { Interval = 15 };
    private readonly Icon _idleIcon = TrayIcons.Circle(Color.FromArgb(110, 110, 124));
    private readonly Icon _recordingIcon = TrayIcons.Circle(Theme.Red);
    private readonly Icon _workingIcon = TrayIcons.Circle(Theme.Orange);

    private OverlayForm? _overlay;
    private SettingsForm? _settingsForm;
    private readonly ToolStripMenuItem _copyLastItem;
    private string? _lastText;
    private ClaudeCleaner? _cleaner;
    /// <summary>Perché Claude non è disponibile pur essendo attivo (CLI non trovata); null se va tutto bene o se è spento.</summary>
    private string? _claudeProblem;
    private ToolStripMenuItem _promptMenu = null!;
    private WhisperTranscriber? _transcriber;
    private Hotkey? _heldHotkey;
    /// <summary>Prompt di Claude per la dettatura in corso; null = solo trascrizione. Si cambia dalla finestrella.</summary>
    private string? _heldPrompt;
    private readonly Stopwatch _recording = new();
    private bool _busy;

    public TrayApp(WhisperAloudSettings settings)
    {
        _settings = settings;
        _mic = new MicRecorder(settings.MicrophoneDevice);
        _overlay = settings.ShowOverlay ? CreateOverlay() : null;
        _copyLastItem = new ToolStripMenuItem(T("Copia ultima dettatura", "Copy last dictation"), null, (_, _) => CopyLast()) { Enabled = false };

        _statusItem = new ToolStripMenuItem(T("Avvio...", "Starting...")) { Enabled = false };
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = Theme.Surface,
            ForeColor = Theme.Fore,
            Font = Theme.Body(9.5f),
            Padding = new Padding(0, Theme.S(4), 0, Theme.S(4)),
        };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_copyLastItem);
        menu.Items.Add(T("Trascrivi un file audio...", "Transcribe an audio file..."), null, (_, _) => TranscribeFile());
        menu.Items.Add(_promptMenu = BuildPromptMenu());
        _promptMenu.Visible = settings.Claude.Enabled;
        menu.Items.Add(BuildMicrophoneMenu());
        menu.Items.Add(T("Impostazioni...", "Settings..."), null, (_, _) => OpenSettings());
        menu.Items.Add(T("Registro latenze", "Latency log"), null, (_, _) => OpenFile(AppPaths.LatencyLog));
        menu.Items.Add(T("Log errori", "Diagnostic log"), null, (_, _) => OpenFile(AppPaths.AppLog));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("Esci", "Quit"), null, (_, _) => ExitThread());

        _tray = new NotifyIcon { Icon = _workingIcon, Text = Title, ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => OpenSettings();
        _releaseTimer.Tick += OnReleaseTick;

        AppLog.Info($"avvio, microfono: {MicRecorder.DeviceName(settings.MicrophoneDevice)}");
        RegisterHotkeys();
        if (settings.StartWithWindows) ApplyAutostart(true); // aggiorna il percorso se l'exe è stato spostato
        StartCleaner();
        _ = LoadTranscriberAsync();
        _ = Task.Run(DeleteOldRecordings);
    }

    /// <summary>Sottomenu per scegliere il microfono; la scelta viene salvata nelle impostazioni.</summary>
    private ToolStripMenuItem BuildMicrophoneMenu()
    {
        var root = new ToolStripMenuItem(T("Microfono", "Microphone"));
        root.DropDownOpening += (_, _) =>
        {
            root.DropDownItems.Clear();
            var devices = new List<(int Index, string Name)> { (-1, T("Predefinito di Windows", "Windows default")) };
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

    /// <summary>Sottomenu per scegliere il prompt predefinito di Claude; la scelta viene salvata.</summary>
    private ToolStripMenuItem BuildPromptMenu()
    {
        var root = new ToolStripMenuItem(T("Prompt di Claude", "Claude prompt"));
        root.DropDownOpening += (_, _) =>
        {
            root.DropDownItems.Clear();
            var selected = _settings.Claude.FindPrompt(null).Name;
            foreach (var prompt in _settings.Claude.Prompts)
            {
                var item = new ToolStripMenuItem(prompt.Name) { Checked = prompt.Name == selected };
                item.Click += (_, _) =>
                {
                    _settings.Claude.SelectedPrompt = prompt.Name;
                    _settings.Save();
                    AppLog.Info($"prompt predefinito: {prompt.Name}");
                };
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
        _hotkeys.UnregisterAll();
        // Ogni scorciatoia si registra per conto suo: se una è già occupata, le altre funzionano lo stesso.
        var errors = new List<string>();
        Register(_settings.HotkeyDictate, hotkey => () => StartDictation(hotkey, cleanup: false));
        // Con Claude spento le sue scorciatoie non si registrano: restano libere per le altre app.
        if (_settings.Claude.Enabled)
        {
            Register(_settings.HotkeyDictateWithClaude, hotkey => () => StartDictation(hotkey, cleanup: true));
            Register(_settings.HotkeyCleanSelection, ignored => () => _ = CleanSelectionAsync());
        }
        if (errors.Count > 0) Report(new InvalidOperationException(string.Join(Environment.NewLine, errors)));
        AppLog.Info($"scorciatoie attive: {_settings.HotkeyDictate}" +
                    (_settings.Claude.Enabled ? $", {_settings.HotkeyDictateWithClaude}, {_settings.HotkeyCleanSelection}" : " (Claude spento)") +
                    (errors.Count > 0 ? $" (errori: {errors.Count})" : string.Empty));

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
        _claudeProblem = null;
        if (!_settings.Claude.Enabled)
        {
            AppLog.Info("claude spento nelle impostazioni");
            return;
        }
        if (!string.IsNullOrWhiteSpace(_settings.Claude.SystemPrompt) && !CleanupPrompt.IsUsableSystem(_settings.Claude.SystemPrompt))
        {
            AppLog.Info("Claude.SystemPrompt ignorato: non spiega i tag <istruzione> e <dettato>, uso il prompt di sistema generale");
        }
        try
        {
            _cleaner = new ClaudeCleaner(_settings.Claude);
        }
        catch (FileNotFoundException e)
        {
            // Claude Code non installato: si trascrive e basta, senza errori all'avvio. Lo si dice se si usa una scorciatoia di Claude.
            _claudeProblem = e.Message;
            AppLog.Info($"claude non trovato: {e.Message}");
        }
        catch (Exception e)
        {
            Report(new InvalidOperationException(T($"Riscrittura con Claude non disponibile, resta la sola trascrizione. {e.Message}",
                $"Rewriting with Claude is not available, transcription still works. {e.Message}"), e));
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
                T($"Pronta ({WhisperTranscriber.LoadedRuntime}). Tieni premuto {_settings.HotkeyDictate} per dettare.",
                    $"Ready ({WhisperTranscriber.LoadedRuntime}). Hold {_settings.HotkeyDictate} to dictate."),
                ToolTipIcon.Info);
        }
        catch (HttpRequestException e)
        {
            SetStatus(T("Modello Whisper non scaricato", "Whisper model not downloaded"));
            Report(new InvalidOperationException(
                T($"Download del modello Whisper non riuscito (rete). Riavvia l'app per riprovare, oppure scarica " +
                  $"ggml-large-v3-turbo-q5_0.bin da huggingface.co/ggerganov/whisper.cpp in {AppPaths.ModelsDir}.",
                  $"Could not download the Whisper model (network). Restart the app to try again, or download " +
                  $"ggml-large-v3-turbo-q5_0.bin from huggingface.co/ggerganov/whisper.cpp into {AppPaths.ModelsDir}."), e));
        }
        catch (Exception e)
        {
            SetStatus(T("Whisper non disponibile", "Whisper not available"));
            Report(e);
        }
    }

    private void StartDictation(Hotkey hotkey, bool cleanup)
    {
        AppLog.Info($"{hotkey.Text} premuta (occupata: {_busy}, in registrazione: {_mic.IsRecording}, whisper pronto: {_transcriber is not null})");
        // Modalità avvio/stop: la seconda pressione (di una delle due scorciatoie di dettatura) termina.
        if (_mic.IsRecording && _settings.Mode == DictationMode.Toggle && _heldHotkey is not null)
        {
            var held = _heldHotkey.Text;
            _heldHotkey = null;
            _ = FinishDictationAsync(held);
            return;
        }
        if (_busy || _mic.IsRecording) return;
        if (cleanup && _cleaner is null)
        {
            NotifyClaudeUnavailable();
            return;
        }
        if (_transcriber is null)
        {
            Notify(T("Non ancora pronta", "Not ready yet"),
                T("Il modello Whisper si sta ancora caricando: riprova tra qualche secondo.", "The Whisper model is still loading: try again in a few seconds."));
            return;
        }

        try
        {
            _mic.Start();
        }
        catch (Exception e)
        {
            Report(new InvalidOperationException(T($"Microfono non disponibile: {e.Message}", $"Microphone not available: {e.Message}"), e));
            return;
        }

        _heldHotkey = hotkey;
        _heldPrompt = cleanup && _cleaner is not null ? _settings.Claude.FindPrompt(null).Name : null;
        // La CLI si avvia mentre si parla: al rilascio è già pronta (o quasi) per la riscrittura.
        if (_heldPrompt is not null) _cleaner?.Prepare();
        _recording.Restart();
        _tray.Icon = _recordingIcon;
        SetStatus(_heldPrompt is null ? T("In ascolto", "Listening") : T($"In ascolto (Claude: {_heldPrompt})", $"Listening (Claude: {_heldPrompt})"));
        var toggle = _settings.Mode == DictationMode.Toggle;
        _overlay?.ShowRecording(T("In ascolto", "Listening"),
            toggle ? T($"{hotkey.Text} per finire", $"{hotkey.Text} to stop") : T($"rilascia {hotkey.Text}", $"release {hotkey.Text}"), () => _mic.Level,
            // Il selettore dei prompt compare solo con la scorciatoia di Claude: con Detta e incolla non serve.
            _heldPrompt is null ? null : _settings.Claude.Prompts.Select(p => p.Name).ToList(), _heldPrompt,
            name =>
            {
                _heldPrompt = name;
                if (name is not null) _cleaner?.Prepare();
                AppLog.Info($"prompt scelto per questa dettatura: {name ?? "solo trascrizione"}");
            });
        if (!toggle) _releaseTimer.Start();
    }

    /// <summary>RegisterHotKey segnala solo la pressione: il rilascio si rileva controllando il tasto.</summary>
    private void OnReleaseTick(object? sender, EventArgs e)
    {
        if (_heldHotkey is null || Native.IsKeyDown((int)_heldHotkey.Key)) return;
        _releaseTimer.Stop();
        var hotkey = _heldHotkey.Text;
        _heldHotkey = null;
        _ = FinishDictationAsync(hotkey);
    }

    private async Task FinishDictationAsync(string hotkey)
    {
        var preset = _heldPrompt is null || _cleaner is null ? null : _settings.Claude.FindPrompt(_heldPrompt);
        var samples = _mic.Stop();
        _recording.Stop();
        var rms = WhisperTranscriber.Rms(samples);
        AppLog.Info($"rilasciata dopo {_recording.ElapsedMilliseconds} ms, {samples.Length} campioni, livello {rms:0.0000}");
        if (_transcriber is null)
        {
            SetIdle();
            return;
        }
        if (_settings.Mode == DictationMode.PushToTalk && _recording.ElapsedMilliseconds < _settings.MinRecordingMs)
        {
            Notify(T("Pressione troppo breve", "Press too short"),
                T($"Tieni premuto {hotkey} mentre parli e rilascia quando hai finito.", $"Hold {hotkey} while you speak and release it when you are done."));
            SetIdle();
            return;
        }
        if (rms < WhisperTranscriber.SilenceRms)
        {
            var device = MicRecorder.DeviceName(_settings.MicrophoneDevice);
            Notify(T("Nessun suono dal microfono", "No sound from the microphone"),
                T($"Il microfono \"{device}\" non manda audio. Scegline un altro dal menu Microfono dell'icona nel tray.",
                  $"The microphone \"{device}\" sends no audio. Pick another one from the Microphone menu of the tray icon."));
            SetIdle();
            return;
        }

        // Con Conserva le registrazioni si salva subito; altrimenti solo se la trascrizione non riesce.
        var recordedAt = DateTime.Now;
        var audioPath = _settings.KeepRecordings ? SaveRecording(samples, recordedAt) : null;

        _busy = true;
        _tray.Icon = _workingIcon;
        var total = Stopwatch.StartNew();
        long transcribeMs = 0, cleanMs = 0, pasteMs = 0;
        try
        {
            SetStatus(T("Trascrizione...", "Transcribing..."));
            _overlay?.ShowWorking(T("Trascrizione…", "Transcribing…"));
            var step = Stopwatch.StartNew();
            string text;
            try
            {
                text = await Task.Run(() => _transcriber.TranscribeAsync(samples));
            }
            catch (Exception e)
            {
                AppLog.Write(e);
                OfferRetry(samples, audioPath ?? SaveRecording(samples, recordedAt),
                    T("Trascrizione non riuscita", "Transcription failed"), e.Message);
                return;
            }
            transcribeMs = step.ElapsedMilliseconds;
            AppLog.Info($"trascrizione in {transcribeMs} ms: {text.Length} caratteri");
            if (string.IsNullOrWhiteSpace(text))
            {
                OfferRetry(samples, audioPath ?? SaveRecording(samples, recordedAt), T("Nessun testo riconosciuto", "No words recognised"),
                    T("Whisper non ha riconosciuto parole.", "Whisper recognised no words."));
                return;
            }

            var paste = true;
            string? rewriteError = null;
            if (preset is not null && _cleaner is not null)
            {
                SetStatus($"Claude: {preset.Name}...");
                _overlay?.ShowWorking($"Claude · {preset.Name}…");
                step.Restart();
                try
                {
                    text = await _cleaner.CleanAsync(text, preset.Instructions);
                    paste = preset.Paste;
                    AppLog.Info($"claude ({preset.Name}) in {step.ElapsedMilliseconds} ms: {text.Length} caratteri, {_cleaner.LastProcessInfo}");
                }
                catch (Exception e) when (e is ClaudeCliException or OperationCanceledException or IOException)
                {
                    // Si incolla comunque la trascrizione; il motivo si mostra dopo, al posto del risultato.
                    AppLog.Write(e);
                    rewriteError = e is OperationCanceledException
                        ? T($"Claude non ha risposto entro {_settings.Claude.TimeoutSeconds} secondi.", $"Claude did not answer within {_settings.Claude.TimeoutSeconds} seconds.")
                        : e.Message;
                }
                cleanMs = step.ElapsedMilliseconds;
            }

            step.Restart();
            if (paste)
            {
                await TextInjector.PasteAsync(_settings.AppendSpace ? text + " " : text, _settings.RestoreClipboard);
                if (rewriteError is null) _overlay?.ShowResult(T("Incollato", "Pasted"), $"{total.Elapsed.TotalSeconds:0.0} s", text);
                else Notify(T("Incollata la trascrizione senza Claude", "Pasted without Claude"), rewriteError);
            }
            else
            {
                // Prompt che non incolla (es. Minuta): il risultato resta nella finestrella finché non si chiude.
                _overlay?.ShowResult(T($"{preset!.Name} pronta", $"{preset!.Name} ready"), $"{total.Elapsed.TotalSeconds:0.0} s", text, sticky: true);
            }
            pasteMs = step.ElapsedMilliseconds;
            Remember(text);

            LatencyLog.Append(new LatencySample(DateTimeOffset.Now, preset is null ? "raw" : "clean",
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
        AppLog.Info($"{_settings.HotkeyCleanSelection} premuta (occupata: {_busy}, claude pronto: {_cleaner is not null})");
        if (_busy || _mic.IsRecording) return;
        if (_cleaner is null)
        {
            NotifyClaudeUnavailable();
            return;
        }

        _busy = true;
        _tray.Icon = _workingIcon;
        var total = Stopwatch.StartNew();
        try
        {
            var (selection, original) = await TextInjector.CopySelectionAsync();
            AppLog.Info($"selezione copiata: {selection?.Length ?? 0} caratteri");
            if (string.IsNullOrWhiteSpace(selection))
            {
                TextInjector.Restore(original);
                Notify(T("Nessun testo selezionato", "No text selected"),
                    T("Seleziona il testo da sistemare e poi premi la scorciatoia.", "Select the text to rewrite, then press the shortcut."));
                return;
            }

            var preset = _settings.Claude.FindPrompt(null);
            SetStatus(T($"Claude: {preset.Name} sulla selezione...", $"Claude: {preset.Name} on the selection..."));
            _overlay?.ShowWorking(T($"Claude · {preset.Name} sulla selezione…", $"Claude · {preset.Name} on the selection…"));
            var step = Stopwatch.StartNew();
            var cleaned = await _cleaner.CleanAsync(selection, preset.Instructions);
            var cleanMs = step.ElapsedMilliseconds;

            step.Restart();
            AppLog.Info($"selezione riscritta ({preset.Name}) in {cleanMs} ms: {cleaned.Length} caratteri");
            if (preset.Paste)
            {
                await TextInjector.PasteAsync(cleaned, _settings.RestoreClipboard, original);
                _overlay?.ShowResult(T("Selezione riscritta", "Selection rewritten"), $"{total.Elapsed.TotalSeconds:0.0} s", cleaned);
            }
            else
            {
                TextInjector.Restore(original);
                _overlay?.ShowResult(T($"{preset.Name} pronta", $"{preset.Name} ready"), $"{total.Elapsed.TotalSeconds:0.0} s", cleaned, sticky: true);
            }
            Remember(cleaned);
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

    /// <summary>Salva l'audio nella cartella delle registrazioni; null se non si riesce (lo dice il log).</summary>
    private static string? SaveRecording(float[] samples, DateTime recordedAt)
    {
        try
        {
            var path = Recordings.Save(AppPaths.RecordingsDir, samples, recordedAt);
            AppLog.Info($"registrazione salvata: {Path.GetFileName(path)}");
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(e);
            return null;
        }
    }

    private void DeleteOldRecordings()
    {
        var days = _settings.RecordingsRetentionDays;
        var deleted = Recordings.DeleteOlderThan(AppPaths.RecordingsDir, days, DateTime.Now);
        if (deleted > 0) AppLog.Info($"registrazioni più vecchie di {days} giorni cancellate: {deleted}");
    }

    /// <summary>Trascrizione non riuscita: l'audio è salvato, si può riprovare subito o più tardi dal menu.</summary>
    private void OfferRetry(float[] samples, string? audioPath, string title, string reason)
    {
        var saved = audioPath is null
            ? string.Empty
            : T($" Audio salvato in {Path.GetFileName(audioPath)}: puoi ritrascriverlo anche dopo, dal menu Trascrivi un file audio.",
                $" Audio saved as {Path.GetFileName(audioPath)}: you can also transcribe it later from the menu, Transcribe an audio file.");
        if (_overlay is null)
        {
            Notify(title, reason + saved);
            return;
        }
        _overlay.ShowMessage(title, reason + saved, T("Riprova", "Try again"),
            () => _ = TranscribeLaterAsync(() => samples, T("di nuovo", "again")));
    }

    /// <summary>Voce del menu: sceglie un file audio e ne mostra la trascrizione, senza incollarla.</summary>
    private void TranscribeFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = T("Trascrivi un file audio", "Transcribe an audio file"),
            Filter = "Audio (*.wav;*.mp3;*.m4a)|*.wav;*.mp3;*.m4a|" + T("Tutti i file", "All files") + " (*.*)|*.*",
            InitialDirectory = Directory.Exists(AppPaths.RecordingsDir)
                ? AppPaths.RecordingsDir
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var path = dialog.FileName;
        _ = TranscribeLaterAsync(() => AudioFile.ReadForWhisper(path), Path.GetFileName(path));
    }

    /// <summary>
    /// Trascrive audio già registrato (Riprova o un file). Il risultato non si incolla, perché nel frattempo
    /// la finestra attiva può essere cambiata: resta nella finestrella, pronto da copiare.
    /// </summary>
    private async Task TranscribeLaterAsync(Func<float[]> load, string source)
    {
        if (_busy || _mic.IsRecording || _transcriber is null)
        {
            Notify(T("Un momento", "Just a moment"), _transcriber is null
                ? T("Il modello Whisper si sta ancora caricando: riprova tra qualche secondo.", "The Whisper model is still loading: try again in a few seconds.")
                : T("C'è già una dettatura in corso: riprova quando è finita.", "A dictation is in progress: try again when it is over."));
            return;
        }

        _busy = true;
        _tray.Icon = _workingIcon;
        var total = Stopwatch.StartNew();
        try
        {
            SetStatus(T("Trascrizione...", "Transcribing..."));
            _overlay?.ShowWorking(T($"Trascrizione {source}…", $"Transcribing {source}…"));
            var transcriber = _transcriber;
            var text = await Task.Run(() => transcriber.TranscribeAsync(load()));
            AppLog.Info($"trascrizione successiva ({source}) in {total.ElapsedMilliseconds} ms: {text.Length} caratteri");
            if (string.IsNullOrWhiteSpace(text))
            {
                Notify(T("Nessun testo riconosciuto", "No words recognised"),
                    T("Whisper non ha riconosciuto parole nemmeno questa volta.", "Whisper recognised no words this time either."));
                return;
            }
            Remember(text);
            if (_overlay is not null)
            {
                _overlay.ShowResult(T("Trascrizione pronta", "Transcription ready"), $"{total.Elapsed.TotalSeconds:0.0} s", text, sticky: true);
            }
            else
            {
                Clipboard.SetText(text);
                _tray.ShowBalloonTip(4000, Title, T("Trascrizione copiata negli appunti.", "Transcription copied to the clipboard."), ToolTipIcon.Info);
            }
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

    private void OpenSettings()
    {
        if (_settingsForm is not null)
        {
            _settingsForm.Activate();
            return;
        }
        if (_busy || _mic.IsRecording)
        {
            Notify(T("Un momento", "Just a moment"), T("Le impostazioni si aprono a dettatura finita.", "The settings open once the dictation is over."));
            return;
        }

        try
        {
            using (_settingsForm = new SettingsForm(_settings, _hotkeys.UnregisterAll, RegisterHotkeys))
            {
                if (_settingsForm.ShowDialog() == DialogResult.OK && _settingsForm.Result is { } updated) _ = ApplySettingsAsync(updated);
            }
        }
        finally
        {
            _settingsForm = null;
            RegisterHotkeys();
        }
    }

    /// <summary>Applica le impostazioni salvate senza riavvio: ricarica solo i componenti toccati.</summary>
    private async Task ApplySettingsAsync(WhisperAloudSettings updated)
    {
        // Con la finestra aperta si può dettare: si applica a dettatura finita, per non cambiare microfono o modello a metà.
        while (_busy || _mic.IsRecording) await Task.Delay(200);
        var old = _settings;
        // La posizione della finestrella non è nella finestra Impostazioni: si tiene quella attuale.
        updated.OverlayLeft = old.OverlayLeft;
        updated.OverlayTop = old.OverlayTop;
        _settings = updated;
        try
        {
            _settings.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Report(new InvalidOperationException(T($"Impostazioni non salvate su disco: {e.Message}", $"Settings not saved to disk: {e.Message}"), e));
        }
        AppLog.Info($"impostazioni salvate (modalità {updated.Mode}, microfono {MicRecorder.DeviceName(updated.MicrophoneDevice)}, GPU {updated.GpuDevice})");

        if (old.StartWithWindows != updated.StartWithWindows) ApplyAutostart(updated.StartWithWindows);

        // La lingua dell'interfaccia vale per menu e finestre già costruiti: si riavvia l'app.
        if (Loc.Resolve(old.UiLanguage) != Loc.Resolve(updated.UiLanguage))
        {
            Restart();
            return;
        }

        if (old.MicrophoneDevice != updated.MicrophoneDevice)
        {
            _mic.Dispose();
            _mic = new MicRecorder(updated.MicrophoneDevice);
        }

        if (old.ShowOverlay != updated.ShowOverlay)
        {
            _overlay?.Dispose();
            _overlay = updated.ShowOverlay ? CreateOverlay() : null;
        }

        if (JsonSerializer.Serialize(old.Claude) != JsonSerializer.Serialize(updated.Claude))
        {
            var previous = _cleaner;
            _cleaner = null;
            if (previous is not null) _ = previous.DisposeAsync().AsTask();
            StartCleaner();
        }

        if (old.Claude.Enabled != updated.Claude.Enabled)
        {
            _promptMenu.Visible = updated.Claude.Enabled;
            RegisterHotkeys();
        }

        if (old.GpuDevice != updated.GpuDevice || old.Language != updated.Language ||
            old.WhisperModel != updated.WhisperModel || old.WhisperPrompt != updated.WhisperPrompt)
        {
            _ = ReloadTranscriberAsync();
        }
    }

    private async Task ReloadTranscriberAsync()
    {
        var previous = _transcriber;
        _transcriber = null;
        _tray.Icon = _workingIcon;
        _overlay?.ShowWorking(T("Ricarico Whisper con le nuove impostazioni…", "Reloading Whisper with the new settings…"));
        if (previous is not null) await previous.DisposeAsync();
        await LoadTranscriberAsync();
        if (_transcriber is not null) _overlay?.ShowMessage(T("Pronta", "Ready"), T($"Whisper ricaricato ({WhisperTranscriber.LoadedRuntime}).", $"Whisper reloaded ({WhisperTranscriber.LoadedRuntime})."));
    }

    private OverlayForm CreateOverlay()
    {
        var overlay = new OverlayForm
        {
            CustomLocation = _settings.OverlayLeft is { } left && _settings.OverlayTop is { } top ? new Point(left, top) : null,
        };
        overlay.PositionChanged += position =>
        {
            _settings.OverlayLeft = position?.X;
            _settings.OverlayTop = position?.Y;
            _settings.Save();
        };
        return overlay;
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
        _overlay?.ShowResult(T("Ultima dettatura copiata", "Last dictation copied"), string.Empty, _lastText);
    }

    private void NotifyClaudeUnavailable() =>
        Notify(T("Claude non disponibile", "Claude not available"),
            _claudeProblem is null
                ? T("Claude non è partito: guarda il log errori.", "Claude did not start: see the diagnostic log.")
                : T("Claude Code non trovato: installalo e accedi, oppure indica il percorso in Impostazioni › Claude › Eseguibile.",
                    "Claude Code not found: install it and sign in, or set its path in Settings › Claude › Executable."));

    /// <summary>Avviso all'utente: nella finestrella se è attiva, altrimenti nel fumetto del tray.</summary>
    private void Notify(string title, string message)
    {
        if (_overlay is not null) _overlay.ShowMessage(title, message);
        else _tray.ShowBalloonTip(4000, Title, $"{title}. {message}", ToolTipIcon.Warning);
    }

    private void SetIdle()
    {
        _tray.Icon = _idleIcon;
        SetStatus(_transcriber is null ? T("Whisper non pronto", "Whisper not ready") : T("Pronta", "Ready"));
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
        if (_overlay is not null) _overlay.ShowMessage(T("Errore", "Error"), e.Message);
        else _tray.ShowBalloonTip(5000, Title, e.Message, ToolTipIcon.Error);
    }

    private void ApplyAutostart(bool enabled)
    {
        try
        {
            Autostart.Apply(enabled);
            AppLog.Info($"avvio con Windows: {(enabled ? "attivo" : "spento")}");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Report(new InvalidOperationException(T($"Avvio con Windows non impostato: {e.Message}", $"Could not set Start with Windows: {e.Message}"), e));
        }
    }

    /// <summary>Avvia una nuova istanza, che aspetta la chiusura di questa prima di partire.</summary>
    private void Restart()
    {
        AppLog.Info("riavvio per la lingua dell'interfaccia");
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Program.RestartArgument) { UseShellExecute = false });
        ExitThread();
    }

    internal static void OpenFile(string path)
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
