// MinecraftStudio.Core/Java/JavaChunk.cs: Chunk NBT layouts used by Java Edition over the years.
// Owns: JavaChunkLayout, JavaChunk, ChunkSummary.
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Java;

/// <summary>Chunk NBT layouts used by Java Edition over the years.</summary>
public enum JavaChunkLayout
{
    Unknown,
    /// <summary>Infdev/Alpha/Beta/McRegion: Level.Blocks with 128-high XZY arrays.</summary>
    Flat128,
    /// <summary>Anvil 1.2-1.12: Level.Sections[].Blocks (+Add, Data).</summary>
    AnvilNumeric,
    /// <summary>1.13-1.15: Level.Sections[].Palette + BlockStates (values span longs).</summary>
    PaletteSpanning,
    /// <summary>1.16-1.17: Level.Sections[].Palette + BlockStates (no spanning).</summary>
    Palette,
    /// <summary>1.18+: sections[].block_states / biomes containers.</summary>
    Containers,
}

public static class JavaChunk
{
    /// <summary>20w17a: block state arrays stop spanning longs.</summary>
    public const int NonSpanningDataVersion = 2529;
    /// <summary>21w43a: Level wrapper removed.</summary>
    public const int NoLevelWrapperDataVersion = 2844;

    public static NbtCompound LevelOf(NbtCompound root) => root.GetCompound("Level") ?? root;

    public static int? DataVersionOf(NbtCompound root) => root.GetLongOrNull("DataVersion") is long v ? (int)v : null;

    public static JavaChunkLayout Detect(NbtCompound root)
    {
        var level = LevelOf(root);
        var sections = level.GetList("sections") ?? level.GetList("Sections");
        if (sections != null)
        {
            foreach (var s in sections.OfType<NbtCompound>())
            {
                if (s.ContainsKey("block_states")) return JavaChunkLayout.Containers;
                if (s.ContainsKey("BlockStates") || s.ContainsKey("Palette"))
                    return (DataVersionOf(root) ?? 0) >= NonSpanningDataVersion ? JavaChunkLayout.Palette : JavaChunkLayout.PaletteSpanning;
                if (s.ContainsKey("Blocks")) return JavaChunkLayout.AnvilNumeric;
            }
            // Only empty sections: judge by data version.
            int dv = DataVersionOf(root) ?? 0;
            if (dv >= 2834) return JavaChunkLayout.Containers;
            if (dv >= NonSpanningDataVersion) return JavaChunkLayout.Palette;
            if (dv >= 1444) return JavaChunkLayout.PaletteSpanning;
            return JavaChunkLayout.AnvilNumeric;
        }
        if (level.ContainsKey("Blocks")) return JavaChunkLayout.Flat128;
        return JavaChunkLayout.Unknown;
    }

    public static (int X, int Z)? Position(NbtCompound root)
    {
        var level = LevelOf(root);
        if (level.GetLongOrNull("xPos") is long x && level.GetLongOrNull("zPos") is long z) return ((int)x, (int)z);
        return null;
    }

    /// <summary>Decodes block data of any Java chunk layout.</summary>
    public static ChunkColumn Decode(NbtCompound root, int fallbackX = 0, int fallbackZ = 0)
    {
        var level = LevelOf(root);
        var pos = Position(root) ?? (fallbackX, fallbackZ);
        var layout = Detect(root);
        int dv = DataVersionOf(root) ?? 0;
        ChunkColumn col;
        switch (layout)
        {
            case JavaChunkLayout.Flat128:
                col = new ChunkColumn(pos.X, pos.Z, 0, 7);
                DecodeFlat(level, col);
                break;
            case JavaChunkLayout.AnvilNumeric:
                col = new ChunkColumn(pos.X, pos.Z, 0, 15);
                DecodeAnvilNumeric(level, col);
                break;
            case JavaChunkLayout.PaletteSpanning:
            case JavaChunkLayout.Palette:
                col = new ChunkColumn(pos.X, pos.Z, 0, 15);
                DecodePalette(level, col, layout == JavaChunkLayout.PaletteSpanning);
                break;
            case JavaChunkLayout.Containers:
                {
                    int minY = (int)(level.GetLongOrNull("yPos") ?? -4);
                    col = new ChunkColumn(pos.X, pos.Z, minY, minY + 23);
                    DecodeContainers(level, col);
                    break;
                }
            default:
                col = new ChunkColumn(pos.X, pos.Z, 0, 15);
                break;
        }

        col.InhabitedTime = level.GetLong("InhabitedTime", -1);
        col.LastUpdate = level.GetLong("LastUpdate", -1);
        col.Status = StatusOf(level) ?? (level.ContainsKey("TerrainPopulated") ? (level.GetBool("TerrainPopulated") ? "populated" : "terrain") : null);
        col.BlockEntityCount = (level.GetList("block_entities") ?? level.GetList("TileEntities"))?.Count ?? 0;
        col.EntityCount = level.GetList("Entities")?.Count ?? 0;
        DecodeBiomes(level, col, dv);
        return col;
    }

