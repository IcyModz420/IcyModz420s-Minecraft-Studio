// MinecraftStudio.Core/World/MapArt.cs: the maps of a world as pictures, and pictures made into map art.
// Owns: MapArt — Picture (a map as 128×128 pixels, see-through where it is), Make (a picture cut into across × down locked
// maps in the colours the world's version shows — all four shades, the nearest colour or dithered), Add (new maps into a
// Java world — data/map_N.dat and idcounts.dat, in its version's layout — or a Bedrock one — map_<id> records — under fresh
// ids, never over a map that is there), Item (the filled map in the world's own item format) and Give (into a player's
// empty slots). Java 26.1's new folder layout and consoles are refused for adding (their map files are not written here).
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LevelDb;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

public static partial class MapArt
{
    public const int Size = 128;

    /// <summary>A map as a 128×128 picture (see-through where the map has no colour).</summary>
    public static RgbaImage Picture(CanonicalMap m)
    {
        var img = new RgbaImage(Size, Size);
        for (int i = 0; i < Size * Size; i++)
        {
            int rgb = Maps.Rgb(m.Colors[i]);
            img.Pixels[i] = rgb < 0 ? 0u : 0xFF000000u | (uint)rgb;
        }
        return img;
    }

    /// <summary>Maps side by side as one picture (a wall of map art), in reading order.</summary>
    public static RgbaImage Wall(IReadOnlyList<CanonicalMap> maps, int across)
    {
        across = Math.Max(1, Math.Min(across, maps.Count));
        int down = (maps.Count + across - 1) / across;
        var img = new RgbaImage(across * Size, down * Size);
        for (int k = 0; k < maps.Count; k++)
        {
            var p = Picture(maps[k]);
            int ox = k % across * Size, oy = k / across * Size;
            for (int y = 0; y < Size; y++) Array.Copy(p.Pixels, y * Size, img.Pixels, (oy + y) * img.Width + ox, Size);
        }
        return img;
    }

    /// <summary>The map colours (index, RGB) a world of this version shows: every base colour it has, in all four shades.</summary>
    public static List<(byte Color, int Rgb)> Colors(int maxBase)
    {
        var list = new List<(byte, int)>();
        for (int c = 4; c < (maxBase + 1) * 4 && c < 256; c++)
            if (Maps.Rgb((byte)c) is int rgb and >= 0) list.Add(((byte)c, rgb));
        return list;
    }

