using System.Drawing.Drawing2D;

namespace Dettatura.App;

/// <summary>Icone del tray disegnate a runtime: grigio a riposo, rosso in ascolto, arancio al lavoro.</summary>
internal static class TrayIcons
{
    public static Icon Circle(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 3, 3, 26, 26);
            using var pen = new Pen(Color.White, 3);
            // Microfono stilizzato: capsula e asta.
            graphics.DrawRectangle(pen, 13, 8, 6, 10);
            graphics.DrawLine(pen, 16, 19, 16, 24);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
