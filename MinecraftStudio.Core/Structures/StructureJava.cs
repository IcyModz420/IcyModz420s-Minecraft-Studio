// Structures/StructureJava.cs: a structure's contents in the form a given Java version stores them, and back.
// Owns: StructureJava — Newest (the newest Java version the program writes), Block / BlockEntity / Entity (canonical →
// the data of Java version dv, for writing a structure file: renames undone, blocks the version lacks replaced, items and
// text in that version's form; numbered blocks and old block entities for 1.12 and older) and FromBlock / FromBlockEntity
// / FromEntity (a file's data of version dv → canonical: old names, items and text upgraded), and FixLegacy (what a
// numbered schematic does not store — bed colours, banners, pots, fence and stair links — worked out as the game does).
// Every structure format goes through these, so a file made for one version loads right in another.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public static class StructureJava
{
    /// <summary>The newest Java data version the program writes; structure files are made for it unless another is chosen.</summary>
    public static int Newest { get; } = TargetCatalog.All.Where(t => t.Kind == TargetKind.Java).Max(t => t.DataVersion);

    /// <summary>1.13 (the flattening): blocks have names from here on, numbers before.</summary>
    public const int Flattening = 1451;

    /// <summary>1.12.2, the last numbered version (what MCEdit schematics hold).</summary>
    public const int Legacy = 1343;

    /// <summary>The data version files are written for: the one chosen, or the newest.</summary>
    public static int Target(int dataVersion) => dataVersion > 0 ? dataVersion : Newest;

    /// <summary>
    /// The data version a structure is written for: the one chosen, else the one its file had (1.13 or later — saving a
    /// 1.16 structure again keeps it a 1.16 file), else the newest.
    /// </summary>
    public static int Target(Structure s, int dataVersion) => dataVersion > 0 ? dataVersion : s.DataVersion >= Flattening ? s.DataVersion : Newest;

    /// <summary>A canonical block as Java version <paramref name="dv"/> (1.13 or later) names it.</summary>
    public static BlockState Block(BlockState canonical, int dv) => JavaBlocks.ToVersion(canonical, dv);

    /// <summary>A canonical block as 1.12-and-older numbers.</summary>
    public static (int Id, int Data) LegacyBlock(BlockState canonical) => JavaBlocks.ToLegacy(canonical);

    /// <summary>
    /// A canonical block entity as Java version <paramref name="dv"/> stores it (null when that version has no such
    /// block entity); its id is in "id", its position is left for the format to say.
    /// </summary>
    public static NbtCompound? BlockEntity(NbtCompound canonical, int dv, BlockState canonicalBlock)
    {
        NbtCompound? j = dv < Flattening
            ? BlockEntities.ToLegacyJava(canonical, canonical.GetString("id") ?? "", dv, canonicalBlock)
            : BlockEntities.ToJava(canonical, dv, Block(canonicalBlock, dv));
        j?.Remove("x"); j?.Remove("y"); j?.Remove("z");
        return j;
    }

    /// <summary>Block entities 1.12 and older keep for blocks that later versions say in the block (bed colours, banners, skulls, pots).</summary>
    public static NbtCompound? ImpliedLegacyBlockEntity(BlockState canonicalBlock, int dv)
    {
        if (BlockEntities.ImpliedForLegacyJava(canonicalBlock, 0, 0, 0) is not NbtCompound implied) return null;
        var j = BlockEntities.ToLegacyJava(implied, implied.GetString("id") ?? "", dv, canonicalBlock);
        j?.Remove("x"); j?.Remove("y"); j?.Remove("z");
        return j;
    }

    /// <summary>A canonical entity as Java version <paramref name="dv"/> stores it (its own Pos is replaced by the format's).</summary>
    public static NbtCompound? Entity(NbtCompound canonical, int dv) => Entities.ToJava(canonical, dv);

    // A data version of 0 (or none) is what this program wrote before structure files carried one: canonical already.
    /// <summary>A block a file of version <paramref name="dv"/> holds, in canonical form.</summary>
    public static BlockState FromBlock(BlockState s, int dv) => dv <= 0 ? s : JavaBlocks.Canonicalize(s, dv);

    /// <summary>A block entity a file of version <paramref name="dv"/> holds (with its "id"), in canonical form.</summary>
    public static NbtCompound FromBlockEntity(NbtCompound be, int dv) => dv <= 0 ? (NbtCompound)be.Clone() : BlockEntities.FromJava(be, dv);

    /// <summary>An entity a file of version <paramref name="dv"/> holds (with its "id"), in canonical form.</summary>
    public static NbtCompound FromEntity(NbtCompound e, int dv) => dv <= 0 ? (NbtCompound)e.Clone() : Entities.FromJava(e, dv);

    /// <summary>
    /// For a structure read from numbers (1.12 and older): what the numbers do not say, worked out as the game does —
    /// bed colours, banners, skulls, pots and the like from the raw block entities (positions inside the structure),
    /// then fence, pane, wall, stair, redstone and double-chest links from the neighbours.
    /// </summary>
    public static void FixLegacy(Structure s, IReadOnlyList<NbtCompound> rawBlockEntities)
    {
        // The structure as chunks (its low corner at 0, 0, 0), so the world reader's own fix-ups run on it.
        var chunks = new Dictionary<(int X, int Z), CanonicalChunk>();
        for (int cz = 0; cz <= (s.SizeZ - 1) >> 4; cz++)
            for (int cx = 0; cx <= (s.SizeX - 1) >> 4; cx++)
            {
                var c = new CanonicalChunk(cx, cz);
                for (int z = cz * 16; z < Math.Min(s.SizeZ, cz * 16 + 16); z++)
                    for (int x = cx * 16; x < Math.Min(s.SizeX, cx * 16 + 16); x++)
                        for (int y = 0; y < s.SizeY; y++)
                            if (s.Get(x, y, z) is { IsAir: false } b) c.SetBlock(x & 15, y, z & 15, b);
                chunks[(cx, cz)] = c;
            }
        foreach (var ((cx, cz), c) in chunks)
            LegacyFixups.Apply(c, rawBlockEntities.Where(be => be.GetInt("x") >> 4 == cx && be.GetInt("z") >> 4 == cz).ToList());
        BlockState? At(int x, int y, int z) =>
            x >= 0 && z >= 0 && y >= 0 && x < s.SizeX && y < s.SizeY && z < s.SizeZ && chunks.TryGetValue((x >> 4, z >> 4), out var c) ? c.GetBlock(x & 15, y, z & 15) : null;
        // Links are worked out from the blocks before any of them changed, as a world is read.
        var fixedChunks = chunks.ToDictionary(kv => kv.Key, kv => Copy(kv.Value));
        foreach (var (key, c) in fixedChunks) ShapeFixer.Fix(c, At, walls: true);
        foreach (var ((cx, cz), c) in fixedChunks)
            for (int z = cz * 16; z < Math.Min(s.SizeZ, cz * 16 + 16); z++)
                for (int x = cx * 16; x < Math.Min(s.SizeX, cx * 16 + 16); x++)
                    for (int y = 0; y < s.SizeY; y++)
                        s.Set(x, y, z, c.GetBlock(x & 15, y, z & 15));
    }

    static CanonicalChunk Copy(CanonicalChunk c)
    {
        var copy = new CanonicalChunk(c.X, c.Z);
        foreach (var (sy, section) in c.Sections) copy.Sections[sy] = new BlockSection(sy, section.Palette, section.Indices?.ToArray());
        return copy;
    }

    /// <summary>A block state the way WorldEdit and the game write it: minecraft:oak_stairs[facing=east,half=bottom].</summary>
    public static string StateString(BlockState s) => s.Properties.Length == 0 ? s.Name : $"{s.Name}[{s.Properties}]";

    /// <summary>Reads minecraft:oak_stairs[facing=east,half=bottom] (properties in any order).</summary>
    public static BlockState ParseState(string text)
    {
        text = text.Trim();
        int open = text.IndexOf('[');
        if (open < 0) return BlockState.Get(text.Contains(':') ? text : "minecraft:" + text);
        string name = text[..open];
        if (!name.Contains(':')) name = "minecraft:" + name;
        string inside = text[(open + 1)..].TrimEnd(']');
        var props = new List<KeyValuePair<string, string>>();
        foreach (var part in inside.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq > 0) props.Add(new(part[..eq], part[(eq + 1)..]));
        }
        return props.Count == 0 ? BlockState.Get(name) : BlockState.Get(name, props);
    }
}
