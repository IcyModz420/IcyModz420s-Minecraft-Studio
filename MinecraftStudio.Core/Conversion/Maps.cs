// MinecraftStudio.Core/Conversion/Maps.cs: A filled map (map art) in edition-neutral form: 128×128 Java map
// colours.
// Owns: CanonicalMap, Maps.
using System.Globalization;
using System.Reflection;
using System.Text;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>A filled map (map art) in edition-neutral form: 128×128 Java map colours.</summary>
public sealed class CanonicalMap
{
    /// <summary>Java map id (Bedrock map ids are renumbered, see <see cref="ConversionLog.MapIdForBedrock"/>).</summary>
    public int Id { get; set; }
    public int Scale { get; set; }
    public string Dimension { get; set; } = "minecraft:overworld";
    public int XCenter { get; set; }
    public int ZCenter { get; set; }
    public bool Locked { get; set; }
    public bool TrackingPosition { get; set; } = true;
    public bool UnlimitedTracking { get; set; }
    /// <summary>Java map colours (base colour × 4 + shade), row by row.</summary>
    public byte[] Colors { get; set; } = new byte[128 * 128];
}

/// <summary>
/// Map art across editions: Java data/map_N.dat (colour indices), Bedrock map_&lt;id&gt; records (RGBA) and Legacy Console
/// archive entries. Colours a version does not have become the nearest colour it has.
/// </summary>
public static class Maps
{
    sealed record Row(int Base, string Name, int[] Rgb, string Added);

