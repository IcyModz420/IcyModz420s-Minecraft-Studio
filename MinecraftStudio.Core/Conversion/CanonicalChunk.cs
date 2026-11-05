// MinecraftStudio.Core/Conversion/CanonicalChunk.cs: The edition-neutral chunk every reader produces and every
// writer consumes: canonical (newest) Java block states, canonical Java biome names in 4×4×4 cells, and block
// entities / entities in canonical Java form.
// Owns: CanonicalChunk.
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// The edition-neutral chunk every reader produces and every writer consumes: canonical (newest) Java block states,
/// canonical Java biome names in 4×4×4 cells, and block entities / entities in canonical Java form.
/// </summary>
public sealed class CanonicalChunk
{
    public CanonicalChunk(int x, int z)
    {
        X = x;
        Z = z;
    }

    public int X { get; }
    public int Z { get; }

    /// <summary>Sections by index (block y / 16): palette of canonical states + YZX indices.</summary>
    public SortedDictionary<int, BlockSection> Sections { get; } = [];

    /// <summary>Biome per 4×4×4 cell, per section: 64 names, index (y/4)*16 + (z/4)*4 + x/4.</summary>
    public Dictionary<int, string[]> Biomes { get; } = [];

    /// <summary>Biome per column (index z*16+x) for sources that only store 2D biomes.</summary>
    public string[]? ColumnBiomes { get; set; }

    /// <summary>Block entities in canonical Java form (absolute x/y/z, namespaced id).</summary>
    public List<NbtCompound> BlockEntities { get; } = [];

    /// <summary>Entities in canonical Java form (absolute Pos).</summary>
    public List<NbtCompound> Entities { get; } = [];

    public long InhabitedTime { get; set; }
    public long LastUpdate { get; set; }

    /// <summary>The source did not store neighbour-dependent shapes (fence links, stair corners...): recompute them.</summary>
    public bool NeedsShapeUpdate { get; set; }

    /// <summary>True when blocks exist below y=0 or above y=255 (a 1.18+ height world).</summary>
    public bool HasExtendedHeight => Sections.Any(kv => (kv.Key < 0 || kv.Key > 15) && !kv.Value.IsEmpty);

    public bool IsEmpty => Sections.Values.All(s => s.IsEmpty) && BlockEntities.Count == 0 && Entities.Count == 0;

    public BlockState GetBlock(int x, int y, int z) =>
        Sections.TryGetValue(y >> 4, out var s) ? s.Get(x & 15, y & 15, z & 15) : BlockState.Air;

    /// <summary>Sets one block, growing the section's palette as needed.</summary>
    public void SetBlock(int x, int y, int z, BlockState state)
    {
        int sy = y >> 4;
        if (!Sections.TryGetValue(sy, out var s))
        {
            if (state.IsAir) return;
            s = new BlockSection(sy, [BlockState.Air], (ushort[]?)null);
            Sections[sy] = s;
        }
        int idx = Array.IndexOf(s.Palette, state);
        if (idx < 0)
        {
            var pal = new BlockState[s.Palette.Length + 1];
            s.Palette.CopyTo(pal, 0);
            pal[^1] = state;
            idx = pal.Length - 1;
            var indices = s.Indices ?? new ushort[4096];
            s.Palette = pal;
            s.Indices = indices;
        }
        if (s.Indices == null)
        {
            if (idx == 0) return;
            s.Indices = new ushort[4096];
        }
        s.Indices[BlockSection.Index(x & 15, y & 15, z & 15)] = (ushort)idx;
    }

    /// <summary>Biome of the 4×4×4 cell containing block (x, y, z), falling back to the column biome, then plains.</summary>
    public string BiomeAt(int x, int y, int z)
    {
        if (Biomes.TryGetValue(y >> 4, out var cells))
            return cells[(((y & 15) >> 2) << 4) | (((z & 15) >> 2) << 2) | ((x & 15) >> 2)];
        if (ColumnBiomes != null) return ColumnBiomes[((z & 15) << 4) | (x & 15)] ?? "minecraft:plains";
        // Nearest section that has biomes (sections above/below the stored range).
        if (Biomes.Count > 0)
        {
            int best = Biomes.Keys.MinBy(k => Math.Abs(k - (y >> 4)));
            return Biomes[best][(((z & 15) >> 2) << 2) | ((x & 15) >> 2)];
        }
        return "minecraft:plains";
    }

