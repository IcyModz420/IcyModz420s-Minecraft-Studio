// Structures/LitematicSchematic.cs: Litematica's schematic file (.litematic).
// Owns: ToNbt / Save (one region: a palette with air first, the blocks bit-packed with values running across longs,
// block entities with x, y, z inside the region, entities with Pos from its corner, metadata with sizes and counts) and
// FromNbt (any number of regions, gathered into one structure; a region's size may be negative: its blocks start at the
// low corner). Contents are converted to and from the file's MinecraftDataVersion through StructureJava.
using System.Numerics;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public static class LitematicSchematic
{
    /// <summary>The format version written: 6 holds item components (1.20.5 and later), 5 is what earlier versions read.</summary>
    static int FormatVersion(int dv) => dv >= 3837 ? 6 : 5;

    public static void Save(Structure s, string path, int dataVersion = 0) =>
        new NbtFile(ToNbt(s, dataVersion, Path.GetFileNameWithoutExtension(path)), NbtFormat.Java, CompressionKind.Gzip).Save(path);

    public static NbtCompound ToNbt(Structure s, int dataVersion = 0, string? name = null)
    {
        int dv = StructureJava.Target(s, dataVersion);
        if (dv < StructureJava.Flattening) throw new ArgumentException("Litematica schematics hold named blocks (1.13 and later); for older versions save an MCEdit .schematic.");
        long volume = s.Volume;
        var palette = new List<BlockState> { BlockState.Air };
        var indexOf = new Dictionary<BlockState, int> { [BlockState.Air] = 0 };
        var java = new Dictionary<BlockState, int>();
        long nonAir = 0;
        var tiles = new NbtList(NbtType.Compound);
        // First the palette (the bits per block depend on its size), then the blocks packed.
        for (int y = 0; y < s.SizeY; y++)
            for (int z = 0; z < s.SizeZ; z++)
                for (int x = 0; x < s.SizeX; x++)
                {
                    var canonical = s.Get(x, y, z);
                    if (!java.TryGetValue(canonical, out int index))
                    {
                        var j = StructureJava.Block(canonical, dv);
                        if (!indexOf.TryGetValue(j, out index)) { index = palette.Count; palette.Add(j); indexOf[j] = index; }
                        java[canonical] = index;
                    }
                    if (index != 0) nonAir++;
                    if (s.BlockEntities.TryGetValue(s.Index(x, y, z), out var be) && StructureJava.BlockEntity(be, dv, canonical) is NbtCompound t)
                    {
                        t.SetInt("x", x); t.SetInt("y", y); t.SetInt("z", z);
                        tiles.Add(t);
                    }
                }
        int bits = Bits(palette.Count);
        var packed = new long[(volume * bits + 63) / 64];
        long at = 0;
        for (int y = 0; y < s.SizeY; y++)
            for (int z = 0; z < s.SizeZ; z++)
                for (int x = 0; x < s.SizeX; x++)
                    Set(packed, at++, bits, java[s.Get(x, y, z)]);

        var entities = new NbtList(NbtType.Compound);
        foreach (var e in s.Entities)
        {
            if (StructureJava.Entity(e.Nbt, dv) is not NbtCompound j) continue;
            j["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(e.X), new NbtDouble(e.Y), new NbtDouble(e.Z) };
            entities.Add(j);
        }
        var paletteList = new NbtList(NbtType.Compound);
        foreach (var p in palette) paletteList.Add(p.ToJavaNbt());
        string regionName = string.IsNullOrWhiteSpace(name) ? "Structure" : name;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        NbtCompound Vec(int x, int y, int z) => new() { ["x"] = new NbtInt(x), ["y"] = new NbtInt(y), ["z"] = new NbtInt(z) };
        return new NbtCompound
        {
            ["MinecraftDataVersion"] = new NbtInt(dv),
            ["Version"] = new NbtInt(FormatVersion(dv)),
            ["SubVersion"] = new NbtInt(1),
            ["Metadata"] = new NbtCompound
            {
                ["Name"] = new NbtString(regionName),
                ["Author"] = new NbtString(s.Author),
                ["Description"] = new NbtString(s.Origin ?? ""),
                ["RegionCount"] = new NbtInt(1),
                ["TotalVolume"] = new NbtInt((int)Math.Min(int.MaxValue, volume)),
                ["TotalBlocks"] = new NbtInt((int)Math.Min(int.MaxValue, nonAir)),
                ["TimeCreated"] = new NbtLong(now),
                ["TimeModified"] = new NbtLong(now),
                ["EnclosingSize"] = Vec(s.SizeX, s.SizeY, s.SizeZ),
            },
            ["Regions"] = new NbtCompound
            {
                [regionName] = new NbtCompound
                {
                    ["Position"] = Vec(0, 0, 0),
                    ["Size"] = Vec(s.SizeX, s.SizeY, s.SizeZ),
                    ["BlockStatePalette"] = paletteList,
                    ["BlockStates"] = new NbtLongArray(packed),
                    ["TileEntities"] = tiles,
                    ["Entities"] = entities,
                    ["PendingBlockTicks"] = new NbtList(NbtType.Compound),
                    ["PendingFluidTicks"] = new NbtList(NbtType.Compound),
                },
            },
        };
    }

    public static bool Looks(NbtCompound root) => root.GetCompound("Regions") != null && root.ContainsKey("MinecraftDataVersion");

    public static Structure FromNbt(NbtCompound root)
    {
        int dv = root.GetInt("MinecraftDataVersion");
        var regions = root.GetCompound("Regions") ?? throw new SaveFormatException("This .litematic has no regions.");
        var boxes = new List<(NbtCompound Region, int X, int Y, int Z, int W, int H, int L, int Px, int Py, int Pz)>();
        foreach (var (_, tag) in regions)
        {
            if (tag is not NbtCompound r) continue;
            var (px, py, pz) = Vec(r.GetCompound("Position"));
            var (sx, sy, sz) = Vec(r.GetCompound("Size"));
            if (sx == 0 || sy == 0 || sz == 0) continue;
            // A negative size runs the other way from the position: the blocks start at the low corner.
            boxes.Add((r, sx < 0 ? px + sx + 1 : px, sy < 0 ? py + sy + 1 : py, sz < 0 ? pz + sz + 1 : pz, Math.Abs(sx), Math.Abs(sy), Math.Abs(sz), px, py, pz));
        }
        if (boxes.Count == 0) throw new SaveFormatException("This .litematic has no blocks.");
        int minX = boxes.Min(b => b.X), minY = boxes.Min(b => b.Y), minZ = boxes.Min(b => b.Z);
        long sizeX = boxes.Max(b => (long)b.X + b.W) - minX, sizeY = boxes.Max(b => (long)b.Y + b.H) - minY, sizeZ = boxes.Max(b => (long)b.Z + b.L) - minZ;
        if (sizeX > int.MaxValue || sizeY > int.MaxValue || sizeZ > int.MaxValue || sizeX * sizeY * sizeZ > Structure.MaxVolume)
            throw new SaveFormatException($"The schematic is far too large ({sizeX} × {sizeY} × {sizeZ}).");
        var meta = root.GetCompound("Metadata");
        var s = new Structure((int)sizeX, (int)sizeY, (int)sizeZ) { DataVersion = dv, Author = meta?.GetString("Author") ?? "", Origin = meta?.GetString("Name") };

        foreach (var b in boxes)
        {
            var palette = (b.Region.GetList("BlockStatePalette") ?? new NbtList()).Select(p => StructureJava.FromBlock(BlockState.FromJavaNbt(p), dv)).ToList();
            if (palette.Count == 0) continue;
            long[] states = (b.Region["BlockStates"] as NbtLongArray)?.Value ?? [];
            int bits = Bits(palette.Count);
            long volume = (long)b.W * b.H * b.L;
            if (states.LongLength * 64 < volume * bits) throw new SaveFormatException("The schematic's block data is shorter than its size.");
            int ox = b.X - minX, oy = b.Y - minY, oz = b.Z - minZ;
            for (long i = 0; i < volume; i++)
            {
                int value = Get(states, i, bits);
                int x = (int)(i % b.W), z = (int)(i / b.W % b.L), y = (int)(i / ((long)b.W * b.L));
                s.Set(ox + x, oy + y, oz + z, value < palette.Count ? palette[value] : BlockState.Air);
            }
            foreach (var t in (b.Region.GetList("TileEntities") ?? new NbtList()).OfType<NbtCompound>())
            {
                int x = ox + t.GetInt("x"), y = oy + t.GetInt("y"), z = oz + t.GetInt("z");
                if (!s.InRange(x, y, z)) continue;
                s.BlockEntities[s.Index(x, y, z)] = StructureJava.FromBlockEntity(t, dv);
            }
            // Entities are placed from the region's position (its first corner).
            foreach (var e in (b.Region.GetList("Entities") ?? new NbtList()).OfType<NbtCompound>())
            {
                if (e.GetList("Pos") is not { Count: 3 } p) continue;
                s.Entities.Add(new StructureEntity(b.Px - minX + p[0].AsDouble, b.Py - minY + p[1].AsDouble, b.Pz - minZ + p[2].AsDouble, StructureJava.FromEntity(e, dv)));
            }
        }
        return s;
    }

    static (int X, int Y, int Z) Vec(NbtCompound? c) => c == null ? (0, 0, 0) : (c.GetInt("x"), c.GetInt("y"), c.GetInt("z"));

    /// <summary>Bits per block: enough for the palette, at least 2.</summary>
    public static int Bits(int paletteSize) => Math.Max(2, 32 - BitOperations.LeadingZeroCount((uint)Math.Max(0, paletteSize - 1)));

    // Values are packed one after another; one can run from the top of a long into the next.
    public static int Get(long[] data, long index, int bits)
    {
        long start = index * bits;
        int first = (int)(start >> 6), last = (int)(((index + 1) * bits - 1) >> 6), offset = (int)(start & 63);
        long mask = (1L << bits) - 1;
        if (first == last) return (int)((long)((ulong)data[first] >> offset) & mask);
        return (int)(((long)((ulong)data[first] >> offset) | data[last] << (64 - offset)) & mask);
    }

    public static void Set(long[] data, long index, int bits, int value)
    {
        long start = index * bits;
        int first = (int)(start >> 6), last = (int)(((index + 1) * bits - 1) >> 6), offset = (int)(start & 63);
        long mask = (1L << bits) - 1, v = value & mask;
        data[first] = data[first] & ~(mask << offset) | v << offset;
        if (first != last)
        {
            int spill = 64 - offset;
            data[last] = (long)((ulong)data[last] >> (bits - spill) << (bits - spill)) | v >> spill;
        }
    }
}
