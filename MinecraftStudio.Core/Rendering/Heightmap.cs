// MinecraftStudio.Core/Rendering/Heightmap.cs: a dimension's ground heights as a 16-bit grey picture, and pictures read
// back as heights.
// Owns: HeightPicture (brightness 0–1 per pixel, see-through = NaN, and what the file says about itself) and Heightmap —
// Encode (heights → a 16-bit grey PNG with alpha: the lowest y black, the highest white, columns without ground
// see-through; the y range and the north-west block go into the file's notes, so it reads back to the exact block in
// the same place) and Read (any PNG; a 16-bit grey at its full precision) / FromImage (a picture from elsewhere).
using System.Globalization;
using MinecraftStudio.Core.Imaging;

namespace MinecraftStudio.Core.Rendering;

/// <summary>
/// A heightmap picture: brightness 0–1 per pixel, row by row (NaN where it is see-through), with the y range and the block
/// of its north-west pixel when the file was saved by this program.
/// </summary>
public sealed record HeightPicture(float[] Levels, int Width, int Height, int? LowY = null, int? HighY = null, int? WestX = null, int? NorthZ = null)
{
    /// <summary>Whether the file says which heights it holds (one saved by "Heights of this dimension").</summary>
    public bool HasRange => LowY is int low && HighY is int high && high > low;
}

public static class Heightmap
{
    const string LowKey = "Minecraft lowest y", HighKey = "Minecraft highest y", WestKey = "Minecraft west x", NorthKey = "Minecraft north z";

    /// <summary>
    /// The heights (row by row; MapRenderer.NoHeight = no ground) as a 16-bit grey PNG: <paramref name="lowY"/> black,
    /// <paramref name="highY"/> white, 65,536 levels between (exact to the block for any range a world has), no ground
    /// see-through. <paramref name="westX"/>/<paramref name="northZ"/>: the block of the first pixel.
    /// </summary>
    public static byte[] Encode(int[] heights, int width, int height, int lowY, int highY, int westX, int northZ)
    {
        if (highY <= lowY) throw new ArgumentException("The highest y must be above the lowest.");
        var gray = new int[heights.Length];
        for (int i = 0; i < heights.Length; i++)
            gray[i] = heights[i] == MapRenderer.NoHeight ? -1
                : (int)Math.Round((Math.Clamp(heights[i], lowY, highY) - lowY) * 65535.0 / (highY - lowY));
        var c = CultureInfo.InvariantCulture;
        return PngWriter.EncodeGray16(gray, width, height,
            [(LowKey, lowY.ToString(c)), (HighKey, highY.ToString(c)), (WestKey, westX.ToString(c)), (NorthKey, northZ.ToString(c))]);
    }

    /// <summary>A PNG read as heights: every grey level a 16-bit file holds, colours by their brightness.</summary>
    public static HeightPicture Read(byte[] png)
    {
        var levels = Png.Brightness(png, out int w, out int h, out var text);
        return new HeightPicture(levels, w, h, Int(text, LowKey), Int(text, HighKey), Int(text, WestKey), Int(text, NorthKey));
    }

    /// <summary>A picture from elsewhere (JPEG, BMP … read by Windows) by its brightness; mostly see-through pixels are NaN.</summary>
    public static HeightPicture FromImage(RgbaImage img)
    {
        var levels = new float[img.Width * img.Height];
        for (int i = 0; i < levels.Length; i++)
        {
            uint px = img.Pixels[i];
            levels[i] = RgbaImage.A(px) < 128 ? float.NaN : (0.299f * RgbaImage.R(px) + 0.587f * RgbaImage.G(px) + 0.114f * RgbaImage.B(px)) / 255f;
        }
        return new HeightPicture(levels, img.Width, img.Height);
    }

    static int? Int(Dictionary<string, string> text, string key) =>
        text.TryGetValue(key, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;
}
