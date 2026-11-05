// MinecraftStudio.Core/Conversion/JavaWorldWriter.Chunks.cs: chunks for JavaWorldWriter.
// Part of JavaWorldWriter (main part in JavaWorldWriter.cs).
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

public partial class JavaWorldWriter
{
    // ---------------------------------------------------------------- 1.18+

    NbtCompound BuildModern(CanonicalChunk c)
    {
        var root = new NbtCompound();
        root.SetInt("DataVersion", Dv);
        var (minS, maxS) = SectionRange(Dv);
        root.SetInt("xPos", c.X);
        root.SetInt("yPos", minS);
        root.SetInt("zPos", c.Z);
        root.SetString(Dv >= 5119 ? "status" : "Status", Dv >= 3463 ? "minecraft:full" : "full");
        root.SetLong("LastUpdate", c.LastUpdate);
        root.SetLong("InhabitedTime", c.InhabitedTime);
        root.SetByte("isLightOn", 0);
        var sections = new NbtList(NbtType.Compound);
        bool perBlockBiomes = Dv >= 5119;
        for (int sy = minS; sy <= maxS; sy++)
        {
            var sec = new NbtCompound();
            sec.SetByte("Y", (sbyte)sy);
            BlockState[] palette = [BlockState.Air];
            ushort[]? idx = null;
            if (c.Sections.TryGetValue(sy, out var s)) (palette, idx) = ChunkPacking.Remap(s, b => MapFlattened(b, Dv));
            var states = new NbtCompound();
            var pal = new NbtList(NbtType.Compound);
            foreach (var p in palette) pal.Add(PaletteEntry(p, Dv));
            states["palette"] = pal;
            if (idx != null && palette.Length > 1)
                states["data"] = new NbtLongArray(PackedArray.PackNonSpanning(idx, Math.Max(4, BinaryUtil.BitsFor(palette.Length))));
            sec["block_states"] = states;
            sec["biomes"] = BiomeContainer(c, sy, perBlockBiomes);
            sections.Add(sec);
        }
        root["sections"] = sections;
        var hm = new NbtCompound();
        foreach (var (k, v) in ChunkPacking.Heightmaps(c, minS * 16)) hm[k] = new NbtLongArray(ChunkPacking.PackHeightmap(v, false));
        root["Heightmaps"] = hm;
        root["block_entities"] = BlockEntityList(c, Dv, b => MapFlattened(b, Dv));
        root["block_ticks"] = new NbtList(NbtType.End);
        root["fluid_ticks"] = new NbtList(NbtType.End);
        var post = new NbtList(NbtType.List);
        for (int i = minS; i <= maxS; i++) post.Add(new NbtList(NbtType.End));
        root["PostProcessing"] = post;
        root["structures"] = new NbtCompound { { "References", new NbtCompound() }, { "starts", new NbtCompound() } };
        return root;
    }

