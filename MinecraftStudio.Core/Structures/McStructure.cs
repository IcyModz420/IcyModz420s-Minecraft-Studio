// Structures/McStructure.cs: the Bedrock .mcstructure file format (little-endian NBT, uncompressed).
// Owns: Save/Load and ToNbt/FromNbt for a Structure as a Bedrock structure block file — format_version, size, a two
// layer block_indices grid (main layer + a water layer), a block_palette of {name,states,version}, and the world
// origin. Blocks are mapped with the same matrix-verified Java->Bedrock translation the converter uses
// (BedrockWorldWriter.ToBedrock + BedrockBlocks.ToPaletteNbt). Block entities and entities are not written here yet —
// the Java .nbt format (StructureNbt) carries those; this is for loading a build straight into a Bedrock structure block.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public static class McStructure
{
    public static void Save(Structure s, string path) => new NbtFile(ToNbt(s), NbtFormat.Bedrock, CompressionKind.None).Save(path);

    /// <summary>Loads a .mcstructure (through the size-capped <see cref="StructureFile.ReadRoot"/>: files come from anywhere).</summary>
    public static Structure Load(string path) => FromNbt(StructureFile.ReadRoot(path));

    /// <summary>
    /// Block index order used by .mcstructure (per the Bedrock-OSS wiki): SZ*SY*X + SZ*Y + Z, i.e. x outermost, then y,
    /// then z (z varies fastest), from the lowest corner to the highest. Both layers use this same order and length.
    /// </summary>
    static int BedrockIndex(Structure s, int x, int y, int z) => (x * s.SizeY + y) * s.SizeZ + z;

    public static NbtCompound ToNbt(Structure s)
    {
        var palette = new NbtList(NbtType.Compound);
        var indexOf = new Dictionary<int, int>();       // canonical state id -> palette index
        var needsWater = new Dictionary<int, bool>();    // canonical state id -> needs the water layer
        int PaletteIndex(BlockState st)
        {
            if (indexOf.TryGetValue(st.Id, out int i)) return i;
            var (bedrock, water) = BedrockWorldWriter.ToBedrock(st);
            i = palette.Count;
            palette.Add(BedrockBlocks.ToPaletteNbt(bedrock));
            indexOf[st.Id] = i;
            needsWater[st.Id] = water;
            return i;
        }

        int count = checked(s.SizeX * s.SizeY * s.SizeZ);
        var layer0 = new int[count];
        var layer1 = new int[count];
        bool anyWater = false;
        for (int x = 0; x < s.SizeX; x++)
            for (int y = 0; y < s.SizeY; y++)
                for (int z = 0; z < s.SizeZ; z++)
                {
                    var st = s.Get(x, y, z);
                    int idx = BedrockIndex(s, x, y, z);
                    layer0[idx] = PaletteIndex(st);
                    layer1[idx] = needsWater[st.Id] ? -2 : -1;   // placeholder; the real water index is filled once known
                    anyWater |= needsWater[st.Id];
                }

        int waterIndex = -1;
        if (anyWater)
        {
            waterIndex = palette.Count;
            palette.Add(BedrockBlocks.ToPaletteNbt(BedrockWorldWriter.ToBedrock(BlockState.Get("minecraft:water")).Block));
            for (int i = 0; i < count; i++) if (layer1[i] == -2) layer1[i] = waterIndex;
        }

        var blockIndices = new NbtList(NbtType.List) { IntList(layer0), IntList(layer1) };
        var def = new NbtCompound
        {
            ["block_palette"] = palette,
            ["block_position_data"] = new NbtCompound(),
        };
        var structure = new NbtCompound
        {
            ["block_indices"] = blockIndices,
            ["entities"] = new NbtList(NbtType.Compound),
            ["palette"] = new NbtCompound { ["default"] = def },
        };
        return new NbtCompound
        {
            ["format_version"] = new NbtInt(1),
            ["size"] = IntList([s.SizeX, s.SizeY, s.SizeZ]),
            ["structure"] = structure,
            ["structure_world_origin"] = IntList([0, 0, 0]),
        };
    }

    public static Structure FromNbt(NbtCompound root)
    {
        var size = root.GetList("size");
        if (size == null || size.Count < 3) throw new SaveFormatException("The .mcstructure has no size.");
        int sx = Num(size[0]), sy = Num(size[1]), sz = Num(size[2]);
        var s = new Structure(sx, sy, sz);

        var structure = root.GetCompound("structure") ?? throw new SaveFormatException("The .mcstructure has no structure.");
        var def = structure.GetCompound("palette")?.GetCompound("default");
        var paletteList = def?.GetList("block_palette");
        // Bedrock palette entry -> Bedrock-canonical state -> Java-canonical state (how a Structure stores blocks).
        var palette = (paletteList ?? new NbtList()).OfType<NbtCompound>()
            .Select(c => CanonicalReader.BedrockToJava(BedrockBlocks.FromPaletteNbt(c))).ToList();

        var layers = structure.GetList("block_indices");
        if (layers is { Count: > 0 } && layers[0] is NbtList layer0)
        {
            for (int x = 0; x < sx; x++)
                for (int y = 0; y < sy; y++)
                    for (int z = 0; z < sz; z++)
                    {
                        int idx = BedrockIndex(s, x, y, z);
                        if (idx >= layer0.Count) continue;
                        int p = Num(layer0[idx]);
                        if (p >= 0 && p < palette.Count) s.Set(x, y, z, palette[p]);   // -1 = structure void -> left as air
                    }
        }
        return s;
    }

    static NbtList IntList(int[] values)
    {
        var l = new NbtList(NbtType.Int, values.Length);
        foreach (var v in values) l.Add(new NbtInt(v));
        return l;
    }

    static int Num(NbtTag t) => t.IsNumeric ? (int)t.AsLong : -1;
}
