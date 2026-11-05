// MinecraftStudio.Core/Conversion/Hanging.cs: paintings, item frames and leash knots — the entities that hang on a block.
// Owns: Hanging and PaintingArt. Canonical form: TileX/TileY/TileZ (the block they hang in), item frames "Facing" 0–5
// (down, up, north, south, west, east), paintings "facing" 0–3 (south, west, north, east) and "variant" (the newest Java
// id). Each edition's own fields: Java Motive → variant and Facing → facing (1.19), TileX/Y/Z → block_pos (1.21.5), four
// directions and CamelCase art names before 1.13, Direction/Dir before 1.8; Bedrock Motive → Motif (1.20.70) with its own
// art names. Which versions have which painting, and the block a painting hangs in from its centre (checked on real saves).
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// A painting's art: its size in blocks, its name in Java before 1.13 (<paramref name="LegacyName"/>) and in Bedrock,
/// and the versions that first have it — a Java data version (0 = every version), a pre-1.13 Java level, a Bedrock version.
/// </summary>
public sealed record PaintingArt(string Id, int Width, int Height, string LegacyName, string BedrockName,
    int JavaSince = 0, double LegacySince = 0, int[]? BedrockSince = null);

public static class Hanging
{
    /// <summary>Java data versions where the fields changed.</summary>
    public const int VariantNames = 3105, BlockPosArray = 4325;

    static readonly int[] Bedrock_1_21_0 = [1, 21, 0], Bedrock_1_21_93 = [1, 21, 93], Pocket_1_0_7 = [1, 0, 7];

    /// <summary>Every vanilla painting (sizes and versions from the Minecraft Wiki; Bedrock names as Bedrock saves them).</summary>
    public static readonly IReadOnlyList<PaintingArt> Arts = BuildArts();

    static PaintingArt[] BuildArts()
    {
        var list = new List<PaintingArt>();
        void Classic(string id, int w, int h, string name, double legacySince = 0, int[]? bedrockSince = null) =>
            list.Add(new(id, w, h, name, name, 0, legacySince, bedrockSince));
        void Added(string id, int w, int h, int javaSince, int[]? bedrockSince, string? bedrockName = null) =>
            list.Add(new(id, w, h, "", bedrockName ?? id, javaSince, 99, bedrockSince));
        // Indev had 19; Alpha 1.1.1 added Graham, Creebet, Donkey Kong, Skeleton and Pigscene; Beta 1.2_01 Burning Skull; 1.4.2 Wither.
        const double alpha111 = LegacyLevel.Alpha_1_2;
        Classic("kebab", 1, 1, "Kebab"); Classic("aztec", 1, 1, "Aztec"); Classic("alban", 1, 1, "Alban"); Classic("aztec2", 1, 1, "Aztec2");
        Classic("bomb", 1, 1, "Bomb"); Classic("plant", 1, 1, "Plant"); Classic("wasteland", 1, 1, "Wasteland");
        Classic("wanderer", 1, 2, "Wanderer"); Classic("graham", 1, 2, "Graham", alpha111);
        Classic("pool", 2, 1, "Pool"); Classic("courbet", 2, 1, "Courbet"); Classic("sea", 2, 1, "Sea"); Classic("sunset", 2, 1, "Sunset"); Classic("creebet", 2, 1, "Creebet", alpha111);
        Classic("match", 2, 2, "Match"); Classic("bust", 2, 2, "Bust"); Classic("stage", 2, 2, "Stage"); Classic("void", 2, 2, "Void");
        Classic("skull_and_roses", 2, 2, "SkullAndRoses"); Classic("wither", 2, 2, "Wither", 1.04, Pocket_1_0_7);
        Classic("fighters", 4, 2, "Fighters"); Classic("skeleton", 4, 3, "Skeleton", alpha111); Classic("donkey_kong", 4, 3, "DonkeyKong", alpha111);
        Classic("pointer", 4, 4, "Pointer"); Classic("pigscene", 4, 4, "Pigscene", alpha111); Classic("burning_skull", 4, 4, "BurningSkull", LegacyLevel.Beta_1_3);
        // Bedrock had these four from the start (unused); Java added them in 1.19.
        foreach (var e in new[] { "earth", "wind", "fire", "water" }) Added(e, 2, 2, 3105, null, char.ToUpperInvariant(e[0]) + e[1..]);
        foreach (var (id, w, h) in new[]
        {
            ("meditative", 1, 1), ("prairie_ride", 1, 2), ("baroque", 2, 2), ("humble", 2, 2), ("bouquet", 3, 3), ("cavebird", 3, 3),
            ("cotan", 3, 3), ("endboss", 3, 3), ("fern", 3, 3), ("owlemons", 3, 3), ("sunflowers", 3, 3), ("tides", 3, 3), ("backyard", 3, 4),
            ("pond", 3, 4), ("changing", 4, 2), ("finding", 4, 2), ("lowmist", 4, 2), ("passage", 4, 2), ("orb", 4, 4), ("unpacked", 4, 4),
        })
            Added(id, w, h, 3953, Bedrock_1_21_0);
        Added("dennis", 3, 3, 4438, Bedrock_1_21_93);
        return [.. list];
    }

