// MinecraftStudio.Core/Imaging/Tga.cs: TGA reading (Bedrock keeps some textures, such as leaves, as TGA): true
// colour (16, 24 and 32 bit), grey (8 bit, and 16 bit with alpha) and colour-mapped images, plain or run-length
// encoded, in any row and column order.
// Owns: Tga.
using System.Buffers.Binary;

namespace MinecraftStudio.Core.Imaging;

/// <summary>
/// TGA reading (Bedrock keeps some textures, such as leaves, as TGA): true colour (16, 24 and 32 bit), grey (8 bit, and
/// 16 bit with alpha) and colour-mapped images, plain or run-length encoded, in any row and column order.
/// </summary>
public static class Tga
{
    /// <summary>TGA has no signature; this checks that the header describes a picture this reader understands.</summary>
    public static bool LooksLikeTga(ReadOnlySpan<byte> d)
    {
        if (d.Length < 18) return false;
        int cmType = d[1], type = d[2], depth = d[16];
        int w = BinaryPrimitives.ReadUInt16LittleEndian(d[12..]), h = BinaryPrimitives.ReadUInt16LittleEndian(d[14..]);
        if (w == 0 || h == 0 || cmType > 1) return false;
        return type switch
        {
            1 or 9 => cmType == 1 && depth is 8 or 16,
            2 or 10 => depth is 15 or 16 or 24 or 32,
            3 or 11 => depth is 8 or 16,
            _ => false,
        };
    }

    public static RgbaImage Decode(byte[] d)
    {
        if (!LooksLikeTga(d)) throw new InvalidDataException("Not a TGA image this program can read.");
        int idLength = d[0], cmType = d[1], type = d[2];
        int cmFirst = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(3)), cmLength = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(5)), cmDepth = d[7];
        int w = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(12)), h = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(14));
        int depth = d[16], descriptor = d[17];
        int alphaBits = descriptor & 15;
        bool rightToLeft = (descriptor & 0x10) != 0, topDown = (descriptor & 0x20) != 0;
        if ((long)w * h > Png.MaxPixels) throw new InvalidDataException($"The image is too large ({w} × {h}).");
        int pos = 18 + idLength;

        uint[]? map = null;
        if (cmType == 1)
        {
            int entryBytes = (cmDepth + 7) / 8;
            if (pos + cmLength * entryBytes > d.Length) throw new InvalidDataException("The TGA colour map is cut off.");
            map = new uint[cmFirst + cmLength];
            for (int i = 0; i < cmLength; i++) map[cmFirst + i] = Color(d, pos + i * entryBytes, cmDepth, cmDepth == 32 || cmDepth == 16 && alphaBits > 0);
            pos += cmLength * entryBytes;
        }

        int bytes = (depth + 7) / 8;
        bool colorMapped = type is 1 or 9, grey = type is 3 or 11, rle = type >= 9;
        var pixels = new uint[w * h];
        int n = 0;
        uint Read(int at)
        {
            if (colorMapped)
            {
                int index = depth == 8 ? d[at] : BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at));
                return index < map!.Length ? map[index] : 0;
            }
            if (grey)
            {
                int g = d[at];
                return RgbaImage.Argb(depth == 16 ? d[at + 1] : 255, g, g, g);
            }
            return Color(d, at, depth, depth == 32 || depth == 16 && alphaBits > 0);
        }
        while (n < pixels.Length)
        {
            if (!rle)
            {
                if (pos + bytes > d.Length) throw new InvalidDataException("The TGA image data is cut off.");
                pixels[n++] = Read(pos);
                pos += bytes;
                continue;
            }
            if (pos >= d.Length) throw new InvalidDataException("The TGA image data is cut off.");
            int head = d[pos++], count = (head & 0x7F) + 1;
            if (n + count > pixels.Length) count = pixels.Length - n;
            if ((head & 0x80) != 0)
            {
                if (pos + bytes > d.Length) throw new InvalidDataException("The TGA image data is cut off.");
                uint c = Read(pos);
                pos += bytes;
                for (int i = 0; i < count; i++) pixels[n++] = c;
            }
            else
            {
                if (pos + count * bytes > d.Length) throw new InvalidDataException("The TGA image data is cut off.");
                for (int i = 0; i < count; i++, pos += bytes) pixels[n++] = Read(pos);
            }
        }

        // A 32-bit image whose header claims no alpha and whose alpha bytes are all zero means "no alpha".
        if (depth == 32 && alphaBits == 0 && !colorMapped && pixels.All(p => RgbaImage.A(p) == 0))
            for (int i = 0; i < pixels.Length; i++) pixels[i] |= 0xFF000000u;

        var img = new RgbaImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[rightToLeft ? w - 1 - x : x, topDown ? y : h - 1 - y] = pixels[y * w + x];
        return img;
    }

    /// <summary>A stored colour: 15/16-bit (A1 R5 G5 B5), 24-bit (B G R) or 32-bit (B G R A).</summary>
    static uint Color(byte[] d, int at, int depth, bool alpha)
    {
        switch (depth)
        {
            case 15:
            case 16:
                {
                    int v = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at));
                    int r = (v >> 10) & 31, g = (v >> 5) & 31, b = v & 31;
                    int a = depth == 16 && alpha ? ((v & 0x8000) != 0 ? 255 : 0) : 255;
                    return RgbaImage.Argb(a, r * 255 / 31, g * 255 / 31, b * 255 / 31);
                }
            case 24:
                return RgbaImage.Argb(255, d[at + 2], d[at + 1], d[at]);
            default:
                return RgbaImage.Argb(alpha ? d[at + 3] : 255, d[at + 2], d[at + 1], d[at]);
        }
    }
}
