using WhisperAloud.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WhisperAloud.Core.Transcription;
using static WhisperAloud.App.Theme;

namespace WhisperAloud.App;

/// <summary>
/// Finestrella in primo piano, in basso al centro dello schermo: dice cosa sta facendo l'app
/// (in ascolto, trascrizione, riscrittura), mostra il testo incollato e permette di copiarlo.
/// Non prende mai il focus, così l'incolla finisce nella finestra in cui si stava scrivendo.
/// </summary>
internal sealed partial class OverlayForm : Form
{
    private const int ResultSeconds = 10;
    private const int MessageSeconds = 6;
    private const int SilenceWarningMs = 1500;

    private readonly PulseDot _dot = new();
    private readonly Label _title = new();
    private readonly Label _detail = new();
    private readonly Waveform _wave = new();
    private readonly PromptPicker _picker = new();
    private readonly Panel _textFrame = new();
    private readonly RichTextBox _text = new();
    private readonly Font _textFont = Body(10f);
    private readonly Font _textBold = Strong(10f);
    private string _rawText = string.Empty;
    private Point? _dragOffset;
    private bool _dragged;
    private readonly ModernButton _copy = new(Loc.T("Copia", "Copy"), primary: true);
    private readonly ModernButton _close = new(Loc.T("Chiudi", "Close"));
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 50 };
    private readonly System.Windows.Forms.Timer _autoHide = new();
    private readonly Stopwatch _elapsed = new();

    private Func<float>? _levelSource;
    private float _maxLevel;
    private string _recordingTitle = string.Empty;
    private Mode _mode;
    private int _resultTextHeight;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Surface;
        ForeColor = Fore;
        Font = Body(10f);
        Width = S(480);

        _dot.SetBounds(S(16), S(14), S(20), S(20));
        _title.SetBounds(S(42), S(12), S(300), S(24));
        _title.Font = Strong(10.5f);
        _title.ForeColor = Fore;
        _detail.SetBounds(S(330), S(14), S(132), S(22));
        _detail.TextAlign = ContentAlignment.TopRight;
        _detail.ForeColor = Muted;
        _detail.Font = Body(9.5f);
        _wave.SetBounds(S(18), S(46), S(444), S(30));

        _textFrame.SetBounds(S(18), S(46), S(444), S(92));
        _textFrame.BackColor = Surface;
        _textFrame.Padding = new Padding(S(12), S(10), S(6), S(8));
        _textFrame.Paint += (_, e) =>
            FillRounded(e.Graphics, Field, new RectangleF(0.5f, 0.5f, _textFrame.Width - 1.5f, _textFrame.Height - 1.5f), S(8), Border);
        _text.Dock = DockStyle.Fill;
        _text.ReadOnly = true;
        _text.BorderStyle = BorderStyle.None;
        _text.BackColor = Field;
        _text.ForeColor = Fore;
        _text.Font = _textFont;
        _text.ScrollBars = RichTextBoxScrollBars.Vertical; // compare solo se il testo non ci sta
        _text.DetectUrls = false;
        _text.TabStop = false;
        DarkScrollbars(_text);
        _textFrame.Controls.Add(_text);

        _picker.Location = new Point(S(18), S(88));

        _copy.Click += (_, _) => CopyText();
        _close.Click += (_, _) => HideNow();

        Controls.AddRange([_dot, _title, _detail, _wave, _picker, _textFrame, _copy, _close]);

        // Si trascina dallo sfondo, dal titolo o dalla forma d'onda; doppio clic la riporta in basso al centro.
        foreach (var handle in new Control[] { this, _dot, _title, _detail, _wave })
        {
            handle.Cursor = Cursors.SizeAll;
            handle.MouseDown += OnDragStart;
            handle.MouseMove += OnDragMove;
            handle.MouseUp += OnDragEnd;
            handle.DoubleClick += (_, _) => ResetPosition();
        }

        _tick.Tick += (_, _) => OnTick();
        _autoHide.Tick += (_, _) =>
        {
            // Finché il mouse è sopra la finestrella resta aperta: si sta leggendo o copiando.
            if (Bounds.Contains(Cursor.Position)) return;
            HideNow();
        };
    }

    /// <summary>Posizione scelta dall'utente (angolo in alto a sinistra); null = in basso al centro dello schermo.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Point? CustomLocation { get; set; }

    /// <summary>La posizione è cambiata trascinando (null = riportata in basso al centro): va salvata.</summary>
    public event Action<Point?>? PositionChanged;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return parameters;
        }
    }

    /// <summary>
    /// Registrazione in corso: forma d'onda del microfono, avviso se non arriva suono e, se Claude è disponibile,
    /// il selettore per scegliere al volo il prompt (o nessuno) da applicare a questa dettatura.
    /// </summary>
    public void ShowRecording(string title, string hint, Func<float> level,
        IReadOnlyList<string>? prompts = null, string? selectedPrompt = null, Action<string?>? promptChosen = null)
    {
        _picker.Configure(prompts, selectedPrompt, promptChosen);
        _recordingTitle = title;
        _levelSource = level;
        _maxLevel = 0;
        _wave.Reset();
        Present(Red, title, hint, Mode.Recording, autoHideSeconds: 0);
    }

    /// <summary>Lavoro in corso (trascrizione, riscrittura): il contatore dei secondi dice che non è bloccata.</summary>
    public void ShowWorking(string title) => Present(Orange, title, "0,0 s", Mode.Working, autoHideSeconds: 0);

    /// <summary>Testo incollato o da copiare, con il pulsante Copia.</summary>
    /// <param name="sticky">Resta aperta finché non si preme Chiudi: per i risultati che non vengono incollati.</param>
    public void ShowResult(string title, string detail, string text, bool sticky = false)
    {
        var plain = SetText(text);
        _copy.Text = Loc.T("Copia", "Copy");
        // La casella cresce con il testo (una minuta è più lunga di una frase), fino a un massimo.
        var measured = TextRenderer.MeasureText(plain, _textFont, new Size(_textFrame.Width - S(34), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
        _resultTextHeight = Math.Clamp(measured + S(24), S(64), S(340));
        Present(Green, title, detail, Mode.Result, sticky ? 0 : ResultSeconds);
    }

    public void ShowMessage(string title, string message)
    {
        SetText(message);
        Present(Orange, title, string.Empty, Mode.Message, MessageSeconds);
    }

    public void HideNow()
    {
        _tick.Stop();
        _autoHide.Stop();
        _levelSource = null;
        Hide();
    }

    private enum Mode { Working, Recording, Result, Message }

    private void Present(Color color, string title, string detail, Mode mode, int autoHideSeconds)
    {
        _mode = mode;
        _dot.Color = color;
        _dot.Pulsing = mode is Mode.Recording or Mode.Working;
        _title.Text = title;
        _title.ForeColor = Fore;
        _detail.Text = detail;
        // Il dettaglio a destra prende lo spazio che gli serve ("rilascia Ctrl+Alt+Shift+D" è lungo), il titolo il resto.
        var detailWidth = Math.Max(TextRenderer.MeasureText(detail, _detail.Font).Width,
            TextRenderer.MeasureText("00,0 s", _detail.Font).Width) + S(4);
        _detail.SetBounds(Width - S(18) - detailWidth, S(14), detailWidth, S(22));
        _title.Width = Math.Max(S(120), _detail.Left - S(8) - _title.Left);
        _wave.Visible = mode == Mode.Recording;
        // Visible si legge falso finché la finestrella è nascosta: per l'altezza si usa il valore calcolato.
        var showPicker = mode == Mode.Recording && _picker.HasPrompts;
        _picker.Visible = showPicker;
        if (mode != Mode.Recording) _picker.CloseMenu();
        _textFrame.Visible = mode is Mode.Result or Mode.Message;
        _textFrame.Height = mode == Mode.Message ? S(58) : _resultTextHeight;
        _copy.Visible = mode == Mode.Result;
        _close.Visible = mode is Mode.Result or Mode.Message;
        if (mode != Mode.Recording) _levelSource = null;

        var buttonsTop = _textFrame.Bottom + S(12);
        _close.Location = new Point(Width - S(18) - _close.Width, buttonsTop);
        _copy.Location = new Point(_close.Left - S(8) - _copy.Width, buttonsTop);
        Height = mode switch
        {
            Mode.Working => S(48),
            Mode.Recording => showPicker ? S(132) : S(90),
            _ => buttonsTop + _close.Height + S(16),
        };

        PlaceOnScreen();

        _elapsed.Restart();
        _tick.Start();
        _autoHide.Stop();
        if (autoHideSeconds > 0)
        {
            _autoHide.Interval = autoHideSeconds * 1000;
            _autoHide.Start();
        }

        if (!Visible) Show();
        // Rimette la finestrella sopra le altre finestre "sempre in primo piano", senza attivarla.
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        Invalidate(true);
    }

    private void OnTick()
    {
        _dot.Advance();
        switch (_mode)
        {
            case Mode.Recording when _levelSource is not null:
                var level = _levelSource();
                _maxLevel = Math.Max(_maxLevel, level);
                // Radice quadrata: la voce normale (RMS 0,01-0,1) riempie metà o più dell'altezza.
                _wave.Push(Math.Clamp(MathF.Sqrt(level / 0.15f), 0, 1));
                var silent = _elapsed.ElapsedMilliseconds > SilenceWarningMs && _maxLevel < WhisperTranscriber.SilenceRms;
                _title.Text = silent ? Loc.T("Nessun suono dal microfono", "No sound from the microphone") : _recordingTitle;
                _title.ForeColor = silent ? Orange : Fore;
                break;
            case Mode.Working:
                _detail.Text = $"{_elapsed.Elapsed.TotalSeconds:0.0} s";
                break;
            default:
                _tick.Stop();
                break;
        }
    }

    /// <summary>
    /// Mostra il testo con un markdown minimo (titoli e **grassetto** in neretto, elenchi con il pallino)
    /// e restituisce la versione senza simboli, che serve a misurare l'altezza.
    /// </summary>
    private string SetText(string markdown)
    {
        _rawText = markdown.Trim();
        _text.Clear();
        var plain = new System.Text.StringBuilder();
        var lines = _rawText.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var heading = line.StartsWith('#');
            if (heading) line = line.TrimStart('#').Trim();
            if (line.StartsWith("- ") || line.StartsWith("* ")) line = "•  " + line[2..];

            var parts = line.Split("**");
            for (var j = 0; j < parts.Length; j++)
            {
                _text.SelectionStart = _text.TextLength;
                _text.SelectionFont = heading || j % 2 == 1 ? _textBold : _textFont;
                _text.SelectionColor = Fore;
                _text.AppendText(parts[j]);
                plain.Append(parts[j]);
            }
            if (i < lines.Length - 1)
            {
                _text.AppendText("\n");
                plain.Append(Environment.NewLine);
            }
        }
        _text.SelectionStart = 0;
        return plain.ToString();
    }

    private void PlaceOnScreen()
    {
        if (CustomLocation is { } custom)
        {
            // Posizione scelta trascinando, tenuta dentro lo schermo anche quando la finestrella cresce.
            var screen = Screen.FromPoint(custom).WorkingArea;
            Location = new Point(
                Math.Clamp(custom.X, screen.Left, Math.Max(screen.Left, screen.Right - Width)),
                Math.Clamp(custom.Y, screen.Top, Math.Max(screen.Top, screen.Bottom - Height)));
            return;
        }
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - S(56));
    }

    private void OnDragStart(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
        _dragged = false;
    }

    private void OnDragMove(object? sender, MouseEventArgs e)
    {
        if (_dragOffset is not { } offset) return;
        var target = new Point(Cursor.Position.X - offset.X, Cursor.Position.Y - offset.Y);
        if (target == Location) return;
        Location = target;
        _dragged = true;
    }

    private void OnDragEnd(object? sender, MouseEventArgs e)
    {
        _dragOffset = null;
        if (!_dragged) return;
        _dragged = false;
        CustomLocation = Location;
        PositionChanged?.Invoke(Location);
    }

    private void ResetPosition()
    {
        CustomLocation = null;
        PlaceOnScreen();
        PositionChanged?.Invoke(null);
    }

    private void CopyText()
    {
        if (string.IsNullOrEmpty(_rawText)) return;
        try
        {
            Clipboard.SetText(_rawText); // si copia il markdown originale, non la versione formattata
            _copy.Text = Loc.T("Copiato", "Copied");
        }
        catch (ExternalException)
        {
            _copy.Text = Loc.T("Riprova", "Try again");
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        StyleWindow(Handle); // angoli arrotondati e bordo sottile su Windows 11
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Dispose();
            _autoHide.Dispose();
            _textFont.Dispose();
            _textBold.Dispose();
        }
        base.Dispose(disposing);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    /// <summary>Pillola "Claude: Correzione ▾" che apre il menu dei prompt; la prima voce è "Solo trascrizione".</summary>
    private sealed class PromptPicker : PaintedControl
    {
        private readonly ContextMenuStrip _menu = new()
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = Theme.Surface,
            ForeColor = Fore,
            Font = Body(9.5f),
            ShowImageMargin = true,
        };
        private IReadOnlyList<string> _prompts = [];
        private string? _selected;
        private Action<string?>? _chosen;

        public PromptPicker()
        {
            Font = Body(9.5f);
            Cursor = Cursors.Hand;
            BackColor = Theme.Surface;
            Height = S(32);
        }

        public bool HasPrompts => _prompts.Count > 0;

        public void Configure(IReadOnlyList<string>? prompts, string? selected, Action<string?>? chosen)
        {
            _prompts = prompts ?? [];
            _chosen = chosen;
            Select(selected);
        }

        public void CloseMenu() => _menu.Close();

        private void Select(string? name)
        {
            _selected = name;
            Text = name is null ? Loc.T("Solo trascrizione", "Transcription only") : $"Claude · {name}";
            Width = TextRenderer.MeasureText(Text, Font).Width + S(48);
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _menu.Items.Clear();
            AddItem(Loc.T("Solo trascrizione", "Transcription only"), null);
            _menu.Items.Add(new ToolStripSeparator());
            foreach (var prompt in _prompts) AddItem(prompt, prompt);
            _menu.Show(this, new Point(0, Height + S(4)));
        }

        private void AddItem(string text, string? value)
        {
            var item = new ToolStripMenuItem(text) { Checked = value == _selected, ForeColor = Fore };
            item.Click += (_, _) =>
            {
                Select(value);
                _chosen?.Invoke(value);
            };
            _menu.Items.Add(item);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            var active = _selected is not null;
            FillRounded(g, Hovered ? SurfaceHover : Field, bounds, bounds.Height / 2, active ? Accent : Border);

            // Pallino d'accento quando Claude è attivo, grigio per la sola trascrizione.
            var dot = S(7);
            using (var brush = new SolidBrush(active ? Accent : Muted))
            {
                g.FillEllipse(brush, S(13), (Height - dot) / 2f, dot, dot);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(S(26), 0, Width - S(46), Height), Fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            var cx = Width - S(16);
            var cy = Height / 2f;
            var arm = S(3);
            using var pen = new Pen(Muted, Math.Max(1.5f, S(2) * 0.75f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, [new PointF(cx - arm, cy - arm / 2f), new PointF(cx, cy + arm / 2f), new PointF(cx + arm, cy - arm / 2f)]);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _menu.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Pallino di stato con un alone che pulsa mentre l'app ascolta o lavora.</summary>
    private sealed class PulseDot : Control
    {
        private Color _color = Color.Gray;
        private float _phase;

        public PulseDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Surface;
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color Color
        {
            get => _color;
            set { _color = value; Invalidate(); }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Pulsing { get; set; }

        public void Advance()
        {
            if (!Pulsing) return;
            _phase = (_phase + 0.06f) % 1f;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var core = Width * 0.5f;
            if (Pulsing)
            {
                var halo = core + (Width - core) * _phase;
                using var haloBrush = new SolidBrush(Color.FromArgb((int)(110 * (1 - _phase)), _color));
                g.FillEllipse(haloBrush, (Width - halo) / 2, (Height - halo) / 2, halo, halo);
            }
            using var brush = new SolidBrush(_color);
            g.FillEllipse(brush, (Width - core) / 2, (Height - core) / 2, core, core);
        }
    }

    /// <summary>Forma d'onda: barre verticali arrotondate che scorrono con il livello del microfono.</summary>
    private sealed class Waveform : Control
    {
        private readonly float[] _levels = new float[48];
        private int _head;

        public Waveform()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Surface;
        }

        public void Reset()
        {
            Array.Clear(_levels);
            Invalidate();
        }

        public void Push(float level)
        {
            _levels[_head] = level;
            _head = (_head + 1) % _levels.Length;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var count = _levels.Length;
            var step = Width / (float)count;
            var barWidth = Math.Max(2f, step * 0.55f);
            var middle = Height / 2f;
            for (var i = 0; i < count; i++)
            {
                var level = _levels[(_head + i) % count];
                var height = Math.Max(barWidth, level * Height);
                // Le barre più vecchie (a sinistra) sfumano.
                var alpha = (int)(70 + 185 * (i / (float)count));
                using var brush = new SolidBrush(Color.FromArgb(alpha, level > 0.02f ? Red : Muted));
                using var path = RoundedRect(new RectangleF(i * step + (step - barWidth) / 2, middle - height / 2, barWidth, height), barWidth / 2);
                g.FillPath(brush, path);
            }
        }
    }
}
