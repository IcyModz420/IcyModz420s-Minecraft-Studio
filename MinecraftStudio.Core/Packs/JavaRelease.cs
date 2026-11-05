// MinecraftStudio.Core/Packs/JavaRelease.cs: How a Java Edition release reads custom textures.
// Owns: JavaPackKind, JavaRelease, JavaReleases.
using System.Text.Json.Nodes;

namespace MinecraftStudio.Core.Packs;

/// <summary>How a Java Edition release reads custom textures.</summary>
public enum JavaPackKind
{
    /// <summary>Alpha 1.2.2 – 1.4.7: a texture pack (pack.txt) with all blocks in one terrain.png.</summary>
    TerrainAtlas,
    /// <summary>1.5 – 1.5.2: a texture pack with one file per block in textures/blocks.</summary>
    TexturePack,
    /// <summary>1.6.1 and newer: a resource pack (pack.mcmeta) with files under assets/minecraft.</summary>
    ResourcePack,
}

/// <summary>
/// A line of Java Edition releases that read packs the same way: the pack format they expect, and when skins and block
/// texture names changed. Formats come from the game's own version.json (checked against the installed jars) and the
/// Minecraft Wiki's pack format history.
/// </summary>
public sealed record JavaRelease(string First, string Last, JavaPackKind Kind, int Major, int Minor = 0)
{
    public string Label => First == Last ? $"Java {First}" : $"Java {First} – {Last}";
    public int Index => JavaReleases.All.ToList().IndexOf(this);
    /// <summary>64×64 skins (and slim arms) arrived in 1.8.</summary>
    public bool ModernSkins => Index >= JavaReleases.Index("1.8");
    /// <summary>Nine default skins (player/wide and player/slim) instead of Steve and Alex arrived in 1.19.3.</summary>
    public bool NineDefaultSkins => Index >= JavaReleases.Index("1.19.3");
    /// <summary>1.13 renamed textures/blocks to textures/block (and most block textures, with the flattening).</summary>
    public bool FlatNames => Index >= JavaReleases.Index("1.13");
    public string FormatText => Kind != JavaPackKind.ResourcePack ? "texture pack" : Minor == 0 && Major < 65 ? $"pack format {Major}" : $"pack format {Major}.{Minor}";
    public override string ToString() => Label;
}

public static class JavaReleases
{
    public static readonly IReadOnlyList<JavaRelease> All =
    [
        new("Alpha 1.2.2", "1.4.7", JavaPackKind.TerrainAtlas, 0),
        new("1.5", "1.5.2", JavaPackKind.TexturePack, 0),
        // 1.7 renamed most block textures (wood → planks_oak, cloth_0 → wool_colored_white …): its own line.
        new("1.6.1", "1.6.4", JavaPackKind.ResourcePack, 1),
        new("1.7.2", "1.7.10", JavaPackKind.ResourcePack, 1),
        new("1.8", "1.8.9", JavaPackKind.ResourcePack, 1),
        new("1.9", "1.10.2", JavaPackKind.ResourcePack, 2),
        new("1.11", "1.12.2", JavaPackKind.ResourcePack, 3),
        new("1.13", "1.14.4", JavaPackKind.ResourcePack, 4),
        new("1.15", "1.16.1", JavaPackKind.ResourcePack, 5),
        new("1.16.2", "1.16.5", JavaPackKind.ResourcePack, 6),
        new("1.17", "1.17.1", JavaPackKind.ResourcePack, 7),
        new("1.18", "1.18.2", JavaPackKind.ResourcePack, 8),
        new("1.19", "1.19.2", JavaPackKind.ResourcePack, 9),
        new("1.19.3", "1.19.3", JavaPackKind.ResourcePack, 12),
        new("1.19.4", "1.19.4", JavaPackKind.ResourcePack, 13),
        new("1.20", "1.20.1", JavaPackKind.ResourcePack, 15),
        new("1.20.2", "1.20.2", JavaPackKind.ResourcePack, 18),
        new("1.20.3", "1.20.4", JavaPackKind.ResourcePack, 22),
        new("1.20.5", "1.20.6", JavaPackKind.ResourcePack, 32),
        new("1.21", "1.21.1", JavaPackKind.ResourcePack, 34),
        new("1.21.2", "1.21.3", JavaPackKind.ResourcePack, 42),
        new("1.21.4", "1.21.4", JavaPackKind.ResourcePack, 46),
        new("1.21.5", "1.21.5", JavaPackKind.ResourcePack, 55),
        new("1.21.6", "1.21.6", JavaPackKind.ResourcePack, 63),
        new("1.21.7", "1.21.8", JavaPackKind.ResourcePack, 64),
        new("1.21.9", "1.21.10", JavaPackKind.ResourcePack, 69),
        new("1.21.11", "1.21.11", JavaPackKind.ResourcePack, 75),
        new("26.1", "26.1.2", JavaPackKind.ResourcePack, 84),
        new("26.2", "26.2", JavaPackKind.ResourcePack, 88),
        new("26.3", "26.3", JavaPackKind.ResourcePack, 97, 1),
    ];

