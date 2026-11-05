// MinecraftStudio.Core/World/StructureMap.Java.cs: Java Edition of StructureMap.
// Part of StructureMap (main part in StructureMap.cs). Each structure start a chunk keeps — structures.starts (1.18+, the box
// from its pieces' BB) or Level.Structures.Starts (1.13–1.17, with its own BB) — and, for worlds from before 1.13, the
// features of data/Village.dat, Temple.dat (desert and jungle temples, witch huts, igloos), Mineshaft, Fortress, Stronghold,
// Monument, Mansion and EndCity.dat in the dimension's folder. The chunk's block entities tell loot and spawners.
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

public static partial class StructureMap
{
    const string SavedNote = "saved in the world";

    static void Java(WorldDimension dim, ChunkPos pos, List<Saved> saved, List<Trace> finds)
    {
        if (dim.LoadChunkNbt(pos) is not { } raw) return;
        var level = raw["Level"] as NbtCompound;
        var starts = raw.GetCompound("structures")?.GetCompound("starts") ?? level?.GetCompound("Structures")?.GetCompound("Starts");
        if (starts != null)
            foreach (var (_, tag) in starts)
                if (tag is NbtCompound start && Start(start, pos) is { } s) saved.Add(s);
        foreach (var be in (raw.GetList("block_entities") ?? level?.GetList("TileEntities"))?.OfType<NbtCompound>() ?? []) BlockEntity(be, finds);
    }

    /// <summary>A structure start: its kind and its box (its own BB, else its pieces', else the chunk).</summary>
    static Saved? Start(NbtCompound start, ChunkPos pos)
    {
        string id = start.GetString("id") ?? "";
        // Before 1.13 every temple is a "Temple": which one, its first piece says (TeDP, TeJP, TeSH, Iglu).
        if (id == "Temple" && start.GetList("Children")?.OfType<NbtCompound>().FirstOrDefault()?.GetString("id") is { Length: > 0 } firstPiece) id = firstPiece;
        if (StructureKinds.FromStructureId(id) is not { } kind) return null;
        var box = Box(start["BB"]);
        foreach (var piece in start.GetList("Children")?.OfType<NbtCompound>() ?? [])
            if (Box(piece["BB"]) is { } p)
                box = box is { } b ? (Math.Min(b.Item1, p.Item1), Math.Min(b.Item2, p.Item2), Math.Min(b.Item3, p.Item3), Math.Max(b.Item4, p.Item4), Math.Max(b.Item5, p.Item5), Math.Max(b.Item6, p.Item6)) : p;
        int cx = start.GetLongOrNull("ChunkX") is long x ? (int)x : pos.X, cz = start.GetLongOrNull("ChunkZ") is long z ? (int)z : pos.Z;
        return new Saved(kind.Key, kind.Name, box ?? (cx * 16, 64, cz * 16, cx * 16 + 15, 64, cz * 16 + 15), SavedNote);
    }

