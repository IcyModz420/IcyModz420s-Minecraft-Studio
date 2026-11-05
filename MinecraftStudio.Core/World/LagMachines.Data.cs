// MinecraftStudio.Core/World/LagMachines.Data.cs: commands and data of LagMachines.
// Part of LagMachines (main part in LagMachines.cs). A repeating command block that is on (always active or powered) and
// summons (also through execute … run summon) makes entities every tick. A container holding megabytes of item data (written
// books full of text, shulker boxes inside it) is a book ban, and a chunk whose block entities and entities come to more
// than players can be sent is a chunk ban: both disconnect or crash whoever comes near. Sizes are worked out from the
// NBT without writing it out.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

public static partial class LagMachines
{
    static void Data(CanonicalChunk c, List<Hit> hits)
    {
        long chunkBytes = 0;
        foreach (var be in c.BlockEntities)
        {
            long bytes = Bytes(be);
            chunkBytes += bytes;
            int x = be.GetInt("x"), y = be.GetInt("y"), z = be.GetInt("z");
            string id = be.GetString("id") ?? "";
            if (id.EndsWith("command_block", StringComparison.Ordinal) && SummonsEveryTick(c, be, x, y, z) is string command)
                hits.Add(new Hit(LagKind.SummoningCommandBlock, x, y, z, Around(x, y, z), 1, $"a repeating command block that is on runs \"{Short(command)}\" every tick"));
            if (bytes >= Limits.ContainerBytes && be["Items"] is NbtList)
                hits.Add(new Hit(LagKind.BookBan, x, y, z, Around(x, y, z), bytes,
                    $"a {WorldSearch.Nice(id)} holding {Size(bytes)} of item data{Books(be)}: players near it can be disconnected"));
        }
        foreach (var e in c.Entities) chunkBytes += Bytes(e);
        if (chunkBytes >= Limits.ChunkBytes)
        {
            int bx = c.X * 16, bz = c.Z * 16;
            hits.Add(new Hit(LagKind.ChunkBan, bx + 8, 64, bz + 8, (bx, 64, bz, bx + 15, 64, bz + 15), chunkBytes,
                $"{Size(chunkBytes)} of block entities and entities in one chunk: players who load it can be disconnected or crash"));
        }
    }

    /// <summary>The command when a repeating command block that is on summons (else null).</summary>
    static string? SummonsEveryTick(CanonicalChunk c, NbtCompound be, int x, int y, int z)
    {
        if (c.GetBlock(x & 15, y, z & 15).BaseName != "repeating_command_block") return null;
        if (be.GetLongOrNull("auto") is not 1 && be.GetLongOrNull("powered") is not 1) return null;
        string command = (be.GetString("Command") ?? "").Trim();
        string bare = command.TrimStart('/').ToLowerInvariant();
        return bare.StartsWith("summon ", StringComparison.Ordinal) || bare.Contains(" run summon ", StringComparison.Ordinal) ? command : null;
    }

    static string Short(string s) => s.Length > 70 ? s[..70] + "…" : s;

    static string Size(long bytes) => bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} MB" : $"{bytes / 1024.0:0} KB";

    /// <summary>How many written books a container holds, shulker boxes and bundles in it too (" (54 written books)").</summary>
    static string Books(NbtCompound container)
    {
        int n = 0;
        void Count(NbtTag? t, int depth)
        {
            if (depth > 8) return;
            switch (t)
            {
                case NbtCompound c:
                    if (c.GetString("id") is "minecraft:written_book" or "minecraft:writable_book") n++;
                    foreach (var (_, v) in c) Count(v, depth + 1);
                    break;
                case NbtList l:
                    foreach (var v in l) Count(v, depth + 1);
                    break;
            }
        }
        Count(container["Items"], 0);
        return n > 0 ? $" ({n:N0} books)" : "";
    }

    /// <summary>The size of a tag as it would be stored, worked out without writing it.</summary>
    static long Bytes(NbtTag t) => t switch
    {
        NbtCompound c => SumCompound(c),
        NbtList l => 5 + l.Sum(Bytes),
        NbtString s => 2 + s.Value.Length,
        NbtByteArray a => 4 + a.Value.Length,
        NbtIntArray a => 4 + 4L * a.Value.Length,
        NbtLongArray a => 4 + 8L * a.Value.Length,
        NbtLong or NbtDouble => 8,
        NbtInt or NbtFloat => 4,
        NbtShort => 2,
        _ => 1,
    };

    static long SumCompound(NbtCompound c)
    {
        long n = 1;
        foreach (var (k, v) in c) n += 3 + k.Length + Bytes(v);
        return n;
    }
}
