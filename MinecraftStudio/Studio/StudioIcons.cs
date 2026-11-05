// MinecraftStudio/Studio/StudioIcons.cs: Toolbar icons of the pixel editors, drawn as simple shapes at any
// size.
// Owns: StudioIcons.
using System.Drawing.Drawing2D;

namespace MinecraftStudio.Studio;

/// <summary>Toolbar icons of the pixel editors, drawn as simple shapes at any size.</summary>
static class StudioIcons
{
    public static Bitmap Draw(string name, int size, Color color)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = size / 16f;
        using var pen = new Pen(color, Math.Max(1.2f, 1.4f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        using var faint = new SolidBrush(Color.FromArgb(110, color));
        PointF P(float x, float y) => new(x * k, y * k);
        switch (name)
        {
            case "pencil":
                g.DrawPolygon(pen, [P(3, 13), P(4, 10), P(11.5f, 2.5f), P(13.5f, 4.5f), P(6, 12)]);
                g.DrawLine(pen, P(10, 4), P(12, 6));
                g.FillPolygon(brush, [P(3, 13), P(4, 10), P(6, 12)]);
                break;
            case "eraser":
                g.DrawPolygon(pen, [P(2.5f, 10.5f), P(8.5f, 4.5f), P(13, 9), P(8.5f, 13.5f), P(5.5f, 13.5f)]);
                g.FillPolygon(faint, [P(2.5f, 10.5f), P(5.5f, 7.5f), P(10, 12), P(8.5f, 13.5f), P(5.5f, 13.5f)]);
                break;
            case "fill":
                g.DrawPolygon(pen, [P(2.5f, 8), P(7.5f, 3), P(12.5f, 8), P(7.5f, 13)]);
                g.FillPolygon(faint, [P(2.5f, 8), P(12.5f, 8), P(7.5f, 13)]);
                g.FillEllipse(brush, 12f * k, 10f * k, 2.6f * k, 3.4f * k);
                break;
            case "picker":
                g.DrawLine(pen, P(3, 13), P(9, 7));
                g.FillEllipse(brush, 9f * k, 2f * k, 5f * k, 5f * k);
                g.DrawLine(pen, P(8, 6), P(10, 8));
                break;
            case "shade":
                g.DrawEllipse(pen, 2.5f * k, 2.5f * k, 11f * k, 11f * k);
                g.FillPie(brush, 2.5f * k, 2.5f * k, 11f * k, 11f * k, 90, 180);
                break;
            case "noise":
                foreach (var (x, y) in new[] { (3, 3), (8, 2), (12, 4), (5, 7), (10, 8), (2, 11), (7, 12), (12, 12) })
                    g.FillRectangle(brush, x * k, y * k, 2.2f * k, 2.2f * k);
                break;
            case "mirror":
                g.FillPolygon(faint, [P(6.5f, 3), P(6.5f, 13), P(1.5f, 13)]);
                g.DrawPolygon(pen, [P(9.5f, 3), P(9.5f, 13), P(14.5f, 13)]);
                using (var dash = new Pen(color, Math.Max(1, k)) { DashStyle = DashStyle.Dot }) g.DrawLine(dash, P(8, 1), P(8, 15));
                break;
            case "dither":
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        if ((x + y) % 2 == 0) g.FillRectangle(brush, (2.5f + x * 2.8f) * k, (2.5f + y * 2.8f) * k, 2.2f * k, 2.2f * k);
                break;
            case "line":
                g.DrawLine(pen, P(3, 13), P(13, 3));
                g.FillEllipse(brush, 1.5f * k, 11.5f * k, 3f * k, 3f * k);
                g.FillEllipse(brush, 11.5f * k, 1.5f * k, 3f * k, 3f * k);
                break;
            case "rectangle":
                g.DrawRectangle(pen, 2.5f * k, 4f * k, 11f * k, 8f * k);
                break;
            case "ellipse":
                g.DrawEllipse(pen, 2f * k, 3.5f * k, 12f * k, 9f * k);
                break;
            case "select":
                using (var dash = new Pen(color, Math.Max(1, 1.2f * k)) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(dash, 2.5f * k, 2.5f * k, 11f * k, 11f * k);
                g.FillRectangle(brush, 1.5f * k, 1.5f * k, 2.2f * k, 2.2f * k);
                g.FillRectangle(brush, 12.3f * k, 12.3f * k, 2.2f * k, 2.2f * k);
                break;
            case "swap":
                g.DrawLine(pen, P(3, 5), P(13, 5)); g.DrawLines(pen, [P(10, 2), P(13, 5), P(10, 8)]);
                g.DrawLine(pen, P(13, 11), P(3, 11)); g.DrawLines(pen, [P(6, 8), P(3, 11), P(6, 14)]);
                break;
        }
        return bmp;
    }
}