    /// <summary>Alpha/McRegion: 32768 blocks, index = y + z*128 + x*2048.</summary>
    static void DecodeFlat(NbtCompound level, ChunkColumn col)
    {
        byte[] blocks = level.GetBytes("Blocks") ?? [];
        byte[] data = level.GetBytes("Data") ?? [];
        int height = blocks.Length / 256;
        if (height <= 0) return;
        int sections = (height + 15) / 16;
        for (int sy = 0; sy < sections; sy++)
        {
            var map = new Dictionary<int, ushort>();
            var palette = new List<BlockState>();
            var idx = new ushort[4096];
            for (int x = 0; x < 16; x++)
                for (int z = 0; z < 16; z++)
                    for (int y = 0; y < 16; y++)
                    {
                        int wy = sy * 16 + y;
                        if (wy >= height) continue;
                        int i = wy + z * height + x * height * 16;
                        int id = blocks[i];
                        int d = PackedArray.Nibble(data, i);
                        int key = (id << 4) | d;
                        if (!map.TryGetValue(key, out ushort p))
                        {
                            p = (ushort)palette.Count;
                            palette.Add(LegacyBlocks.Get(id, d));
                            map[key] = p;
                        }
                        idx[BlockSection.Index(x, y, z)] = p;
                    }
            if (palette.Count == 1 && palette[0].IsAir) continue;
            col.Sections[sy] = new BlockSection(sy, palette.ToArray(), palette.Count == 1 ? null : idx);
        }
    }

    /// <summary>Anvil 1.2-1.12: Blocks (YZX), Add (high nibble of id), Data.</summary>
    static void DecodeAnvilNumeric(NbtCompound level, ChunkColumn col)
    {
        foreach (var s in (level.GetList("Sections") ?? []).OfType<NbtCompound>())
        {
            byte[]? blocks = s.GetBytes("Blocks");
            if (blocks == null || blocks.Length < 4096) continue;
            int sy = (sbyte)s.GetInt("Y");
            byte[] data = s.GetBytes("Data") ?? [];
            byte[]? add = s.GetBytes("Add");
            byte[]? add2 = s.GetBytes("Add2"); // NEI/forge extended ids
            var map = new Dictionary<int, ushort>();
            var palette = new List<BlockState>();
            var idx = new ushort[4096];
            for (int i = 0; i < 4096; i++)
            {
                int id = blocks[i];
                if (add != null) id |= PackedArray.Nibble(add, i) << 8;
                if (add2 != null) id |= PackedArray.Nibble(add2, i) << 12;
                int d = PackedArray.Nibble(data, i);
                int key = (id << 4) | d;
                if (!map.TryGetValue(key, out ushort p))
                {
                    p = (ushort)palette.Count;
                    palette.Add(LegacyBlocks.Get(id, d));
                    map[key] = p;
                }
                idx[i] = p;
            }
            if (palette.Count == 1 && palette[0].IsAir) continue;
            col.Sections[sy] = new BlockSection(sy, palette.ToArray(), palette.Count == 1 ? null : idx);
        }
    }

    static BlockState[] ReadPalette(NbtList? list)
    {
        if (list == null || list.Count == 0) return [BlockState.Air];
        var arr = new BlockState[list.Count];
        for (int i = 0; i < arr.Length; i++) arr[i] = BlockState.FromJavaNbt(list[i]);
        return arr;
    }

    /// <summary>Chunk status ("Status" until 26.4 snapshot 1, then "status"), without the minecraft: prefix.</summary>
    public static string? StatusOf(NbtCompound level)
    {
        string? s = level.GetString("Status") ?? level.GetString("status");
        if (s != null && s.StartsWith("minecraft:", StringComparison.Ordinal)) s = s[10..];
        return s;
    }

    static void DecodePalette(NbtCompound level, ChunkColumn col, bool spanning)
    {
        foreach (var s in (level.GetList("Sections") ?? []).OfType<NbtCompound>())
        {
            int sy = (sbyte)s.GetInt("Y");
            var palette = ReadPalette(s.GetList("Palette"));
            long[]? states = s.GetLongs("BlockStates");
            if (states == null || states.Length == 0)
            {
                if (palette.Length == 1 && palette[0].IsAir) continue;
                col.Sections[sy] = new BlockSection(sy, palette, (ushort[]?)null);
                continue;
            }
            int bits = Math.Max(4, Core.IO.BinaryUtil.BitsFor(palette.Length));
            bool span = spanning;
            if (span) { if (states.Length * 64 / 4096 != bits && states.Length * 64 % 4096 == 0) bits = states.Length * 64 / 4096; }
            else bits = PackedArray.InferNonSpanningBits(states.Length, 4096, bits);
            int b = bits;
            var pal = palette;
            col.Sections[sy] = new BlockSection(sy, palette, () => Unpack(states, b, span, pal.Length));
        }
    }