    /// <summary>Structures a world from before 1.13 kept in its dimension's data folder.</summary>
    static IEnumerable<Saved> OldJavaFeatures(SaveWorld world, WorldDimension dim)
    {
        if (world is not JavaWorld || dim is not JavaRegionDimension j) yield break;
        string data = Path.Combine(j.Folder, "data");
        if (!Directory.Exists(data)) yield break;
        foreach (var name in (string[])["Village", "Temple", "Mineshaft", "Fortress", "Stronghold", "Monument", "Mansion", "EndCity"])
        {
            string file = Path.Combine(data, name + ".dat");
            if (!File.Exists(file)) continue;
            // Gzipped, like every Java .dat; a damaged one is passed over (the rest of the world is still shown).
            NbtCompound? features;
            try { features = (NbtFile.Load(file).Roots.FirstOrDefault()?.Tag as NbtCompound)?.GetCompound("data")?.GetCompound("Features"); }
            catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException or IO.SaveFormatException) { continue; }
            foreach (var (_, tag) in features ?? [])
            {
                if (tag is not NbtCompound f || string.IsNullOrEmpty(f.GetString("id"))) continue;
                if (Start(f, new ChunkPos(f.GetInt("ChunkX"), f.GetInt("ChunkZ"))) is { } s) yield return s;
            }
        }
    }

    /// <summary>
    /// A console save's structures: the same data/*.dat files as Java before 1.13, inside the save, all in one folder —
    /// the Nether's fortresses and the End's cities beside the Overworld's (Village, Temple, Mineshaft, StrongHold, Monument,
    /// Mansion, and from the Aquatic update Ocean Ruin, Shipwreck and Buried Treasure). villages.dat is the villagers' doors,
    /// not a structure. A damaged file is passed over.
    /// </summary>
    static IEnumerable<Saved> ConsoleFeatures(SaveWorld world, WorldDimension dim)
    {
        if (world is not LegacyConsole.ConsoleWorld c) yield break;
        string here = Conversion.CanonicalReader.DimensionId(dim);
        // The files also keep starts past the world's edge (the classic world is 864 blocks across): only those that touch
        // a chunk the save has are shown, as Java saves only generated land's.
        var have = dim.ChunkPositions.ToHashSet();
        bool OnLand(Saved s)
        {
            for (int cz = s.Box.MinZ >> 4; cz <= s.Box.MaxZ >> 4; cz++)
                for (int cx = s.Box.MinX >> 4; cx <= s.Box.MaxX >> 4; cx++)
                    if (have.Contains(new ChunkPos(cx, cz))) return true;
            return false;
        }
        foreach (var file in c.Archive.Files)
        {
            string name = file.Name.Replace('\\', '/');
            if (!name.StartsWith("data/", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)) continue;
            string? of = name[5..^4].ToLowerInvariant() switch
            {
                "fortress" => "minecraft:the_nether",
                "endcity" => "minecraft:the_end",
                "village" or "temple" or "mineshaft" or "stronghold" or "monument" or "mansion" or "ocean ruin" or "shipwreck" or "buried treasure" => "minecraft:overworld",
                _ => null,
            };
            if (of != here) continue;
            NbtCompound? features;
            try { features = (NbtFile.Parse(file.Data, NbtFormat.Java).Roots.FirstOrDefault()?.Tag as NbtCompound)?.GetCompound("data")?.GetCompound("Features"); }
            catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException or IO.SaveFormatException or ArgumentException) { continue; }
            foreach (var (_, tag) in features ?? [])
            {
                Saved? s = tag switch
                {
                    NbtByteArray bytes => ConsoleStart(bytes.Value, name[5..^4].ToLowerInvariant()),
                    NbtCompound f when !string.IsNullOrEmpty(f.GetString("id")) => Start(f, new ChunkPos(f.GetInt("ChunkX"), f.GetInt("ChunkZ"))),
                    _ => null,
                };
                if (s != null && OnLand(s)) yield return s;
            }
        }
    }

    /// <summary>
    /// One structure of a console save's data/*.dat, kept as bytes in 4J's own layout (read from the Xbox 360 TU70 sample's
    /// ten files): the int 4, the id as Java writes text (2-byte length; empty for ocean ruins and end cities, whose file
    /// says), the chunk's x and z, the box (6 ints), how many pieces, then each piece beginning with its own id (ViStart,
    /// TeSH, OcRu …). Ints are big-endian as on the Xbox 360; a save whose marker reads 4 only the other way round is read
    /// that way. A box of int.MaxValue … int.MinValue (no pieces) is a start that never generated, and is left out.
    /// </summary>
    static Saved? ConsoleStart(byte[] data, string file)
    {
        foreach (bool big in (bool[])[true, false])
        {
            int p = 0;
            bool Fits(int n) => p + n <= data.Length;
            int Int() { int v = big ? System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(p)) : System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p)); p += 4; return v; }
            string? Text()
            {
                if (!Fits(2)) return null;
                int n = big ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p)) : System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p));
                p += 2;
                if (n > 64 || !Fits(n)) return null;
                string t = System.Text.Encoding.UTF8.GetString(data, p, n);
                p += n;
                return t;
            }
            if (!Fits(4) || Int() != 4) continue;
            if (Text() is not string id || !Fits(4 * 9)) return null;
            Int(); Int(); // the chunk (the box says where it is)
            int x0 = Int(), y0 = Int(), z0 = Int(), x1 = Int(), y1 = Int(), z1 = Int(), pieces = Int();
            // No pieces: never built (Java treats a start without pieces so; two shipwrecks and two buried treasures of the
            // sample are such, with nothing of them told by loot either).
            if (pieces <= 0 || x0 > x1 || y0 > y1 || z0 > z1) return null;
            string first = Text() ?? "";
            string kindId = id.Length > 0 ? id : file switch { "ocean ruin" => "ocean_ruin", "endcity" => "end_city", "buried treasure" => "buried_treasure", _ => file.Replace(' ', '_') };
            if (kindId == "Temple" && first.Length > 0) kindId = first;
            if (StructureKinds.FromStructureId(kindId) is not { } kind) return null;
            // The sea floor's structures keep their template's height in the box (the sample's ocean ruins: y 0 – 6; their chests at 24 – 44).
            return new Saved(kind.Key, kind.Name, (x0, y0, z0, x1, y1, z1), SavedNote, AnyHeight: kind.Key is "ocean_ruin" or "shipwreck");
        }
        return null;
    }
}