    public static JavaRelease Newest => All[^1];

    /// <summary>The release line whose first version is <paramref name="first"/>.</summary>
    public static int Index(string first) => All.ToList().FindIndex(r => r.First == first);

    /// <summary>The release line a version number belongs to ("1.20.4" → 1.20.3 – 1.20.4), or null if unknown.</summary>
    public static JavaRelease? Of(string version)
    {
        if (!TryParse(version, out var v)) return null;
        foreach (var r in All)
            if (TryParse(r.First, out var first) && TryParse(r.Last, out var last) && Compare(v, first) >= 0 && Compare(v, last) <= 0) return r;
        if (TryParse(All[0].Last, out var oldest) && Compare(v, oldest) <= 0) return All[0];
        return null;
    }

    /// <summary>Numeric version parts ("1.21.11" → 1, 21, 11); alpha and beta versions are not numeric.</summary>
    static bool TryParse(string version, out int[] parts)
    {
        parts = [];
        var bits = version.Split('.');
        var list = new List<int>();
        foreach (var b in bits)
        {
            if (!int.TryParse(b, out int n)) return false;
            list.Add(n);
        }
        parts = list.ToArray();
        return parts.Length >= 2;
    }

    static int Compare(int[] a, int[] b)
    {
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    /// <summary>
    /// The "pack" section of pack.mcmeta for a resource pack meant for the releases <paramref name="from"/> to
    /// <paramref name="to"/>. Releases before 1.21.9 read pack_format (and, from 1.20.2, supported_formats); 1.21.9 and
    /// newer read min_format and max_format, and need the older fields only when the pack also declares older formats.
    /// </summary>
    public static JsonObject PackSection(JavaRelease from, JavaRelease to, string description)
    {
        if (from.Kind != JavaPackKind.ResourcePack || to.Kind != JavaPackKind.ResourcePack || to.Index < from.Index)
            throw new ArgumentException("pack.mcmeta is for resource packs (1.6.1 and newer), oldest release first.");
        var pack = new JsonObject { ["description"] = description };
        const int modern = 65;
        if (from.Major < modern)
        {
            // Older games compare pack_format with their own: use the newest older format in the range.
            var newestOld = All.Where(r => r.Index >= from.Index && r.Index <= to.Index && r.Major < modern).Last();
            pack["pack_format"] = newestOld.Major;
            if (to.Major != from.Major && to.Major >= 18) pack["supported_formats"] = new JsonArray(from.Major, to.Major);
        }
        if (to.Major >= modern)
        {
            pack["min_format"] = from.Minor == 0 ? from.Major : new JsonArray(from.Major, from.Minor);
            pack["max_format"] = to.Major;
        }
        return new JsonObject { ["pack"] = pack };
    }
}
