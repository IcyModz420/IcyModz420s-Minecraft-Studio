// MinecraftStudio.Core/Imaging/ColorAdjust.cs: colour changes for a whole picture or a selection.
// Owns: ColorAdjustment (hue, saturation, brightness, contrast, colourise toward a colour, invert, grey, fewer colour
// levels) and ColorAdjust, which applies one to a colour. See-through pixels and every pixel's opacity are left alone.
namespace MinecraftStudio.Core.Imaging;

/// <summary>A colour change; every value at its default changes nothing.</summary>
public sealed record ColorAdjustment
{
    /// <summary>Hue turn in degrees (−180 to 180).</summary>
    public double Hue { get; init; }
    /// <summary>−100 (grey) to 100 (twice as vivid).</summary>
    public double Saturation { get; init; }
    /// <summary>−100 (black) to 100 (white).</summary>
    public double Brightness { get; init; }
    /// <summary>−100 (flat grey) to 100 (strong).</summary>
    public double Contrast { get; init; }
    /// <summary>Colourise toward this colour (its hue and saturation, each pixel keeping its own brightness), or null.</summary>
    public uint? Tint { get; init; }
    /// <summary>How much of the colourising shows: 0 to 100.</summary>
    public double TintStrength { get; init; } = 100;
    public bool Invert { get; init; }
    public bool Grayscale { get; init; }
    /// <summary>Levels per colour channel (2–32), or 0 to keep them all.</summary>
    public int Posterize { get; init; }

    public bool IsNone => Hue == 0 && Saturation == 0 && Brightness == 0 && Contrast == 0 && (Tint == null || TintStrength <= 0) && !Invert && !Grayscale && Posterize < 2;
}

/// <summary>Applies a <see cref="ColorAdjustment"/> to colours.</summary>
public static class ColorAdjust
{
    public static uint Apply(uint c, ColorAdjustment a)
    {
        int alpha = RgbaImage.A(c);
        if (alpha == 0 || a.IsNone) return c;
        double r = RgbaImage.R(c), g = RgbaImage.G(c), b = RgbaImage.B(c);
        if (a.Invert) { r = 255 - r; g = 255 - g; b = 255 - b; }
        if (a.Grayscale) r = g = b = Luma(r, g, b);
        if (a.Hue != 0 || a.Saturation != 0)
        {
            var (h, s, v) = Paint.ToHsv(RgbaImage.Argb(255, (int)Math.Round(r), (int)Math.Round(g), (int)Math.Round(b)));
            h += a.Hue;
            double k = Math.Clamp(a.Saturation, -100, 100) / 100;
            s = k >= 0 ? s + (1 - s) * k * s : s * (1 + k);
            uint t = Paint.FromHsv(h, Math.Clamp(s, 0, 1), v);
            (r, g, b) = (RgbaImage.R(t), RgbaImage.G(t), RgbaImage.B(t));
        }
        if (a.Brightness != 0)
        {
            double k = Math.Clamp(a.Brightness, -100, 100) / 100;
            double Step(double x) => k < 0 ? x * (1 + k) : x + (255 - x) * k;
            (r, g, b) = (Step(r), Step(g), Step(b));
        }
        if (a.Contrast != 0)
        {
            double k = Math.Clamp(a.Contrast, -100, 100) / 100;
            double f = k >= 0 ? 1 + 2 * k : 1 + k;
            (r, g, b) = ((r - 128) * f + 128, (g - 128) * f + 128, (b - 128) * f + 128);
        }
        if (a.Tint is uint tint && a.TintStrength > 0)
        {
            var (th, ts, _) = Paint.ToHsv(tint);
            double v = Math.Max(r, Math.Max(g, b)) / 255;
            uint t = Paint.FromHsv(th, ts, Math.Clamp(v, 0, 1));
            double k = Math.Clamp(a.TintStrength, 0, 100) / 100;
            (r, g, b) = (r + (RgbaImage.R(t) - r) * k, g + (RgbaImage.G(t) - g) * k, b + (RgbaImage.B(t) - b) * k);
        }
        if (a.Posterize >= 2)
        {
            int n = Math.Clamp(a.Posterize, 2, 32) - 1;
            double Level(double x) => Math.Round(Math.Clamp(x, 0, 255) / 255 * n) * 255 / n;
            (r, g, b) = (Level(r), Level(g), Level(b));
        }
        return RgbaImage.Argb(alpha, Clamp(r), Clamp(g), Clamp(b));
    }

    /// <summary>Perceived brightness (0–255).</summary>
    public static double Luma(double r, double g, double b) => 0.299 * r + 0.587 * g + 0.114 * b;

    static int Clamp(double v) => v <= 0 ? 0 : v >= 255 ? 255 : (int)Math.Round(v);
}
