using System.ComponentModel;
using System.Drawing.Drawing2D;
using static WhisperAloud.App.Theme;

namespace WhisperAloud.App;

/// <summary>Base dei controlli disegnati a mano: doppio buffer, niente sfarfallio, proprietà nascoste al designer.</summary>
internal abstract class PaintedControl : Control
{
    protected PaintedControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected bool Hovered { get; private set; }

    protected override void OnMouseEnter(EventArgs e) { Hovered = true; Invalidate(); base.OnMouseEnter(e); }

    protected override void OnMouseLeave(EventArgs e) { Hovered = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
}

/// <summary>Pulsante arrotondato: pieno col colore d'accento (principale) o tenue (secondario).</summary>
internal sealed class ModernButton : PaintedControl, IButtonControl
{
    private bool _pressed;

    public ModernButton(string text, bool primary = false)
    {
        Text = text;
        Primary = primary;
        Font = Strong(9.5f);
        Cursor = Cursors.Hand;
        var width = TextRenderer.MeasureText(text, Font).Width + S(36);
        Size = new Size(Math.Max(width, S(92)), S(34));
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DialogResult DialogResult { get; set; }

    public void NotifyDefault(bool value) { }

    public void PerformClick() => OnClick(EventArgs.Empty);

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (DialogResult != DialogResult.None && FindForm() is { } form) form.DialogResult = DialogResult;
    }

    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }

    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) PerformClick();
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var fill = Primary
            ? (_pressed ? Accent : Hovered ? AccentHover : Accent)
            : (_pressed ? Field : Hovered ? SurfaceHover : Surface);
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRounded(e.Graphics, fill, bounds, S(6), Primary ? null : Border);
        if (Focused) FillFocus(e.Graphics, bounds);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Primary ? Color.White : Fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void FillFocus(Graphics g, RectangleF bounds)
    {
        using var path = RoundedRect(RectangleF.Inflate(bounds, -2, -2), S(5));
        using var pen = new Pen(Color.FromArgb(160, Color.White));
        g.DrawPath(pen, path);
    }
}

/// <summary>Interruttore acceso/spento in stile Windows 11.</summary>
internal sealed class ToggleSwitch : PaintedControl
{
    private bool _checked;

    public ToggleSwitch()
    {
        Size = new Size(S(44), S(24));
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public event EventHandler? CheckedChanged;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e) { Focus(); Checked = !Checked; base.OnClick(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) Checked = !Checked;
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var radius = track.Height / 2;
        if (_checked) FillRounded(g, Hovered ? AccentHover : Accent, track, radius);
        else FillRounded(g, Hovered ? SurfaceHover : Field, track, radius, Focused ? Fore : Muted);

        var knob = Height - S(_checked ? 8 : 10);
        var x = _checked ? Width - knob - S(4) : S(5);
        using var brush = new SolidBrush(_checked ? Color.White : Muted);
        g.FillEllipse(brush, x, (Height - knob) / 2f, knob, knob);
    }
}

/// <summary>Cornice arrotondata attorno a TextBox o NumericUpDown, che si colora d'accento quando il campo ha il focus.</summary>
internal sealed class FieldFrame : Panel
{
    private readonly Control _inner;
    private readonly bool _resizable;
    private readonly int _compactHeight;
    private int? _dragStartY;
    private int _dragStartHeight;
    private bool _gripHovered;

