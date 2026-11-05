// Structures/McEditSchematic.cs: the MCEdit / Schematica / old WorldEdit schematic file (.schematic, 1.12 and older).
// Owns: ToNbt / Save (numbered blocks in Blocks and Data, AddBlocks for numbers past 255, block entities and entities
// as 1.12.2 writes them, with the block entities 1.12 keeps for bed colours, banners, skulls and pots) and FromNbt
// ("Alpha" and "Classic" materials; Schematica's AddBlocks or Add): numbers become canonical blocks, then what the
// numbers do not say (bed colours, pots, fence and stair links …) is worked out as the game does (StructureJava.FixLegacy).
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public static class McEditSchematic
{
    public static void Save(Structure s, string path) => new NbtFile(ToNbt(s), NbtFormat.Java, CompressionKind.Gzip, "Schematic").Save(path);

    public static NbtCompound ToNbt(Structure s)
    {
        if (s.SizeX > short.MaxValue || s.SizeY > short.MaxValue || s.SizeZ > short.MaxValue) throw new ArgumentException("A .schematic can be at most 32,767 blocks along each side.");
        const int dv = StructureJava.Legacy;
        long volume = s.Volume;
        var blocks = new byte[volume];
        var data = new byte[volume];
        byte[]? add = null;
        var tiles = new NbtList(NbtType.Compound);
        var numbers = new Dictionary<BlockState, (int Id, int Data)>();
        for (int y = 0; y < s.SizeY; y++)
            for (int z = 0; z < s.SizeZ; z++)
                for (int x = 0; x < s.SizeX; x++)
                {
                    var canonical = s.Get(x, y, z);
                    if (!numbers.TryGetValue(canonical, out var n)) numbers[canonical] = n = StructureJava.LegacyBlock(canonical);
                    long i = ((long)y * s.SizeZ + z) * s.SizeX + x;
                    blocks[i] = (byte)n.Id;
                    data[i] = (byte)(n.Data & 15);
                    if (n.Id > 255)
                    {
                        add ??= new byte[(volume + 1) / 2];
                        int high = (n.Id >> 8) & 15;
                        add[i >> 1] |= (byte)((i & 1) == 0 ? high << 4 : high);
                    }
                    NbtCompound? t = s.BlockEntities.TryGetValue(s.Index(x, y, z), out var be) ? StructureJava.BlockEntity(be, dv, canonical) : null;
                    t ??= StructureJava.ImpliedLegacyBlockEntity(canonical, dv);
                    if (t != null)
                    {
                        t.SetInt("x", x); t.SetInt("y", y); t.SetInt("z", z);
                        tiles.Add(t);
                    }
                }
        var entities = new NbtList(NbtType.Compound);
        foreach (var e in s.Entities)
        {
            if (StructureJava.Entity(e.Nbt, dv) is not NbtCompound j) continue;
            j["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(e.X), new NbtDouble(e.Y), new NbtDouble(e.Z) };
            entities.Add(j);
        }
        var root = new NbtCompound
        {
            ["Width"] = new NbtShort((short)s.SizeX),
            ["Height"] = new NbtShort((short)s.SizeY),
            ["Length"] = new NbtShort((short)s.SizeZ),
            ["Materials"] = new NbtString("Alpha"),
            ["Blocks"] = new NbtByteArray(blocks),
            ["Data"] = new NbtByteArray(data),
            ["Entities"] = entities,
            ["TileEntities"] = tiles,
            ["WEOriginX"] = new NbtInt(0), ["WEOriginY"] = new NbtInt(0), ["WEOriginZ"] = new NbtInt(0),
            ["WEOffsetX"] = new NbtInt(0), ["WEOffsetY"] = new NbtInt(0), ["WEOffsetZ"] = new NbtInt(0),
        };
        if (add != null) root["AddBlocks"] = new NbtByteArray(add);
        return root;
    }

    public static bool Looks(NbtCompound root) => root["Blocks"] is NbtByteArray && root.ContainsKey("Width") && root.ContainsKey("Length");

    public static Structure FromNbt(NbtCompound root)
    {
        int w = root.GetInt("Width") & 0xFFFF, h = root.GetInt("Height") & 0xFFFF, l = root.GetInt("Length") & 0xFFFF;
        if (w == 0 || h == 0 || l == 0) throw new SaveFormatException("The schematic has no size.");
        string materials = root.GetString("Materials") ?? "Alpha";
        if (materials is not ("Alpha" or "Classic"))
            throw new SaveFormatException($"This schematic holds \"{materials}\" blocks (MCEdit's Pocket Edition numbers); only Java (\"Alpha\") and Classic schematics are read.");
        bool classic = materials == "Classic";
        long volume = (long)w * h * l;
        byte[] blocks = root.GetBytes("Blocks") ?? throw new SaveFormatException("The schematic has no blocks.");
        byte[] data = root.GetBytes("Data") ?? new byte[volume];
        if (blocks.LongLength < volume) throw new SaveFormatException($"The schematic's blocks are shorter than its size ({blocks.LongLength:N0} of {volume:N0}).");
        byte[]? addNibbles = root.GetBytes("AddBlocks");
        byte[]? addBytes = root.GetBytes("Add") is { } a && a.LongLength >= volume ? a : null;

        var s = new Structure(w, h, l) { DataVersion = StructureJava.Legacy, Author = root.GetString("Author") ?? "" };
        var cache = new Dictionary<int, BlockState>();
        for (long i = 0; i < volume; i++)
        {
            int id = blocks[i];
            if (addNibbles != null && (i >> 1) < addNibbles.LongLength) id |= ((i & 1) == 0 ? (addNibbles[i >> 1] >> 4) & 15 : addNibbles[i >> 1] & 15) << 8;
            else if (addBytes != null) id |= addBytes[i] << 8;
            int d = i < data.LongLength ? data[i] & 15 : 0;
            int key = (id << 4) | d;
            if (!cache.TryGetValue(key, out var state)) cache[key] = state = classic ? JavaBlocks.FromClassic(id, d) : JavaBlocks.FromLegacy(id, d);
            int x = (int)(i % w), z = (int)(i / w % l), y = (int)(i / ((long)w * l));
            s.Set(x, y, z, state);
        }

        // Block entities as they are (bed colours, pots … are read from them), then each in canonical form.
        var raw = (root.GetList("TileEntities") ?? new NbtList()).OfType<NbtCompound>().Where(t => s.InRange(t.GetInt("x"), t.GetInt("y"), t.GetInt("z"))).ToList();
        StructureJava.FixLegacy(s, raw);
        foreach (var t in raw)
        {
            try { s.BlockEntities[s.Index(t.GetInt("x"), t.GetInt("y"), t.GetInt("z"))] = StructureJava.FromBlockEntity(t, StructureJava.Legacy); }
            catch (Exception e) when (e is InvalidCastException or FormatException or KeyNotFoundException or ArgumentException) { }
        }
        foreach (var e in (root.GetList("Entities") ?? new NbtList()).OfType<NbtCompound>())
        {
            if (e.GetList("Pos") is not { Count: 3 } p) continue;
            try { s.Entities.Add(new StructureEntity(p[0].AsDouble, p[1].AsDouble, p[2].AsDouble, StructureJava.FromEntity(e, StructureJava.Legacy))); }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or KeyNotFoundException or ArgumentException) { }
        }
        return s;
    }
}
