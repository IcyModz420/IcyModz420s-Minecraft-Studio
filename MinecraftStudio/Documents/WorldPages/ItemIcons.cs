// MinecraftStudio/Documents/WorldPages/ItemIcons.cs: Item pictures for the inventory editor, read from the
// newest Java version installed on this PC (nothing of the game's is shipped with the program). Items without a
// picture there are drawn as text.
// Owns: ItemIcons.
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;

namespace MinecraftStudio.Documents.WorldPages;

/// <summary>
/// Item pictures for the inventory editor, read from the newest Java version installed on this PC (nothing of the
/// game's is shipped with the program). Items without a picture there are drawn as text.
/// </summary>
static class ItemIcons
{
    static readonly Lazy<ZipArchive?> Jar = new(() =>
    {
        try
        {
            var jars = GameFolders.JavaJars();
            // Newest first.
            return jars.Count == 0 ? null : ZipFile.OpenRead(jars[0].Jar);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
    });

    static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    /// <summary>The picture for an item id (minecraft:diamond_sword), or null. Call from the UI thread.</summary>
    public static Bitmap? For(string id)
    {
        string name = id.StartsWith("minecraft:", StringComparison.Ordinal) ? id[10..] : id;
        if (Cache.TryGetValue(name, out var hit)) return hit;
        Bitmap? bmp = null;
        if (Jar.Value is ZipArchive jar && name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.'))
            foreach (var path in new[]
            {
                $"assets/minecraft/textures/item/{name}.png", $"assets/minecraft/textures/block/{name}.png", $"assets/minecraft/textures/block/{name}_front.png",
                $"assets/minecraft/textures/block/{name}_top.png", $"assets/minecraft/textures/items/{name}.png", $"assets/minecraft/textures/blocks/{name}.png",
            })
            {
                if (jar.GetEntry(path) is not ZipArchiveEntry entry) continue;
                try
                {
                    using var stream = entry.Open();
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    var img = Png.Decode(ms.ToArray());
                    if (Tint(name) is uint tint) Multiply(img, tint);
                    bmp = FirstFrame(img);
                    break;
                }
                catch (Exception e) when (e is IOException or InvalidDataException) { }
            }
        Cache[name] = bmp;
        return bmp;
    }

    /// <summary>Grass and leaves are grey in the game's files and coloured by the biome; items show the default colours.</summary>
    static uint? Tint(string name) => name switch
    {
        "grass_block" or "short_grass" or "grass" or "tall_grass" or "fern" or "large_fern" => 0x91BD59,
        "spruce_leaves" => 0x619961,
        "birch_leaves" => 0x80A755,
        "oak_leaves" or "jungle_leaves" or "acacia_leaves" or "dark_oak_leaves" or "mangrove_leaves" or "vine" or "lily_pad" => 0x77AB2F,
        _ => null,
    };

    static void Multiply(RgbaImage img, uint rgb)
    {
        uint r = rgb >> 16 & 0xFF, g = rgb >> 8 & 0xFF, b = rgb & 0xFF;
        for (int i = 0; i < img.Pixels.Length; i++)
        {
            uint p = img.Pixels[i];
            img.Pixels[i] = p & 0xFF000000 | ((p >> 16 & 0xFF) * r / 255) << 16 | ((p >> 8 & 0xFF) * g / 255) << 8 | (p & 0xFF) * b / 255;
        }
    }

    /// <summary>The top square of the picture (animated textures are strips of frames).</summary>
    static Bitmap FirstFrame(RgbaImage img)
    {
        int size = Math.Min(img.Width, img.Height);
        var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var data = b.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var row = new int[size];
        for (int y = 0; y < size; y++)
        {
            Buffer.BlockCopy(img.Pixels, y * img.Width * 4, row, 0, size * 4);
            Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, size);
        }
        b.UnlockBits(data);
        return b;
    }
}