    static ushort[] Unpack(long[] data, int bits, bool spanning, int paletteSize)
    {
        var idx = new ushort[4096];
        if (spanning) PackedArray.UnpackSpanning(data, bits, idx);
        else PackedArray.UnpackNonSpanning(data, bits, idx);
        for (int i = 0; i < idx.Length; i++) if (idx[i] >= paletteSize) idx[i] = 0;
        return idx;
    }

    static void DecodeContainers(NbtCompound level, ChunkColumn col)
    {
        foreach (var s in (level.GetList("sections") ?? level.GetList("Sections") ?? []).OfType<NbtCompound>())
        {
            int sy = (sbyte)s.GetInt("Y");
            var bs = s.GetCompound("block_states");
            BlockSection? section = null;
            if (bs != null)
            {
                var palette = ReadPalette(bs.GetList("palette"));
                long[]? data = bs.GetLongs("data");
                if (data == null || data.Length == 0 || palette.Length == 1)
                {
                    if (!(palette.Length == 1 && palette[0].IsAir)) section = new BlockSection(sy, palette, (ushort[]?)null);
                }
                else
                {
                    int bits = PackedArray.InferNonSpanningBits(data.Length, 4096, Math.Max(4, Core.IO.BinaryUtil.BitsFor(palette.Length)));
                    var pal = palette;
                    section = new BlockSection(sy, palette, () => Unpack(data, bits, false, pal.Length));
                }
            }
            var biomes = s.GetCompound("biomes");
            if (biomes != null)
            {
                var names = (biomes.GetList("palette") ?? []).Select(BiomeName).ToArray();
                if (names.Length > 0)
                {
                    long[]? bdata = biomes.GetLongs("data");
                    // 4x4x4 cells (64 entries) until 26.4 snapshot 1, then one biome per block (4096).
                    // 64 entries never need more than 7 longs; 4096 entries need at least 64.
                    int count = bdata != null && bdata.Length > 7 ? 4096 : 64;
                    var arr = new string[count];
                    if (bdata == null || bdata.Length == 0 || names.Length == 1) Array.Fill(arr, names[0]);
                    else
                    {
                        int bits = PackedArray.InferNonSpanningBits(bdata.Length, count, Core.IO.BinaryUtil.BitsFor(names.Length));
                        var tmp = new ushort[count];
                        PackedArray.UnpackNonSpanning(bdata, bits, tmp);
                        for (int i = 0; i < count; i++) arr[i] = names[tmp[i] < names.Length ? tmp[i] : 0];
                    }
                    section ??= new BlockSection(sy, [BlockState.Air], (ushort[]?)null);
                    section.Biomes = arr;
                }
            }
            if (section != null) col.Sections[sy] = section;
        }
    }

    static string BiomeName(NbtTag t) => t switch
    {
        NbtString s => s.Value,
        NbtCompound c when c[""] is NbtString w => w.Value,
        _ => "minecraft:plains",
    };

    static void DecodeBiomes(NbtCompound level, ChunkColumn col, int dataVersion)
    {
        var tag = level["Biomes"];
        switch (tag)
        {
            case NbtByteArray b when b.Value.Length >= 256:
                col.BiomeIds = b.Value.Take(256).Select(v => (int)v).ToArray();
                break;
            case NbtIntArray i when i.Value.Length == 256:
                col.BiomeIds = (int[])i.Value.Clone();
                break;
            case NbtIntArray i when i.Value.Length >= 1024:
                {
                    // 4x4x4 cells; take the layer at sea level (y 64 -> layer 16) for the 2D view.
                    var ids = new int[256];
                    int layer = Math.Min(i.Value.Length / 16 - 1, 16);
                    for (int z = 0; z < 16; z++)
                        for (int x = 0; x < 16; x++)
                            ids[z * 16 + x] = i.Value[layer * 16 + (z >> 2) * 4 + (x >> 2)];
                    col.BiomeIds = ids;
                    break;
                }
        }
    }

    /// <summary>Reads the metadata the pruner needs without decoding blocks.</summary>
    public static ChunkSummary Summarize(NbtCompound root)
    {
        var level = LevelOf(root);
        return new ChunkSummary
        {
            InhabitedTime = level.GetLong("InhabitedTime", -1),
            LastUpdate = level.GetLong("LastUpdate", -1),
            Status = StatusOf(level),
            DataVersion = DataVersionOf(root),
            Layout = Detect(root),
        };
    }
}

public sealed class ChunkSummary
{
    public long InhabitedTime { get; init; }
    public long LastUpdate { get; init; }
    public string? Status { get; init; }
    public int? DataVersion { get; init; }
    public JavaChunkLayout Layout { get; init; }

    /// <summary>True for fully generated chunks ("full", "minecraft:full", or pre-1.13 populated chunks).</summary>
    public bool IsFull => Status is null or "full" or "minecraft:full" or "populated" or "postprocessed" or "fullchunk";
}
