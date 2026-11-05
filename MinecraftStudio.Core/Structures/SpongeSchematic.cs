// Structures/SpongeSchematic.cs: WorldEdit's schematic file (.schem — the Sponge format, versions 1, 2 and 3).
// Owns: ToNbt / Save (version 2, which every WorldEdit 7 and FastAsyncWorldEdit reads; root named "Schematic") and FromNbt
// (versions 1 to 3): a palette of block-state strings, the blocks as varints in y, z, x order, block entities with
// their position and id beside their data, entities with positions from the low corner. Contents are converted to and
// from the file's data version through StructureJava, so a schematic of any version places right in any world.
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public static class SpongeSchematic
{
    public static void Save(Structure s, string path, int dataVersion = 0) =>
        new NbtFile(ToNbt(s, dataVersion), NbtFormat.Java, CompressionKind.Gzip, "Schematic").Save(path);

    public static NbtCompound ToNbt(Structure s, int dataVersion = 0)
    {
        int dv = StructureJava.Target(s, dataVersion);
        if (dv < StructureJava.Flattening) throw new ArgumentException("WorldEdit .schem files are for Minecraft 1.13 and later; for older versions save an MCEdit .schematic.");
        if (s.SizeX > 65535 || s.SizeY > 65535 || s.SizeZ > 65535) throw new ArgumentException("A .schem can be at most 65,535 blocks along each side.");
        var palette = new Dictionary<string, int>(StringComparer.Ordinal);
        var named = new Dictionary<BlockState, string>();
        var data = new MemoryStream();
        var blockEntities = new NbtList(NbtType.Compound);
        for (int y = 0; y < s.SizeY; y++)
            for (int z = 0; z < s.SizeZ; z++)
                for (int x = 0; x < s.SizeX; x++)
                {
                    var canonical = s.Get(x, y, z);
                    if (!named.TryGetValue(canonical, out var key)) named[canonical] = key = StructureJava.StateString(StructureJava.Block(canonical, dv));
                    if (!palette.TryGetValue(key, out int index)) palette[key] = index = palette.Count;
                    WriteVarInt(data, index);
                    if (s.BlockEntities.TryGetValue(s.Index(x, y, z), out var be) && StructureJava.BlockEntity(be, dv, canonical) is NbtCompound j)
                    {
                        string id = j.GetString("id") ?? "";
                        j.Remove("id");
                        j["Pos"] = new NbtIntArray([x, y, z]);
                        j["Id"] = new NbtString(id);
                        blockEntities.Add(j);
                    }
                }
        var entities = new NbtList(NbtType.Compound);
        foreach (var e in s.Entities)
        {
            if (StructureJava.Entity(e.Nbt, dv) is not NbtCompound j) continue;
            string id = j.GetString("id") ?? "";
            j.Remove("id");
            j["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(e.X), new NbtDouble(e.Y), new NbtDouble(e.Z) };
            j["Id"] = new NbtString(id);
            entities.Add(j);
        }
        var paletteTag = new NbtCompound();
        foreach (var (key, index) in palette) paletteTag[key] = new NbtInt(index);
        return new NbtCompound
        {
            ["Version"] = new NbtInt(2),
            ["DataVersion"] = new NbtInt(dv),
            ["Metadata"] = new NbtCompound
            {
                ["Name"] = new NbtString(s.Origin ?? "Structure"),
                ["Author"] = new NbtString(s.Author),
                ["Date"] = new NbtLong(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                // Pasted with the low corner where the player stands.
                ["WEOffsetX"] = new NbtInt(0), ["WEOffsetY"] = new NbtInt(0), ["WEOffsetZ"] = new NbtInt(0),
            },
            ["Width"] = new NbtShort(unchecked((short)s.SizeX)),
            ["Height"] = new NbtShort(unchecked((short)s.SizeY)),
            ["Length"] = new NbtShort(unchecked((short)s.SizeZ)),
            ["Offset"] = new NbtIntArray([0, 0, 0]),
            ["PaletteMax"] = new NbtInt(palette.Count),
            ["Palette"] = paletteTag,
            ["BlockData"] = new NbtByteArray(data.ToArray()),
            ["BlockEntities"] = blockEntities,
            ["Entities"] = entities,
        };
    }

    /// <summary>Whether a file's root looks like a Sponge schematic (any version).</summary>
    public static bool Looks(NbtCompound root) =>
        root.GetCompound("Schematic") is NbtCompound inner ? inner.ContainsKey("Blocks") || inner.ContainsKey("BlockData") : root.ContainsKey("BlockData") || root["Blocks"] is NbtCompound;

    public static Structure FromNbt(NbtCompound root)
    {
        // Version 3 keeps everything in a "Schematic" compound under the root.
        if (root.GetCompound("Schematic") is NbtCompound inner) root = inner;
        int version = root.GetInt("Version", 1);
        int w = root.GetInt("Width") & 0xFFFF, h = root.GetInt("Height") & 0xFFFF, l = root.GetInt("Length") & 0xFFFF;
        if (w == 0 || h == 0 || l == 0) throw new SaveFormatException("The schematic has no size.");
        // Version 1 carried no data version: it was 1.13.
        int dv = root.GetInt("DataVersion", 1519);
        var blocks = version >= 3 ? root.GetCompound("Blocks") ?? throw new SaveFormatException("The schematic has no blocks.") : root;
        var paletteTag = blocks.GetCompound("Palette") ?? throw new SaveFormatException("The schematic has no block palette.");
        byte[] data = (blocks[version >= 3 ? "Data" : "BlockData"] as NbtByteArray)?.Value ?? throw new SaveFormatException("The schematic has no block data.");

        int max = paletteTag.Select(kv => kv.Value.IsNumeric ? (int)kv.Value.AsLong : -1).DefaultIfEmpty(-1).Max();
        if (max < 0 || max > 1 << 20) throw new SaveFormatException("The schematic's block palette is damaged.");
        var palette = new BlockState[max + 1];
        Array.Fill(palette, BlockState.Air);
        foreach (var (key, value) in paletteTag)
            if (value.IsNumeric && value.AsLong >= 0) palette[(int)value.AsLong] = StructureJava.FromBlock(StructureJava.ParseState(key), dv);

        var s = new Structure(w, h, l) { DataVersion = dv, Author = (root.GetCompound("Metadata")?.GetString("Author")) ?? "", Origin = root.GetCompound("Metadata")?.GetString("Name") };
        int pos = 0;
        long volume = (long)w * h * l;
        for (long i = 0; i < volume; i++)
        {
            int index = ReadVarInt(data, ref pos);
            if (index < 0) throw new SaveFormatException($"The schematic's block data ends early (after {i:N0} of {volume:N0} blocks).");
            int x = (int)(i % w), z = (int)(i / w % l), y = (int)(i / ((long)w * l));
            s.Set(x, y, z, index < palette.Length ? palette[index] : BlockState.Air);
        }

        // Block entities: version 1 calls them TileEntities; version 3 keeps their data in "Data".
        var beList = blocks.GetList("BlockEntities") ?? root.GetList("TileEntities") ?? new NbtList();
        foreach (var be in beList.OfType<NbtCompound>())
        {
            if (be["Pos"] is not NbtIntArray { Value.Length: 3 } p) continue;
            int x = p.Value[0], y = p.Value[1], z = p.Value[2];
            if (!s.InRange(x, y, z)) continue;
            var j = version >= 3 ? (NbtCompound)(be.GetCompound("Data") ?? new NbtCompound()).Clone() : Without((NbtCompound)be.Clone(), "Pos", "Id");
            j.SetString("id", be.GetString("Id") ?? be.GetString("id") ?? "");
            j.SetInt("x", x); j.SetInt("y", y); j.SetInt("z", z);
            s.BlockEntities[s.Index(x, y, z)] = StructureJava.FromBlockEntity(j, dv);
        }
        foreach (var e in (root.GetList("Entities") ?? new NbtList()).OfType<NbtCompound>())
        {
            if (e.GetList("Pos") is not { Count: 3 } p) continue;
            var j = version >= 3 ? (NbtCompound)(e.GetCompound("Data") ?? new NbtCompound()).Clone() : Without((NbtCompound)e.Clone(), "Id");
            j.SetString("id", e.GetString("Id") ?? e.GetString("id") ?? "");
            j["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(p[0].AsDouble), new NbtDouble(p[1].AsDouble), new NbtDouble(p[2].AsDouble) };
            s.Entities.Add(new StructureEntity(p[0].AsDouble, p[1].AsDouble, p[2].AsDouble, StructureJava.FromEntity(j, dv)));
        }
        return s;
    }

    static NbtCompound Without(NbtCompound c, params string[] keys)
    {
        foreach (var k in keys) c.Remove(k);
        return c;
    }

    static void WriteVarInt(Stream s, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80) { s.WriteByte((byte)(v | 0x80)); v >>= 7; }
        s.WriteByte((byte)v);
    }

    /// <summary>The next varint, or -1 when the data ends (or is damaged) first.</summary>
    static int ReadVarInt(byte[] data, ref int pos)
    {
        int value = 0, shift = 0;
        while (pos < data.Length)
        {
            byte b = data[pos++];
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
            if (shift > 28) return -1;
        }
        return -1;
    }
}
