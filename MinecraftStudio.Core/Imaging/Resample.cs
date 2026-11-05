// MinecraftStudio.Core/Imaging/Resample.cs: changing a picture's size and orientation.
// Owns: Resample — scaling (nearest pixel for crisp pixel art, or smooth: averaged when shrinking, blended when
// enlarging, with see-through pixels never darkening their neighbours), flipping, turning, and shifting with wrap-around
// (to check and fix the seams of a tiling texture).
namespace MinecraftStudio.Core.Imaging;

/// <summary>Changing a picture's size and orientation.</summary>
public static class Resample
{
    /// <summary>A copy at another size: nearest pixel, or smooth (averaged down, blended up).</summary>
    public static RgbaImage Scale(RgbaImage src, int width, int height, bool smooth)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == src.Width && height == src.Height) return src.Clone();
        if (!smooth) return src.Scaled(width, height);
        // Premultiplied colour, so a see-through pixel adds no colour of its own to what is averaged.
        int n = src.Width * src.Height;
        var plane = new float[4][];
        for (int c = 0; c < 4; c++) plane[c] = new float[n];
        for (int i = 0; i < n; i++)
        {
            uint p = src.Pixels[i];
            float a = RgbaImage.A(p) / 255f;
            plane[0][i] = a;
            plane[1][i] = RgbaImage.R(p) * a;
            plane[2][i] = RgbaImage.G(p) * a;
            plane[3][i] = RgbaImage.B(p) * a;
        }
        // Rows first, then columns.
        var rows = new float[4][];
        for (int c = 0; c < 4; c++) rows[c] = Pass(plane[c], src.Width, src.Height, width, horizontal: true);
        var cols = new float[4][];
        for (int c = 0; c < 4; c++) cols[c] = Pass(rows[c], width, src.Height, height, horizontal: false);
        var dst = new RgbaImage(width, height);
        for (int i = 0; i < width * height; i++)
        {
            float a = cols[0][i];
            if (a <= 0.5f / 255f) { dst.Pixels[i] = 0; continue; }
            dst.Pixels[i] = RgbaImage.Argb(ToByte(a * 255), ToByte(cols[1][i] / a), ToByte(cols[2][i] / a), ToByte(cols[3][i] / a));
        }
        return dst;
    }

    static int ToByte(float v) => v <= 0 ? 0 : v >= 255 ? 255 : (int)MathF.Round(v);

    /// <summary>One direction of a smooth resize: box-averaged when shrinking, linear when enlarging.</summary>
    static float[] Pass(float[] src, int w, int h, int size, bool horizontal)
    {
        int n = horizontal ? w : h, lines = horizontal ? h : w;
        int ow = horizontal ? size : w, oh = horizontal ? h : size;
        var dst = new float[ow * oh];
        float scale = (float)n / size;
        float Get(int line, int i) => horizontal ? src[line * w + i] : src[i * w + line];
        void Set(int line, int j, float v) { if (horizontal) dst[line * ow + j] = v; else dst[j * ow + line] = v; }
        for (int line = 0; line < lines; line++)
            for (int j = 0; j < size; j++)
            {
                if (scale > 1)
                {
                    float lo = j * scale, hi = lo + scale, sum = 0;
                    for (int i = (int)MathF.Floor(lo); i < Math.Min(n, (int)MathF.Ceiling(hi)); i++)
                        sum += Get(line, i) * (Math.Min(hi, i + 1) - Math.Max(lo, i));
                    Set(line, j, sum / scale);
                }
                else
                {
                    float c = (j + 0.5f) * scale - 0.5f;
                    int i0 = Math.Clamp((int)MathF.Floor(c), 0, n - 1), i1 = Math.Min(n - 1, i0 + 1);
                    float t = Math.Clamp(c - i0, 0, 1);
                    Set(line, j, Get(line, i0) * (1 - t) + Get(line, i1) * t);
                }
            }
        return dst;
    }

    public static RgbaImage Flip(RgbaImage src, bool horizontal)
    {
        var dst = new RgbaImage(src.Width, src.Height);
        dst.CopyFrom(src, 0, 0, src.Width, src.Height, 0, 0, flipX: horizontal, flipY: !horizontal);
        return dst;
    }

    /// <summary>Turned a quarter clockwise (width and height swap).</summary>
    public static RgbaImage RotateRight(RgbaImage src)
    {
        var dst = new RgbaImage(src.Height, src.Width);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                dst[src.Height - 1 - y, x] = src[x, y];
        return dst;
    }

    public static RgbaImage RotateLeft(RgbaImage src)
    {
        var dst = new RgbaImage(src.Height, src.Width);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                dst[y, src.Width - 1 - x] = src[x, y];
        return dst;
    }

    /// <summary>Shifted with wrap-around: what leaves one side comes back on the other.</summary>
    public static RgbaImage Wrap(RgbaImage src, int dx, int dy)
    {
        var dst = new RgbaImage(src.Width, src.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                dst[((x + dx) % src.Width + src.Width) % src.Width, ((y + dy) % src.Height + src.Height) % src.Height] = src[x, y];
        return dst;
    }
}