    static readonly Dictionary<string, PaintingArt> ById = Arts.ToDictionary(a => a.Id, StringComparer.Ordinal);
    static readonly Dictionary<string, PaintingArt> ByName = BuildNames();

    static Dictionary<string, PaintingArt> BuildNames()
    {
        var d = new Dictionary<string, PaintingArt>(StringComparer.Ordinal);
        foreach (var a in Arts)
        {
            if (a.LegacyName.Length > 0) d.TryAdd(a.LegacyName, a);
            d.TryAdd(a.BedrockName, a);
        }
        return d;
    }

    public static bool Is(string id) => id is "minecraft:painting" or "minecraft:item_frame" or "minecraft:glow_item_frame" or "minecraft:leash_knot";
    public static bool IsFrame(string id) => id is "minecraft:item_frame" or "minecraft:glow_item_frame";

    /// <summary>The art a variant names, or null for art this program does not know (a data pack's, or a newer game's).</summary>
    public static PaintingArt? Art(string? variant)
    {
        if (string.IsNullOrEmpty(variant)) return null;
        string bare = variant.StartsWith("minecraft:", StringComparison.Ordinal) ? variant[10..] : variant.Contains(':') ? "" : variant;
        return ById.GetValueOrDefault(bare);
    }

    /// <summary>
    /// The canonical variant for any edition's art name ("SkullAndRoses", "minecraft:kebab", "prairie_ride"...); an unknown
    /// name is kept, lower case and namespaced (resource locations are lower case).
    /// </summary>
    public static string CanonicalVariant(string name)
    {
        name = name.Trim();
        if (name.Contains(':')) return name.ToLowerInvariant();
        if (ByName.TryGetValue(name, out var art) || ById.TryGetValue(name, out art)) return "minecraft:" + art.Id;
        foreach (var a in Arts)
            if (a.Id.Equals(name, StringComparison.OrdinalIgnoreCase) || a.LegacyName.Equals(name, StringComparison.OrdinalIgnoreCase) || a.BedrockName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return "minecraft:" + a.Id;
        return "minecraft:" + name.ToLowerInvariant();
    }

    // ---------------------------------------------------------------- directions

    /// <summary>A four-way direction (0 south, 1 west, 2 north, 3 east) as the six-way one (2 north, 3 south, 4 west, 5 east).</summary>
    public static int SixWay(int fourWay) => (fourWay & 3) switch { 0 => 3, 1 => 4, 2 => 2, _ => 5 };
    /// <summary>A six-way direction as the four-way one; up and down have none (null).</summary>
    public static int? FourWay(int sixWay) => sixWay switch { 2 => 2, 3 => 0, 4 => 1, 5 => 3, _ => null };

    /// <summary>The unit vector of a six-way direction.</summary>
    public static (int X, int Y, int Z) Vector(int sixWay) => sixWay switch
    {
        0 => (0, -1, 0), 1 => (0, 1, 0), 2 => (0, 0, -1), 3 => (0, 0, 1), 4 => (-1, 0, 0), _ => (1, 0, 0),
    };

    /// <summary>The direction a hanging entity faces (six-way), from its canonical fields.</summary>
    public static int Facing(NbtCompound canonical)
    {
        string id = canonical.GetString("id") ?? "";
        if (id == "minecraft:painting") return SixWay(canonical.GetInt("facing", canonical.GetInt("Facing")));
        if (IsFrame(id)) return Math.Clamp(canonical.GetInt("Facing", 3), 0, 5);
        return 1; // leash knots: no facing; "up" gives no offset sideways
    }

    // ---------------------------------------------------------------- the block it hangs in

    /// <summary>
    /// How far a hanging entity's centre sits from the middle of its block, beyond the 15/32 toward the wall: ½ along the
    /// wall (to the left as seen from the front) for an even width, ½ up for an even height.
    /// </summary>
    public readonly record struct Offset(double Along, double Up)
    {
        public static Offset None => default;
    }

    /// <summary>The offset for a painting of known art, else worked out from the centre and block it was saved with.</summary>
    public static Offset OffsetOf(NbtCompound canonical)
    {
        string id = canonical.GetString("id") ?? "";
        if (id != "minecraft:painting") return Offset.None;
        if (Art(canonical.GetString("variant")) is PaintingArt art) return new(art.Width % 2 == 0 ? 0.5 : 0, art.Height % 2 == 0 ? 0.5 : 0);
        if (Entities.Position(canonical) is not var (px, py, pz) || !canonical.ContainsKey("TileX")) return Offset.None;
        int facing = Facing(canonical);
        var (dx, _, dz) = Vector(facing);
        double ox = px - (canonical.GetInt("TileX") + 0.5) + 0.46875 * dx, oz = pz - (canonical.GetInt("TileZ") + 0.5) + 0.46875 * dz;
        double along = ox * dz + oz * -dx, up = py - (canonical.GetInt("TileY") + 0.5);
        return new(Math.Abs(along) > 0.25 ? 0.5 : 0, Math.Abs(up) > 0.25 ? 0.5 : 0);
    }

    /// <summary>The block a hanging entity with this centre hangs in (the game's own placement, worked backwards).</summary>
    public static (int X, int Y, int Z) BlockFromCentre(double x, double y, double z, int sixWay, Offset offset)
    {
        var (dx, dy, dz) = Vector(sixWay);
        // Seen from the front, "along" runs to the counter-clockwise side: (dz, -dx).
        double cx = x + 0.46875 * dx - offset.Along * dz, cy = y + 0.46875 * dy - offset.Up, cz = z + 0.46875 * dz + offset.Along * dx;
        return ((int)Math.Floor(cx), (int)Math.Floor(cy), (int)Math.Floor(cz));
    }

    /// <summary>The centre the game gives a hanging entity in that block.</summary>
    public static (double X, double Y, double Z) CentreFromBlock(int x, int y, int z, int sixWay, Offset offset)
    {
        var (dx, dy, dz) = Vector(sixWay);
        return (x + 0.5 - 0.46875 * dx + offset.Along * dz, y + 0.5 - 0.46875 * dy + offset.Up, z + 0.5 - 0.46875 * dz - offset.Along * dx);
    }

    /// <summary>Sets TileX/TileY/TileZ from the entity's centre (after it was moved or turned), keeping its offset.</summary>
    public static void SetBlockFromCentre(NbtCompound canonical, Offset offset)
    {
        if (Entities.Position(canonical) is not var (x, y, z)) return;
        string id = canonical.GetString("id") ?? "";
        var (bx, by, bz) = id == "minecraft:leash_knot" ? ((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z)) : BlockFromCentre(x, y, z, Facing(canonical), offset);
        canonical.SetInt("TileX", bx); canonical.SetInt("TileY", by); canonical.SetInt("TileZ", bz);
        canonical.Remove("block_pos");
    }

    // ---------------------------------------------------------------- Java

    /// <summary>
    /// Java hanging-entity fields of any version → canonical, on the copy <paramref name="c"/> of <paramref name="e"/>
    /// (whose id is already canonical).
    /// </summary>
    public static void FromJava(NbtCompound c, NbtCompound e, int dv)
    {
        string id = c.GetString("id") ?? "";
        if (!Is(id)) return;
        int? fourWay = e["Facing"] is { IsNumeric: true } f ? (int)f.AsLong
            : e["Direction"] is { IsNumeric: true } d ? (int)d.AsLong
            : e["Dir"] is { IsNumeric: true } old ? (int)old.AsLong switch { 0 => 2, 2 => 0, var v => v } : null;
        if (id == "minecraft:painting")
        {
            string? art = e.GetString("variant") ?? e.GetString("Motive");
            if (art != null)
            {
                string variant = CanonicalVariant(art);
                c.SetString("variant", variant);
                if (Art(variant) == null && !art.Contains(':')) c.SetString("Motive", art); // an unknown old name, kept as written
                else c.Remove("Motive");
            }
            // Paintings always faced four ways ("facing" since 1.19, "Facing" before, "Direction"/"Dir" before 1.8).
            if (e["facing"] is { IsNumeric: true } lower) fourWay = (int)lower.AsLong;
            if (fourWay is int p) c.SetByte("facing", (sbyte)(p & 3));
            c.Remove("Facing");
        }
        else if (IsFrame(id))
        {
            // Frames faced four ways before 1.13 (frames on floors and ceilings came with it), six ways since.
            if (dv >= 1451 && e["Facing"] is { IsNumeric: true }) { }
            else if (dv >= 1451 && e["facing"] is { IsNumeric: true } six) c.SetByte("Facing", (sbyte)Math.Clamp(six.AsLong, 0, 5));
            else if (fourWay is int p) c.SetByte("Facing", (sbyte)SixWay(p));
            c.Remove("facing");
        }
        c.Remove("Direction"); c.Remove("Dir");
        if (e["block_pos"] is NbtIntArray { Value.Length: 3 } bp)
        {
            c.SetInt("TileX", bp.Value[0]); c.SetInt("TileY", bp.Value[1]); c.SetInt("TileZ", bp.Value[2]);
            c.Remove("block_pos");
        }
        else if (!c.ContainsKey("TileX")) SetBlockFromCentre(c, OffsetOf(c));
    }

    /// <summary>Why a Java target cannot show this painting's art (null when it can, or the art is not one this program knows).</summary>
    public static string? MissingInJava(NbtCompound canonical, int dv, double legacyLevel)
    {
        if (canonical.GetString("id") != "minecraft:painting" || Art(canonical.GetString("variant")) is not PaintingArt art) return null;
        bool has = dv >= 1451 ? dv >= art.JavaSince : art.JavaSince == 0 && legacyLevel >= art.LegacySince;
        return has ? null : $"the \"{Title(art)}\" painting needs a newer Java version";
    }

    /// <summary>
    /// Canonical → the fields of Java data version <paramref name="dv"/> (0 before 15w32a; <paramref name="legacyLevel"/>
    /// tells those apart) on the copy <paramref name="c"/>. Returns why the entity cannot be written, or null.
    /// </summary>
    public static string? ToJava(NbtCompound c, NbtCompound canonical, int dv, double legacyLevel)
    {
        string id = canonical.GetString("id") ?? "";
        if (!Is(id)) return null;
        bool fourWays = dv < 1451, oldNames = dv == 0 && legacyLevel < LegacyLevel.Release_1_8;
        int sixWay = Facing(canonical);
        c.Remove("Facing"); c.Remove("facing"); c.Remove("Direction"); c.Remove("Dir");
        if (id == "minecraft:painting")
        {
            if (MissingInJava(canonical, dv, legacyLevel) is string why) return why;
            string variant = canonical.GetString("variant") ?? "minecraft:kebab";
            var art = Art(variant);
            c.Remove("variant"); c.Remove("Motive");
            if (dv >= VariantNames) c.SetString("variant", variant);
            else if (dv >= 1451) c.SetString("Motive", variant);
            else c.SetString("Motive", art?.LegacyName is { Length: > 0 } legacy ? legacy : canonical.GetString("Motive") ?? Bare(variant));
            int four = FourWay(sixWay) ?? 0;
            if (dv >= VariantNames) c.SetByte("facing", (sbyte)four);
            else Direction(c, four, oldNames);
        }
        else if (IsFrame(id))
        {
            if (fourWays)
            {
                if (FourWay(sixWay) is not int four) return "item frames on floors and ceilings need Java 1.13";
                Direction(c, four, oldNames);
            }
            else c.SetByte("Facing", (sbyte)sixWay);
        }
        var (x, y, z) = BlockOf(canonical);
        c.Remove("TileX"); c.Remove("TileY"); c.Remove("TileZ"); c.Remove("block_pos");
        if (dv >= BlockPosArray) c["block_pos"] = new NbtIntArray([x, y, z]);
        else { c.SetInt("TileX", x); c.SetInt("TileY", y); c.SetInt("TileZ", z); }
        return null;
    }

    /// <summary>A four-way direction as the version writes it: Facing (1.8+), else Direction and the older Dir.</summary>
    static void Direction(NbtCompound c, int four, bool oldNames)
    {
        if (!oldNames) { c.SetByte("Facing", (sbyte)four); return; }
        c.SetByte("Direction", (sbyte)four);
        c.SetByte("Dir", (sbyte)(four switch { 0 => 2, 2 => 0, var v => v }));
    }

    /// <summary>The block a canonical hanging entity hangs in: its TileX/Y/Z (or block_pos), else worked out from its centre.</summary>
    public static (int X, int Y, int Z) BlockOf(NbtCompound canonical)
    {
        if (canonical.ContainsKey("TileX")) return (canonical.GetInt("TileX"), canonical.GetInt("TileY"), canonical.GetInt("TileZ"));
        if (canonical["block_pos"] is NbtIntArray { Value.Length: 3 } bp) return (bp.Value[0], bp.Value[1], bp.Value[2]);
        if (Entities.Position(canonical) is not var (x, y, z)) return (0, 0, 0);
        if (canonical.GetString("id") == "minecraft:leash_knot") return ((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));
        return BlockFromCentre(x, y, z, Facing(canonical), OffsetOf(canonical));
    }

    // ---------------------------------------------------------------- Bedrock

    /// <summary>Bedrock painting and leash knot fields → canonical, on <paramref name="c"/> (id and Pos already canonical).</summary>
    public static void FromBedrock(NbtCompound c, NbtCompound e)
    {
        string id = c.GetString("id") ?? "";
        if (id == "minecraft:painting")
        {
            string art = e.GetString("Motif") ?? e.GetString("Motive") ?? "Kebab";
            string variant = CanonicalVariant(art);
            c.SetString("variant", variant);
            if (Art(variant) == null) c.SetString("Motive", art);
            c.SetByte("facing", (sbyte)(e.GetInt("Direction") & 3));
            c.Remove("Motif"); c.Remove("Direction");
        }
        if (id is "minecraft:painting" or "minecraft:leash_knot" && !c.ContainsKey("TileX")) SetBlockFromCentre(c, OffsetOf(c));
    }

    /// <summary>Why a Bedrock target (version <paramref name="version"/>) cannot show this painting's art, or null.</summary>
    public static string? MissingInBedrock(NbtCompound canonical, int[] version)
    {
        if (canonical.GetString("id") != "minecraft:painting") return null;
        string variant = canonical.GetString("variant") ?? "minecraft:kebab";
        if (Art(variant) is not PaintingArt art)
            return variant.StartsWith("minecraft:", StringComparison.Ordinal) ? null : "Bedrock has no paintings from Java data packs";
        return art.BedrockSince == null || AtLeast(version, art.BedrockSince) ? null : $"the \"{Title(art)}\" painting needs a newer Bedrock version";
    }

    /// <summary>Canonical painting → Bedrock Motif (1.20.70+; both names for 1.20, which spans the change) or Motive, and Direction.</summary>
    public static void ToBedrock(NbtCompound b, NbtCompound canonical, int[] version)
    {
        if (canonical.GetString("id") != "minecraft:painting") return;
        string variant = canonical.GetString("variant") ?? "minecraft:kebab";
        string name = Art(variant)?.BedrockName ?? canonical.GetString("Motive") ?? Bare(variant);
        bool motif = AtLeast(version, [1, 20, 0]), motive = !AtLeast(version, Bedrock_1_21_0);
        if (motif) b.SetString("Motif", name);
        if (motive) b.SetString("Motive", name);
        b.SetByte("Direction", (sbyte)(FourWay(Facing(canonical)) ?? 0));
    }

    /// <summary>Whether a Bedrock version list (1, 21, 120, …) is at least <paramref name="min"/>; an empty one counts as newest.</summary>
    public static bool AtLeast(int[] version, int[] min)
    {
        if (version.Length == 0) return true;
        for (int i = 0; i < min.Length; i++)
        {
            int v = i < version.Length ? version[i] : 0;
            if (v != min[i]) return v > min[i];
        }
        return true;
    }

    static string Bare(string id) => id.StartsWith("minecraft:", StringComparison.Ordinal) ? id[10..] : id;

    static string Title(PaintingArt art) => string.Join(' ', art.Id.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
}
