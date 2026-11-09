using System.Drawing.Drawing2D;

namespace Dettatura.App;

/// <summary>Icone disegnate a runtime: cerchio colorato (grigio a riposo, rosso in ascolto, arancio al lavoro) con un microfono.</summary>
internal static class TrayIcons
{
    public static Icon Circle(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            DrawMic(graphics, new RectangleF(1, 1, 30, 30), color);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    /// <summary>Microfono bianco su cerchio pieno, scalato sul rettangolo dato.</summary>
    public static void DrawMic(Graphics g, RectangleF bounds, Color background)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(background)) g.FillEllipse(brush, bounds);

        var unit = bounds.Width / 30f;
        float X(float v) => bounds.X + v * unit;
        float Y(float v) => bounds.Y + v * unit;

        using var white = new SolidBrush(Color.White);
        using (var capsule = Theme.RoundedRect(new RectangleF(X(11.5f), Y(6), 7 * unit, 12 * unit), 3.5f * unit))
        {
            g.FillPath(white, capsule);
        }
        using var pen = new Pen(Color.White, 2f * unit) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, X(8.5f), Y(9), 13 * unit, 12 * unit, 0, 180);
        g.DrawLine(pen, X(15), Y(21), X(15), Y(24));
    }
}
