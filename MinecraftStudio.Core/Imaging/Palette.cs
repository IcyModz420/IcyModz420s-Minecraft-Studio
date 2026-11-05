// MinecraftStudio.Core/Imaging/Palette.cs: the colours of a picture, and pictures reduced to a few colours.
// Owns: Palette — the colours a picture uses (most used first), a small set that stands for a picture (median cut),
// and mapping a picture onto a set of colours (nearest colour, optionally dithered), so a photo can be made to look like
// pixel art or use exactly the colours a texture already has. Opacity is kept as it is.
namespace MinecraftStudio.Core.Imaging;

/// <summary>The colours of a picture, and pictures reduced to a few colours.</summary>
public static class Palette
{
    /// <summary>The colours the picture's visible pixels use, most used first (opacity ignored).</summary>
    public static List<uint> Colors(RgbaImage img, int max = 256)
    {
        var count = new Dictionary<uint, int>();
        foreach (uint p in img.Pixels)
        {
            if (RgbaImage.A(p) < 128) continue;
            uint c = p | 0xFF000000;
            count[c] = count.GetValueOrDefault(c) + 1;
        }
        return count.OrderByDescending(k => k.Value).ThenBy(k => k.Key).Take(max).Select(k => k.Key).ToList();
    }

    /// <summary>Up to <paramref name="n"/> colours that stand for the picture's visible pixels (median cut).</summary>
    public static List<uint> MedianCut(RgbaImage img, int n)
    {
        var pixels = img.Pixels.Where(p => RgbaImage.A(p) >= 128).Select(p => p | 0xFF000000).ToArray();
        if (pixels.Length == 0 || n < 1) return [];
        var distinct = pixels.Distinct().ToList();
        if (distinct.Count <= n) return distinct;
        var boxes = new List<uint[]> { pixels };
        while (boxes.Count < n)
        {
            // Split the box with the widest spread of any channel, at its median along that channel.
            int best = -1, bestAxis = 0, bestRange = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Length < 2) continue;
                for (int axis = 0; axis < 3; axis++)
                {
                    int lo = 255, hi = 0;
                    foreach (uint p in boxes[i]) { int v = Channel(p, axis); lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
                    if (hi - lo > bestRange) { bestRange = hi - lo; best = i; bestAxis = axis; }
                }
            }
            if (best < 0) break;
            var box = boxes[best];
            int ax = bestAxis;
            Array.Sort(box, (a, b) => Channel(a, ax).CompareTo(Channel(b, ax)));
            int mid = box.Length / 2;
            boxes[best] = box[..mid];
            boxes.Add(box[mid..]);
        }
        return boxes.Where(b => b.Length > 0).Select(Average).Distinct().ToList();
    }

    static int Channel(uint p, int axis) => axis == 0 ? RgbaImage.R(p) : axis == 1 ? RgbaImage.G(p) : RgbaImage.B(p);

    static uint Average(uint[] box)
    {
        long r = 0, g = 0, b = 0;
        foreach (uint p in box) { r += RgbaImage.R(p); g += RgbaImage.G(p); b += RgbaImage.B(p); }
        return RgbaImage.Argb(255, (int)(r / box.Length), (int)(g / box.Length), (int)(b / box.Length));
    }

    /// <summary>The palette colour closest to a colour, as the eye sees it.</summary>
    public static uint Nearest(IReadOnlyList<uint> palette, double r, double g, double b)
    {
        uint best = palette[0];
        double bestD = double.MaxValue;
        foreach (uint c in palette)
        {
            double dr = RgbaImage.R(c) - r, dg = RgbaImage.G(c) - g, db = RgbaImage.B(c) - b;
            // Red-mean weighting: close to how different two colours look.
            double rm = (RgbaImage.R(c) + r) / 2;
            double d = (2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    /// <summary>The picture with each visible pixel's colour replaced by the nearest palette colour; dithering spreads the difference to neighbours.</summary>
    public static RgbaImage Map(RgbaImage img, IReadOnlyList<uint> palette, bool dither)
    {
        var dst = img.Clone();
        if (palette.Count == 0) return dst;
        int w = img.Width, h = img.Height;
        var err = dither ? new double[w * h * 3] : null;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                uint p = img.Pixels[i];
                int alpha = RgbaImage.A(p);
                if (alpha == 0) continue;
                double r = RgbaImage.R(p), g = RgbaImage.G(p), b = RgbaImage.B(p);
                if (err != null) { r += err[i * 3]; g += err[i * 3 + 1]; b += err[i * 3 + 2]; }
                uint c = Nearest(palette, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
                dst.Pixels[i] = (c & 0x00FFFFFF) | (uint)alpha << 24;
                if (err == null) continue;
                // Floyd–Steinberg: the difference goes to the pixels not yet done.
                double er = r - RgbaImage.R(c), eg = g - RgbaImage.G(c), eb = b - RgbaImage.B(c);
                void Spread(int nx, int ny, double k)
                {
                    if (nx < 0 || nx >= w || ny >= h || RgbaImage.A(img[nx, ny]) == 0) return;
                    int j = (ny * w + nx) * 3;
                    err[j] += er * k; err[j + 1] += eg * k; err[j + 2] += eb * k;
                }
                Spread(x + 1, y, 7 / 16.0);
                Spread(x - 1, y + 1, 3 / 16.0);
                Spread(x, y + 1, 5 / 16.0);
                Spread(x + 1, y + 1, 1 / 16.0);
            }
        return dst;
    }
}