    /// <param name="resizable">
    /// Mostra in basso una maniglia: trascinandola il riquadro si allunga per scrivere comodamente,
    /// con un doppio clic passa da compatto a grande.
    /// </param>
    public FieldFrame(Control inner, int height, bool resizable = false)
    {
        _inner = inner;
        _resizable = resizable;
        _compactHeight = height;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Surface;
        Height = height;
        Padding = new Padding(S(10), S(7), S(6), resizable ? S(16) : S(6));
        if (resizable) new ToolTip().SetToolTip(this, "Trascina per ingrandire, doppio clic per allargare o ridurre");
        inner.BackColor = Field;
        inner.ForeColor = Fore;
        inner.Font = Body(10f);
        inner.Dock = DockStyle.Fill;
        if (inner is TextBox box) box.BorderStyle = BorderStyle.None;
        if (inner is NumericUpDown number) number.BorderStyle = BorderStyle.None;
        inner.GotFocus += (_, _) => Invalidate();
        inner.LostFocus += (_, _) => Invalidate();
        Controls.Add(inner);
        DarkScrollbars(inner);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRounded(e.Graphics, Field, bounds, S(6), _inner.ContainsFocus ? Accent : Border);
        if (_inner.ContainsFocus)
        {
            // Riga d'accento più spessa in basso, come i campi di testo di Windows 11.
            using var pen = new Pen(Accent, S(2));
            e.Graphics.DrawLine(pen, S(6), Height - S(1), Width - S(7), Height - S(1));
        }
        if (_resizable)
        {
            // Maniglia: due trattini centrati in basso.
            var color = _gripHovered || _dragStartY is not null ? Fore : Muted;
            using var grip = new Pen(color, Math.Max(1.5f, S(2) * 0.75f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var cx = Width / 2f;
            var y = Height - S(10);
            e.Graphics.DrawLine(grip, cx - S(14), y, cx + S(14), y);
            e.Graphics.DrawLine(grip, cx - S(8), y + S(4), cx + S(8), y + S(4));
        }
    }

    private bool InGrip(Point location) => _resizable && location.Y >= Height - S(16);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && InGrip(e.Location))
        {
            _dragStartY = Cursor.Position.Y;
            _dragStartHeight = Height;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragStartY is { } startY)
        {
            Resize(_dragStartHeight + Cursor.Position.Y - startY);
        }
        else
        {
            var hovered = InGrip(e.Location);
            Cursor = hovered ? Cursors.SizeNS : Cursors.IBeam;
            if (hovered != _gripHovered)
            {
                _gripHovered = hovered;
                Invalidate();
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragStartY = null;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _gripHovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        if (InGrip(PointToClient(Cursor.Position)))
        {
            Resize(Height > _compactHeight + S(20) ? _compactHeight : Math.Max(_compactHeight, S(340)));
        }
        base.OnDoubleClick(e);
    }

    /// <summary>Cambia l'altezza e chiede ai contenitori di ridisporsi, così le schede sotto scorrono.</summary>
    private new void Resize(int height)
    {
        height = Math.Clamp(height, _compactHeight, S(800));
        if (height == Height) return;
        Height = height;
        for (var parent = Parent; parent is not null; parent = parent.Parent) parent.PerformLayout();
    }

    protected override void OnClick(EventArgs e)
    {
        if (!InGrip(PointToClient(Cursor.Position))) _inner.Focus();
        base.OnClick(e);
    }
}

/// <summary>Scheda selezionabile con titolo e descrizione: le scelte esclusive al posto dei radio button.</summary>
internal sealed class OptionCard : PaintedControl
{
    private bool _selected;

    public OptionCard(string title, string description)
    {
        Text = title;
        Description = description;
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Surface;
    }

    public event EventHandler? Selected;

    public string Description { get; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsSelected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    protected override void OnClick(EventArgs e) { Focus(); Selected?.Invoke(this, EventArgs.Empty); base.OnClick(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) Selected?.Invoke(this, EventArgs.Empty);
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRounded(g, _selected ? Color.FromArgb(44, 41, 66) : Hovered ? SurfaceHover : Field, bounds, S(8),
            _selected ? Accent : Focused ? Muted : Border);

        // Pallino di selezione.
        var dot = S(16);
        var dotRect = new RectangleF(S(14), S(15), dot, dot);
        using (var pen = new Pen(_selected ? Accent : Muted, S(2))) g.DrawEllipse(pen, dotRect);
        if (_selected)
        {
            using var brush = new SolidBrush(Accent);
            g.FillEllipse(brush, RectangleF.Inflate(dotRect, -S(4), -S(4)));
        }

        var textLeft = S(40);
        using var title = Strong(10f);
        using var body = Body(9f);
        TextRenderer.DrawText(g, Text, title, new Rectangle(textLeft, S(12), Width - textLeft - S(12), S(22)), Theme.Fore,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, Description, body, new Rectangle(textLeft, S(34), Width - textLeft - S(12), Height - S(40)), Muted,
            TextFormatFlags.Left | TextFormatFlags.WordBreak);
    }
}

/// <summary>Voce della barra laterale.</summary>
internal sealed class NavButton : PaintedControl
{
    private bool _selected;

    public NavButton(string text)
    {
        Text = text;
        Font = Body(10f);
        Cursor = Cursors.Hand;
        Height = S(38);
        BackColor = Sidebar;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_selected || Hovered)
        {
            FillRounded(g, _selected ? SurfaceHover : Surface, new RectangleF(0, 0, Width - 1, Height - 1), S(6));
        }
        if (_selected)
        {
            FillRounded(g, Accent, new RectangleF(0, S(10), S(3), Height - S(20)), S(2));
        }
        using var font = _selected ? Strong(10f) : Body(10f);
        TextRenderer.DrawText(g, Text, font, new Rectangle(S(14), 0, Width - S(14), Height), _selected ? Theme.Fore : Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Riga di impostazione: titolo e descrizione a sinistra, controllo a destra oppure sotto a tutta larghezza.</summary>
internal sealed class SettingRow : Panel
{
    private readonly Label _title;
    private readonly Label? _description;
    private readonly Control? _control;
    private readonly bool _below;

    public SettingRow(string title, string? description, Control? control, bool below = false)
    {
        BackColor = Surface;
        _below = below;
        _control = control;
        _title = new Label { Text = title, Font = Body(10f), ForeColor = Fore, BackColor = Surface, AutoSize = false };
        Controls.Add(_title);
        if (description is not null)
        {
            _description = new Label { Text = description, Font = Body(9f), ForeColor = Muted, BackColor = Surface, AutoSize = false };
            Controls.Add(_description);
        }
        if (control is not null) Controls.Add(control);
    }

    /// <summary>Dispone i figli per la larghezza data e restituisce l'altezza necessaria.</summary>
    public int Arrange(int width)
    {
        var pad = S(14);
        var gap = S(20);
        var textWidth = _below || _control is null ? width : width - _control.Width - gap;
        var titleHeight = Measure(_title, textWidth);
        var descriptionHeight = _description is null ? 0 : Measure(_description, textWidth);
        var textHeight = titleHeight + (_description is null ? 0 : S(2) + descriptionHeight);

        int height;
        int textTop;
        if (_below && _control is not null)
        {
            textTop = pad;
            _control.SetBounds(0, textTop + textHeight + S(10), width, _control.Height);
            height = _control.Bottom + pad;
        }
        else
        {
            var controlHeight = _control?.Height ?? 0;
            height = Math.Max(textHeight, controlHeight) + 2 * pad;
            textTop = (height - textHeight) / 2;
            _control?.SetBounds(width - _control.Width, (height - controlHeight) / 2, _control.Width, controlHeight);
        }

        _title.SetBounds(0, textTop, textWidth, titleHeight);
        _description?.SetBounds(0, textTop + titleHeight + S(2), textWidth, descriptionHeight);
        Size = new Size(width, height);
        return height;
    }

    private static int Measure(Label label, int width) =>
        TextRenderer.MeasureText(label.Text, label.Font, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
}

/// <summary>Riquadro arrotondato che impila righe di impostazioni separate da una linea sottile.</summary>
internal sealed class SettingsCard : Panel
{
    private readonly List<int> _dividers = [];

    public SettingsCard(params SettingRow[] rows)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Window;
        Controls.AddRange(rows);
    }

    public int Arrange(int width)
    {
        var pad = S(18);
        var y = S(4);
        _dividers.Clear();
        var first = true;
        foreach (var row in Controls.OfType<SettingRow>())
        {
            if (!first) _dividers.Add(y);
            first = false;
            var height = row.Arrange(width - 2 * pad);
            row.Location = new Point(pad, y);
            y += height;
        }
        Size = new Size(width, y + S(4));
        Invalidate();
        return Height;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        FillRounded(e.Graphics, Surface, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), S(8), Divider);
        using var pen = new Pen(Divider);
        foreach (var y in _dividers) e.Graphics.DrawLine(pen, S(18), y, Width - S(18), y);
    }
}

/// <summary>
/// Campo per scegliere una scorciatoia: mostra i tasti come tasti della tastiera e,
/// quando ha il focus, registra la combinazione premuta ("Ctrl+Alt+D").
/// </summary>
internal sealed class KeyCapBox : PaintedControl
{
    public KeyCapBox()
    {
        Size = new Size(S(230), S(36));
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Surface;
        Font = Strong(9f);
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
        var key = e.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
        if (key == Keys.Escape && e.Modifiers == Keys.None)
        {
            Release();
            return;
        }

        var parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Alt) parts.Add("Alt");
        if (e.Shift) parts.Add("Shift");
        if (Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN)) parts.Add("Win");
        // Senza modificatori la scorciatoia ruberebbe il tasto a ogni app: ammessi solo i tasti funzione.
        if (parts.Count == 0 && key is not (>= Keys.F1 and <= Keys.F24)) return;

        parts.Add(key.ToString());
        Text = string.Join("+", parts);
        Release();
    }

    /// <summary>Toglie il focus dopo la scelta: le scorciatoie globali tornano attive.</summary>
    private void Release()
    {
        if (FindForm() is { } form) form.ActiveControl = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRounded(g, Focused ? Color.FromArgb(44, 41, 66) : Hovered ? SurfaceHover : Field, bounds, S(6), Focused ? Accent : Border);

        if (Focused)
        {
            using var hint = Body(9.5f);
            TextRenderer.DrawText(g, "Premi la combinazione…", hint, ClientRectangle, Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        // Tasti allineati a destra, come in un campo valore.
        var keys = Text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sizes = keys.Select(k => TextRenderer.MeasureText(k, Font).Width + S(16)).ToArray();
        var gap = S(5);
        var x = Width - S(8) - sizes.Sum() - gap * (keys.Length - 1);
        var capHeight = Height - S(12);
        for (var i = 0; i < keys.Length; i++)
        {
            var cap = new RectangleF(x, S(6), sizes[i], capHeight);
            FillRounded(g, Color.FromArgb(60, 60, 70), new RectangleF(cap.X, cap.Y + S(1), cap.Width, cap.Height), S(4));
            FillRounded(g, Color.FromArgb(72, 72, 84), new RectangleF(cap.X, cap.Y, cap.Width, cap.Height - S(2)), S(4));
            TextRenderer.DrawText(g, keys[i], Font, Rectangle.Round(new RectangleF(cap.X, cap.Y, cap.Width, cap.Height - S(2))), Theme.Fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += sizes[i] + gap;
        }
    }
}

/// <summary>Menu del tray in tema scuro.</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkMenuColors()) => RoundedEdges = true;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Fore : Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Muted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        var size = S(6);
        using var brush = new SolidBrush(Accent);
        g.FillEllipse(brush, r.X + (r.Width - size) / 2f, r.Y + (r.Height - size) / 2f, size, size);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var r = new RectangleF(S(4), 1, e.Item.Width - S(8), e.Item.Height - 2);
        FillRounded(e.Graphics, SurfaceHover, r, S(4));
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => SurfaceHover;
        public override Color SeparatorDark => Divider;
        public override Color SeparatorLight => Surface;
        public override Color CheckBackground => Surface;
        public override Color CheckSelectedBackground => SurfaceHover;
        public override Color CheckPressedBackground => SurfaceHover;
    }
}

/// <summary>Elenco a discesa scuro: riquadro arrotondato con freccia, voci con evidenziazione d'accento.</summary>
internal sealed class DarkComboBox : ComboBox
{
    private const int WM_PAINT = 0x000F;
    private const int WM_PRINT = 0x0317;
    private const int WM_PRINTCLIENT = 0x0318;
    private bool _hovered;

    public DarkComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = Field;
        ForeColor = Fore;
        Font = Body(10f);
        ItemHeight = S(28);
        IntegralHeight = false;
        MaxDropDownItems = 10;
        DarkScrollbars(this, "DarkMode_CFD");
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var brush = new SolidBrush(selected ? SurfaceHover : Field)) e.Graphics.FillRectangle(brush, e.Bounds);
        if (selected)
        {
            using var accent = new SolidBrush(Accent);
            e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y + S(6), S(3), e.Bounds.Height - S(12));
        }
        var text = new Rectangle(e.Bounds.X + S(12), e.Bounds.Y, e.Bounds.Width - S(16), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, text, Fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // Il riquadro chiuso lo disegna Windows: lo si ridipinge sopra, come gli altri campi.
        if (m.Msg == WM_PAINT)
        {
            using var g = Graphics.FromHwnd(Handle);
            PaintBox(g);
        }
        else if (m.Msg is WM_PRINT or WM_PRINTCLIENT && m.WParam != IntPtr.Zero)
        {
            using var g = Graphics.FromHdc(m.WParam);
            PaintBox(g);
        }
    }

    private void PaintBox(Graphics g)
    {
        // FillRectangle e non Clear: con WM_PRINT il contesto è condiviso con il resto della finestra.
        using (var background = new SolidBrush(Parent?.BackColor ?? Surface)) g.FillRectangle(background, 0, 0, Width, Height);
        var active = Focused || DroppedDown;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        FillRounded(g, _hovered || DroppedDown ? SurfaceHover : Field, bounds, S(6), active ? Accent : Border);

        var text = new Rectangle(S(12), 0, Width - S(44), Height);
        TextRenderer.DrawText(g, SelectedItem is null ? string.Empty : GetItemText(SelectedItem), Font, text, Fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // Freccia a V.
        var cx = Width - S(20);
        var cy = Height / 2f;
        var arm = S(4);
        using var pen = new Pen(Muted, S(2) * 0.75f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, [new PointF(cx - arm, cy - arm / 2f), new PointF(cx, cy + arm / 2f), new PointF(cx + arm, cy - arm / 2f)]);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }

    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnDropDownClosed(EventArgs e) { Invalidate(); base.OnDropDownClosed(e); }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}
