using System.Diagnostics;
using WhisperAloud.Core;
using WhisperAloud.Core.Cleanup;
using WhisperAloud.Core.Transcription;
using static WhisperAloud.App.Theme;
using static WhisperAloud.Core.Loc;

namespace WhisperAloud.App;

/// <summary>
/// Finestra delle impostazioni in tema scuro, con barra laterale e pagine a schede. Modifica una copia di
/// <see cref="WhisperAloudSettings"/> e la restituisce in <see cref="Result"/> solo se si preme Salva.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly WhisperAloudSettings _settings;

    private readonly OptionCard _pushToTalk = new(T("Tieni premuto", "Hold"),
        T("Registra finché tieni premuta la scorciatoia. Ideale per frasi brevi.", "Records while you hold the shortcut. Best for short sentences."));
    private readonly OptionCard _toggle = new(T("Avvio e stop", "Start and stop"),
        T("Premi una volta per iniziare e una per finire. Comodo per dettature lunghe.", "Press once to start and again to stop. Handy for long dictations."));
    private readonly KeyCapBox _hotkeyDictate = new();
    private readonly KeyCapBox _hotkeyClaude = new();
    private readonly KeyCapBox _hotkeySelection = new();
    private readonly NumericUpDown _minRecording = Number(0, 5000, 50);
    private readonly DarkComboBox _uiLanguage = Combo(220);
    private readonly ToggleSwitch _startWithWindows = new();

    private readonly DarkComboBox _microphone = Combo(300);
    private readonly DarkComboBox _gpu = Combo(300);
    private readonly DarkComboBox _language = Combo(140, "it", "en", "fr", "de", "es", "pt", "auto");
    private readonly TextBox _whisperModel = new();
    private readonly TextBox _whisperPrompt = Multiline();
    private readonly ToggleSwitch _keepRecordings = new();
    private readonly NumericUpDown _retentionDays = Number(0, 365, 1);
    private readonly ModernButton _openRecordings = new(T("Apri cartella", "Open folder"));

    private readonly ToggleSwitch _useClaude = new();
    private readonly SettingRow _claudeMissing = new(T("Claude Code non trovato", "Claude Code not found"),
        T("Installa Claude Code e accedi, oppure indica il percorso in Eseguibile. Finché manca, Whisper Aloud trascrive soltanto.",
          "Install Claude Code and sign in, or set its path in Executable. Until then, Whisper Aloud only transcribes."), null);
    private SettingsCard? _claudeMissingCard;
    private readonly DarkComboBox _claudeModel = Combo(160, "sonnet", "haiku", "opus");
    private readonly ToggleSwitch _warmProcess = new();
    private readonly NumericUpDown _timeout = Number(5, 300, 5);
    private readonly TextBox _claudeExecutable = new();
    private readonly DarkComboBox _defaultPrompt = Combo(260);
    private readonly ModernButton _addPrompt = new(T("+ Aggiungi prompt", "+ Add prompt"));
    private readonly List<PromptEditor> _promptEditors = [];
    private Page? _claudePage;

    private readonly ToggleSwitch _appendSpace = new();
    private readonly ToggleSwitch _restoreClipboard = new();
    private readonly ToggleSwitch _showOverlay = new();

    private readonly Label _pageTitle = new() { Font = Display(18f), ForeColor = Fore, AutoSize = true, BackColor = Window };
    private readonly Label _pageSubtitle = new() { Font = Body(9.5f), ForeColor = Muted, AutoSize = true, BackColor = Window };
    private readonly Label _error = new() { Font = Body(9.5f), ForeColor = Red, AutoSize = false, BackColor = Window, TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<(NavButton Button, Page Page, string Title, string Subtitle)> _pages = [];

    /// <param name="suspendHotkeys">Libera le scorciatoie mentre se ne sceglie una nuova.</param>
    /// <param name="resumeHotkeys">Le riattiva a scelta fatta.</param>
    public SettingsForm(WhisperAloudSettings current, Action suspendHotkeys, Action resumeHotkeys)
    {
        _settings = current.Clone();

        Text = T("Whisper Aloud · Impostazioni", "Whisper Aloud · Settings");
        AutoScaleMode = AutoScaleMode.None;
        Font = Body(10f);
        BackColor = Window;
        ForeColor = Fore;
        ClientSize = new Size(S(880), S(640));
        MinimumSize = new Size(S(760), S(520));
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        Icon = TrayIcons.App; // senza, la barra delle applicazioni mostra un'icona vuota

        foreach (var box in new[] { _hotkeyDictate, _hotkeyClaude, _hotkeySelection })
        {
            box.Enter += (_, _) => suspendHotkeys();
            box.Leave += (_, _) => resumeHotkeys();
        }
        _pushToTalk.Selected += (_, _) => SelectMode(DictationMode.PushToTalk);
        _toggle.Selected += (_, _) => SelectMode(DictationMode.Toggle);

        var pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Window };
        AddPage(pageHost, T("Dettatura", "Dictation"), T("Come detti, le scorciatoie da tastiera, lingua e avvio dell'app.", "How you dictate, the keyboard shortcuts, the app language and startup."), BuildDictationPage());
        AddPage(pageHost, T("Trascrizione", "Transcription"), T("Microfono e Whisper, il modello che trascrive in locale sulla GPU.", "Microphone and Whisper, the model that transcribes locally on the GPU."), BuildTranscriptionPage());
        AddPage(pageHost, "Claude", T("Riscrittura con i tuoi prompt, solo quando la chiedi.", "Rewriting with your prompts, only when you ask for it."), BuildClaudePage());
        AddPage(pageHost, T("Incolla", "Paste"), T("Cosa succede dopo la trascrizione.", "What happens after the transcription."), BuildPastePage());

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Window };
        content.Controls.Add(pageHost);
        content.Controls.Add(BuildHeader());
        content.Controls.Add(BuildFooter());

        Controls.Add(content);
        Controls.Add(BuildSidebar());

        LoadValues();
        ShowPage(0);
    }

    /// <summary>Impostazioni modificate, valorizzate solo dopo Salva.</summary>
    public WhisperAloudSettings? Result { get; private set; }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        StyleWindow(Handle, roundCorners: false);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = null; // nessun campo scorciatoia attivo all'apertura
        Activate(); // aperta dal tray, altrimenti resta dietro la finestra attiva
    }

    // --- pagine ---

    private Page BuildDictationPage()
    {
        var modes = new Panel { BackColor = Surface, Height = S(92) };
        modes.Controls.AddRange([_pushToTalk, _toggle]);
        modes.Resize += (_, _) =>
        {
            var half = (modes.Width - S(12)) / 2;
            _pushToTalk.SetBounds(0, 0, half, modes.Height);
            _toggle.SetBounds(half + S(12), 0, modes.Width - half - S(12), modes.Height);
        };

        return new Page(
            new SettingsCard(new SettingRow(T("Modalità", "Mode"), null, modes, below: true)),
            Section(T("Scorciatoie", "Shortcuts")),
            new SettingsCard(
                new SettingRow(T("Detta e incolla", "Dictate and paste"), T("La trascrizione di Whisper così com'è.", "Whisper's transcription as it is."), _hotkeyDictate),
                new SettingRow(T("Detta e riscrivi con Claude", "Dictate and rewrite with Claude"), T("Whisper, poi Claude con il prompt predefinito.", "Whisper, then Claude with the default prompt."), _hotkeyClaude),
                new SettingRow(T("Riscrivi la selezione", "Rewrite the selection"), T("Sistema con Claude il testo selezionato nella finestra attiva.", "Rewrites the text selected in the active window with Claude."), _hotkeySelection),
                new SettingRow(T("Come cambiarle", "How to change them"),
                    T("Clicca su una scorciatoia e premi la nuova combinazione; Esc annulla. Mentre scegli, le scorciatoie sono sospese.",
                      "Click a shortcut and press the new combination; Esc cancels. While you choose, the shortcuts are suspended."), null)),
            Section("App"),
            new SettingsCard(
                new SettingRow(T("Avvia con Windows", "Start with Windows"),
                    T("Whisper Aloud parte da solo quando accedi a Windows.", "Whisper Aloud starts by itself when you sign in to Windows."), _startWithWindows),
                new SettingRow(T("Lingua dell'app", "App language"),
                    T("Menu, finestre e prompt di esempio. Cambiandola l'app si riavvia; i tuoi prompt restano come sono.",
                      "Menus, windows and sample prompts. Changing it restarts the app; your prompts stay as they are."), _uiLanguage)),
            Section(T("Avanzate", "Advanced")),
            new SettingsCard(
                new SettingRow(T("Pressione minima (ms)", "Minimum press (ms)"),
                    T("Solo con Tieni premuto: le pressioni più brevi sono considerate accidentali.", "Hold mode only: shorter presses are treated as accidental."),
                    Frame(_minRecording, S(110), S(34)))));
    }

    private Page BuildTranscriptionPage() => new(
        new SettingsCard(
            new SettingRow(T("Microfono", "Microphone"), null, _microphone),
            new SettingRow("GPU", T("Sui portatili con due GPU serve la dedicata: con l'integrata la trascrizione è circa 20 volte più lenta.",
                "On laptops with two GPUs use the dedicated one: with the integrated one transcription is about 20 times slower."), _gpu)),
        Section("Whisper"),
        new SettingsCard(
            new SettingRow(T("Lingua", "Language"), T("Lingua parlata; auto la riconosce da sola ma sbaglia più spesso.", "Spoken language; auto detects it but makes more mistakes."), _language),
            new SettingRow(T("Modello", "Model"), T($"Nome del file in {AppPaths.ModelsDir} oppure percorso completo.", $"File name in {AppPaths.ModelsDir} or a full path."), Frame(_whisperModel, 0, S(36)), below: true),
            new SettingRow(T("Contesto", "Context"), T("Aiuta punteggiatura, nomi e termini tecnici ricorrenti.", "Helps with punctuation, names and recurring technical terms."), Frame(_whisperPrompt, 0, S(82), resizable: true), below: true)),
        Section(T("Registrazioni", "Recordings")),
        new SettingsCard(
            new SettingRow(T("Conserva le registrazioni", "Keep recordings"),
                T("Salva l'audio di ogni dettatura, da ritrascrivere con Trascrivi un file audio nel menu. Se la trascrizione non riesce, l'audio si salva comunque.",
                  "Saves the audio of every dictation, to transcribe again with Transcribe an audio file in the menu. If a transcription fails, the audio is saved anyway."), _keepRecordings),
            new SettingRow(T("Cancella dopo (giorni)", "Delete after (days)"),
                T("All'avvio si cancellano le registrazioni più vecchie; 0 le tiene per sempre.", "Older recordings are deleted at start; 0 keeps them forever."),
                Frame(_retentionDays, S(110), S(34))),
            new SettingRow(T("Cartella", "Folder"), AppPaths.RecordingsDir, _openRecordings)));

    private Page BuildClaudePage()
    {
        _addPrompt.Click += (_, _) => AddPromptEditor(new PromptPreset { Name = T("Nuovo prompt", "New prompt") }, focus: true);
        _useClaude.CheckedChanged += (_, _) => RefreshClaudePage();
        _claudeExecutable.Leave += (_, _) => RefreshClaudePage();
        _claudeMissing.MarkAsWarning();
        _claudeMissingCard = new SettingsCard(_claudeMissing);
        _claudePage = new Page(
            new SettingsCard(new SettingRow(T("Usa Claude", "Use Claude"),
                T("Spento: solo trascrizione, nessun processo claude e scorciatoie di Claude libere per altre app.",
                  "Off: transcription only, no claude process, and Claude's shortcuts stay free for other apps."), _useClaude)),
            _claudeMissingCard,
            new SettingsCard(
                new SettingRow(T("Modello", "Model"), T("sonnet è il più fedele al testo dettato; haiku è più veloce ma a volte riformula.", "sonnet is the most faithful to the dictated text; haiku is faster but sometimes rephrases."), _claudeModel),
                new SettingRow(T("Processo pronto", "Process ready"), T("Avvia claude mentre detti e lo tiene pronto per 5 minuti dopo l'ultima riscrittura: risponde in circa 2 secondi invece di 4-5.",
                  "Starts claude while you dictate and keeps it ready for 5 minutes after the last rewrite: about 2 seconds instead of 4-5."), _warmProcess),
                new SettingRow(T("Timeout (secondi)", "Timeout (seconds)"), T("Attesa massima della risposta, poi incolla la trascrizione grezza.", "Maximum wait for the answer, then the raw transcription is pasted."), Frame(_timeout, S(110), S(34)))),
            Section(T("Avanzate", "Advanced")),
            new SettingsCard(
                new SettingRow(T("Eseguibile", "Executable"), T("\"claude\" la cerca nel PATH e in %USERPROFILE%\\.local\\bin.", "\"claude\" is looked up on the PATH and in %USERPROFILE%\\.local\\bin."), Frame(_claudeExecutable, 0, S(36)), below: true)),
            Section("Prompt"),
            new SettingsCard(
                new SettingRow(T("Prompt predefinito", "Default prompt"),
                    T("Usato da Detta e riscrivi e da Riscrivi la selezione. Mentre registri puoi sceglierne un altro dalla finestrella.",
                      "Used by Dictate and rewrite and by Rewrite the selection. While recording you can pick another one in the status window."),
                    _defaultPrompt)),
            Section(T("I tuoi prompt", "Your prompts")),
            // Le schede dei prompt vengono inserite qui, prima del pulsante (vedi AddPromptEditor).
            _addPrompt);
        return _claudePage;
    }

    /// <summary>Con Claude spento resta solo l'interruttore; l'avviso compare se Claude è acceso ma la CLI non si trova.</summary>
    private void RefreshClaudePage()
    {
        if (_claudePage is null) return;
        var missing = false;
        if (_useClaude.Checked)
        {
            try
            {
                ExecutableResolver.Resolve(string.IsNullOrWhiteSpace(_claudeExecutable.Text) ? "claude" : _claudeExecutable.Text.Trim());
            }
            catch (FileNotFoundException)
            {
                missing = true;
            }
        }
        foreach (Control item in _claudePage.Items)
        {
            if (item == _claudePage.Items[0]) continue;
            _claudePage.SetHidden(item, item == _claudeMissingCard ? !missing : !_useClaude.Checked);
        }
    }

    private void AddPromptEditor(PromptPreset preset, bool focus = false)
    {
        var editor = new PromptEditor(preset, RemovePromptEditor, RefreshDefaultPrompt);
        _promptEditors.Add(editor);
        _claudePage!.Items.Add(editor.Card);
        _claudePage.Items.SetChildIndex(editor.Card, _claudePage.Items.GetChildIndex(_addPrompt));
        RefreshDefaultPrompt();
        if (!focus) return;
        _claudePage.PerformLayout();
        _claudePage.ScrollControlIntoView(editor.Card);
        editor.NameBox.Focus();
        editor.NameBox.SelectAll();
    }

    private void RemovePromptEditor(PromptEditor editor)
    {
        if (_promptEditors.Count == 1)
        {
            ShowError(T("Serve almeno un prompt.", "At least one prompt is needed."), 2);
            return;
        }
        _promptEditors.Remove(editor);
        _claudePage!.Items.Remove(editor.Card);
        editor.Card.Dispose();
        RefreshDefaultPrompt();
    }

    /// <summary>Ricostruisce l'elenco del predefinito quando si aggiunge, rinomina o elimina un prompt.</summary>
    private void RefreshDefaultPrompt()
    {
        var selected = _defaultPrompt.SelectedItem as PromptEditor;
        _defaultPrompt.Items.Clear();
        foreach (var editor in _promptEditors) _defaultPrompt.Items.Add(editor);
        _defaultPrompt.SelectedItem = selected is not null && _promptEditors.Contains(selected) ? selected : _promptEditors.FirstOrDefault();
        _defaultPrompt.Invalidate();
    }

    private Page BuildPastePage() => new(
        new SettingsCard(
            new SettingRow(T("Spazio dopo il testo", "Space after the text"), T("Le dettature consecutive non si attaccano.", "Consecutive dictations do not stick together."), _appendSpace),
            new SettingRow(T("Ripristina gli appunti", "Restore the clipboard"), T("Dopo l'incolla gli appunti tornano com'erano.", "After pasting, the clipboard goes back to what it was."), _restoreClipboard),
            new SettingRow(T("Finestrella di stato", "Status window"), T("In primo piano mentre ascolta, trascrive e incolla, con il pulsante Copia.", "Always on top while listening, transcribing and pasting, with a Copy button."), _showOverlay)));

    // --- struttura della finestra ---

    private Panel BuildSidebar()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = S(220), BackColor = Sidebar };
        var logo = new MicBadge { Location = new Point(S(20), S(24)) };
        var title = new Label { Text = "Whisper Aloud", Font = Display(14f), ForeColor = Fore, AutoSize = true, BackColor = Sidebar, Location = new Point(S(64), S(22)) };
        var subtitle = new Label { Text = T("Impostazioni", "Settings"), Font = Body(9f), ForeColor = Muted, AutoSize = true, BackColor = Sidebar, Location = new Point(S(65), S(48)) };
        sidebar.Controls.AddRange([logo, title, subtitle]);

        for (var i = 0; i < _pages.Count; i++)
        {
            var button = _pages[i].Button;
            button.SetBounds(S(12), S(100) + i * S(42), S(196), S(38));
            var index = i;
            button.Click += (_, _) => ShowPage(index);
            sidebar.Controls.Add(button);
        }

        sidebar.Paint += (_, e) =>
        {
            using var pen = new Pen(Divider);
            e.Graphics.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
        };
        return sidebar;
    }

    private Panel BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = S(96), BackColor = Window };
        _pageTitle.Location = new Point(S(28), S(24));
        _pageSubtitle.Location = new Point(S(30), S(62));
        header.Controls.AddRange([_pageTitle, _pageSubtitle]);
        return header;
    }

    private Panel BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = S(68), BackColor = Window };
        var link = new LinkLabel
        {
            Text = T("Apri settings.json", "Open settings.json"),
            AutoSize = true,
            Font = Body(9.5f),
            LinkColor = Accent,
            ActiveLinkColor = AccentHover,
            VisitedLinkColor = Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
            BackColor = Window,
        };
        link.LinkClicked += (_, _) => TrayApp.OpenFile(AppPaths.SettingsFile);
        new ToolTip().SetToolTip(link, T($"{AppPaths.SettingsFile}\nLe modifiche fatte a mano nel file valgono al riavvio dell'app.", $"{AppPaths.SettingsFile}\nManual edits to the file apply when the app restarts."));

        var save = new ModernButton(T("Salva", "Save"), primary: true);
        save.Click += (_, _) => Save();
        var cancel = new ModernButton(T("Annulla", "Cancel")) { DialogResult = DialogResult.Cancel };
        AcceptButton = save;
        CancelButton = cancel;

        footer.Controls.AddRange([link, _error, save, cancel]);
        footer.Resize += (_, _) =>
        {
            var middle = footer.Height / 2;
            cancel.Location = new Point(footer.Width - S(28) - cancel.Width, middle - cancel.Height / 2);
            save.Location = new Point(cancel.Left - S(10) - save.Width, cancel.Top);
            link.Location = new Point(S(28), middle - link.Height / 2);
            _error.SetBounds(link.Right + S(16), middle - S(14), Math.Max(0, save.Left - link.Right - S(28)), S(28));
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Divider);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        return footer;
    }

    private void AddPage(Panel host, string title, string subtitle, Page page)
    {
        page.Visible = false;
        host.Controls.Add(page);
        _pages.Add((new NavButton(title) { TabStop = false }, page, title, subtitle));
    }

    private void ShowPage(int index)
    {
        for (var i = 0; i < _pages.Count; i++)
        {
            _pages[i].Button.Selected = i == index;
            _pages[i].Page.Visible = i == index;
        }
        _pageTitle.Text = _pages[index].Title;
        _pageSubtitle.Text = _pages[index].Subtitle;
    }

    private void SelectMode(DictationMode mode)
    {
        _pushToTalk.IsSelected = mode == DictationMode.PushToTalk;
        _toggle.IsSelected = mode == DictationMode.Toggle;
    }

    // --- valori ---

    private void LoadValues()
    {
        SelectMode(_settings.Mode);
        _hotkeyDictate.Text = _settings.HotkeyDictate;
        _hotkeyClaude.Text = _settings.HotkeyDictateWithClaude;
        _hotkeySelection.Text = _settings.HotkeyCleanSelection;
        _minRecording.Value = Math.Clamp(_settings.MinRecordingMs, (int)_minRecording.Minimum, (int)_minRecording.Maximum);
        _uiLanguage.Items.AddRange([new TextChoice("auto", T("Automatica (Windows)", "Automatic (Windows)")),
            new TextChoice("it", "Italiano"), new TextChoice("en", "English")]);
        _uiLanguage.SelectedItem = _uiLanguage.Items.Cast<TextChoice>().FirstOrDefault(c => c.Value == _settings.UiLanguage) ?? _uiLanguage.Items[0];
        _startWithWindows.Checked = _settings.StartWithWindows;

        _microphone.Items.Add(new Choice(-1, T("Predefinito di Windows", "Windows default")));
        foreach (var (index, name) in MicRecorder.ListDevices()) _microphone.Items.Add(new Choice(index, name));
        Select(_microphone, _settings.MicrophoneDevice);

        var gpus = VulkanDevices.List();
        var discrete = gpus.FirstOrDefault(g => g.Discrete);
        var automatic = T("Automatica", "Automatic");
        _gpu.Items.Add(new Choice(-1, discrete.Name is null ? automatic : $"{automatic} · {discrete.Name}"));
        foreach (var (index, name, isDiscrete) in gpus)
        {
            _gpu.Items.Add(new Choice(index, $"{name} ({(isDiscrete ? T("dedicata", "dedicated") : T("integrata", "integrated"))})"));
        }
        Select(_gpu, _settings.GpuDevice);

        SelectText(_language, _settings.Language);
        _whisperModel.Text = _settings.WhisperModel;
        _whisperPrompt.Text = _settings.WhisperPrompt;
        _keepRecordings.Checked = _settings.KeepRecordings;
        _retentionDays.Value = Math.Clamp(_settings.RecordingsRetentionDays, (int)_retentionDays.Minimum, (int)_retentionDays.Maximum);
        _openRecordings.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.RecordingsDir);
            Process.Start(new ProcessStartInfo(AppPaths.RecordingsDir) { UseShellExecute = true });
        };

        SelectText(_claudeModel, _settings.Claude.Model);
        _warmProcess.Checked = _settings.Claude.UseWarmProcess;
        _timeout.Value = Math.Clamp(_settings.Claude.TimeoutSeconds, (int)_timeout.Minimum, (int)_timeout.Maximum);
        _claudeExecutable.Text = _settings.Claude.Executable;
        _useClaude.Checked = _settings.Claude.Enabled;
        foreach (var preset in _settings.Claude.Prompts) AddPromptEditor(preset);
        var defaultName = _settings.Claude.FindPrompt(null).Name;
        _defaultPrompt.SelectedItem = _promptEditors.FirstOrDefault(e => e.ToString() == defaultName) ?? _promptEditors.FirstOrDefault();
        RefreshClaudePage();

        _appendSpace.Checked = _settings.AppendSpace;
        _restoreClipboard.Checked = _settings.RestoreClipboard;
        _showOverlay.Checked = _settings.ShowOverlay;
    }

    private void Save()
    {
        var hotkeys = new[] { _hotkeyDictate.Text, _hotkeyClaude.Text, _hotkeySelection.Text };
        try
        {
            foreach (var hotkey in hotkeys) Hotkey.Parse(hotkey);
        }
        catch (FormatException e)
        {
            ShowError(e.Message, 0);
            return;
        }
        if (hotkeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() < hotkeys.Length)
        {
            ShowError(T("Le tre scorciatoie devono essere diverse.", "The three shortcuts must be different."), 0);
            return;
        }
        if (string.IsNullOrWhiteSpace(_language.Text) || string.IsNullOrWhiteSpace(_whisperModel.Text))
        {
            ShowError(T("Lingua e modello di Whisper non possono essere vuoti.", "Whisper's language and model cannot be empty."), 1);
            return;
        }
        if (_useClaude.Checked && (string.IsNullOrWhiteSpace(_claudeModel.Text) || string.IsNullOrWhiteSpace(_claudeExecutable.Text)))
        {
            ShowError(T("Modello ed eseguibile di Claude non possono essere vuoti.", "Claude's model and executable cannot be empty."), 2);
            return;
        }
        var names = _promptEditors.Select(e => e.NameBox.Text.Trim()).ToList();
        if (names.Any(string.IsNullOrEmpty) || _promptEditors.Any(e => string.IsNullOrWhiteSpace(e.InstructionsBox.Text)))
        {
            ShowError(T("Ogni prompt deve avere un nome e delle istruzioni.", "Every prompt needs a name and instructions."), 2);
            return;
        }
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() < names.Count)
        {
            ShowError(T("Due prompt hanno lo stesso nome.", "Two prompts have the same name."), 2);
            return;
        }

        _settings.Mode = _toggle.IsSelected ? DictationMode.Toggle : DictationMode.PushToTalk;
        _settings.HotkeyDictate = _hotkeyDictate.Text;
        _settings.HotkeyDictateWithClaude = _hotkeyClaude.Text;
        _settings.HotkeyCleanSelection = _hotkeySelection.Text;
        _settings.MinRecordingMs = (int)_minRecording.Value;
        _settings.UiLanguage = ((TextChoice)_uiLanguage.SelectedItem!).Value;
        _settings.StartWithWindows = _startWithWindows.Checked;

        _settings.MicrophoneDevice = ((Choice)_microphone.SelectedItem!).Value;
        _settings.GpuDevice = ((Choice)_gpu.SelectedItem!).Value;
        _settings.Language = _language.Text.Trim();
        _settings.WhisperModel = _whisperModel.Text.Trim();
        _settings.WhisperPrompt = _whisperPrompt.Text.Trim();
        _settings.KeepRecordings = _keepRecordings.Checked;
        _settings.RecordingsRetentionDays = (int)_retentionDays.Value;

        _settings.Claude.Enabled = _useClaude.Checked;
        _settings.Claude.Model = _claudeModel.Text.Trim();
        _settings.Claude.UseWarmProcess = _warmProcess.Checked;
        _settings.Claude.TimeoutSeconds = (int)_timeout.Value;
        _settings.Claude.Executable = _claudeExecutable.Text.Trim();
        _settings.Claude.Prompts = _promptEditors.Select(e => e.ToPreset()).ToList();
        _settings.Claude.SelectedPrompt = (_defaultPrompt.SelectedItem as PromptEditor)?.ToString() ?? names[0];

        _settings.AppendSpace = _appendSpace.Checked;
        _settings.RestoreClipboard = _restoreClipboard.Checked;
        _settings.ShowOverlay = _showOverlay.Checked;

        Result = _settings;
        DialogResult = DialogResult.OK;
    }

    /// <summary>Errore nel piè di pagina, aprendo la pagina che contiene il campo da correggere.</summary>
    private void ShowError(string message, int page)
    {
        ShowPage(page);
        _error.Text = message;
    }

    private static void Select(ComboBox combo, int value)
    {
        var item = combo.Items.Cast<Choice>().FirstOrDefault(c => c.Value == value);
        if (item is null)
        {
            // Valore non più presente (microfono scollegato, GPU cambiata): lo si mostra comunque per non perderlo.
            item = new Choice(value, $"{value} · {T("non trovato", "not found")}");
            combo.Items.Add(item);
        }
        combo.SelectedItem = item;
    }

    private sealed record Choice(int Value, string Text)
    {
        public override string ToString() => Text;
    }

    private sealed record TextChoice(string Value, string Text)
    {
        public override string ToString() => Text;
    }

    // --- fabbriche dei controlli ---

    private static Label Section(string title) => new()
    {
        Text = title,
        Font = Strong(10.5f),
        ForeColor = Fore,
        BackColor = Window,
        AutoSize = false,
        Height = S(30),
        TextAlign = ContentAlignment.BottomLeft,
    };

    private static FieldFrame Frame(Control inner, int width, int height, bool resizable = false)
    {
        var frame = new FieldFrame(inner, height, resizable);
        if (width > 0) frame.Width = width;
        return frame;
    }

    private static DarkComboBox Combo(int width, params string[] items)
    {
        var combo = new DarkComboBox { Width = S(width), DropDownWidth = S(Math.Max(width, 360)) };
        combo.Items.AddRange(items);
        return combo;
    }

    /// <summary>Seleziona una voce di testo; se non è nell'elenco (valore scritto a mano nel file) la aggiunge.</summary>
    private static void SelectText(ComboBox combo, string value)
    {
        if (!combo.Items.Contains(value)) combo.Items.Add(value);
        combo.SelectedItem = value;
    }

    private static NumericUpDown Number(int min, int max, int step) =>
        new() { Minimum = min, Maximum = max, Increment = step, TextAlign = HorizontalAlignment.Left };

    private static TextBox Multiline() =>
        new() { Multiline = true, ScrollBars = ScrollBars.None, AcceptsReturn = true };

    /// <summary>Scheda di modifica di un prompt: nome con pulsante Elimina, istruzioni, incolla sì/no.</summary>
    private sealed class PromptEditor
    {
        public PromptEditor(PromptPreset preset, Action<PromptEditor> remove, Action renamed)
        {
            NameBox.Text = preset.Name;
            InstructionsBox.Text = preset.Instructions.ReplaceLineEndings(Environment.NewLine); // nel file JSON gli a capo sono LF
            PasteSwitch.Checked = preset.Paste;
            NameBox.TextChanged += (_, _) => renamed();

            var delete = new ModernButton(T("Elimina", "Delete"));
            delete.Click += (_, _) => remove(this);
            var nameFrame = Frame(NameBox, S(260), S(36));
            var nameRow = new Panel { BackColor = Surface, Size = new Size(nameFrame.Width + S(10) + delete.Width, S(36)) };
            nameFrame.Location = Point.Empty;
            delete.Location = new Point(nameFrame.Right + S(10), (nameRow.Height - delete.Height) / 2);
            nameRow.Controls.AddRange([nameFrame, delete]);

            Card = new SettingsCard(
                new SettingRow(T("Nome", "Name"), null, nameRow),
                new SettingRow(T("Istruzioni", "Instructions"),
                    T("Cosa deve fare Claude con il testo dettato. Trascina la maniglia in basso per ingrandire il riquadro.",
                      "What Claude should do with the dictated text. Drag the handle at the bottom to enlarge the box."),
                    Frame(InstructionsBox, 0, S(104), resizable: true), below: true),
                new SettingRow(T("Incolla il risultato", "Paste the result"),
                    T("Spento: il risultato resta nella finestrella, da copiare. Utile per minute e riassunti.", "Off: the result stays in the status window, ready to copy. Useful for meeting notes and summaries."), PasteSwitch));
        }

        public TextBox NameBox { get; } = new();

        public TextBox InstructionsBox { get; } = Multiline();

        public ToggleSwitch PasteSwitch { get; } = new();

        public SettingsCard Card { get; }

        public PromptPreset ToPreset() => new()
        {
            Name = NameBox.Text.Trim(),
            Instructions = InstructionsBox.Text.Trim(),
            Paste = PasteSwitch.Checked,
        };

        public override string ToString() => string.IsNullOrWhiteSpace(NameBox.Text) ? T("(senza nome)", "(unnamed)") : NameBox.Text.Trim();
    }

    /// <summary>Pagina scorrevole: impila schede e titoletti di sezione a tutta larghezza.</summary>
    private sealed class Page : Panel
    {
        // Le schede stanno in un unico contenitore alto quanto il contenuto: lo scorrimento lo gestisce
        // Windows spostando il contenitore, e le posizioni interne non dipendono da quanto si è scorso.
        private readonly Panel _content = new() { BackColor = Window, Location = Point.Empty };
        // Visible si legge falso finché la pagina è nascosta: gli elementi nascosti si tengono qui.
        private readonly HashSet<Control> _hidden = [];
        private bool _arranging;

        public Page(params Control[] items)
        {
            Dock = DockStyle.Fill;
            BackColor = Window;
            // Solo scorrimento verticale: la barra orizzontale va spenta prima di attivare AutoScroll.
            HorizontalScroll.Maximum = 0;
            HorizontalScroll.Enabled = false;
            HorizontalScroll.Visible = false;
            AutoScroll = true;
            _content.Controls.AddRange(items);
            _content.ControlAdded += (_, _) => PerformLayout();
            _content.ControlRemoved += (_, _) => PerformLayout();
            Controls.Add(_content);
            DarkScrollbars(this);
        }

        /// <summary>Schede e titoletti della pagina, nell'ordine in cui compaiono.</summary>
        public ControlCollection Items => _content.Controls;

        public void SetHidden(Control item, bool hidden)
        {
            if (hidden ? !_hidden.Add(item) : !_hidden.Remove(item)) return;
            item.Visible = !hidden;
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (!_arranging)
            {
                _arranging = true;
                try
                {
                    Arrange();
                }
                finally
                {
                    _arranging = false;
                }
            }
            base.OnLayout(levent);
        }

        private void Arrange()
        {
            var x = S(28);
            // Si lascia sempre il posto della barra verticale: se compare dopo, le schede non sforano a destra.
            var scrollbar = VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth;
            var width = ClientSize.Width - 2 * x - scrollbar;
            if (width <= 0) return;

            var y = 0;
            _content.SuspendLayout();
            foreach (Control item in _content.Controls)
            {
                if (_hidden.Contains(item)) continue;
                var height = item is SettingsCard card ? card.Arrange(width) : item.Height;
                if (item is not SettingsCard and not ModernButton) item.Width = width;
                item.Location = new Point(x, y);
                y += height + (item is SettingsCard ? S(18) : S(8));
            }
            _content.ResumeLayout(false);
            _content.Size = new Size(ClientSize.Width - scrollbar, y + S(8));
        }
    }

    /// <summary>Icona dell'app nella barra laterale: microfono bianco su cerchio d'accento.</summary>
    private sealed class MicBadge : Control
    {
        public MicBadge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Size = new Size(S(34), S(34));
            BackColor = Sidebar;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            TrayIcons.DrawMic(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1), Accent);
        }
    }
}
