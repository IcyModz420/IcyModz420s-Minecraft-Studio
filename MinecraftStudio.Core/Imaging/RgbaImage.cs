// MinecraftStudio.Core/Imaging/RgbaImage.cs: A 32-bit image for skins and textures: one ARGB value per pixel,
// row by row. Works without platform graphics, so the converters and their tests run anywhere.
// Owns: RgbaImage.
using System.Security.Cryptography;

namespace MinecraftStudio.Core.Imaging;

/// <summary>
/// A 32-bit image for skins and textures: one ARGB value per pixel, row by row. Works without platform graphics, so
/// the converters and their tests run anywhere.
/// </summary>
public sealed class RgbaImage
{
    public RgbaImage(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("An image needs a positive size.");
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
    }

    public RgbaImage(int width, int height, uint[] pixels) : this(width, height)
    {
        if (pixels.Length != width * height) throw new ArgumentException("Pixel count does not match the size.");
        Array.Copy(pixels, Pixels, pixels.Length);
    }

    public int Width { get; }
    public int Height { get; }
    /// <summary>ARGB (alpha in the top byte), index y * Width + x.</summary>
    public uint[] Pixels { get; }

    public uint this[int x, int y]
    {
        get => Pixels[y * Width + x];
        set => Pixels[y * Width + x] = value;
    }

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public static uint Argb(int a, int r, int g, int b) => (uint)((a & 255) << 24 | (r & 255) << 16 | (g & 255) << 8 | (b & 255));
    public static int A(uint c) => (int)(c >> 24);
    public static int R(uint c) => (int)(c >> 16) & 255;
    public static int G(uint c) => (int)(c >> 8) & 255;
    public static int B(uint c) => (int)c & 255;

    public RgbaImage Clone() => new(Width, Height, Pixels);

    public static RgbaImage FromPng(byte[] png) => Png.Decode(png);
    public byte[] ToPng() => Png.Encode(this);

    public RgbaImage Crop(int x, int y, int w, int h)
    {
        var r = new RgbaImage(w, h);
        r.CopyFrom(this, x, y, w, h, 0, 0);
        return r;
    }

    /// <summary>Copies a rectangle (pixels outside either image are skipped), optionally mirrored.</summary>
    public void CopyFrom(RgbaImage src, int sx, int sy, int w, int h, int dx, int dy, bool flipX = false, bool flipY = false)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int fx = sx + (flipX ? w - 1 - x : x), fy = sy + (flipY ? h - 1 - y : y);
                int tx = dx + x, ty = dy + y;
                if (src.Contains(fx, fy) && Contains(tx, ty)) this[tx, ty] = src[fx, fy];
            }
    }

    public void Fill(int x, int y, int w, int h, uint color)
    {
        for (int j = Math.Max(0, y); j < Math.Min(Height, y + h); j++)
            for (int i = Math.Max(0, x); i < Math.Min(Width, x + w); i++)
                this[i, j] = color;
    }

    /// <summary>Nearest-neighbour scaling (pixel art stays crisp).</summary>
    public RgbaImage Scaled(int w, int h)
    {
        var r = new RgbaImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                r[x, y] = this[x * Width / w, y * Height / h];
        return r;
    }

    /// <summary>Halving: a uniform 2×2 block keeps its colour, a mixed one becomes the average (alpha-weighted).</summary>
    public RgbaImage Halved()
    {
        var r = new RgbaImage(Math.Max(1, Width / 2), Math.Max(1, Height / 2));
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
            {
                uint a = this[x * 2, y * 2], b = this[Math.Min(Width - 1, x * 2 + 1), y * 2],
                     c = this[x * 2, Math.Min(Height - 1, y * 2 + 1)], d = this[Math.Min(Width - 1, x * 2 + 1), Math.Min(Height - 1, y * 2 + 1)];
                if (a == b && a == c && a == d) { r[x, y] = a; continue; }
                int sa = A(a) + A(b) + A(c) + A(d);
                if (sa == 0) { r[x, y] = 0; continue; }
                int sr = (R(a) * A(a) + R(b) * A(b) + R(c) * A(c) + R(d) * A(d)) / sa;
                int sg = (G(a) * A(a) + G(b) * A(b) + G(c) * A(c) + G(d) * A(d)) / sa;
                int sb = (B(a) * A(a) + B(b) * A(b) + B(c) * A(c) + B(d) * A(d)) / sa;
                r[x, y] = Argb(sa / 4, sr, sg, sb);
            }
        return r;
    }

    public bool AnyTransparent(int x, int y, int w, int h)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++)
                if (Contains(i, j) && A(this[i, j]) < 255) return true;
        return false;
    }

    public bool AllTransparent(int x, int y, int w, int h)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++)
                if (Contains(i, j) && A(this[i, j]) != 0) return false;
        return true;
    }

    /// <summary>A hash of the pixels (texture matching across versions).</summary>
    public string Hash()
    {
        var bytes = new byte[Pixels.Length * 4 + 8];
        BitConverter.GetBytes(Width).CopyTo(bytes, 0);
        BitConverter.GetBytes(Height).CopyTo(bytes, 4);
        Buffer.BlockCopy(Pixels, 0, bytes, 8, Pixels.Length * 4);
        return Convert.ToHexString(SHA1.HashData(bytes));
    }

    public bool SamePixels(RgbaImage other) => other.Width == Width && other.Height == Height && other.Pixels.AsSpan().SequenceEqual(Pixels);
}