    NbtCompound BiomeContainer(CanonicalChunk c, int sy, bool perBlock)
    {
        int count = perBlock ? 4096 : 64;
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            int x, y, z;
            if (perBlock) { x = i & 15; z = (i >> 4) & 15; y = i >> 8; }
            else { x = (i & 3) * 4; z = ((i >> 2) & 3) * 4; y = (i >> 4) * 4; }
            names[i] = BiomeNames.ToJavaName(c.BiomeAt(x, sy * 16 + y, z), Dv);
        }
        var palette = names.Distinct().ToList();
        var container = new NbtCompound();
        var pal = new NbtList(NbtType.String);
        foreach (var n in palette) pal.Add(new NbtString(n));
        container["palette"] = pal;
        if (palette.Count > 1)
        {
            var map = palette.Select((n, i) => (n, i)).ToDictionary(t => t.n, t => (ushort)t.i);
            var values = names.Select(n => map[n]).ToArray();
            container["data"] = new NbtLongArray(PackedArray.PackNonSpanning(values, BinaryUtil.BitsFor(palette.Count)));
        }
        return container;
    }

    // ---------------------------------------------------------------- 1.13 - 1.17 (Level wrapper, palettes)

    NbtCompound BuildPalette(CanonicalChunk c, int chunkDv, int targetDv)
    {
        var root = new NbtCompound();
        root.SetInt("DataVersion", chunkDv);
        var level = new NbtCompound();
        level.SetInt("xPos", c.X);
        level.SetInt("zPos", c.Z);
        level.SetLong("LastUpdate", c.LastUpdate);
        level.SetLong("InhabitedTime", c.InhabitedTime);
        level.SetString("Status", chunkDv >= 1901 ? "full" : "postprocessed");
        bool spanning = chunkDv < 2529;
        bool computeLight = chunkDv < 1901;
        var sky = computeLight ? ChunkPacking.SkyLight(c, 0, 15, HasSky) : null;
        var blockLight = computeLight ? ChunkPacking.BlockLight(c, 0, 15) : null;
        var sections = new NbtList(NbtType.Compound);
        for (int sy = 0; sy <= 15; sy++)
        {
            if (!c.Sections.TryGetValue(sy, out var s) || s.IsEmpty) continue;
            var (palette, idx) = ChunkPacking.Remap(s, b => MapFlattened(b, chunkDv));
            if (palette.Length == 1 && palette[0].IsAir) continue;
            var sec = new NbtCompound();
            sec.SetByte("Y", (sbyte)sy);
            var pal = new NbtList(NbtType.Compound);
            foreach (var p in palette) pal.Add(PaletteEntry(p, chunkDv));
            sec["Palette"] = pal;
            int bits = Math.Max(4, BinaryUtil.BitsFor(palette.Length));
            var values = idx ?? new ushort[4096];
            sec["BlockStates"] = new NbtLongArray(spanning ? PackedArray.PackSpanning(values, bits) : PackedArray.PackNonSpanning(values, bits));
            if (computeLight)
            {
                sec["SkyLight"] = new NbtByteArray(ChunkPacking.Nibbles(sky![sy]));
                sec["BlockLight"] = new NbtByteArray(ChunkPacking.Nibbles(blockLight![sy]));
            }
            sections.Add(sec);
        }
        level["Sections"] = sections;
        if (chunkDv >= 2203)
        {
            var biomes = new int[1024];
            for (int cy = 0; cy < 64; cy++)
                for (int cz = 0; cz < 4; cz++)
                    for (int cx = 0; cx < 4; cx++)
                        biomes[(cy * 4 + cz) * 4 + cx] = BiomeNames.ToJavaId(c.BiomeAt(cx * 4, cy * 4, cz * 4));
            level["Biomes"] = new NbtIntArray(biomes);
        }
        else
        {
            var biomes = new int[256];
            var surface = c.Heights();
            for (int i = 0; i < 256; i++) biomes[i] = BiomeNames.ToJavaId(c.SurfaceBiome(i & 15, i >> 4, surface));
            level["Biomes"] = new NbtIntArray(biomes);
        }
        var hm = new NbtCompound();
        foreach (var (k, v) in ChunkPacking.Heightmaps(c, 0)) hm[k] = new NbtLongArray(ChunkPacking.PackHeightmap(v, spanning));
        if (chunkDv < 1908) hm["LIGHT_BLOCKING"] = new NbtLongArray(ChunkPacking.PackHeightmap(ChunkPacking.Heightmaps(c, 0)["MOTION_BLOCKING"], spanning));
        level["Heightmaps"] = hm;
        level["TileEntities"] = BlockEntityList(c, chunkDv, b => MapFlattened(b, chunkDv));
        if (chunkDv < 2681) level["Entities"] = EntityList(c, chunkDv);
        level["TileTicks"] = new NbtList(NbtType.End);
        level["LiquidTicks"] = new NbtList(NbtType.End);
        var post = new NbtList(NbtType.List);
        for (int i = 0; i < 16; i++) post.Add(new NbtList(NbtType.End));
        level["PostProcessing"] = post;
        level["Structures"] = new NbtCompound { { "References", new NbtCompound() }, { "Starts", new NbtCompound() } };
        if (chunkDv >= 1901) level.SetByte("isLightOn", 0);
        root["Level"] = level;
        return root;
    }

    // ---------------------------------------------------------------- 1.2 - 1.12 (numeric sections)

    NbtCompound BuildAnvilNumeric(CanonicalChunk c)
    {
        var root = new NbtCompound();
        if (Dv >= 100) root.SetInt("DataVersion", Dv);
        var level = new NbtCompound();
        level.SetInt("xPos", c.X);
        level.SetInt("zPos", c.Z);
        level.SetLong("LastUpdate", c.LastUpdate);
        level.SetByte("LightPopulated", 0);
        level.SetByte("TerrainPopulated", 1);
        level.SetByte("V", 1);
        level.SetLong("InhabitedTime", c.InhabitedTime);
        var sky = ChunkPacking.SkyLight(c, 0, 15, HasSky);
        var blockLight = ChunkPacking.BlockLight(c, 0, 15);
        var sections = new NbtList(NbtType.Compound);
        var heights = new int[256];
        for (int sy = 0; sy <= 15; sy++)
        {
            if (!c.Sections.TryGetValue(sy, out var s) || s.IsEmpty) continue;
            var ids = new byte[4096];
            var data = new byte[2048];
            byte[]? add = null;
            var numeric = s.Palette.Select(p => JavaBlocks.ToLegacy(p, _target.LegacyLevel)).ToArray();
            bool any = false;
            for (int i = 0; i < 4096; i++)
            {
                var (id, d) = numeric[s.Indices?[i] ?? 0];
                if (id == 0) continue;
                any = true;
                ids[i] = (byte)id;
                if (id > 255) { add ??= new byte[2048]; PackedArray.SetNibble(add, i, id >> 8); }
                PackedArray.SetNibble(data, i, d);
                int col = ((i >> 4) & 15) * 16 + (i & 15);
                heights[col] = Math.Max(heights[col], sy * 16 + (i >> 8) + 1);
            }
            if (!any) continue;
            var sec = new NbtCompound();
            sec.SetByte("Y", (sbyte)sy);
            sec["Blocks"] = new NbtByteArray(ids);
            if (add != null) sec["Add"] = new NbtByteArray(add);
            sec["Data"] = new NbtByteArray(data);
            sec["BlockLight"] = new NbtByteArray(ChunkPacking.Nibbles(blockLight[sy]));
            sec["SkyLight"] = new NbtByteArray(ChunkPacking.Nibbles(sky[sy]));
            sections.Add(sec);
        }
        level["Sections"] = sections;
        var biomes = new byte[256];
        var surface = c.Heights();
        for (int i = 0; i < 256; i++) biomes[i] = (byte)BiomeNames.ToJavaId(c.SurfaceBiome(i & 15, i >> 4, surface));
        level["Biomes"] = new NbtByteArray(biomes);
        level["HeightMap"] = new NbtIntArray(heights);
        level["Entities"] = EntityList(c, Dv);
        level["TileEntities"] = LegacyBlockEntityList(c);
        root["Level"] = level;
        return root;
    }

    // ---------------------------------------------------------------- Alpha / McRegion (128 high, XZY)

    NbtCompound BuildFlat(CanonicalChunk c)
    {
        var level = new NbtCompound();
        level.SetInt("xPos", c.X);
        level.SetInt("zPos", c.Z);
        level.SetLong("LastUpdate", c.LastUpdate);
        level.SetByte("TerrainPopulated", 1);
        var blocks = new byte[32768];
        var data = new byte[16384];
        var skyArr = new byte[16384];
        var lightArr = new byte[16384];
        var heights = new byte[256];
        var sky = ChunkPacking.SkyLight(c, 0, 7, HasSky);
        var blockLight = ChunkPacking.BlockLight(c, 0, 7);
        for (int sy = 0; sy < 8; sy++)
        {
            c.Sections.TryGetValue(sy, out var s);
            var numeric = s?.Palette.Select(p => JavaBlocks.ToLegacy(p, _target.LegacyLevel)).ToArray();
            for (int i = 0; i < 4096; i++)
            {
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                int at = y + z * 128 + x * 2048;
                if (s != null && numeric != null)
                {
                    var (id, d) = numeric[s.Indices?[i] ?? 0];
                    blocks[at] = (byte)Math.Min(255, id);
                    PackedArray.SetNibble(data, at, d);
                    if (id != 0 && ChunkPacking.Opacity(s.Indices == null ? s.Palette[0] : s.Palette[s.Indices[i]]) > 0)
                        heights[z * 16 + x] = (byte)Math.Max(heights[z * 16 + x], y + 1);
                }
                PackedArray.SetNibble(skyArr, at, sky[sy][i]);
                PackedArray.SetNibble(lightArr, at, blockLight[sy][i]);
            }
        }
        level["Blocks"] = new NbtByteArray(blocks);
        level["Data"] = new NbtByteArray(data);
        level["SkyLight"] = new NbtByteArray(skyArr);
        level["BlockLight"] = new NbtByteArray(lightArr);
        level["HeightMap"] = new NbtByteArray(heights);
        level["Entities"] = EntityList(c, 0);
        level["TileEntities"] = LegacyBlockEntityList(c);
        return new NbtCompound { { "Level", level } };
    }
}