    /// <summary>Highest non-air block per column (index z*16+x), or int.MinValue for an empty column.</summary>
    /// <summary>The old sea level in this chunk's coordinates (moves with a vertical shift).</summary>
    public int SeaLevelY { get; set; } = 64;

    /// <summary>
    /// The biome a column shows at its surface, for targets that keep one biome per column: the biome at the top block
    /// (1.18+ worlds have cave biomes underground), or at the old sea level for an empty column.
    /// </summary>
    public string SurfaceBiome(int x, int z, int[] heights)
    {
        int top = heights[((z & 15) << 4) | (x & 15)];
        return BiomeAt(x, top == int.MinValue ? SeaLevelY : top, z);
    }

    public int[] Heights()
    {
        var h = new int[256];
        Array.Fill(h, int.MinValue);
        foreach (var (sy, s) in Sections.Reverse())
        {
            if (s.IsEmpty) continue;
            for (int i = 0; i < 256; i++)
            {
                if (h[i] != int.MinValue) continue;
                int top = s.TopY(i & 15, i >> 4);
                if (top >= 0) h[i] = sy * 16 + top;
            }
        }
        return h;
    }

    /// <summary>
    /// A copy limited to the sections a target can store. When terrain below the new bottom is cut away, the bottom
    /// layer becomes bedrock wherever the column continued downwards, so nobody falls out of the world.
    /// </summary>
    public CanonicalChunk FitHeight(int minSection, int maxSection, bool bedrockFloor)
    {
        bool cutBelow = Sections.Any(kv => kv.Key < minSection && !kv.Value.IsEmpty);
        bool cutAbove = Sections.Any(kv => kv.Key > maxSection && !kv.Value.IsEmpty);
        if (!cutBelow && !cutAbove) return this;
        var c = new CanonicalChunk(X, Z) { InhabitedTime = InhabitedTime, LastUpdate = LastUpdate, ColumnBiomes = ColumnBiomes, NeedsShapeUpdate = NeedsShapeUpdate, SeaLevelY = SeaLevelY };
        foreach (var (sy, s) in Sections)
            if (sy >= minSection && sy <= maxSection) c.Sections[sy] = new BlockSection(sy, s.Palette, s.Indices?.ToArray()) { Biomes = s.Biomes };
        foreach (var (sy, b) in Biomes)
            if (sy >= minSection && sy <= maxSection) c.Biomes[sy] = b;
        int minY = minSection * 16, maxY = maxSection * 16 + 15;
        foreach (var be in BlockEntities)
            if (be.GetInt("y") >= minY && be.GetInt("y") <= maxY) c.BlockEntities.Add(be);
        foreach (var e in Entities)
            if (e.GetList("Pos") is { Count: 3 } p && p[1].AsDouble >= minY && p[1].AsDouble <= maxY + 1) c.Entities.Add(e);
        if (cutBelow && bedrockFloor)
        {
            var bedrock = BlockState.Get("minecraft:bedrock");
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                    if (!GetBlock(x, minY - 1, z).IsAir || !GetBlock(x, minY, z).IsAir) c.SetBlock(x, minY, z, bedrock);
        }
        return c;
    }

    /// <summary>Drops sections that hold only air and compacts palettes (unused entries removed, air first).</summary>
    public void Compact()
    {
        foreach (var sy in Sections.Keys.ToList())
        {
            var s = Sections[sy];
            if (s.Indices == null)
            {
                if (s.Palette.Length == 0 || s.Palette[0].IsAir) Sections.Remove(sy);
                else if (s.Palette.Length > 1) s.Palette = [s.Palette[0]];
                continue;
            }
            var used = new int[s.Palette.Length];
            foreach (var i in s.Indices) used[i]++;
            var map = new ushort[s.Palette.Length];
            var pal = new List<BlockState>();
            for (int i = 0; i < used.Length; i++)
            {
                if (used[i] == 0) continue;
                int existing = pal.IndexOf(s.Palette[i]);
                if (existing >= 0) { map[i] = (ushort)existing; continue; }
                map[i] = (ushort)pal.Count;
                pal.Add(s.Palette[i]);
            }
            if (pal.All(p => p.IsAir)) { Sections.Remove(sy); continue; }
            var idx = s.Indices;
            for (int i = 0; i < idx.Length; i++) idx[i] = map[idx[i]];
            s.Palette = pal.ToArray();
            if (pal.Count == 1) s.Indices = null;
        }
    }
}
