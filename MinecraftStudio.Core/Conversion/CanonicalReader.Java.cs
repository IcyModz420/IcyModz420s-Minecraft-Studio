// MinecraftStudio.Core/Conversion/CanonicalReader.Java.cs: java for CanonicalReader.
// Part of CanonicalReader (main part in CanonicalReader.cs).
using System.Collections.Concurrent;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

public partial class CanonicalReader
{
    // ---------------------------------------------------------------- Java

    CanonicalChunk? ReadJava(ChunkPos pos)
    {
        var root = _dim.LoadChunkNbt(pos);
        if (root == null) return null;
        var level = JavaChunk.LevelOf(root);
        var layout = JavaChunk.Detect(root);
        int dv = JavaChunk.DataVersionOf(root) ?? 0;
        var col = JavaChunk.Decode(root, pos.X, pos.Z);
        var c = new CanonicalChunk(pos.X, pos.Z)
        {
            InhabitedTime = Math.Max(0, col.InhabitedTime),
            LastUpdate = Math.Max(0, col.LastUpdate),
            // 1.14+ statuses (structure_starts … light, spawn); before 1.13 an unpopulated chunk still has its terrain
            // and may have been walked in, so it is kept.
            Unfinished = dv >= 1901 && col.Status is string st && st is not ("full" or "minecraft:full" or "postprocessed" or "populated"),
        };
        bool numeric = layout is JavaChunkLayout.Flat128 or JavaChunkLayout.AnvilNumeric;
        foreach (var (sy, s) in col.Sections)
        {
            var palette = numeric ? s.Palette : s.Palette.Select(p => JavaBlocks.Canonicalize(p, dv)).ToArray();
            c.Sections[sy] = new BlockSection(sy, palette, s.Indices);
            if (s.Biomes != null) c.Biomes[sy] = Cells(s.Biomes.Select(BiomeNames.FromJavaName).ToArray());
        }
        ReadJavaBiomes(level, c);
        var tileEntities = (level.GetList("block_entities") ?? level.GetList("TileEntities"))?.OfType<NbtCompound>().ToList() ?? [];
        foreach (var te in tileEntities) Keep(pos, "block entity", () => c.BlockEntities.Add(BlockEntities.FromJava(te, dv)));
        var entities = level.GetList("Entities")?.OfType<NbtCompound>().ToList() ?? level.GetList("entities")?.OfType<NbtCompound>().ToList() ?? [];
        if (entities.Count == 0 && _dim is JavaRegionDimension jr)
        {
            try { if (jr.LoadEntitiesNbt(pos) is NbtCompound en) entities = en.GetList("Entities")?.OfType<NbtCompound>().ToList() ?? []; }
            catch (Exception ex) { Warnings.Add($"Chunk {pos}: its entities could not be read ({ex.Message}); the blocks were kept."); }
        }
        foreach (var e in entities) Keep(pos, "entity", () => c.Entities.Add(Entities.FromJava(e, dv)));
        if (numeric)
        {
            LegacyFixups.Apply(c, tileEntities);
            c.NeedsShapeUpdate = true;
        }
        return c;
    }

    void ReadJavaBiomes(NbtCompound level, CanonicalChunk c)
    {
        if (c.Biomes.Count > 0) return;
        switch (level["Biomes"])
        {
            case NbtByteArray b when b.Value.Length >= 256:
                c.ColumnBiomes = b.Value.Take(256).Select(v => JavaBiome(v)).ToArray();
                break;
            case NbtIntArray i when i.Value.Length == 256:
                c.ColumnBiomes = i.Value.Select(JavaBiome).ToArray();
                break;
            case NbtIntArray i when i.Value.Length >= 1024:
                {
                    // 1.15-1.17: 4x4x4 cells over y 0..255 (1536 in 21w06a-21w14a: y -64..319).
                    int rows = i.Value.Length / 16, baseSection = rows > 64 ? -4 : 0;
                    for (int cy = 0; cy < rows; cy++)
                    {
                        int sy = baseSection + cy / 4;
                        if (!c.Biomes.TryGetValue(sy, out var cells)) c.Biomes[sy] = cells = new string[64];
                        for (int j = 0; j < 16; j++)
                            cells[((cy & 3) << 4) | j] = JavaBiome(i.Value[cy * 16 + j]);
                    }
                    break;
                }
        }
    }

    /// <summary>64 cells from per-section biomes (64 cells as stored, or 4096 per block in 26.4+).</summary>
    static string[] Cells(string[] biomes)
    {
        if (biomes.Length == 64) return biomes;
        var cells = new string[64];
        for (int cy = 0; cy < 4; cy++)
            for (int cz = 0; cz < 4; cz++)
                for (int cx = 0; cx < 4; cx++)
                    cells[(cy << 4) | (cz << 2) | cx] = biomes[BlockSection.Index(cx * 4 + 1, cy * 4 + 1, cz * 4 + 1) % biomes.Length];
        return cells;
    }
}
