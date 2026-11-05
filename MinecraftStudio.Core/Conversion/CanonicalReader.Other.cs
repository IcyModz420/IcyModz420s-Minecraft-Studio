// MinecraftStudio.Core/Conversion/CanonicalReader.Other.cs: other for CanonicalReader.
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
    // ---------------------------------------------------------------- Legacy Console

    CanonicalChunk? ReadConsole(ConsoleDimension dim, ChunkPos pos)
    {
        var data = dim.LoadConsoleChunk(pos);
        if (data == null) return null;
        var col = data.Column;
        var c = new CanonicalChunk(pos.X, pos.Z)
        {
            InhabitedTime = Math.Max(0, data.InhabitedTime),
            LastUpdate = Math.Max(0, data.LastUpdate),
        };
        foreach (var (sy, s) in col.Sections) c.Sections[sy] = new BlockSection(sy, s.Palette.Select(ConsoleBlocks.ToCanonical).ToArray(), s.Indices);
        if (col.BiomeIds != null) c.ColumnBiomes = col.BiomeIds.Select(BiomeNames.FromConsoleId).ToArray();
        var level = data.Nbt == null ? null : data.Nbt.GetCompound("Level") ?? data.Nbt;
        var tileEntities = level?.GetList("TileEntities")?.OfType<NbtCompound>().ToList() ?? [];
        foreach (var te in tileEntities) Keep(pos, "block entity", () => c.BlockEntities.Add(BlockEntities.FromJava(te, 1343)));
        foreach (var e in level?.GetList("Entities")?.OfType<NbtCompound>() ?? []) Keep(pos, "entity", () => c.Entities.Add(Entities.FromJava(e, 1343)));
        LegacyFixups.Apply(c, tileEntities);
        BedrockFixups.FixPlantColumns(c);
        c.NeedsShapeUpdate = true;
        return c;
    }

    // ---------------------------------------------------------------- Classic / Indev and anything else

    CanonicalChunk? ReadFinite(ChunkPos pos)
    {
        var c = ReadGeneric(pos);
        if (c == null) return null;
        c.NeedsShapeUpdate = true;
        if (_world is FiniteWorld fw && fw.Indev != null)
        {
            foreach (var te in fw.Indev.Root.GetList("TileEntities")?.OfType<NbtCompound>() ?? [])
            {
                int packed = te.GetInt("Pos");
                int x = packed & 1023, y = (packed >> 10) & 1023, z = (packed >> 20) & 1023;
                if (x >> 4 != pos.X || z >> 4 != pos.Z) continue;
                var copy = (NbtCompound)te.Clone();
                copy.Remove("Pos");
                copy.SetInt("x", x); copy.SetInt("y", y); copy.SetInt("z", z);
                c.BlockEntities.Add(BlockEntities.FromJava(copy, 0));
            }
        }
        return c;
    }

    CanonicalChunk? ReadGeneric(ChunkPos pos)
    {
        var col = _dim.LoadChunk(pos);
        if (col == null) return null;
        var c = new CanonicalChunk(pos.X, pos.Z);
        foreach (var (sy, s) in col.Sections) c.Sections[sy] = new BlockSection(sy, s.Palette, s.Indices);
        // Biomes by name when the source gives them (land from a heightmap), else plains.
        c.ColumnBiomes = col.SurfaceBiomes is { Length: 256 } named && named.All(b => b == null || b.StartsWith("minecraft:", StringComparison.Ordinal))
            ? named.Select(b => b ?? "minecraft:plains").ToArray()
            : Enumerable.Repeat("minecraft:plains", 256).ToArray();
        return c;
    }
}
