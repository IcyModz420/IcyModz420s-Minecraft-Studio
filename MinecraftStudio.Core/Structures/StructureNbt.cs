// Structures/StructureNbt.cs: the vanilla Java structure file format (.nbt, gzip).
// Owns: Save/Load and ToNbt/FromNbt for a Structure in the exact shape a structure block writes and reads — size,
// a block-state palette, a blocks list (palette index + pos + optional block-entity nbt), an entities list and a
// DataVersion. Written for a chosen Java version (blocks, items and text in that version's form, its DataVersion
// stamped); read from the file's own version into canonical form. Blocks are stored in y,z,x order (the order does not
// matter to the format: each block carries its own pos).
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public static class StructureNbt
{
    public static void Save(Structure s, string path, int dataVersion = 0) => new NbtFile(ToNbt(s, dataVersion), NbtFormat.Java, CompressionKind.Gzip).Save(path);

    /// <summary>Loads a .nbt structure (through the size-capped <see cref="StructureFile.ReadRoot"/>: files come from anywhere).</summary>
    public static Structure Load(string path) => FromNbt(StructureFile.ReadRoot(path));

    /// <summary>The structure as Java version <paramref name="dataVersion"/> (0 = the newest) stores it.</summary>
    public static NbtCompound ToNbt(Structure s, int dataVersion = 0)
    {
        int dv = StructureJava.Target(s, dataVersion);
        if (dv < StructureJava.Flattening) throw new ArgumentException("Structure .nbt files written here are for Minecraft 1.13 and later; for older versions save an MCEdit .schematic.");
        var palette = new List<BlockState>();
        var indexOf = new Dictionary<BlockState, int>();
        var java = new Dictionary<BlockState, BlockState>();
        int PaletteIndex(BlockState canonical)
        {
            if (!java.TryGetValue(canonical, out var st)) java[canonical] = st = StructureJava.Block(canonical, dv);
            if (!indexOf.TryGetValue(st, out int i)) { i = palette.Count; palette.Add(st); indexOf[st] = i; }
            return i;
        }

        var blocks = new NbtList(NbtType.Compound);
        for (int y = 0; y < s.SizeY; y++)
            for (int z = 0; z < s.SizeZ; z++)
                for (int x = 0; x < s.SizeX; x++)
                {
                    var canonical = s.Get(x, y, z);
                    var entry = new NbtCompound();
                    entry["state"] = new NbtInt(PaletteIndex(canonical));
                    entry["pos"] = IntList(x, y, z);
                    // The vanilla format keeps the block entity's data under "nbt"; its own x/y/z are set on load.
                    if (s.BlockEntities.TryGetValue(s.Index(x, y, z), out var be) && StructureJava.BlockEntity(be, dv, canonical) is NbtCompound j)
                        entry["nbt"] = j;
                    blocks.Add(entry);
                }

        var paletteList = new NbtList(NbtType.Compound, palette.Count);
        foreach (var st in palette) paletteList.Add(st.ToJavaNbt());

        var entities = new NbtList(NbtType.Compound);
        foreach (var e in s.Entities)
        {
            if (StructureJava.Entity(e.Nbt, dv) is not NbtCompound j) continue;
            entities.Add(new NbtCompound
            {
                ["pos"] = DoubleList(e.X, e.Y, e.Z),
                ["blockPos"] = IntList((int)Math.Floor(e.X), (int)Math.Floor(e.Y), (int)Math.Floor(e.Z)),
                ["nbt"] = j,
            });
        }

        return new NbtCompound
        {
            ["size"] = IntList(s.SizeX, s.SizeY, s.SizeZ),
            ["palette"] = paletteList,
            ["blocks"] = blocks,
            ["entities"] = entities,
            ["DataVersion"] = new NbtInt(dv),
            ["author"] = new NbtString(s.Author),
        };
    }

    public static bool Looks(NbtCompound root) => root.GetList("size") is { Count: 3 } && (root.ContainsKey("palette") || root.ContainsKey("palettes")) && root.ContainsKey("blocks");

    /// <summary>A .nbt structure in canonical form, upgraded from the version that wrote it.</summary>
    public static Structure FromNbt(NbtCompound root)
    {
        var size = root.GetList("size");
        if (size == null || size.Count < 3) throw new SaveFormatException("The structure file has no size.");
        // 0 (or none) is what this program wrote before files carried their version: already canonical.
        int dv = root.GetInt("DataVersion");
        var s = new Structure(Num(size[0]), Num(size[1]), Num(size[2]))
        {
            DataVersion = dv,
            Author = root.GetString("author") ?? "",
        };

        // Some structures (shipwrecks) keep several palettes, one per look: the first is the one used.
        var paletteTag = root.GetList("palette") ?? root.GetList("palettes")?.OfType<NbtList>().FirstOrDefault() ?? new NbtList();
        var palette = paletteTag.Select(p => StructureJava.FromBlock(BlockState.FromJavaNbt(p), dv)).ToList();
        foreach (var t in root.GetList("blocks") ?? new NbtList())
        {
            if (t is not NbtCompound bc) continue;
            var pos = bc.GetList("pos");
            if (pos == null || pos.Count < 3) continue;
            int x = Num(pos[0]), y = Num(pos[1]), z = Num(pos[2]);
            if (!s.InRange(x, y, z)) continue;
            int state = bc.GetInt("state", -1);
            if (state >= 0 && state < palette.Count) s.Set(x, y, z, palette[state]);
            if (bc.GetCompound("nbt") is NbtCompound be)
            {
                var j = (NbtCompound)be.Clone();
                j.SetInt("x", x); j.SetInt("y", y); j.SetInt("z", z);
                s.BlockEntities[s.Index(x, y, z)] = StructureJava.FromBlockEntity(j, dv);
            }
        }

        foreach (var t in root.GetList("entities") ?? new NbtList())
        {
            if (t is not NbtCompound ec || ec.GetCompound("nbt") is not NbtCompound en) continue;
            var pos = ec.GetList("pos");
            double ex = pos is { Count: > 0 } ? Dbl(pos[0]) : 0, ey = pos is { Count: > 1 } ? Dbl(pos[1]) : 0, ez = pos is { Count: > 2 } ? Dbl(pos[2]) : 0;
            s.Entities.Add(new StructureEntity(ex, ey, ez, StructureJava.FromEntity(en, dv)));
        }
        return s;
    }

    static NbtList IntList(params int[] values)
    {
        var l = new NbtList(NbtType.Int, values.Length);
        foreach (var v in values) l.Add(new NbtInt(v));
        return l;
    }

    static NbtList DoubleList(params double[] values)
    {
        var l = new NbtList(NbtType.Double, values.Length);
        foreach (var v in values) l.Add(new NbtDouble(v));
        return l;
    }

    static int Num(NbtTag t) => t.IsNumeric ? (int)t.AsLong : 0;
    static double Dbl(NbtTag t) => t.IsNumeric ? t.AsDouble : 0;
}
