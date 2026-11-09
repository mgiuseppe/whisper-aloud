using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Dettatura.Core.Transcription;

namespace Dettatura.App;

/// <summary>
/// Finestrella in primo piano, in basso al centro dello schermo: dice cosa sta facendo l'app
/// (in ascolto, trascrizione, riscrittura), mostra il testo incollato e permette di copiarlo.
/// Non prende mai il focus, così l'incolla finisce nella finestra in cui si stava scrivendo.
/// </summary>
internal sealed partial class OverlayForm : Form
{
    private static readonly Color Background = Color.FromArgb(30, 30, 34);
    private static readonly Color TextColor = Color.FromArgb(236, 236, 240);
    private static readonly Color MutedColor = Color.FromArgb(150, 150, 160);
    private static readonly Color BoxColor = Color.FromArgb(44, 44, 50);
    private static readonly Color Red = Color.FromArgb(232, 64, 64);
    private static readonly Color Orange = Color.FromArgb(240, 165, 30);
    private static readonly Color Green = Color.FromArgb(70, 190, 110);

    private const int ResultSeconds = 10;
    private const int MessageSeconds = 6;
    private const int SilenceWarningMs = 1500;

    private readonly Dot _dot = new();
    private readonly Label _title = new();
    private readonly Label _detail = new();
    private readonly LevelBar _level = new();
    private readonly TextBox _text = new();
    private readonly Button _copy;
    private readonly Button _close;
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _autoHide = new();
    private readonly Stopwatch _elapsed = new();

    private Func<float>? _levelSource;
    private float _maxLevel;
    private string _recordingTitle = string.Empty;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = Background;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 10f);
        Width = S(460);

        _dot.SetBounds(S(16), S(17), S(12), S(12));
        _title.SetBounds(S(36), S(12), S(300), S(22));
        _title.Font = new Font("Segoe UI Semibold", 10.5f);
        _detail.SetBounds(S(330), S(13), S(114), S(22));
        _detail.TextAlign = ContentAlignment.TopRight;
        _detail.ForeColor = MutedColor;
        _level.SetBounds(S(16), S(44), S(428), S(6));

        _text.SetBounds(S(16), S(44), S(428), S(84));
        _text.Multiline = true;
        _text.ReadOnly = true;
        _text.BorderStyle = BorderStyle.None;
        _text.BackColor = BoxColor;
        _text.ForeColor = TextColor;
        _text.ScrollBars = ScrollBars.Vertical;
        _text.TabStop = false;

        _copy = MakeButton("Copia", S(268));
        _copy.Click += (_, _) => CopyText();
        _close = MakeButton("Chiudi", S(360));
        _close.Click += (_, _) => HideNow();

        Controls.AddRange([_dot, _title, _detail, _level, _text, _copy, _close]);

        _tick.Tick += (_, _) => OnTick();
        _autoHide.Tick += (_, _) =>
        {
            // Finché il mouse è sopra la finestrella resta aperta: si sta leggendo o copiando.
            if (Bounds.Contains(Cursor.Position)) return;
            HideNow();
        };
    }

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

    /// <summary>Registrazione in corso: indicatore di livello e avviso se dal microfono non arriva suono.</summary>
    public void ShowRecording(string title, string hint, Func<float> level)
    {
        _recordingTitle = title;
        _levelSource = level;
        _maxLevel = 0;
        _level.Value = 0;
        Present(Red, title, hint, Mode.Recording, autoHideSeconds: 0);
    }

    /// <summary>Lavoro in corso (trascrizione, riscrittura): il contatore dei secondi dice che non è bloccata.</summary>
    public void ShowWorking(string title) => Present(Orange, title, "0,0 s", Mode.Compact, autoHideSeconds: 0);

    /// <summary>Testo incollato, con il pulsante per copiarlo di nuovo.</summary>
    public void ShowResult(string title, string detail, string text)
    {
        _text.Text = text;
        _copy.Text = "Copia";
        Present(Green, title, detail, Mode.Result, ResultSeconds);
    }

    public void ShowMessage(string title, string message)
    {
        _text.Text = message;
        Present(Orange, title, string.Empty, Mode.Message, MessageSeconds);
    }

    public void HideNow()
    {
        _tick.Stop();
        _autoHide.Stop();
        _levelSource = null;
        Hide();
    }

    private enum Mode { Compact, Recording, Result, Message }

    private void Present(Color color, string title, string detail, Mode layout, int autoHideSeconds)
    {
        _dot.Color = color;
        _title.Text = title;
        _title.ForeColor = TextColor;
        _detail.Text = detail;
        _level.Visible = layout == Mode.Recording;
        _text.Visible = layout is Mode.Result or Mode.Message;
        _text.Height = layout == Mode.Message ? S(44) : S(84);
        _copy.Visible = layout == Mode.Result;
        _close.Visible = layout is Mode.Result or Mode.Message;
        if (layout != Mode.Recording) _levelSource = null;

        var buttonsTop = _text.Bottom + S(10);
        _copy.Top = _close.Top = buttonsTop;
        Height = layout switch
        {
            Mode.Compact => S(46),
            Mode.Recording => S(64),
            _ => buttonsTop + _close.Height + S(12),
        };

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - S(56));

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
        if (_levelSource is not null)
        {
            var level = _levelSource();
            _maxLevel = Math.Max(_maxLevel, level);
            // Radice quadrata: la voce normale (RMS 0,01-0,1) riempie la barra a metà o più.
            _level.Value = Math.Max(Math.Clamp(MathF.Sqrt(level / 0.15f), 0, 1), _level.Value * 0.85f);
            if (_elapsed.ElapsedMilliseconds > SilenceWarningMs && _maxLevel < WhisperTranscriber.SilenceRms)
            {
                _title.Text = "Nessun suono dal microfono";
                _title.ForeColor = Orange;
            }
            else if (_title.ForeColor == Orange)
            {
                _title.Text = _recordingTitle;
                _title.ForeColor = TextColor;
            }
        }
        else if (_dot.Color == Orange && !_text.Visible)
        {
            _detail.Text = $"{_elapsed.Elapsed.TotalSeconds:0.0} s";
        }
        else
        {
            _tick.Stop();
        }
    }

    private void CopyText()
    {
        if (string.IsNullOrEmpty(_text.Text)) return;
        try
        {
            Clipboard.SetText(_text.Text);
            _copy.Text = "Copiato";
        }
        catch (ExternalException)
        {
            _copy.Text = "Riprova";
        }
    }

    private Button MakeButton(string text, int left)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = BoxColor,
            ForeColor = TextColor,
            TabStop = false,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 78);
        button.SetBounds(left, 0, S(84), S(30));
        return button;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11: angoli arrotondati come le finestre di sistema. Su Windows 10 la chiamata viene ignorata.
        var round = 2;
        _ = DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Dispose();
            _autoHide.Dispose();
        }
        base.Dispose(disposing);
    }

    private int S(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96f);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private sealed class Dot : Control
    {
        private Color _color = Color.Gray;

        public Dot()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Background;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color Color
        {
            get => _color;
            set { _color = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(_color);
            e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
        }
    }

    private sealed class LevelBar : Control
    {
        private float _value;

        public LevelBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = BoxColor;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float Value
        {
            get => _value;
            set { _value = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BoxColor);
            using var brush = new SolidBrush(Red);
            e.Graphics.FillRectangle(brush, 0, 0, (int)(Width * _value), Height);
        }
    }
}