    static readonly Lazy<Row[]> Palette = new(() =>
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MinecraftStudio.Data.map_palette.csv")
            ?? throw new FileNotFoundException("Embedded map palette is missing.");
        using var r = new StreamReader(s);
        var rows = new List<Row>();
        r.ReadLine();
        while (r.ReadLine() is string line)
        {
            var p = line.Split(',');
            if (p.Length < 12 || !int.TryParse(p[0], out int id)) continue;
            int Rgb(string hex) => int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v) ? v : -1;
            rows.Add(new Row(id, p[1], [Rgb(p[5]), Rgb(p[7]), Rgb(p[9]), Rgb(p[11])], p[3]));
        }
        return [.. rows.OrderBy(x => x.Base)];
    });

    /// <summary>The base colour of a map colour name (GRASS, COLOR_ORANGE, TERRACOTTA_WHITE …), or null.</summary>
    public static int? BaseId(string name) => Palette.Value.FirstOrDefault(r => r.Name == name)?.Base;

    /// <summary>RGB of a Java map colour, or -1 for transparent.</summary>
    public static int Rgb(byte color)
    {
        var pal = Palette.Value;
        int b = color >> 2, shade = color & 3;
        return b < pal.Length && b > 0 ? pal[b].Rgb[shade] : -1;
    }

    /// <summary>Highest base colour a target's maps can show (older versions crash or show black on unknown colours).</summary>
    public static int MaxBase(ConversionTarget t) => t.Kind switch
    {
        TargetKind.Java when t.DataVersion >= 2724 => 61,
        TargetKind.Java when t.DataVersion >= 2566 => 58,
        TargetKind.Java when t.DataVersion >= 1139 => 51,
        TargetKind.Java when t.DataVersion >= 100 || t.LegacyLevel >= LegacyLevel.Release_1_8 => 35,
        TargetKind.Java when t.LegacyLevel >= LegacyLevel.Release_1_7 => 34,
        TargetKind.Java => 27,
        TargetKind.LegacyConsole => 51,
        _ => 61,
    };

    static readonly Dictionary<int, byte[]> RemapCache = [];

    /// <summary>Colour table for a palette limit: unknown colours become the nearest colour of the same shade.</summary>
    static byte[] Remap(int maxBase)
    {
        lock (RemapCache)
        {
            if (RemapCache.TryGetValue(maxBase, out var t)) return t;
            t = new byte[256];
            var pal = Palette.Value;
            for (int c = 0; c < 256; c++)
            {
                int b = c >> 2, shade = c & 3;
                if (b <= maxBase || b >= pal.Length) { t[c] = (byte)(b >= pal.Length ? 0 : c); continue; }
                t[c] = (byte)(Nearest(pal[b].Rgb[shade], maxBase, shade) * 4 + shade);
            }
            return RemapCache[maxBase] = t;
        }
    }

    static int Nearest(int rgb, int maxBase, int? shade)
    {
        var pal = Palette.Value;
        int best = 0;
        long bestD = long.MaxValue;
        for (int b = 1; b <= Math.Min(maxBase, pal.Length - 1); b++)
            for (int s = 0; s < 4; s++)
            {
                if (shade is int only && s != only) continue;
                int c = pal[b].Rgb[s];
                if (c < 0) continue;
                long dr = ((rgb >> 16) & 255) - ((c >> 16) & 255), dg = ((rgb >> 8) & 255) - ((c >> 8) & 255), db = (rgb & 255) - (c & 255);
                long d = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
                if (d < bestD) { bestD = d; best = shade == null ? b * 4 + s : b; }
            }
        return best;
    }

    static readonly Dictionary<int, byte> FromRgbCache = [];

    /// <summary>The Java map colour closest to an RGB colour (Bedrock maps store RGBA).</summary>
    public static byte FromRgb(int rgb)
    {
        lock (FromRgbCache)
        {
            if (FromRgbCache.TryGetValue(rgb, out var c)) return c;
            return FromRgbCache[rgb] = (byte)Nearest(rgb, 61, null);
        }
    }

    // ---------------------------------------------------------------- reading

    /// <summary>Every map of a source world (Java, Bedrock or Legacy Console).</summary>
    public static List<CanonicalMap> Read(SaveWorld w)
    {
        var maps = new List<CanonicalMap>();
        try
        {
            switch (w)
            {
                case JavaWorld j:
                    string data = Path.Combine(j.Folder, "data");
                    if (Directory.Exists(data))
                        foreach (var f in Directory.EnumerateFiles(data, "map_*.dat"))
                            if (ParseId(Path.GetFileNameWithoutExtension(f)) is int id && FromJava(NbtFile.Load(f).RootCompound, id) is CanonicalMap m)
                                maps.Add(m);
                    break;
                case ConsoleWorld c:
                    foreach (var f in c.Archive.Files.Where(f => !f.Blank && f.Name.StartsWith("data/map_", StringComparison.OrdinalIgnoreCase)))
                        if (ParseId(Path.GetFileNameWithoutExtension(f.Name)) is int id && FromJava(NbtFile.Parse(f.Data).RootCompound, id) is CanonicalMap m)
                            maps.Add(m);
                    break;
                case BedrockWorld b:
                    foreach (var key in b.Db.Keys())
                    {
                        if (key.Length < 5 || key[0] != (byte)'m' || !Encoding.ASCII.GetString(key).StartsWith("map_", StringComparison.Ordinal)) continue;
                        if (!long.TryParse(Encoding.ASCII.GetString(key, 4, key.Length - 4), out long bid) || b.Db.Get(key) is not byte[] raw) continue;
                        if (FromBedrock(NbtBinary.ReadCompound(raw, NbtFormat.Bedrock), bid) is CanonicalMap m) maps.Add(m);
                    }
                    break;
                // The 3DS keeps its maps in its value store, in Bedrock's own form and under Bedrock's keys (map_<id>).
                case ThreeDsWorld t:
                    foreach (var key in t.Values.Keys.Where(k => k.StartsWith("map_", StringComparison.Ordinal)))
                        if (long.TryParse(key.AsSpan(4), out long tid) && t.Value(key) is byte[] raw3ds && FromBedrock(NbtBinary.ReadCompound(raw3ds, NbtFormat.Bedrock), tid) is CanonicalMap m3)
                            maps.Add(m3);
                    break;
            }
        }
        catch (Exception ex)
        {
            ConversionLog.Note(LossKind.World, "Map art", "some maps could not be read: " + ex.Message, lost: true);
        }
        return maps;
    }

    static int? ParseId(string name) => name.StartsWith("map_", StringComparison.Ordinal) && int.TryParse(name[4..], out int id) ? id : null;

    static CanonicalMap? FromJava(NbtCompound root, int id)
    {
        var d = root.GetCompound("data") ?? root;
        if (d["colors"] is not NbtByteArray colors) return null;
        var m = new CanonicalMap
        {
            Id = id, Scale = d.GetInt("scale"), XCenter = d.GetInt("xCenter"), ZCenter = d.GetInt("zCenter"),
            Locked = d.GetBool("locked"), TrackingPosition = d.GetBool("trackingPosition", true), UnlimitedTracking = d.GetBool("unlimitedTracking"),
            Dimension = d["dimension"] switch
            {
                NbtString s => s.Value,
                { IsNumeric: true } n => n.AsLong switch { -1 => "minecraft:the_nether", 1 => "minecraft:the_end", _ => "minecraft:overworld" },
                _ => "minecraft:overworld",
            },
        };
        var bytes = colors.Value;
        Array.Copy(bytes, m.Colors, Math.Min(bytes.Length, m.Colors.Length));
        return m;
    }

    static CanonicalMap? FromBedrock(NbtCompound d, long bedrockId)
    {
        if (d["colors"] is not NbtByteArray colors) return null;
        var rgba = colors.Value;
        var m = new CanonicalMap
        {
            Id = ConversionLog.Current?.MapIdForBedrock(bedrockId) ?? (int)bedrockId,
            Scale = d.GetInt("scale"), XCenter = d.GetInt("xCenter"), ZCenter = d.GetInt("zCenter"), Locked = d.GetBool("mapLocked"),
            UnlimitedTracking = d.GetBool("unlimitedTracking"),
            Dimension = d.GetInt("dimension") switch { 1 => "minecraft:the_nether", 2 => "minecraft:the_end", _ => "minecraft:overworld" },
        };
        int width = d.GetInt("width", 128), height = d.GetInt("height", 128);
        for (int y = 0; y < Math.Min(128, height); y++)
            for (int x = 0; x < Math.Min(128, width); x++)
            {
                int o = (y * width + x) * 4;
                if (o + 3 >= rgba.Length) break;
                m.Colors[y * 128 + x] = rgba[o + 3] < 128 ? (byte)0 : FromRgb((rgba[o] << 16) | (rgba[o + 1] << 8) | rgba[o + 2]);
            }
        return m;
    }

    // ---------------------------------------------------------------- writing

    /// <summary>Java data/map_N.dat files and idcounts.dat, in the layout of the target version.</summary>
    public static void WriteJava(string folder, IReadOnlyList<CanonicalMap> maps, ConversionTarget t, int dv)
    {
        if (maps.Count == 0) return;
        string data = Path.Combine(folder, "data");
        Directory.CreateDirectory(data);
        foreach (var m in maps)
        {
            var file = new NbtFile { Format = NbtFormat.Java, Compression = IO.CompressionKind.Gzip };
            var root = new NbtCompound { { "data", JavaData(m, t, dv, legacyDimension: dv < 2566) } };
            if (dv >= 1451) root.SetInt("DataVersion", dv);
            file.Roots.Add(new NamedTag("", root));
            file.Save(Path.Combine(data, $"map_{m.Id}.dat"));
        }
        var counts = new NbtFile { Format = NbtFormat.Java, Compression = dv >= 1451 ? IO.CompressionKind.Gzip : IO.CompressionKind.None };
        int max = maps.Max(m => m.Id);
        var idRoot = dv >= 1451
            ? new NbtCompound { { "data", new NbtCompound { { "map", new NbtInt(max) } } }, { "DataVersion", new NbtInt(dv) } }
            : new NbtCompound { { "map", new NbtShort((short)Math.Min(max, short.MaxValue)) } };
        counts.Roots.Add(new NamedTag("", idRoot));
        counts.Save(Path.Combine(data, "idcounts.dat"));
    }

    /// <summary>One map as a Java data/map_N.dat file for data version <paramref name="dv"/> (colours limited to the target's).</summary>
    public static NbtFile JavaMapFile(CanonicalMap m, ConversionTarget t, int dv)
    {
        var file = new NbtFile { Format = NbtFormat.Java, Compression = IO.CompressionKind.Gzip };
        var root = new NbtCompound { { "data", JavaData(m, t, dv, legacyDimension: dv < 2566) } };
        if (dv >= 1451) root.SetInt("DataVersion", dv);
        file.Roots.Add(new NamedTag("", root));
        return file;
    }

    static NbtCompound JavaData(CanonicalMap m, ConversionTarget t, int dv, bool legacyDimension)
    {
        var table = Remap(MaxBase(t));
        var colors = new byte[m.Colors.Length];
        int changed = 0;
        for (int i = 0; i < colors.Length; i++) { colors[i] = table[m.Colors[i]]; if (colors[i] != m.Colors[i]) changed++; }
        if (changed > 0) ConversionLog.Note(LossKind.World, "Map art colours", $"colours {t.Name} does not have became the nearest colour it has", changed);
        var d = new NbtCompound();
        d.SetByte("scale", (sbyte)m.Scale);
        if (legacyDimension) d.SetByte("dimension", (sbyte)(m.Dimension switch { "minecraft:the_nether" => -1, "minecraft:the_end" => 1, _ => 0 }));
        else d.SetString("dimension", m.Dimension);
        d.SetInt("xCenter", m.XCenter);
        d.SetInt("zCenter", m.ZCenter);
        d.SetByte("trackingPosition", (sbyte)(m.TrackingPosition ? 1 : 0));
        d.SetByte("unlimitedTracking", (sbyte)(m.UnlimitedTracking ? 1 : 0));
        d.SetByte("locked", (sbyte)(m.Locked ? 1 : 0));
        d.SetShort("width", 128);
        d.SetShort("height", 128);
        d["colors"] = new NbtByteArray(colors);
        d["banners"] = new NbtList(NbtType.Compound);
        d["frames"] = new NbtList(NbtType.Compound);
        return d;
    }

    /// <summary>Bedrock map_&lt;id&gt; records (RGBA colours).</summary>
    public static IEnumerable<(byte[] Key, byte[] Value)> BedrockRecords(IReadOnlyList<CanonicalMap> maps)
    {
        foreach (var m in maps)
        {
            var rgba = new byte[128 * 128 * 4];
            for (int i = 0; i < m.Colors.Length; i++)
            {
                int rgb = Rgb(m.Colors[i]);
                if (rgb < 0) continue;
                rgba[i * 4] = (byte)(rgb >> 16); rgba[i * 4 + 1] = (byte)(rgb >> 8); rgba[i * 4 + 2] = (byte)rgb; rgba[i * 4 + 3] = 255;
            }
            var d = new NbtCompound();
            d.SetLong("mapId", m.Id);
            d.SetLong("parentMapId", -1);
            d.SetByte("dimension", (sbyte)(m.Dimension switch { "minecraft:the_nether" => 1, "minecraft:the_end" => 2, _ => 0 }));
            d.SetByte("fullyExplored", 1);
            d.SetByte("mapLocked", (sbyte)(m.Locked ? 1 : 0));
            d.SetByte("scale", (sbyte)m.Scale);
            d.SetShort("height", 128);
            d.SetShort("width", 128);
            d.SetInt("xCenter", m.XCenter);
            d.SetInt("zCenter", m.ZCenter);
            d.SetByte("unlimitedTracking", (sbyte)(m.UnlimitedTracking ? 1 : 0));
            d["colors"] = new NbtByteArray(rgba);
            d["decorations"] = new NbtList(NbtType.Compound);
            yield return (Encoding.ASCII.GetBytes("map_" + m.Id.ToString(CultureInfo.InvariantCulture)), NbtBinary.Serialize(d, NbtFormat.Bedrock));
        }
    }

    /// <summary>Legacy Console archive entries (uncompressed Java NBT, 1.12 layout).</summary>
    public static IEnumerable<(string Name, byte[] Data)> ConsoleEntries(IReadOnlyList<CanonicalMap> maps, ConversionTarget t)
    {
        foreach (var m in maps)
            yield return ($"data/map_{m.Id}.dat", NbtBinary.Serialize(new NbtCompound { { "data", JavaData(m, t, 0, legacyDimension: true) } }, NbtFormat.Java));
        if (maps.Count > 0)
            yield return ("data/idcounts.dat", NbtBinary.Serialize(new NbtCompound { { "map", new NbtShort((short)Math.Min(maps.Max(m => m.Id), short.MaxValue)) } }, NbtFormat.Java));
    }
}
