// MinecraftStudio.Core/Imaging/PictureFit.cs: turning any picture (a photo, a logo, art from another program) into
// pixels for a skin or texture.
// Owns: PictureFitMode, PictureOptions and PictureFit — the size it gets (fit inside, fill and crop, stretch, or as it
// is), crisp or smooth scaling, a plain background made see-through, and fewer colours (its own best few, or exactly
// the colours the texture already uses), optionally dithered.
namespace MinecraftStudio.Core.Imaging;

public enum PictureFitMode
{
    /// <summary>As large as fits, keeping its shape (may leave see-through edges).</summary>
    Fit,
    /// <summary>Covers the whole area, keeping its shape (the edges that stick out are cut off).</summary>
    Fill,
    /// <summary>Exactly the area's size, shape changed to match.</summary>
    Stretch,
    /// <summary>Not scaled.</summary>
    Original,
}

/// <summary>How a picture is turned into pixels.</summary>
public sealed record PictureOptions
{
    public PictureFitMode Fit { get; init; } = PictureFitMode.Fit;
    /// <summary>Smooth scaling (photos) instead of nearest pixel (pixel art).</summary>
    public bool Smooth { get; init; } = true;
    /// <summary>The colour of the picture's corners becomes see-through (and every touching pixel close to it).</summary>
    public bool RemoveBackground { get; init; }
    /// <summary>How close to the corner colour a pixel must be to go (0–100).</summary>
    public int BackgroundTolerance { get; init; } = 12;
    /// <summary>Reduce to this many colours (2–256), or 0 to keep every colour.</summary>
    public int Colors { get; init; }
    /// <summary>Use only the colours given as the palette (the texture's own), instead of the picture's best few.</summary>
    public bool UsePalette { get; init; }
    public bool Dither { get; init; }
}

/// <summary>Turns a picture into pixels for an area of a given size.</summary>
public static class PictureFit
{
    /// <summary>The size a picture gets in an area.</summary>
    public static (int Width, int Height) Size(int pw, int ph, int tw, int th, PictureFitMode mode)
    {
        pw = Math.Max(1, pw); ph = Math.Max(1, ph);
        double k = mode switch
        {
            PictureFitMode.Fit => Math.Min((double)tw / pw, (double)th / ph),
            PictureFitMode.Fill => Math.Max((double)tw / pw, (double)th / ph),
            _ => 1,
        };
        if (mode == PictureFitMode.Stretch) return (Math.Max(1, tw), Math.Max(1, th));
        return (Math.Max(1, (int)Math.Round(pw * k)), Math.Max(1, (int)Math.Round(ph * k)));
    }

    /// <summary>
    /// The picture ready to place in an area of the given size: scaled (Fill crops the middle to the area), its background
    /// made see-through and its colours reduced as asked. <paramref name="palette"/> is used when <see cref="PictureOptions.UsePalette"/>.
    /// </summary>
    public static RgbaImage Prepare(RgbaImage picture, int areaWidth, int areaHeight, PictureOptions o, IReadOnlyList<uint>? palette = null)
    {
        var img = o.RemoveBackground ? RemoveBackground(picture, o.BackgroundTolerance) : picture;
        var (w, h) = Size(img.Width, img.Height, areaWidth, areaHeight, o.Fit);
        // Shrinking a big photo straight to a few pixels averages it well; going through halves first keeps it quick.
        while (o.Smooth && img.Width / 2 >= w * 2 && img.Height / 2 >= h * 2) img = img.Halved();
        img = Resample.Scale(img, w, h, o.Smooth);
        if (o.Fit == PictureFitMode.Fill && (w > areaWidth || h > areaHeight))
            img = img.Crop((w - areaWidth) / 2, (h - areaHeight) / 2, Math.Min(w, areaWidth), Math.Min(h, areaHeight));
        if (o.UsePalette && palette is { Count: > 0 }) img = Palette.Map(img, palette, o.Dither);
        else if (o.Colors >= 2) img = Palette.Map(img, Palette.MedianCut(img, Math.Clamp(o.Colors, 2, 256)), o.Dither);
        return img;
    }

    /// <summary>The picture with its plain background see-through: pixels close to the corner colours, reached from the edges.</summary>
    public static RgbaImage RemoveBackground(RgbaImage src, int tolerance)
    {
        var img = src.Clone();
        int w = img.Width, h = img.Height, tol = Math.Clamp(tolerance, 0, 100) * 255 / 100;
        var corners = new[] { src[0, 0], src[w - 1, 0], src[0, h - 1], src[w - 1, h - 1] };
        bool Background(uint p) => RgbaImage.A(p) == 0 || corners.Any(c => Paint.Near(p, c, tol));
        var seen = new bool[w * h];
        var stack = new Stack<(int X, int Y)>();
        void Seed(int x, int y) { if (!seen[y * w + x] && Background(src[x, y])) { seen[y * w + x] = true; stack.Push((x, y)); } }
        for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
        for (int y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            img[x, y] = 0;
            if (x > 0) Seed(x - 1, y);
            if (x < w - 1) Seed(x + 1, y);
            if (y > 0) Seed(x, y - 1);
            if (y < h - 1) Seed(x, y + 1);
        }
        return img;
    }
}
