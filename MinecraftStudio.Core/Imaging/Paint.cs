// MinecraftStudio.Core/Imaging/Paint.cs: Pixel-art painting operations shared by the skin and texture editors.
// Owns: Paint.
namespace MinecraftStudio.Core.Imaging;

/// <summary>Pixel-art painting operations shared by the skin and texture editors.</summary>
public static class Paint
{
    /// <summary>
    /// Fills the 4-connected area of pixels with exactly the colour of (x, y), staying where <paramref name="inside"/>
    /// allows (one face of a skin, for example). Returns the pixels it changed.
    /// </summary>
    public static List<int> FloodFill(RgbaImage img, int x, int y, uint color, Func<int, int, bool>? inside = null)
    {
        var changed = new List<int>();
        if (!img.Contains(x, y) || inside != null && !inside(x, y)) return changed;
        uint target = img[x, y];
        if (target == color) return changed;
        var seen = new bool[img.Width * img.Height];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        seen[y * img.Width + x] = true;
        while (stack.Count > 0)
        {
            var (px, py) = stack.Pop();
            img[px, py] = color;
            changed.Add(py * img.Width + px);
            foreach (var (nx, ny) in new[] { (px + 1, py), (px - 1, py), (px, py + 1), (px, py - 1) })
            {
                if (!img.Contains(nx, ny) || seen[ny * img.Width + nx]) continue;
                seen[ny * img.Width + nx] = true;
                if (img[nx, ny] == target && (inside == null || inside(nx, ny))) stack.Push((nx, ny));
            }
        }
        return changed;
    }

    /// <summary>Darker (negative amount) or lighter (positive) by a fraction of the way to black or white; alpha is kept.</summary>
    public static uint Shade(uint c, double amount)
    {
        int Step(int v) => (int)Math.Round(amount < 0 ? v * (1 + amount) : v + (255 - v) * amount);
        return RgbaImage.Argb(RgbaImage.A(c), Clamp(Step(RgbaImage.R(c))), Clamp(Step(RgbaImage.G(c))), Clamp(Step(RgbaImage.B(c))));
    }

    /// <summary>The colour with its brightness varied at random by up to ±<paramref name="amount"/> (0–1); alpha is kept.</summary>
    public static uint Noise(uint c, Random rng, double amount)
    {
        double f = 1 + (rng.NextDouble() * 2 - 1) * amount;
        return RgbaImage.Argb(RgbaImage.A(c), Clamp((int)Math.Round(RgbaImage.R(c) * f)), Clamp((int)Math.Round(RgbaImage.G(c) * f)), Clamp((int)Math.Round(RgbaImage.B(c) * f)));
    }

    /// <summary>The pixels of a straight line from (x0, y0) to (x1, y1), both ends included (Bresenham).</summary>
    public static IEnumerable<(int X, int Y)> Line(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            yield return (x0, y0);
            if (x0 == x1 && y0 == y1) yield break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>The pixels of a rectangle: its outline, or all of it when filled.</summary>
    public static IEnumerable<(int X, int Y)> Rectangle(PixelRect r, bool filled)
    {
        for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
                if (filled || x == r.X || y == r.Y || x == r.Right - 1 || y == r.Bottom - 1) yield return (x, y);
    }

    /// <summary>
    /// The pixels of the ellipse that fits a rectangle: its outline (inside pixels touching the outside), or all of it
    /// when filled. Small sizes come out the way pixel art draws them (a 3 × 3 circle is a plus).
    /// </summary>
    public static IEnumerable<(int X, int Y)> Ellipse(PixelRect r, bool filled)
    {
        double cx = r.X + (r.Width - 1) / 2.0, cy = r.Y + (r.Height - 1) / 2.0;
        double rx = Math.Max(0.25, r.Width / 2.0 - 0.25), ry = Math.Max(0.25, r.Height / 2.0 - 0.25);
        bool In(int x, int y)
        {
            if (!r.Contains(x, y)) return false;
            double dx = (x - cx) / rx, dy = (y - cy) / ry;
            return dx * dx + dy * dy <= 1.0 + 1e-9;
        }
        for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
                if (In(x, y) && (filled || !In(x - 1, y) || !In(x + 1, y) || !In(x, y - 1) || !In(x, y + 1))) yield return (x, y);
    }

    /// <summary>
    /// Whether two colours are within a tolerance of each other (0–255 on every channel, alpha too). Two see-through
    /// pixels always count as the same, whatever colour they hide.
    /// </summary>
    public static bool Near(uint a, uint b, int tolerance)
    {
        if (RgbaImage.A(a) == 0 && RgbaImage.A(b) == 0) return true;
        if (tolerance <= 0) return a == b;
        return Math.Abs(RgbaImage.A(a) - RgbaImage.A(b)) <= tolerance && Math.Abs(RgbaImage.R(a) - RgbaImage.R(b)) <= tolerance &&
               Math.Abs(RgbaImage.G(a) - RgbaImage.G(b)) <= tolerance && Math.Abs(RgbaImage.B(a) - RgbaImage.B(b)) <= tolerance;
    }

    /// <summary>A colour laid over another (the way a picture with see-through parts covers what is under it).</summary>
    public static uint Over(uint top, uint under)
    {
        int ta = RgbaImage.A(top);
        if (ta == 255) return top;
        if (ta == 0) return under;
        int ua = RgbaImage.A(under);
        int oa = ta + ua * (255 - ta) / 255;
        if (oa == 0) return 0;
        int Mix(int t, int u) => (t * ta + u * ua * (255 - ta) / 255) / oa;
        return RgbaImage.Argb(oa, Mix(RgbaImage.R(top), RgbaImage.R(under)), Mix(RgbaImage.G(top), RgbaImage.G(under)), Mix(RgbaImage.B(top), RgbaImage.B(under)));
    }

    static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    // ------------------------------------------------------------------ colour models

    /// <summary>Hue (0–360), saturation and value (0–1).</summary>
    public static (double H, double S, double V) ToHsv(uint c)
    {
        double r = RgbaImage.R(c) / 255.0, g = RgbaImage.G(c) / 255.0, b = RgbaImage.B(c) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    public static uint FromHsv(double h, double s, double v, int alpha = 255)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : h < 180 ? (0.0, c, x) : h < 240 ? (0.0, x, c) : h < 300 ? (x, 0.0, c) : (c, 0.0, x);
        return RgbaImage.Argb(alpha, (int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    /// <summary>"#RRGGBB" or "#AARRGGBB" (as typed: with or without #, 3, 6 or 8 digits).</summary>
    public static bool TryParseHex(string text, out uint color)
    {
        color = 0;
        string t = text.Trim().TrimStart('#');
        if (t.Length == 3) t = string.Concat(t.Select(ch => new string(ch, 2)));
        if (t.Length == 6) t = "FF" + t;
        if (t.Length != 8 || !uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out color)) return false;
        return true;
    }

    public static string ToHex(uint c) => RgbaImage.A(c) == 255 ? $"#{c & 0xFFFFFF:X6}" : $"#{c:X8}";
}
