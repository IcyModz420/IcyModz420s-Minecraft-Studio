// MinecraftStudio.Core/World/StructureMap.Bedrock.cs: Bedrock Edition of StructureMap.
// Part of StructureMap (main part in StructureMap.cs). Bedrock saves no structure starts. What it does keep: the hardcoded
// spawn areas of each chunk (key tag 57: a count, then per box six little-endian ints and a type — 1 nether fortress, 2 witch
// hut, 3 ocean monument, 5 pillager outpost; one box per piece, joined into one structure later) and a record per village
// the game keeps track of (VILLAGE_[dimension_]uuid_INFO with X0…Z1). Its BlockEntity records tell loot and spawners.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

public static partial class StructureMap
{
    static void Bedrock(BedrockDimension b, ChunkPos pos, List<Saved> saved, List<Trace> finds)
    {
        foreach (var key in b.KeysAt(pos))
        {
            if (!BedrockKeys.TryParseChunkKey(key, out var ck)) continue;
            if (ck.Tag == ChunkTag.HardcodedSpawners && b.ReadValue(key) is { Length: >= 4 } v)
            {
                int n = BitConverter.ToInt32(v, 0);
                for (int i = 0, at = 4; i < n && at + 25 <= v.Length; i++, at += 25)
                {
                    int Int(int k) => BitConverter.ToInt32(v, at + 4 * k);
                    string? kind = v[at + 24] switch { 1 => "fortress", 2 => "swamp_hut", 3 => "monument", 5 => "pillager_outpost", _ => null };
                    if (kind == null) continue;
                    var box = (Math.Min(Int(0), Int(3)), Math.Min(Int(1), Int(4)), Math.Min(Int(2), Int(5)), Math.Max(Int(0), Int(3)), Math.Max(Int(1), Int(4)), Math.Max(Int(2), Int(5)));
                    saved.Add(new Saved(kind, StructureKinds.Name(kind), box, SavedNote, Pieces: true));
                }
            }
            else if (ck.Tag == ChunkTag.BlockEntity)
                foreach (var be in b.ReadRecords(key)) BlockEntity(be, finds);
        }
    }

    /// <summary>Every village the game keeps track of, by dimension number (0 the overworld, 1 the Nether, 2 the End).</summary>
    static List<(int Dimension, Saved Site)> BedrockVillages(BedrockWorld w)
    {
        var list = new List<(int, Saved)>();
        foreach (var key in w.Db.Keys())
        {
            if (key.Length < 13 || key[0] != (byte)'V') continue;
            string s = System.Text.Encoding.ASCII.GetString(key);
            if (!s.StartsWith("VILLAGE_", StringComparison.Ordinal) || !s.EndsWith("_INFO", StringComparison.Ordinal)) continue;
            // VILLAGE_<uuid>_INFO, or VILLAGE_<dimension>_<uuid>_INFO from 1.16 on.
            string middle = s["VILLAGE_".Length..^"_INFO".Length];
            int dim = middle.StartsWith("Nether_", StringComparison.Ordinal) ? 1 : middle.StartsWith("TheEnd_", StringComparison.Ordinal) ? 2 : 0;
            NbtCompound? info;
            try { info = w.Db.Get(key) is { } v ? NbtBinary.ReadAll(v, NbtFormat.Bedrock).FirstOrDefault()?.Tag as NbtCompound : null; }
            catch (Exception e) when (e is not OutOfMemoryException) { continue; }
            if (info?["X0"] is not { IsNumeric: true } || info["Z1"] is not { IsNumeric: true }) continue;
            int x0 = info.GetInt("X0"), y0 = info.GetInt("Y0"), z0 = info.GetInt("Z0"), x1 = info.GetInt("X1"), y1 = info.GetInt("Y1"), z1 = info.GetInt("Z1");
            list.Add((dim, new Saved("village", "Village", (Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1), Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)),
                "a village the game keeps track of")));
        }
        return list;
    }
}
