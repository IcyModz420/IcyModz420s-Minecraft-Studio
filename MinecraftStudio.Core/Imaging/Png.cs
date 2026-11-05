// MinecraftStudio.Core/Imaging/Png.cs: PNG reading and writing without platform graphics: every colour type
// (grey, RGB, palette, grey + alpha, RGBA), bit depths 1–16, transparency chunks and Adam7 interlacing. Written
// files are 8-bit RGBA.
// Owns: Png.
using System.Buffers.Binary;
using System.IO.Compression;

namespace MinecraftStudio.Core.Imaging;

/// <summary>
/// PNG reading and writing without platform graphics: every colour type (grey, RGB, palette, grey + alpha, RGBA), bit
/// depths 1–16, transparency chunks and Adam7 interlacing. Written files are 8-bit RGBA.
/// </summary>
public static class Png
{
    static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>The largest image read (64 million pixels; a 256 MB bitmap).</summary>
    public const long MaxPixels = 1L << 26;

    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    /// <summary>A PNG's width and height, read from its header alone (nothing is decoded).</summary>
    public static (int Width, int Height) Size(ReadOnlySpan<byte> data)
    {
        // Signature, then the IHDR chunk: length (4), "IHDR" (4), width (4), height (4).
        if (!IsPng(data) || data.Length < 24 || !data.Slice(12, 4).SequenceEqual("IHDR"u8)) throw new InvalidDataException("Not a PNG file.");
        int w = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data[16..]), h = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data[20..]);
        if (w <= 0 || h <= 0) throw new InvalidDataException("The PNG image header is damaged.");
        return (w, h);
    }

    /// <summary>Size of the decompressed image data: every row of every pass, each with its filter byte.</summary>
    static int ExpectedSize(int width, int height, int bitsPerPixel, int interlace)
    {
        long Rows(int w, int h) => w <= 0 || h <= 0 ? 0 : h * (1 + ((long)w * bitsPerPixel + 7) / 8);
        long total = 0;
        if (interlace == 0) total = Rows(width, height);
        else
            foreach (var (x0, y0, dx, dy) in Adam7)
                total += Rows((width - x0 + dx - 1) / dx, (height - y0 + dy - 1) / dy);
        if (total > int.MaxValue) throw new InvalidDataException($"The image is too large ({width} × {height}).");
        return (int)total;
    }

    static readonly (int X0, int Y0, int DX, int DY)[] Adam7 = [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    /// <summary>A PNG read as far as its pixel rows: the header's numbers, the inflated rows, the palette, tRNS and text.</summary>
    sealed record Parsed(int Width, int Height, int Depth, int ColorType, int Interlace, int BitsPerPixel, int Bpp, byte[] Raw, byte[]? Palette, byte[]? Trns, Dictionary<string, string> Text);

    static Parsed Parse(byte[] data)
    {
        if (!IsPng(data)) throw new InvalidDataException("Not a PNG file.");
        int pos = 8, width = 0, height = 0, depth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null, trns = null;
        var text = new Dictionary<string, string>(StringComparer.Ordinal);
        using var idat = new MemoryStream();
        bool header = false;
        while (pos + 8 <= data.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
            string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            if (type == "IEND") break;
            // A missing checksum on the last chunk is harmless; missing data is not.
            if (len < 0 || pos + 8 + len > data.Length) throw new InvalidDataException("The PNG file is cut off.");
            var body = data.AsSpan(pos + 8, len);
            switch (type)
            {
                case "IHDR":
                    if (len < 13) throw new InvalidDataException("The PNG image header is damaged.");
                    width = BinaryPrimitives.ReadInt32BigEndian(body);
                    height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    depth = body[8];
                    colorType = body[9];
                    interlace = body[12];
                    header = true;
                    break;
                case "PLTE": palette = body.ToArray(); break;
                case "tRNS": trns = body.ToArray(); break;
                case "IDAT": idat.Write(body); break;
                case "tEXt":
                    if (body.IndexOf((byte)0) is int nul and > 0)
                        text[System.Text.Encoding.Latin1.GetString(body[..nul])] = System.Text.Encoding.Latin1.GetString(body[(nul + 1)..]);
                    break;
            }
            pos += 12 + len;
        }
        if (!header || width <= 0 || height <= 0) throw new InvalidDataException("The PNG file has no image header.");
        if ((long)width * height > MaxPixels) throw new InvalidDataException($"The image is too large ({width} × {height}).");
        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException($"Unknown PNG colour type {colorType}.") };
        bool depthOk = colorType switch { 0 => depth is 1 or 2 or 4 or 8 or 16, 3 => depth is 1 or 2 or 4 or 8, _ => depth is 8 or 16 };
        if (!depthOk) throw new InvalidDataException($"A PNG with colour type {colorType} cannot have {depth}-bit samples.");
        if (colorType == 3 && palette == null) throw new InvalidDataException("A palette PNG without a palette.");
        if (interlace > 1) throw new InvalidDataException($"Unknown PNG interlace method {interlace}.");
        int bitsPerPixel = channels * depth;
        int bpp = Math.Max(1, bitsPerPixel / 8);

        idat.Position = 0;
        var raw = new byte[ExpectedSize(width, height, bitsPerPixel, interlace)];
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            // Only as much as the image needs: extra data is ignored, too little is an error.
            int got = 0;
            while (got < raw.Length)
            {
                int n = z.Read(raw, got, raw.Length - got);
                if (n == 0) throw new InvalidDataException("The PNG image data is cut off.");
                got += n;
            }
        }
        return new Parsed(width, height, depth, colorType, interlace, bitsPerPixel, bpp, raw, palette, trns, text);
    }

    /// <summary>
    /// Each pixel's brightness from 0 to 1, at the file's own precision (a 16-bit grey keeps all 65,536 levels — a
    /// heightmap), NaN where it is see-through; and the file's text notes (tEXt).
    /// </summary>
    public static float[] Brightness(byte[] data, out int width, out int height, out Dictionary<string, string> text)
    {
        var p = Parse(data);
        (width, height, text) = (p.Width, p.Height, p.Text);
        var result = new float[p.Width * p.Height];
        if (p.ColorType is 0 or 4 && p.Depth == 16 && p.Interlace == 0)
        {
            int channels = p.ColorType == 4 ? 2 : 1, rowBytes = p.Width * channels * 2;
            var prev = new byte[rowBytes];
            var cur = new byte[rowBytes];
            for (int y = 0, at = 0; y < p.Height; y++)
            {
                int filter = p.Raw[at];
                Array.Copy(p.Raw, at + 1, cur, 0, rowBytes);
                at += 1 + rowBytes;
                Unfilter(filter, cur, prev, p.Bpp);
                for (int x = 0; x < p.Width; x++)
                {
                    int i = x * channels * 2;
                    bool clear = channels == 2 && (cur[i + 2] << 8 | cur[i + 3]) == 0;
                    result[y * p.Width + x] = clear ? float.NaN : (cur[i] << 8 | cur[i + 1]) / 65535f;
                }
                (prev, cur) = (cur, prev);
            }
            return result;
        }
        var img = Decode(data);
        for (int i = 0; i < result.Length; i++)
        {
            uint px = img.Pixels[i];
            result[i] = RgbaImage.A(px) < 128 ? float.NaN : (0.299f * RgbaImage.R(px) + 0.587f * RgbaImage.G(px) + 0.114f * RgbaImage.B(px)) / 255f;
        }
        return result;
    }

    public static RgbaImage Decode(byte[] data)
    {
        var p = Parse(data);
        var (width, height, depth, colorType, interlace, bitsPerPixel, bpp, raw, palette, trns) = (p.Width, p.Height, p.Depth, p.ColorType, p.Interlace, p.BitsPerPixel, p.Bpp, p.Raw, p.Palette, p.Trns);
        var img = new RgbaImage(width, height);
        int offset = 0;
        if (interlace == 0) offset = Pass(raw, offset, width, height, bitsPerPixel, bpp, (x, y) => (x, y));
        else
        {
            // Adam7: seven passes over a sparser and sparser grid.
            foreach (var (x0, y0, dx, dy) in Adam7)
            {
                int pw = (width - x0 + dx - 1) / dx, ph = (height - y0 + dy - 1) / dy;
                if (pw <= 0 || ph <= 0) continue;
                offset = Pass(raw, offset, pw, ph, bitsPerPixel, bpp, (x, y) => (x0 + x * dx, y0 + y * dy));
            }
        }
        return img;

        int Pass(byte[] src, int at, int w, int h, int bits, int stride, Func<int, int, (int X, int Y)> place)
        {
            int rowBytes = (w * bits + 7) / 8;
            var prev = new byte[rowBytes];
            var cur = new byte[rowBytes];
            for (int y = 0; y < h; y++)
            {
                if (at + 1 + rowBytes > src.Length) throw new InvalidDataException("The PNG image data is cut off.");
                int filter = src[at];
                Array.Copy(src, at + 1, cur, 0, rowBytes);
                at += 1 + rowBytes;
                Unfilter(filter, cur, prev, stride);
                for (int x = 0; x < w; x++)
                {
                    var (px, py) = place(x, y);
                    img[px, py] = Pixel(cur, x);
                }
                (prev, cur) = (cur, prev);
            }
            return at;
        }

        uint Pixel(byte[] row, int x)
        {
            int Sample(int index)
            {
                // Samples of 1, 2 and 4 bits are packed from the high bit; 16-bit samples keep their high byte.
                return depth switch
                {
                    8 => row[index],
                    16 => row[index * 2],
                    _ => (row[index * depth / 8] >> (8 - depth - (index * depth % 8))) & ((1 << depth) - 1),
                };
            }
            int Scale(int v) => depth switch { 1 => v * 255, 2 => v * 85, 4 => v * 17, _ => v };
            switch (colorType)
            {
                case 0:
                    {
                        // tRNS names one grey value (in the image's own bit depth) as transparent.
                        int value = depth == 16 ? row[x * 2] << 8 | row[x * 2 + 1] : Sample(x);
                        bool clear = trns is { Length: >= 2 } && value == (trns[0] << 8 | trns[1]);
                        int g = Scale(Sample(x));
                        return RgbaImage.Argb(clear ? 0 : 255, g, g, g);
                    }
                case 2:
                    {
                        int r = Sample(x * 3), g = Sample(x * 3 + 1), b = Sample(x * 3 + 2);
                        bool clear = false;
                        if (trns is { Length: >= 6 })
                        {
                            int Key(int i) => depth == 16 ? row[(x * 3 + i) * 2] << 8 | row[(x * 3 + i) * 2 + 1] : Sample(x * 3 + i);
                            clear = Key(0) == (trns[0] << 8 | trns[1]) && Key(1) == (trns[2] << 8 | trns[3]) && Key(2) == (trns[4] << 8 | trns[5]);
                        }
                        return RgbaImage.Argb(clear ? 0 : 255, r, g, b);
                    }
                case 3:
                    {
                        int i = Sample(x);
                        if (i * 3 + 2 >= palette!.Length) return 0;
                        int a = trns != null && i < trns.Length ? trns[i] : 255;
                        return RgbaImage.Argb(a, palette[i * 3], palette[i * 3 + 1], palette[i * 3 + 2]);
                    }
                case 4:
                    {
                        int g = Sample(x * 2), a = Sample(x * 2 + 1);
                        return RgbaImage.Argb(a, g, g, g);
                    }
                default:
                    return RgbaImage.Argb(Sample(x * 4 + 3), Sample(x * 4), Sample(x * 4 + 1), Sample(x * 4 + 2));
            }
        }
    }

    static void Unfilter(int filter, byte[] cur, byte[] prev, int bpp)
    {
        for (int i = 0; i < cur.Length; i++)
        {
            int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
            cur[i] = filter switch
            {
                0 => cur[i],
                1 => (byte)(cur[i] + a),
                2 => (byte)(cur[i] + b),
                3 => (byte)(cur[i] + (a + b) / 2),
                4 => (byte)(cur[i] + Paeth(a, b, c)),
                _ => throw new InvalidDataException($"Unknown PNG row filter {filter}."),
            };
        }
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>8-bit RGBA PNG; each row gets the filter that compresses best (the usual minimum-sum heuristic).</summary>
    public static byte[] Encode(RgbaImage img)
    {
        int w = img.Width, h = img.Height, rowBytes = w * 4;
        var raw = new byte[h * (rowBytes + 1)];
        var prev = new byte[rowBytes];
        var cur = new byte[rowBytes];
        var trial = new byte[rowBytes];
        var best = new byte[rowBytes];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                uint p = img[x, y];
                cur[x * 4] = (byte)RgbaImage.R(p); cur[x * 4 + 1] = (byte)RgbaImage.G(p); cur[x * 4 + 2] = (byte)RgbaImage.B(p); cur[x * 4 + 3] = (byte)RgbaImage.A(p);
            }
            int bestFilter = 0;
            long bestSum = long.MaxValue;
            for (int f = 0; f <= 4; f++)
            {
                long sum = 0;
                for (int i = 0; i < rowBytes; i++)
                {
                    int a = i >= 4 ? cur[i - 4] : 0, b = prev[i], c = i >= 4 ? prev[i - 4] : 0;
                    byte v = f switch { 0 => cur[i], 1 => (byte)(cur[i] - a), 2 => (byte)(cur[i] - b), 3 => (byte)(cur[i] - (a + b) / 2), _ => (byte)(cur[i] - Paeth(a, b, c)) };
                    trial[i] = v;
                    sum += v < 128 ? v : 256 - v;
                }
                if (sum < bestSum) { bestSum = sum; bestFilter = f; Array.Copy(trial, best, rowBytes); }
            }
            raw[y * (rowBytes + 1)] = (byte)bestFilter;
            Array.Copy(best, 0, raw, y * (rowBytes + 1) + 1, rowBytes);
            (prev, cur) = (cur, prev);
        }
        using var ms = new MemoryStream();
        ms.Write(Signature);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        Chunk(ms, "IHDR", ihdr);
        using (var compressed = new MemoryStream())
        {
            using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(raw);
            Chunk(ms, "IDAT", compressed.ToArray());
        }
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    static void Chunk(Stream s, string type, byte[] body)
    {
        var head = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(head, body.Length);
        System.Text.Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
        s.Write(head);
        s.Write(body);
        uint crc = Crc32(head.AsSpan(4, 4), 0xFFFFFFFFu);
        crc = Crc32(body, crc) ^ 0xFFFFFFFFu;
        var tail = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, crc);
        s.Write(tail);
    }

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc32(ReadOnlySpan<byte> data, uint crc)
    {
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
