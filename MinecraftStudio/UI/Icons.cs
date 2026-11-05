// MinecraftStudio/UI/Icons.cs: Small icons drawn in code (no image resources): NBT tag types, editions and
// world items.
// Owns: Icons.
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.UI;

/// <summary>Small icons drawn in code (no image resources): NBT tag types, editions and world items.</summary>
static class Icons
{
    public static ImageList Create(int size)
    {
        var list = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(size, size) };
        // NBT tag types
        list.Images.Add("Byte", Badge(size, "B", Color.FromArgb(220, 140, 70)));
        list.Images.Add("Short", Badge(size, "S", Color.FromArgb(214, 160, 60)));
        list.Images.Add("Int", Badge(size, "I", Color.FromArgb(90, 170, 220)));
        list.Images.Add("Long", Badge(size, "L", Color.FromArgb(70, 130, 210)));
        list.Images.Add("Float", Badge(size, "F", Color.FromArgb(160, 200, 90)));
        list.Images.Add("Double", Badge(size, "D", Color.FromArgb(110, 180, 90)));
        list.Images.Add("ByteArray", Badge(size, "[B", Color.FromArgb(190, 110, 70)));
        list.Images.Add("String", Badge(size, "\"", Color.FromArgb(206, 145, 120)));
        list.Images.Add("List", Badge(size, "[ ]", Color.FromArgb(150, 120, 210)));
        list.Images.Add("Compound", Badge(size, "{ }", Color.FromArgb(200, 170, 80)));
        list.Images.Add("IntArray", Badge(size, "[I", Color.FromArgb(70, 140, 190)));
        list.Images.Add("LongArray", Badge(size, "[L", Color.FromArgb(60, 110, 180)));
        list.Images.Add("End", Badge(size, "·", Color.Gray));
        // Editions and world parts
        list.Images.Add("java", Draw(size, GrassBlock));
        list.Images.Add("bedrock", Draw(size, BedrockBlock));
        list.Images.Add("console", Draw(size, Controller));
        list.Images.Add("3ds", Draw(size, Handheld));
        list.Images.Add("pocket", Draw(size, Phone));
        list.Images.Add("classic", Draw(size, (g, s) => Cube(g, Color.FromArgb(130, 130, 130), Color.FromArgb(100, 100, 100), Color.FromArgb(80, 80, 80))));
        list.Images.Add("folder", Draw(size, Folder));
        list.Images.Add("file", Draw(size, (g, s) => Page(g, Color.FromArgb(150, 150, 150))));
        list.Images.Add("nbt", Draw(size, (g, s) => Page(g, Color.FromArgb(200, 170, 80))));
        list.Images.Add("map", Draw(size, MapIcon));
        list.Images.Add("player", Draw(size, PlayerHead));
        list.Images.Add("region", Draw(size, RegionGrid));
        list.Images.Add("database", Draw(size, Database));
        list.Images.Add("info", Draw(size, InfoIcon));
        list.Images.Add("nether", Draw(size, (g, s) => Cube(g, Color.FromArgb(150, 50, 50), Color.FromArgb(110, 35, 35), Color.FromArgb(85, 25, 25))));
        list.Images.Add("end", Draw(size, (g, s) => Cube(g, Color.FromArgb(225, 225, 160), Color.FromArgb(190, 190, 125), Color.FromArgb(160, 160, 100))));
        return list;
    }

    public static string KeyFor(NbtType t) => t.ToString();

    static Bitmap Draw(int size, Action<Graphics, float> paint)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(size / 16f, size / 16f);
        paint(g, 16f);
        return bmp;
    }

    static Bitmap Badge(int size, string text, Color color)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var r = new RectangleF(0.5f, 0.5f, size - 1.5f, size - 1.5f);
        using (var path = Rounded(r, size * 0.22f))
        using (var b = new SolidBrush(Color.FromArgb(40, color)))
        using (var p = new Pen(color, Math.Max(1f, size / 16f)))
        {
            g.FillPath(b, path);
            g.DrawPath(p, path);
        }
        float fs = text.Length >= 3 ? size * 0.38f : text.Length == 2 ? size * 0.44f : size * 0.6f;
        using var font = new Font("Segoe UI", fs, FontStyle.Bold, GraphicsUnit.Pixel);
        using var tb = new SolidBrush(color);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, tb, new RectangleF(0, 0.5f, size, size), fmt);
        return bmp;
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static void Cube(Graphics g, Color top, Color left, Color right)
    {
        using var t = new SolidBrush(top);
        using var l = new SolidBrush(left);
        using var r = new SolidBrush(right);
        g.FillPolygon(t, [new PointF(8, 1.5f), new PointF(14.5f, 4.5f), new PointF(8, 7.5f), new PointF(1.5f, 4.5f)]);
        g.FillPolygon(l, [new PointF(1.5f, 4.5f), new PointF(8, 7.5f), new PointF(8, 14.5f), new PointF(1.5f, 11.5f)]);
        g.FillPolygon(r, [new PointF(14.5f, 4.5f), new PointF(8, 7.5f), new PointF(8, 14.5f), new PointF(14.5f, 11.5f)]);
    }

    static void GrassBlock(Graphics g, float s)
    {
        Cube(g, Color.FromArgb(110, 180, 70), Color.FromArgb(134, 96, 67), Color.FromArgb(110, 78, 54));
        using var grass = new SolidBrush(Color.FromArgb(95, 160, 60));
        g.FillPolygon(grass, [new PointF(1.5f, 4.5f), new PointF(8, 7.5f), new PointF(8, 9), new PointF(1.5f, 6)]);
        g.FillPolygon(grass, [new PointF(14.5f, 4.5f), new PointF(8, 7.5f), new PointF(8, 9), new PointF(14.5f, 6)]);
    }

    static void BedrockBlock(Graphics g, float s) =>
        Cube(g, Color.FromArgb(120, 120, 120), Color.FromArgb(70, 70, 70), Color.FromArgb(50, 50, 50));

    static void Controller(Graphics g, float s)
    {
        using var b = new SolidBrush(Color.FromArgb(120, 190, 90));
        using var dark = new SolidBrush(Color.FromArgb(30, 30, 30));
        g.FillEllipse(b, 1, 5, 6, 8);
        g.FillEllipse(b, 9, 5, 6, 8);
        g.FillRectangle(b, 4, 5, 8, 6);
        g.FillRectangle(dark, 3.2f, 8, 2.6f, 0.9f);
        g.FillRectangle(dark, 4.05f, 7.1f, 0.9f, 2.6f);
        g.FillEllipse(dark, 10.5f, 7.2f, 1.5f, 1.5f);
        g.FillEllipse(dark, 12, 8.6f, 1.5f, 1.5f);
    }

    static void Handheld(Graphics g, float s)
    {
        using var body = new SolidBrush(Color.FromArgb(200, 60, 60));
        using var screen = new SolidBrush(Color.FromArgb(40, 40, 50));
        g.FillRectangle(body, 2, 1, 12, 14);
        g.FillRectangle(screen, 3.5f, 2.5f, 9, 5);
        g.FillRectangle(screen, 4.5f, 9, 7, 4.5f);
    }

    static void Phone(Graphics g, float s)
    {
        using var body = new SolidBrush(Color.FromArgb(90, 90, 100));
        using var screen = new SolidBrush(Color.FromArgb(110, 180, 70));
        g.FillRectangle(body, 4, 1, 8, 14);
        g.FillRectangle(screen, 5, 2.5f, 6, 10);
    }

    static void Folder(Graphics g, float s)
    {
        using var back = new SolidBrush(Color.FromArgb(190, 150, 80));
        using var front = new SolidBrush(Color.FromArgb(220, 182, 110));
        g.FillRectangle(back, 1.5f, 3f, 6f, 3f);
        g.FillRectangle(back, 1.5f, 4.5f, 13f, 9f);
        g.FillRectangle(front, 1.5f, 6.5f, 13f, 7f);
    }

    static void Page(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.2f);
        var path = new GraphicsPath();
        path.AddLines([new PointF(3, 1.5f), new PointF(10, 1.5f), new PointF(13, 4.5f), new PointF(13, 14.5f), new PointF(3, 14.5f)]);
        path.CloseFigure();
        g.DrawPath(pen, path);
        g.DrawLines(pen, [new PointF(10, 1.5f), new PointF(10, 4.5f), new PointF(13, 4.5f)]);
        using var thin = new Pen(color, 1f);
        for (float y = 7f; y <= 12f; y += 2.5f) g.DrawLine(thin, 5f, y, 11f, y);
    }

    static void MapIcon(Graphics g, float s)
    {
        using var paper = new SolidBrush(Color.FromArgb(225, 205, 160));
        using var land = new SolidBrush(Color.FromArgb(110, 170, 80));
        using var water = new SolidBrush(Color.FromArgb(80, 130, 210));
        g.FillRectangle(paper, 1.5f, 1.5f, 13, 13);
        g.FillRectangle(water, 3, 3, 10, 10);
        g.FillPolygon(land, [new PointF(3, 13), new PointF(3, 6), new PointF(7, 4), new PointF(10, 8), new PointF(13, 7), new PointF(13, 13)]);
    }

    static void PlayerHead(Graphics g, float s)
    {
        using var skin = new SolidBrush(Color.FromArgb(200, 150, 110));
        using var hair = new SolidBrush(Color.FromArgb(80, 50, 30));
        using var eye = new SolidBrush(Color.FromArgb(70, 90, 180));
        g.FillRectangle(skin, 2, 2, 12, 12);
        g.FillRectangle(hair, 2, 2, 12, 4);
        g.FillRectangle(eye, 4, 8, 2, 2);
        g.FillRectangle(eye, 10, 8, 2, 2);
    }

    static void RegionGrid(Graphics g, float s)
    {
        using var pen = new Pen(Color.FromArgb(150, 170, 200), 1f);
        using var fill = new SolidBrush(Color.FromArgb(60, 90, 140));
        g.FillRectangle(fill, 2, 2, 12, 12);
        for (float v = 2; v <= 14; v += 4) { g.DrawLine(pen, v, 2, v, 14); g.DrawLine(pen, 2, v, 14, v); }
    }

    static void Database(Graphics g, float s)
    {
        using var b = new SolidBrush(Color.FromArgb(120, 160, 210));
        using var dark = new SolidBrush(Color.FromArgb(70, 100, 150));
        g.FillRectangle(b, 3, 4, 10, 9);
        g.FillEllipse(b, 3, 10.5f, 10, 4);
        g.FillEllipse(dark, 3, 2, 10, 4);
    }

    static void InfoIcon(Graphics g, float s)
    {
        using var b = new SolidBrush(Color.FromArgb(90, 160, 230));
        using var w = new SolidBrush(Color.White);
        g.FillEllipse(b, 1.5f, 1.5f, 13, 13);
        g.FillRectangle(w, 7.2f, 7, 1.8f, 5);
        g.FillEllipse(w, 7.1f, 4, 2, 2);
    }
}
