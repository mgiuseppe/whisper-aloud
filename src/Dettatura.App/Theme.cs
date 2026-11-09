using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Dettatura.App;

/// <summary>Colori, font e misure del tema scuro, condivisi da impostazioni, finestrella e menu del tray.</summary>
internal static partial class Theme
{
    public static readonly Color Window = Color.FromArgb(24, 24, 27);
    public static readonly Color Sidebar = Color.FromArgb(31, 31, 35);
    public static readonly Color Surface = Color.FromArgb(37, 37, 42);
    public static readonly Color SurfaceHover = Color.FromArgb(47, 47, 54);
    public static readonly Color Field = Color.FromArgb(46, 46, 53);
    public static readonly Color Border = Color.FromArgb(62, 62, 71);
    public static readonly Color Divider = Color.FromArgb(50, 50, 57);
    public static readonly Color Fore = Color.FromArgb(237, 237, 242);
    public static readonly Color Muted = Color.FromArgb(152, 152, 166);
    public static readonly Color Accent = Color.FromArgb(124, 108, 246);
    public static readonly Color AccentHover = Color.FromArgb(143, 129, 250);
    public static readonly Color Red = Color.FromArgb(242, 95, 92);
    public static readonly Color Orange = Color.FromArgb(245, 165, 36);
    public static readonly Color Green = Color.FromArgb(61, 214, 140);

    private static readonly string TextFamily = FirstInstalled("Segoe UI Variable Text", "Segoe UI");
    private static readonly string StrongFamily = FirstInstalled("Segoe UI Variable Text Semibold", "Segoe UI Semibold");
    private static readonly string DisplayFamily = FirstInstalled("Segoe UI Variable Display Semibold", "Segoe UI Semibold");

    /// <summary>DPI di sistema: l'app è DPI-aware a livello di sistema, quindi basta leggerla una volta.</summary>
    private static readonly float Dpi = ReadSystemDpi();

    public static Font Body(float size = 10f) => new(TextFamily, size);

    public static Font Strong(float size = 10f) => new(StrongFamily, size);

    public static Font Display(float size = 16f) => new(DisplayFamily, size);

    /// <summary>Converte pixel a 96 DPI nei pixel dello schermo.</summary>
    public static int S(int pixels) => (int)Math.Round(pixels * Dpi / 96f);

    public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (d <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Color fill, RectangleF bounds, float radius, Color? border = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(bounds, radius);
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        if (border is { } color)
        {
            using var pen = new Pen(color);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>Barra del titolo scura, bordo e angoli arrotondati (Windows 11; su Windows 10 le chiamate vengono ignorate).</summary>
    public static void StyleWindow(IntPtr handle, bool roundCorners = true)
    {
        var on = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        var caption = ColorRef(Window);
        _ = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
        var border = ColorRef(Border);
        _ = DwmSetWindowAttribute(handle, 34, ref border, sizeof(int)); // DWMWA_BORDER_COLOR
        if (roundCorners)
        {
            var round = 2;
            _ = DwmSetWindowAttribute(handle, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
        }
    }

    /// <summary>Barre di scorrimento e controlli nativi in versione scura.</summary>
    public static void DarkScrollbars(Control control, string theme = "DarkMode_Explorer") =>
        control.HandleCreated += (_, _) => _ = SetWindowTheme(control.Handle, theme, null);

    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    private static string FirstInstalled(params string[] families)
    {
        foreach (var family in families)
        {
            using var font = new Font(family, 10f);
            if (string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase)) return family;
        }
        return families[^1];
    }

    private static float ReadSystemDpi()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiX;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);
}
