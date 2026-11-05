// McTests/RestoreTests.cs: restore: deleting chunks and taking the deletion back (the pruner's Undo) leaves
// every format exactly as it was: the chunks are listed again and their blocks and block entities are
// unchanged.
// Owns: RestoreTests.
using System.Security.Cryptography;
using System.Text;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.World;

/// <summary>
/// restore: deleting chunks and taking the deletion back (the pruner's Undo) leaves every format exactly as it was:
/// the chunks are listed again and their blocks and block entities are unchanged. A deletion that was saved cannot be
/// taken back. Runs on temporary copies of the test worlds.
/// </summary>
static class RestoreTests
{
    static readonly (string Label, string Path)[] Worlds =
    [
        ("Java 1.13 (regions)", @"amulet\java\vanilla\1_13"),
        ("Java 1.12 (regions)", @"amulet\java\vanilla\1_12_2"),
        ("Bedrock 1.16 (LevelDB)", @"amulet\bedrock\vanilla\1_16"),
        ("Bedrock 1.18 (LevelDB)", @"amulet\bedrock\vanilla\1_18\vanilla"),
        ("Console TU70 (Xbox 360)", @"lce\mcsaves_x360_fgh_tu70\savegame.dat"),
        ("Pocket 0.x (chunks.dat)", @"pymclevel\testfiles\PocketWorld"),
        ("Classic (.dat)", @"pymclevel\testfiles\Dojo_64_64_128.dat"),
        ("Indev (.mclevel)", @"pymclevel\testfiles\hell.mclevel"),
    ];

    public static void Run(string[] args)
    {
        string temp = Path.Combine(TestPaths.Output, "restore");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        foreach (var (label, rel) in Worlds)
        {
            string src = Path.Combine(TestPaths.Data, rel);
            if (!File.Exists(src) && !Directory.Exists(src)) { Console.WriteLine($"   {label}: not in TestData, skipped"); continue; }
            string copy = Copy(src, Path.Combine(temp, Sanitize(label)));
            using var world = WorldLoader.Open(copy);
            var dim = world.Dimensions.Where(d => d.ChunkPositions.Count > 0).OrderByDescending(d => d.ChunkPositions.Count).First();
            if (!dim.CanRestoreChunks) { Program.Check(false, $"{label}: deletions can be taken back"); continue; }
            var victims = dim.ChunkPositions.OrderBy(p => (p.X * 7919 + p.Z * 104729) & 0xFFFF).Take(40).ToList();
            var before = victims.ToDictionary(p => p, p => Fingerprint(world, dim, p));
            bool finite = dim is MinecraftStudio.Core.Legacy.FiniteDimension;

            dim.DeleteChunks(victims);
            // Finite levels clear the area to air; other formats drop the chunks from the list.
            bool gone = finite ? victims.All(p => IsAir(world, dim, p)) : victims.All(p => !dim.ChunkPositions.Contains(p));
            var back = dim.RestoreChunks(victims);
            bool listed = victims.All(dim.ChunkPositions.Contains);
            int same = victims.Count(p => Fingerprint(world, dim, p) == before[p]);
            Console.WriteLine($"   {label}: deleted {victims.Count}, restored {back.Count}, identical {same}; still modified: {dim.IsModified}");
            Program.Check(gone && back.Count == victims.Count && listed && same == victims.Count, $"{label}: undo brings every deleted chunk back unchanged");
            // A second undo has nothing left to take back.
            Program.Check(dim.RestoreChunks(victims).Count == 0, $"{label}: a chunk is only brought back once");
        }

        // After saving, a deletion is final.
        {
            string copy = Copy(Path.Combine(TestPaths.Data, @"amulet\java\vanilla\1_13"), Path.Combine(temp, "saved"));
            using var world = WorldLoader.Open(copy);
            var dim = world.Dimensions.First(d => d.ChunkPositions.Count > 0);
            var victims = dim.ChunkPositions.Take(10).ToList();
            dim.DeleteChunks(victims);
            dim.SaveChanges();
            Program.Check(dim.RestoreChunks(victims).Count == 0 && victims.All(p => !dim.ChunkPositions.Contains(p)), "Java: a saved deletion is not undone");
        }
    }

    static bool IsAir(SaveWorld world, WorldDimension dim, ChunkPos p)
    {
        var c = new CanonicalReader(world, dim).Get(p);
        return c == null || c.Sections.Values.All(s => s.IsEmpty);
    }

    /// <summary>Every block name and block entity of a chunk, hashed (read through a fresh reader, no cache).</summary>
    static string Fingerprint(SaveWorld world, WorldDimension dim, ChunkPos p)
    {
        var c = new CanonicalReader(world, dim).Get(p);
        if (c == null) return "missing";
        var sb = new StringBuilder();
        foreach (var (sy, s) in c.Sections)
        {
            sb.Append(sy).Append(':');
            for (int i = 0; i < 4096; i++) sb.Append((s.Indices == null ? s.Palette[0] : s.Palette[s.Indices[i]]).Id).Append(',');
        }
        foreach (var be in c.BlockEntities.OrderBy(b => b.GetInt("x")).ThenBy(b => b.GetInt("y")).ThenBy(b => b.GetInt("z")))
            sb.Append(MinecraftStudio.Core.Nbt.Snbt.Write(be, MinecraftStudio.Core.Nbt.SnbtStyle.Compact));
        sb.Append('|').Append(c.Entities.Count);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    static string Copy(string src, string dest)
    {
        if (File.Exists(src))
        {
            Directory.CreateDirectory(dest);
            string f = Path.Combine(dest, Path.GetFileName(src));
            File.Copy(src, f);
            return f;
        }
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(dest, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
        return dest;
    }

    static string Sanitize(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
}
