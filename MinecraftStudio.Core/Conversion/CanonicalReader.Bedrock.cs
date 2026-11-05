// MinecraftStudio.Core/Conversion/CanonicalReader.Bedrock.cs: bedrock for CanonicalReader.
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
    // ---------------------------------------------------------------- Bedrock

    CanonicalChunk? ReadBedrock(BedrockDimension dim, ChunkPos pos)
    {
        var world = (BedrockWorld)_world;
        var col = dim.LoadChunk(pos);
        if (col == null) return null;
        var c = new CanonicalChunk(pos.X, pos.Z);
        foreach (var (sy, s) in col.Sections) c.Sections[sy] = FromBedrockSection(s);
        // 3D biomes straight from the Data3D record (the column decoder keeps only the surface layer).
        var data3d = world.Db.Get(BedrockKeys.Make(pos.X, pos.Z, dim.Dimension, ChunkTag.Data3D));
        if (data3d != null) ReadData3D(data3d, c, col.MinSection);
        else if (col.BiomeIds != null) c.ColumnBiomes = col.BiomeIds.Select(BiomeNames.FromBedrockId).ToArray();
        // Block entities
        var be = world.Db.Get(BedrockKeys.Make(pos.X, pos.Z, dim.Dimension, ChunkTag.BlockEntity));
        var sourceBlockEntities = new List<NbtCompound>();
        if (be != null)
        {
            try { foreach (var t in NbtBinary.ReadAll(be, NbtFormat.Bedrock)) if (t.Tag is NbtCompound bc) sourceBlockEntities.Add(bc); }
            catch (Exception ex) { Warnings.Add($"Chunk {pos}: block entities: {ex.Message}"); }
        }
        foreach (var t in sourceBlockEntities)
            Keep(pos, "block entity", () => { if (BlockEntities.FromBedrock(t) is NbtCompound converted) c.BlockEntities.Add(converted); });
        // Entities: actor storage (1.18.30+) and the older per-chunk list; riders (actors of their own, linked from what they
        // ride) seated in it, as canonical keeps them.
        var actors = new List<(NbtCompound, NbtCompound)>();
        foreach (var actor in ReadBedrockActors(world, dim.Dimension, pos))
            Keep(pos, "entity", () => { if (Entities.FromBedrock(actor) is NbtCompound e) actors.Add((actor, e)); });
        c.Entities.AddRange(Entities.SeatRiders(actors));
        BedrockFixups.Apply(c, sourceBlockEntities);
        LegacyFixups.FixHalves(c);
        c.NeedsShapeUpdate = true;
        return c;
    }

    IEnumerable<NbtCompound> ReadBedrockActors(BedrockWorld world, int dim, ChunkPos pos)
    {
        var list = new List<NbtCompound>();
        var digp = world.Db.Get(BedrockKeys.DigestKey(pos.X, pos.Z, dim));
        if (digp != null)
            for (int i = 0; i + 8 <= digp.Length; i += 8)
            {
                var v = world.Db.Get(BedrockKeys.ActorKey(digp.AsSpan(i, 8)));
                if (v == null) continue;
                try { if (NbtBinary.ReadCompound(v, NbtFormat.Bedrock) is NbtCompound a) list.Add(a); }
                catch (Exception ex) { Warnings.Add($"Chunk {pos}: an entity record could not be read ({ex.Message}) and was left out."); }
            }
        var old = world.Db.Get(BedrockKeys.Make(pos.X, pos.Z, dim, ChunkTag.Entity));
        if (old != null)
        {
            try { foreach (var t in NbtBinary.ReadAll(old, NbtFormat.Bedrock)) if (t.Tag is NbtCompound a) list.Add(a); }
            catch (Exception ex) { Warnings.Add($"Chunk {pos}: some entity records could not be read ({ex.Message}); the ones before the damage were kept."); }
        }
        return list;
    }

    static void ReadData3D(byte[] data, CanonicalChunk c, int minSection)
    {
        if (data.Length < 512) return;
        int p = 512, sy = minSection;
        int[]? previous = null;
        while (p < data.Length)
        {
            int header = data[p++];
            int[] ids;
            if (header == 0xFF)
            {
                if (previous == null) break;
                ids = previous;
            }
            else
            {
                int bits = header >> 1;
                ids = new int[4096];
                if (bits == 0)
                {
                    if (p + 4 > data.Length) break;
                    Array.Fill(ids, BitConverter.ToInt32(data, p));
                    p += 4;
                }
                else
                {
                    int perWord = 32 / bits, words = (4096 + perWord - 1) / perWord;
                    if (p + words * 4 + 4 > data.Length) break;
                    var raw = new ushort[4096];
                    PackedArray.UnpackWords(data.AsSpan(p, words * 4), bits, raw);
                    p += words * 4;
                    int count = BitConverter.ToInt32(data, p);
                    p += 4;
                    if (count <= 0 || p + count * 4 > data.Length) break;
                    var pal = new int[count];
                    for (int i = 0; i < count; i++) pal[i] = BitConverter.ToInt32(data, p + i * 4);
                    p += count * 4;
                    for (int i = 0; i < 4096; i++) ids[i] = pal[raw[i] < count ? raw[i] : 0];
                }
            }
            previous = ids;
            var cells = new string[64];
            for (int cy = 0; cy < 4; cy++)
                for (int cz = 0; cz < 4; cz++)
                    for (int cx = 0; cx < 4; cx++)
                        cells[(cy << 4) | (cz << 2) | cx] = BiomeNames.FromBedrockId(ids[((cx * 4 + 1) << 8) | ((cz * 4 + 1) << 4) | (cy * 4 + 1)]);
            c.Biomes[sy] = cells;
            sy++;
        }
    }

    static readonly ConcurrentDictionary<int, BlockState> BedrockToJavaCache = new();

    /// <summary>Canonical Java state for a current Bedrock state.</summary>
    public static BlockState BedrockToJava(BlockState b) => BedrockToJavaCache.GetOrAdd(b.Id, _ =>
    {
        if (b.IsAir || b.Name == "runtime_id") return BlockState.Air;
        // Item frames are blocks on Bedrock and entities on Java: keep the Bedrock state for BedrockFixups.
        if (b.Name is "minecraft:frame" or "minecraft:glow_frame") return b;
        // The inverse table picks an arbitrary potted plant; the plant comes from the block entity.
        if (b.Name == "minecraft:flower_pot") return BlockState.Get("minecraft:flower_pot");
        if (b.Name is "minecraft:unknown" or "minecraft:info_update" or "minecraft:info_update2" or "minecraft:reserved6") return BlockState.Get("minecraft:stone");
        var j = BedrockJavaMap.ToJava(BedrockBlocks.Fit(b));
        if (j != null) return JavaBlocks.Canonicalize(j);
        // Add-on, Education or removed blocks: a Java block of the same (or approximated) name, else keep the name
        // so the writer picks a stand-in.
        string approx = "minecraft:" + BlockNames.JavaBaseName(b);
        if (JavaBlockData.Latest.Blocks.ContainsKey(approx)) return JavaBlocks.Canonicalize(BlockState.Get(approx));
        return BlockState.Get(b.Name);
    });

    static bool IsWater(BlockState s) => s.Name is "minecraft:water" or "minecraft:flowing_water";

    /// <summary>Converts a Bedrock section; a water second layer becomes waterlogged=true.</summary>
    public static BlockSection FromBedrockSection(BlockSection s)
    {
        var main = s.Palette.Select(BedrockToJava).ToArray();
        var extra = s.Extra;
        if (extra == null || extra.Palette.All(p => !IsWater(p)))
            return new BlockSection(s.Y, main, s.Indices);
        var extraWater = extra.Palette.Select(IsWater).ToArray();
        var mainIdx = s.Indices;
        var extraIdx = extra.Indices;
        var map = new Dictionary<int, ushort>();
        var pal = new List<BlockState>();
        var idx = new ushort[4096];
        for (int i = 0; i < 4096; i++)
        {
            int m = mainIdx?[i] ?? 0;
            bool water = extraWater[extraIdx?[i] ?? 0];
            int key = (m << 1) | (water ? 1 : 0);
            if (!map.TryGetValue(key, out ushort pi))
            {
                var st = main[m];
                if (water) st = Waterlogged(st);
                pi = (ushort)pal.Count;
                pal.Add(st);
                map[key] = pi;
            }
            idx[i] = pi;
        }
        return new BlockSection(s.Y, pal.ToArray(), idx);
    }

    static BlockState Waterlogged(BlockState s)
    {
        if (s.IsAir) return BlockState.Get("minecraft:water", "level=0");
        if (s.GetProperty("waterlogged") != "false") return s;
        return BlockState.Get(s.Name, s.PropertyPairs().Select(p => new KeyValuePair<string, string>(p.Key, p.Key == "waterlogged" ? "true" : p.Value)));
    }

    CanonicalChunk? ReadBedrockNumeric(ChunkPos pos)
    {
        var col = _dim.LoadChunk(pos);
        if (col == null) return null;
        var c = new CanonicalChunk(pos.X, pos.Z);
        foreach (var (sy, s) in col.Sections) c.Sections[sy] = FromBedrockSection(s);
        if (col.BiomeIds != null) c.ColumnBiomes = col.BiomeIds.Select(BiomeNames.FromBedrockId).ToArray();
        c.NeedsShapeUpdate = true;
        if (_world is PocketLegacyWorld pw && pw.Entities?.Roots.FirstOrDefault()?.Tag is NbtCompound ents)
        {
            foreach (var te in ents.GetList("TileEntities")?.OfType<NbtCompound>() ?? [])
                if (InChunk(te, pos)) Keep(pos, "block entity", () => { if (BlockEntities.FromBedrock(te) is NbtCompound bc) c.BlockEntities.Add(bc); });
            foreach (var e in ents.GetList("Entities")?.OfType<NbtCompound>() ?? [])
                if (EntityInChunk(e, pos)) Keep(pos, "entity", () => { if (Entities.FromBedrock(e) is NbtCompound ec) c.Entities.Add(ec); });
        }
        return c;
    }

    static bool InChunk(NbtCompound te, ChunkPos pos) => te.GetInt("x") >> 4 == pos.X && te.GetInt("z") >> 4 == pos.Z;

    static bool EntityInChunk(NbtCompound e, ChunkPos pos) =>
        e.GetList("Pos") is { Count: 3 } p && (int)Math.Floor(p[0].AsDouble) >> 4 == pos.X && (int)Math.Floor(p[2].AsDouble) >> 4 == pos.Z;
}