    /// <summary>
    /// A picture (already the size of the wall: across × 128 by down × 128) as map colours, row by row: see-through pixels
    /// stay empty, the rest the nearest colour of <paramref name="palette"/>, or error-diffused (Floyd–Steinberg) when dithered.
    /// </summary>
    public static byte[] Quantize(RgbaImage picture, IReadOnlyList<(byte Color, int Rgb)> palette, bool dither)
    {
        int w = picture.Width, h = picture.Height;
        var result = new byte[w * h];
        var cache = new Dictionary<int, int>();
        int Nearest(int r, int g, int b)
        {
            int key = (r << 16) | (g << 8) | b;
            if (cache.TryGetValue(key, out int hit)) return hit;
            int best = 0;
            long bestD = long.MaxValue;
            for (int i = 0; i < palette.Count; i++)
            {
                int c = palette[i].Rgb;
                long dr = r - ((c >> 16) & 255), dg = g - ((c >> 8) & 255), db = b - (c & 255);
                long d = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
                if (d < bestD) { bestD = d; best = i; }
            }
            return cache[key] = best;
        }
        var err = dither ? new float[w * h * 3] : null;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                uint px = picture.Pixels[i];
                if (RgbaImage.A(px) < 128) continue;
                float r = RgbaImage.R(px), g = RgbaImage.G(px), b = RgbaImage.B(px);
                if (err != null) { r += err[i * 3]; g += err[i * 3 + 1]; b += err[i * 3 + 2]; }
                int ri = Math.Clamp((int)MathF.Round(r), 0, 255), gi = Math.Clamp((int)MathF.Round(g), 0, 255), bi = Math.Clamp((int)MathF.Round(b), 0, 255);
                int k = Nearest(ri, gi, bi);
                result[i] = palette[k].Color;
                if (err == null) continue;
                int c = palette[k].Rgb;
                float er = r - ((c >> 16) & 255), eg = g - ((c >> 8) & 255), eb = b - (c & 255);
                void Spread(int dx, int dy, float f)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= w || ny >= h) return;
                    // Not into see-through pixels: they stay empty.
                    if (RgbaImage.A(picture.Pixels[ny * w + nx]) < 128) return;
                    int j = (ny * w + nx) * 3;
                    err[j] += er * f; err[j + 1] += eg * f; err[j + 2] += eb * f;
                }
                Spread(1, 0, 7 / 16f); Spread(-1, 1, 3 / 16f); Spread(0, 1, 5 / 16f); Spread(1, 1, 1 / 16f);
            }
        return result;
    }

    /// <summary>
    /// A picture as <paramref name="across"/> × <paramref name="down"/> maps (left to right, then down), fitted to the wall
    /// with <paramref name="options"/>, in the colours of <paramref name="maxBase"/>. The maps are locked (the game never
    /// draws terrain over them). Their ids are given when they are added to a world.
    /// </summary>
    public static List<CanonicalMap> Make(RgbaImage picture, int across, int down, PictureOptions options, int maxBase, bool dither) =>
        Make(picture, across, down, options, Colors(maxBase), dither);

    /// <summary>The same in the colours of <paramref name="palette"/> only (the buildable ones, say).</summary>
    public static List<CanonicalMap> Make(RgbaImage picture, int across, int down, PictureOptions options, IReadOnlyList<(byte Color, int Rgb)> palette, bool dither)
    {
        if (across is < 1 or > 16 || down is < 1 or > 16) throw new ArgumentException("A wall of map art is 1 to 16 maps across and down.");
        if (palette.Count == 0) throw new ArgumentException("There are no colours to make the map art from.");
        int w = across * Size, h = down * Size;
        var fitted = PictureFit.Prepare(picture, w, h, options with { Colors = 0, UsePalette = false, Dither = false });
        var colors = Quantize(fitted, palette, dither);
        var maps = new List<CanonicalMap>();
        for (int j = 0; j < down; j++)
            for (int i = 0; i < across; i++)
            {
                var m = new CanonicalMap { Scale = 0, Locked = true, TrackingPosition = false, UnlimitedTracking = false, XCenter = 0, ZCenter = 0 };
                for (int y = 0; y < Size; y++) Array.Copy(colors, (j * Size + y) * w + i * Size, m.Colors, y * Size, Size);
                maps.Add(m);
            }
        return maps;
    }

    /// <summary>The highest base colour the world's version shows.</summary>
    public static int MaxBase(SaveWorld world) => Maps.MaxBase(TargetCatalog.ClosestTo(world));

    /// <summary>Why maps can't be added to this world here, or null.</summary>
    public static string? CannotAdd(SaveWorld world) => world switch
    {
        { OpenedForRescue: true } => SaveWorld.RescueReadOnly,
        JavaWorld { IsNewLayout: true } => "Java 26.1's new world layout keeps maps elsewhere; adding map art to it is not supported yet.",
        JavaWorld j when j.Data == null => "This world has no level.dat.",
        JavaWorld or BedrockWorld => null,
        _ => "Map art can be added to Java and Bedrock worlds (PC).",
    };

    /// <summary>
    /// Writes the maps into the world under fresh ids (one past the highest it uses), setting each map's Id; returns the
    /// ids. Java: data/map_N.dat in the world's version and idcounts.dat raised (in its own layout); Bedrock: map_&lt;id&gt;
    /// records, committed. An id whose file or record already exists is never written over.
    /// </summary>
    public static List<int> Add(SaveWorld world, IReadOnlyList<CanonicalMap> maps)
    {
        if (CannotAdd(world) is string why) throw new InvalidOperationException(why);
        var target = TargetCatalog.ClosestTo(world);
        var ids = new List<int>();
        switch (world)
        {
            case JavaWorld j:
                {
                    string data = Path.Combine(j.Folder, "data");
                    Directory.CreateDirectory(data);
                    int dv = (int)(j.Data!.GetLongOrNull("DataVersion") ?? 0);
                    string counts = Path.Combine(data, "idcounts.dat");
                    NbtFile? countFile = File.Exists(counts) ? NbtFile.Load(counts) : null;
                    int highest = HighestJavaId(data, countFile);
                    foreach (var m in maps)
                    {
                        m.Id = ++highest;
                        string path = Path.Combine(data, $"map_{m.Id}.dat");
                        if (File.Exists(path)) throw new IOException($"map_{m.Id}.dat is there already.");
                        Maps.JavaMapFile(m, target, dv).Save(path);
                        ids.Add(m.Id);
                    }
                    // The game hands out the next id after idcounts.dat's: raise it past the new maps, in its own layout.
                    if (countFile?.Root is NbtCompound root)
                    {
                        if (root.GetCompound("data") is NbtCompound inner) inner.SetInt("map", highest);
                        else if (root["map"] is NbtShort) root.SetShort("map", (short)Math.Min(highest, short.MaxValue));
                        else root.SetInt("map", highest);
                        countFile.Save(counts);
                    }
                    else
                    {
                        var file = new NbtFile { Format = NbtFormat.Java, Compression = dv >= 1451 ? IO.CompressionKind.Gzip : IO.CompressionKind.None };
                        file.Roots.Add(new NamedTag("", dv >= 1451
                            ? new NbtCompound { { "data", new NbtCompound { { "map", new NbtInt(highest) } } }, { "DataVersion", new NbtInt(dv) } }
                            : new NbtCompound { { "map", new NbtShort((short)Math.Min(highest, short.MaxValue)) } }));
                        file.Save(counts);
                    }
                    break;
                }
            case BedrockWorld b:
                {
                    long highest = 0;
                    foreach (var key in b.Db.Keys())
                        if (key.Length > 4 && key[0] == (byte)'m' && System.Text.Encoding.ASCII.GetString(key).StartsWith("map_", StringComparison.Ordinal)
                            && long.TryParse(System.Text.Encoding.ASCII.GetString(key, 4, key.Length - 4), out long id))
                            highest = Math.Max(highest, Math.Abs(id));
                    if (highest + maps.Count >= int.MaxValue) throw new IOException("The world's map ids are too large to add more maps.");
                    foreach (var m in maps) { m.Id = (int)++highest; ids.Add(m.Id); }
                    foreach (var (key, value) in Maps.BedrockRecords(maps))
                    {
                        if (b.Db.Get(key) != null) throw new IOException($"{System.Text.Encoding.ASCII.GetString(key)} is there already.");
                        b.Db.Put(key, value);
                    }
                    b.Save(CommitMode.AppendLog);
                    break;
                }
        }
        return ids;
    }

    static int HighestJavaId(string data, NbtFile? counts)
    {
        int highest = -1;
        if (counts?.Root is NbtCompound root) highest = (int)((root.GetCompound("data") ?? root).GetLongOrNull("map") ?? -1);
        foreach (var f in Directory.EnumerateFiles(data, "map_*.dat"))
            if (int.TryParse(Path.GetFileNameWithoutExtension(f)[4..], out int id)) highest = Math.Max(highest, id);
        return highest;
    }

    /// <summary>
    /// A filled map item for map <paramref name="id"/>, in the record's item format. Bedrock links a map by the item's
    /// map_uuid in every version (old Bedrock with numbered items too); Java before 1.13 by its damage value.
    /// </summary>
    public static NbtCompound Item(ItemFormat format, int id, bool bedrock = false) => format switch
    {
        ItemFormat.Numeric when bedrock => new NbtCompound
        {
            { "id", new NbtShort(358) }, { "Count", new NbtByte(1) }, { "Damage", new NbtShort(0) }, { "tag", new NbtCompound { { "map_uuid", new NbtLong(id) } } },
        },
        ItemFormat.Components => new NbtCompound { { "id", new NbtString("minecraft:filled_map") }, { "count", new NbtInt(1) }, { "components", new NbtCompound { { "minecraft:map_id", new NbtInt(id) } } } },
        ItemFormat.Flattened => new NbtCompound { { "id", new NbtString("minecraft:filled_map") }, { "Count", new NbtByte(1) }, { "tag", new NbtCompound { { "map", new NbtInt(id) } } } },
        ItemFormat.Named => new NbtCompound { { "id", new NbtString("minecraft:filled_map") }, { "Count", new NbtByte(1) }, { "Damage", new NbtShort((short)id) } },
        ItemFormat.Numeric => new NbtCompound { { "id", new NbtShort(358) }, { "Count", new NbtByte(1) }, { "Damage", new NbtShort((short)id) } },
        _ => new NbtCompound
        {
            { "Name", new NbtString("minecraft:filled_map") }, { "Count", new NbtByte(1) }, { "Damage", new NbtShort(0) }, { "WasPickedUp", new NbtByte(0) },
            { "tag", new NbtCompound { { "map_uuid", new NbtLong(id) } } },
        },
    };

    /// <summary>
    /// Puts the maps into the player's empty hotbar and inventory slots and saves the player; returns how many fitted
    /// (the rest can be fetched with /give).
    /// </summary>
    public static int Give(SaveWorld world, PlayerRecord player, IReadOnlyList<int> ids)
    {
        var inv = new PlayerInventory(world, player);
        int given = 0;
        foreach (var (part, size) in new[] { (InventoryPart.Hotbar, 9), (InventoryPart.Storage, 27) })
            for (int i = 0; i < size && given < ids.Count; i++)
            {
                if (inv.Get(part, i) is InventoryItem { IsEmpty: false }) continue;
                inv.Set(part, i, inv.ReadItem(Item(inv.Format, ids[given], world is BedrockWorld)));
                given++;
            }
        if (given > 0)
        {
            inv.WriteBack();
            PlayerStore.Save(world, player);
        }
        return given;
    }
}
