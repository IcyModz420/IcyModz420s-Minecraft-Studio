// McTests/PruneTests.cs: prune: on a copy of a world of every kind, deletes every chunk farther than 3 chunks
// from the spawn, saves the way the app does, reopens it and checks that exactly those chunks are gone and the
// kept ones are unchanged.
// Owns: PruneTests.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.World;

/// <summary>
/// prune: on a copy of a world of every kind, deletes every chunk farther than 3 chunks from the spawn, saves the way
/// the app does, reopens it and checks that exactly those chunks are gone and the kept ones are unchanged.
/// </summary>
static class PruneTests
{
    static readonly string Downloads = TestPaths.Data;

    static readonly (string Label, string Path)[] Sources =
    [
        ("Java 1.13 (Anvil)", Downloads + @"\amulet\java\vanilla\1_13"),
        ("Java 1.12.2 (Anvil)", Downloads + @"\amulet\java\vanilla\1_12_2"),
        ("Bedrock 1.18 (LevelDB)", Downloads + @"\amulet\bedrock\vanilla\1_18\vanilla"),
        ("Bedrock 1.16 (LevelDB)", Downloads + @"\amulet\bedrock\vanilla\1_16"),
        ("Console TU70", Downloads + @"\lce\mcsaves_x360_fgh_tu70\savegame.dat"),
        ("Pocket 0.x chunks.dat", Downloads + @"\pymclevel\testfiles\PocketWorld"),
        ("Indev", Downloads + @"\pymclevel\testfiles\hell.mclevel"),
        ("Classic", Downloads + @"\pymclevel\testfiles\Dojo_64_64_128.dat"),
    ];

    static void Copy(string from, string to)
    {
        if (File.Exists(from)) { Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(from, to, true); return; }
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(dir.Replace(from, to));
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)) File.Copy(f, f.Replace(from, to), true);
    }

    static string Fingerprint(ChunkColumn? c)
    {
        if (c == null) return "null";
        var h = new HashCode();
        foreach (var (sy, s) in c.Sections.OrderBy(k => k.Key))
            for (int i = 0; i < 4096; i += 7) h.Add(HashCode.Combine(sy, s.Indices == null ? s.Palette[0].Id : s.Palette[s.Indices[i]].Id));
        return h.ToHashCode().ToString("X8");
    }

    public static void Run(string[] args)
    {
        string root = Path.Combine(ConvertTests.OutRoot, "prune");
        foreach (var (label, src) in Sources)
        {
            string copy = Path.Combine(root, string.Concat(label.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')), Path.GetFileName(src));
            if (Directory.Exists(Path.GetDirectoryName(copy))) Directory.Delete(Path.GetDirectoryName(copy)!, true);
            Copy(src, copy);
            var expectedKept = new Dictionary<string, Dictionary<ChunkPos, string>>();
            var expectedGone = new Dictionary<string, HashSet<ChunkPos>>();
            int deletedTotal = 0;
            using (var w = WorldLoader.Open(copy))
            {
                var info = WorldInfoReader.Read(w);
                int cx = info.SpawnX >> 4, cz = info.SpawnZ >> 4;
                foreach (var d in w.Dimensions)
                {
                    var all = d.ChunkPositions.ToList();
                    var gone = all.Where(p => Math.Max(Math.Abs(p.X - cx), Math.Abs(p.Z - cz)) > 3).ToList();
                    // Keep at least something to compare and always delete something.
                    if (gone.Count == all.Count && all.Count > 1) gone = all.Skip(1).ToList();
                    if (gone.Count == 0 && all.Count > 1) gone = [all[^1]];
                    expectedGone[d.Id] = [.. gone];
                    expectedKept[d.Id] = all.Except(gone).Take(40).ToDictionary(p => p, p => Fingerprint(d.LoadChunk(p)));
                    d.DeleteChunks(gone);
                    deletedTotal += gone.Count;
                }
                // Save exactly like WorldDocument.SaveAsync.
                foreach (var d in w.Dimensions)
                {
                    if (w is BedrockWorld bw) { if (d.IsModified) { bw.Save(MinecraftStudio.Core.LevelDb.CommitMode.Compact); break; } }
                    else if (d.IsModified) d.SaveChanges();
                }
                if (w is ConsoleWorld cw && cw.IsModified) cw.Save();
            }
            using (var w = WorldLoader.Open(copy))
            {
                int stillThere = 0, lost = 0, changed = 0;
                foreach (var d in w.Dimensions)
                {
                    var now = d.ChunkPositions.ToHashSet();
                    bool finite = w is MinecraftStudio.Core.Legacy.FiniteWorld;
                    foreach (var p in expectedGone.GetValueOrDefault(d.Id) ?? [])
                    {
                        // Classic/Indev are finite: deleted chunks stay but become air.
                        if (finite) { if (d.LoadChunk(p) is ChunkColumn col && col.Sections.Values.Any(s => s.Palette.Any(b => !b.IsAir))) stillThere++; }
                        else if (now.Contains(p)) stillThere++;
                    }
                    foreach (var (p, fp) in expectedKept.GetValueOrDefault(d.Id) ?? [])
                    {
                        if (!now.Contains(p)) { lost++; continue; }
                        if (Fingerprint(d.LoadChunk(p)) != fp) changed++;
                    }
                }
                Program.Info($"{label}: deleted {deletedTotal:N0} chunks; {stillThere} still present, {lost} kept chunks lost, {changed} kept chunks changed");
                Program.Check(stillThere == 0 && lost == 0 && changed == 0 && deletedTotal > 0, $"{label}: prune removes exactly the chosen chunks");
            }
        }
    }
}
