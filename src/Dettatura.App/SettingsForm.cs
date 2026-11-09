using Dettatura.Core;
using Dettatura.Core.Transcription;

namespace Dettatura.App;

/// <summary>
/// Finestra delle impostazioni: modifica una copia di <see cref="DettaturaSettings"/> e la restituisce in
/// <see cref="Result"/> solo se si preme Salva. Il file settings.json resta apribile per le voci avanzate.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly DettaturaSettings _settings;

    private readonly RadioButton _pushToTalk = Radio("Tieni premuta la scorciatoia mentre parli (push-to-talk)");
    private readonly RadioButton _toggle = Radio("Premi la scorciatoia per iniziare, premila di nuovo per finire");
    private readonly HotkeyBox _hotkeyDictate = new();
    private readonly HotkeyBox _hotkeyClaude = new();
    private readonly HotkeyBox _hotkeySelection = new();
    private readonly NumericUpDown _minRecording = Number(0, 5000, 50);

    private readonly ComboBox _microphone = DropDownList();
    private readonly ComboBox _gpu = DropDownList();
    private readonly ComboBox _language = new() { Items = { "it", "en", "auto" } };
    private readonly TextBox _whisperModel = new();
    private readonly TextBox _whisperPrompt = MultilineBox(2);

    private readonly ComboBox _claudeModel = new() { Items = { "sonnet", "haiku", "opus" } };
    private readonly CheckBox _warmProcess = Check("Tieni pronto un processo claude per la dettatura successiva (più veloce)");
    private readonly NumericUpDown _timeout = Number(5, 300, 5);
    private readonly TextBox _claudeExecutable = new();
    private readonly TextBox _systemPrompt = MultilineBox(5);

    private readonly CheckBox _appendSpace = Check("Aggiungi uno spazio dopo il testo incollato");
    private readonly CheckBox _restoreClipboard = Check("Ripristina gli appunti com'erano prima dell'incolla");
    private readonly CheckBox _showOverlay = Check("Mostra la finestrella di stato in primo piano");

    /// <param name="suspendHotkeys">Libera le scorciatoie mentre se ne scrive una nuova in un campo.</param>
    /// <param name="resumeHotkeys">Le riattiva quando il campo perde il focus.</param>
    public SettingsForm(DettaturaSettings current, Action suspendHotkeys, Action resumeHotkeys)
    {
        _settings = current.Clone();
        foreach (var box in new[] { _hotkeyDictate, _hotkeyClaude, _hotkeySelection })
        {
            box.Enter += (_, _) => suspendHotkeys();
            box.Leave += (_, _) => resumeHotkeys();
        }

        Text = "Dettatura · Impostazioni";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(680, 580);
        MinimumSize = new Size(600, 480);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 5) };
        tabs.TabPages.Add(BuildDictationTab());
        tabs.TabPages.Add(BuildTranscriptionTab());
        tabs.TabPages.Add(BuildClaudeTab());
        tabs.TabPages.Add(BuildOtherTab());

        Controls.Add(tabs);
        Controls.Add(BuildFooter());
        LoadValues();
    }

    /// <summary>Impostazioni modificate, valorizzate solo dopo Salva.</summary>
    public DettaturaSettings? Result { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate(); // aperta dal tray, altrimenti resta dietro la finestra attiva
    }

    private TabPage BuildDictationTab()
    {
        var grid = Grid();
        Section(grid, "Come si detta");
        Full(grid, _pushToTalk);
        Full(grid, _toggle);
        Section(grid, "Scorciatoie (clicca nel campo e premi la combinazione)");
        Full(grid, Note("Mentre il cursore è in uno di questi campi le scorciatoie sono sospese; altrove funzionano anche a finestra aperta."));
        Row(grid, "Detta e incolla", _hotkeyDictate);
        Row(grid, "Detta, riscrivi con Claude", _hotkeyClaude);
        Row(grid, "Riscrivi la selezione", _hotkeySelection);
        Section(grid, "Altro");
        Row(grid, "Pressione minima (ms)", _minRecording,
            "Solo push-to-talk: le pressioni più brevi sono considerate accidentali.");
        return Page("Dettatura", grid);
    }

    private TabPage BuildTranscriptionTab()
    {
        var grid = Grid();
        Row(grid, "Microfono", _microphone);
        Row(grid, "GPU per Whisper", _gpu,
            "Sui portatili con due GPU serve quella dedicata: con l'integrata la trascrizione è circa 20 volte più lenta.");
        Row(grid, "Lingua", _language, "Codice di lingua di Whisper (it, en, ...) oppure auto.");
        Row(grid, "Modello Whisper", _whisperModel, $"Nome del file in {AppPaths.ModelsDir} oppure percorso completo.");
        Row(grid, "Contesto per Whisper", _whisperPrompt, "Aiuta punteggiatura, nomi e termini tecnici ricorrenti.");
        return Page("Trascrizione", grid);
    }

    private TabPage BuildClaudeTab()
    {
        var grid = Grid();
        Row(grid, "Modello", _claudeModel, "sonnet è il più fedele al testo dettato; haiku è più veloce ma a volte riformula.");
        Full(grid, _warmProcess);
        Row(grid, "Timeout (secondi)", _timeout);
        Row(grid, "Eseguibile", _claudeExecutable, "\"claude\" la cerca nel PATH e in %USERPROFILE%\\.local\\bin.");
        Row(grid, "Prompt personalizzato", _systemPrompt, "Vuoto usa il prompt da correttore di bozze predefinito.");
        return Page("Claude", grid);
    }

    private TabPage BuildOtherTab()
    {
        var grid = Grid();
        Full(grid, _appendSpace);
        Full(grid, _restoreClipboard);
        Full(grid, _showOverlay);
        return Page("Incolla e finestrella", grid);
    }

    private Panel BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 3,
            AutoSize = true,
            Padding = new Padding(10, 8, 10, 10),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var link = new LinkLabel { Text = "Apri settings.json", AutoSize = true, Anchor = AnchorStyles.Left };
        link.LinkClicked += (_, _) => TrayApp.OpenFile(AppPaths.SettingsFile);
        new ToolTip().SetToolTip(link, $"{AppPaths.SettingsFile}\nLe modifiche fatte a mano nel file valgono al riavvio dell'app.");

        var save = new Button { Text = "Salva", AutoSize = true, MinimumSize = new Size(90, 0) };
        save.Click += (_, _) => Save();
        var cancel = new Button { Text = "Annulla", AutoSize = true, MinimumSize = new Size(90, 0), DialogResult = DialogResult.Cancel };
        AcceptButton = save;
        CancelButton = cancel;

        footer.Controls.Add(link, 0, 0);
        footer.Controls.Add(save, 1, 0);
        footer.Controls.Add(cancel, 2, 0);
        return footer;
    }

    private void LoadValues()
    {
        (_settings.Mode == DictationMode.Toggle ? _toggle : _pushToTalk).Checked = true;
        _hotkeyDictate.Text = _settings.HotkeyDictate;
        _hotkeyClaude.Text = _settings.HotkeyDictateWithClaude;
        _hotkeySelection.Text = _settings.HotkeyCleanSelection;
        _minRecording.Value = Math.Clamp(_settings.MinRecordingMs, (int)_minRecording.Minimum, (int)_minRecording.Maximum);

        _microphone.Items.Add(new Choice(-1, "Predefinito di Windows"));
        foreach (var (index, name) in MicRecorder.ListDevices()) _microphone.Items.Add(new Choice(index, name));
        Select(_microphone, _settings.MicrophoneDevice);

        var gpus = VulkanDevices.List();
        var discrete = gpus.FirstOrDefault(g => g.Discrete);
        _gpu.Items.Add(new Choice(-1, discrete.Name is null ? "Automatica" : $"Automatica ({discrete.Name})"));
        foreach (var (index, name, isDiscrete) in gpus)
        {
            _gpu.Items.Add(new Choice(index, $"{index} · {name} ({(isDiscrete ? "dedicata" : "integrata")})"));
        }
        Select(_gpu, _settings.GpuDevice);

        _language.Text = _settings.Language;
        _whisperModel.Text = _settings.WhisperModel;
        _whisperPrompt.Text = _settings.WhisperPrompt;

        _claudeModel.Text = _settings.Claude.Model;
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
            MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (hotkeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() < hotkeys.Length)
        {
            MessageBox.Show(this, "Le tre scorciatoie devono essere diverse.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_language.Text) || string.IsNullOrWhiteSpace(_whisperModel.Text) ||
            string.IsNullOrWhiteSpace(_claudeModel.Text) || string.IsNullOrWhiteSpace(_claudeExecutable.Text))
        {
            MessageBox.Show(this, "Lingua, modello Whisper, modello ed eseguibile di Claude non possono essere vuoti.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.Mode = _toggle.Checked ? DictationMode.Toggle : DictationMode.PushToTalk;
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

    // --- costruzione della griglia: etichetta a sinistra, controllo a destra, nota grigia sotto ---

    private static TableLayoutPanel Grid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12, 10, 16, 10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    private static TabPage Page(string title, TableLayoutPanel grid)
    {
        AddRow(grid); // riga vuota finale: prende lo spazio avanzato, così le altre restano in alto
        var page = new TabPage(title) { UseVisualStyleBackColor = true };
        page.Controls.Add(grid);
        return page;
    }

    private static void Row(TableLayoutPanel grid, string label, Control control, string? hint = null)
    {
        var row = AddRow(grid);
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 7, 14, 3) }, 0, row);
        control.Anchor = control is NumericUpDown ? AnchorStyles.Left | AnchorStyles.Top : AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        control.Margin = new Padding(0, 4, 0, hint is null ? 6 : 0);
        grid.Controls.Add(control, 1, row);
        if (hint is null) return;

        row = AddRow(grid);
        var note = new Label { Text = hint, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 8), MaximumSize = new Size(460, 0) };
        grid.Controls.Add(note, 1, row);
    }

    private static void Full(TableLayoutPanel grid, Control control)
    {
        var row = AddRow(grid);
        control.Margin = new Padding(0, 4, 0, 4);
        grid.Controls.Add(control, 0, row);
        grid.SetColumnSpan(control, 2);
    }

    private static void Section(TableLayoutPanel grid, string title)
    {
        var label = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5f),
            Margin = new Padding(0, grid.RowCount == 0 ? 2 : 12, 0, 4),
        };
        Full(grid, label);
    }

    private static int AddRow(TableLayoutPanel grid)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return grid.RowCount++;
    }

    private static Label Note(string text) =>
        new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(620, 0) };

    private static RadioButton Radio(string text) => new() { Text = text, AutoSize = true };

    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true };

    private static ComboBox DropDownList() => new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private static NumericUpDown Number(int min, int max, int step) =>
        new() { Minimum = min, Maximum = max, Increment = step, Width = 90, Anchor = AnchorStyles.Left };

    private static TextBox MultilineBox(int lines) =>
        new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 10 + lines * 23, AcceptsReturn = true };
}

/// <summary>Campo che registra la combinazione di tasti premuta, nel formato delle impostazioni ("Ctrl+Alt+D").</summary>
internal sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        ShortcutsEnabled = false;
        Cursor = Cursors.Hand;
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
        var key = e.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;

        var parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Alt) parts.Add("Alt");
        if (e.Shift) parts.Add("Shift");
        if (Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN)) parts.Add("Win");
        // Senza modificatori la scorciatoia ruberebbe il tasto a ogni app: ammessi solo i tasti funzione.
        if (parts.Count == 0 && key is not (>= Keys.F1 and <= Keys.F24)) return;

        parts.Add(key.ToString());
        Text = string.Join("+", parts);
    }
}
