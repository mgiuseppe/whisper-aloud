using Dettatura.Core;
using Dettatura.Core.Transcription;
using static Dettatura.App.Theme;

namespace Dettatura.App;

/// <summary>
/// Finestra delle impostazioni in tema scuro, con barra laterale e pagine a schede. Modifica una copia di
/// <see cref="DettaturaSettings"/> e la restituisce in <see cref="Result"/> solo se si preme Salva.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly DettaturaSettings _settings;

    private readonly OptionCard _pushToTalk = new("Tieni premuto", "Registra finché tieni premuta la scorciatoia. Ideale per frasi brevi.");
    private readonly OptionCard _toggle = new("Avvio e stop", "Premi una volta per iniziare e una per finire. Comodo per dettature lunghe.");
    private readonly KeyCapBox _hotkeyDictate = new();
    private readonly KeyCapBox _hotkeyClaude = new();
    private readonly KeyCapBox _hotkeySelection = new();
    private readonly NumericUpDown _minRecording = Number(0, 5000, 50);

    private readonly DarkComboBox _microphone = Combo(300);
    private readonly DarkComboBox _gpu = Combo(300);
    private readonly DarkComboBox _language = Combo(140, "it", "en", "fr", "de", "es", "pt", "auto");
    private readonly TextBox _whisperModel = new();
    private readonly TextBox _whisperPrompt = Multiline();

    private readonly DarkComboBox _claudeModel = Combo(160, "sonnet", "haiku", "opus");
    private readonly ToggleSwitch _warmProcess = new();
    private readonly NumericUpDown _timeout = Number(5, 300, 5);
    private readonly TextBox _claudeExecutable = new();
    private readonly TextBox _systemPrompt = Multiline();

    private readonly ToggleSwitch _appendSpace = new();
    private readonly ToggleSwitch _restoreClipboard = new();
    private readonly ToggleSwitch _showOverlay = new();

    private readonly Label _pageTitle = new() { Font = Display(18f), ForeColor = Fore, AutoSize = true, BackColor = Window };
    private readonly Label _pageSubtitle = new() { Font = Body(9.5f), ForeColor = Muted, AutoSize = true, BackColor = Window };
    private readonly Label _error = new() { Font = Body(9.5f), ForeColor = Red, AutoSize = false, BackColor = Window, TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<(NavButton Button, Page Page, string Title, string Subtitle)> _pages = [];

    /// <param name="suspendHotkeys">Libera le scorciatoie mentre se ne sceglie una nuova.</param>
    /// <param name="resumeHotkeys">Le riattiva a scelta fatta.</param>
    public SettingsForm(DettaturaSettings current, Action suspendHotkeys, Action resumeHotkeys)
    {
        _settings = current.Clone();

        Text = "Dettatura · Impostazioni";
        AutoScaleMode = AutoScaleMode.None;
        Font = Body(10f);
        BackColor = Window;
        ForeColor = Fore;
        ClientSize = new Size(S(880), S(640));
        MinimumSize = new Size(S(760), S(520));
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;

        foreach (var box in new[] { _hotkeyDictate, _hotkeyClaude, _hotkeySelection })
        {
            box.Enter += (_, _) => suspendHotkeys();
            box.Leave += (_, _) => resumeHotkeys();
        }
        _pushToTalk.Selected += (_, _) => SelectMode(DictationMode.PushToTalk);
        _toggle.Selected += (_, _) => SelectMode(DictationMode.Toggle);

        var pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Window };
        AddPage(pageHost, "Dettatura", "Come detti e le scorciatoie da tastiera.", BuildDictationPage());
        AddPage(pageHost, "Trascrizione", "Microfono e Whisper, il modello che trascrive in locale sulla GPU.", BuildTranscriptionPage());
        AddPage(pageHost, "Claude", "Riscrittura leggera, solo quando la chiedi: non cambia il senso e non aggiunge nulla.", BuildClaudePage());
        AddPage(pageHost, "Incolla", "Cosa succede dopo la trascrizione.", BuildPastePage());

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
    public DettaturaSettings? Result { get; private set; }

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
            new SettingsCard(new SettingRow("Modalità", null, modes, below: true)),
            Section("Scorciatoie"),
            new SettingsCard(
                new SettingRow("Detta e incolla", "La trascrizione di Whisper così com'è.", _hotkeyDictate),
                new SettingRow("Detta e riscrivi con Claude", "Whisper, poi una correzione leggera di Claude.", _hotkeyClaude),
                new SettingRow("Riscrivi la selezione", "Sistema con Claude il testo selezionato nella finestra attiva.", _hotkeySelection),
                new SettingRow("Come cambiarle", "Clicca su una scorciatoia e premi la nuova combinazione; Esc annulla. Mentre scegli, le scorciatoie sono sospese.", null)),
            Section("Avanzate"),
            new SettingsCard(
                new SettingRow("Pressione minima (ms)", "Solo con Tieni premuto: le pressioni più brevi sono considerate accidentali.",
                    Frame(_minRecording, S(110), S(34)))));
    }

    private Page BuildTranscriptionPage() => new(
        new SettingsCard(
            new SettingRow("Microfono", null, _microphone),
            new SettingRow("GPU", "Sui portatili con due GPU serve la dedicata: con l'integrata la trascrizione è circa 20 volte più lenta.", _gpu)),
        Section("Whisper"),
        new SettingsCard(
            new SettingRow("Lingua", "Lingua parlata; auto la riconosce da sola ma sbaglia più spesso.", _language),
            new SettingRow("Modello", $"Nome del file in {AppPaths.ModelsDir} oppure percorso completo.", Frame(_whisperModel, 0, S(36)), below: true),
            new SettingRow("Contesto", "Aiuta punteggiatura, nomi e termini tecnici ricorrenti.", Frame(_whisperPrompt, 0, S(74)), below: true)));

    private Page BuildClaudePage() => new(
        new SettingsCard(
            new SettingRow("Modello", "sonnet è il più fedele al testo dettato; haiku è più veloce ma a volte riformula.", _claudeModel),
            new SettingRow("Processo pronto", "Tiene avviato un processo claude: la riscrittura risponde in circa 2 secondi invece di 4-5.", _warmProcess),
            new SettingRow("Timeout (secondi)", "Attesa massima della risposta, poi incolla la trascrizione grezza.", Frame(_timeout, S(110), S(34)))),
        Section("Avanzate"),
        new SettingsCard(
            new SettingRow("Eseguibile", "\"claude\" la cerca nel PATH e in %USERPROFILE%\\.local\\bin.", Frame(_claudeExecutable, 0, S(36)), below: true),
            new SettingRow("Prompt personalizzato", "Vuoto usa il prompt da correttore di bozze predefinito.", Frame(_systemPrompt, 0, S(130)), below: true)));

    private Page BuildPastePage() => new(
        new SettingsCard(
            new SettingRow("Spazio dopo il testo", "Le dettature consecutive non si attaccano.", _appendSpace),
            new SettingRow("Ripristina gli appunti", "Dopo l'incolla gli appunti tornano com'erano.", _restoreClipboard),
            new SettingRow("Finestrella di stato", "In primo piano mentre ascolta, trascrive e incolla, con il pulsante Copia.", _showOverlay)));

    // --- struttura della finestra ---

    private Panel BuildSidebar()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = S(220), BackColor = Sidebar };
        var logo = new MicBadge { Location = new Point(S(20), S(24)) };
        var title = new Label { Text = "Dettatura", Font = Display(14f), ForeColor = Fore, AutoSize = true, BackColor = Sidebar, Location = new Point(S(64), S(22)) };
        var subtitle = new Label { Text = "Impostazioni", Font = Body(9f), ForeColor = Muted, AutoSize = true, BackColor = Sidebar, Location = new Point(S(65), S(48)) };
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
            Text = "Apri settings.json",
            AutoSize = true,
            Font = Body(9.5f),
            LinkColor = Accent,
            ActiveLinkColor = AccentHover,
            VisitedLinkColor = Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
            BackColor = Window,
        };
        link.LinkClicked += (_, _) => TrayApp.OpenFile(AppPaths.SettingsFile);
        new ToolTip().SetToolTip(link, $"{AppPaths.SettingsFile}\nLe modifiche fatte a mano nel file valgono al riavvio dell'app.");

        var save = new ModernButton("Salva", primary: true);
        save.Click += (_, _) => Save();
        var cancel = new ModernButton("Annulla") { DialogResult = DialogResult.Cancel };
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

        _microphone.Items.Add(new Choice(-1, "Predefinito di Windows"));
        foreach (var (index, name) in MicRecorder.ListDevices()) _microphone.Items.Add(new Choice(index, name));
        Select(_microphone, _settings.MicrophoneDevice);

        var gpus = VulkanDevices.List();
        var discrete = gpus.FirstOrDefault(g => g.Discrete);
        _gpu.Items.Add(new Choice(-1, discrete.Name is null ? "Automatica" : $"Automatica · {discrete.Name}"));
        foreach (var (index, name, isDiscrete) in gpus)
        {
            _gpu.Items.Add(new Choice(index, $"{name} ({(isDiscrete ? "dedicata" : "integrata")})"));
        }
        Select(_gpu, _settings.GpuDevice);

        SelectText(_language, _settings.Language);
        _whisperModel.Text = _settings.WhisperModel;
        _whisperPrompt.Text = _settings.WhisperPrompt;

        SelectText(_claudeModel, _settings.Claude.Model);
        _warmProcess.Checked = _settings.Claude.UseWarmProcess;
        _timeout.Value = Math.Clamp(_settings.Claude.TimeoutSeconds, (int)_timeout.Minimum, (int)_timeout.Maximum);
        _claudeExecutable.Text = _settings.Claude.Executable;
        _systemPrompt.Text = _settings.Claude.SystemPrompt ?? string.Empty;

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
            ShowError("Le tre scorciatoie devono essere diverse.", 0);
            return;
        }
        if (string.IsNullOrWhiteSpace(_language.Text) || string.IsNullOrWhiteSpace(_whisperModel.Text))
        {
            ShowError("Lingua e modello di Whisper non possono essere vuoti.", 1);
            return;
        }
        if (string.IsNullOrWhiteSpace(_claudeModel.Text) || string.IsNullOrWhiteSpace(_claudeExecutable.Text))
        {
            ShowError("Modello ed eseguibile di Claude non possono essere vuoti.", 2);
            return;
        }

        _settings.Mode = _toggle.IsSelected ? DictationMode.Toggle : DictationMode.PushToTalk;
        _settings.HotkeyDictate = _hotkeyDictate.Text;
        _settings.HotkeyDictateWithClaude = _hotkeyClaude.Text;
        _settings.HotkeyCleanSelection = _hotkeySelection.Text;
        _settings.MinRecordingMs = (int)_minRecording.Value;

        _settings.MicrophoneDevice = ((Choice)_microphone.SelectedItem!).Value;
        _settings.GpuDevice = ((Choice)_gpu.SelectedItem!).Value;
        _settings.Language = _language.Text.Trim();
        _settings.WhisperModel = _whisperModel.Text.Trim();
        _settings.WhisperPrompt = _whisperPrompt.Text.Trim();

        _settings.Claude.Model = _claudeModel.Text.Trim();
        _settings.Claude.UseWarmProcess = _warmProcess.Checked;
        _settings.Claude.TimeoutSeconds = (int)_timeout.Value;
        _settings.Claude.Executable = _claudeExecutable.Text.Trim();
        _settings.Claude.SystemPrompt = string.IsNullOrWhiteSpace(_systemPrompt.Text) ? null : _systemPrompt.Text.Trim();

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
            item = new Choice(value, $"{value} · non trovato");
            combo.Items.Add(item);
        }
        combo.SelectedItem = item;
    }

    private sealed record Choice(int Value, string Text)
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

    private static FieldFrame Frame(Control inner, int width, int height)
    {
        var frame = new FieldFrame(inner, height);
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

    /// <summary>Pagina scorrevole: impila schede e titoletti di sezione a tutta larghezza.</summary>
    private sealed class Page : Panel
    {
        private bool _arranging;

        public Page(params Control[] items)
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            BackColor = Window;
            Controls.AddRange(items);
            DarkScrollbars(this);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_arranging) return;
            _arranging = true;
            try
            {
                var x = S(28);
                var width = ClientSize.Width - 2 * x;
                var y = 0;
                foreach (Control item in Controls)
                {
                    var height = item is SettingsCard card ? card.Arrange(width) : item.Height;
                    if (item is not SettingsCard) item.Width = width;
                    item.Location = new Point(x, y + AutoScrollPosition.Y);
                    y += height + (item is SettingsCard ? S(18) : S(8));
                }
                AutoScrollMinSize = new Size(0, y + S(8));
            }
            finally
            {
                _arranging = false;
            }
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
