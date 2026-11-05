// McTests/BedrockTests.cs: BedrockTests.
// Owns: BedrockTests.
using System.Diagnostics;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.LevelDb;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

static class BedrockTests
{
    /// <summary>bedrock [sampleChunks] : opens every Bedrock world read-only and decodes chunks.</summary>
    public static void All(string[] args)
    {
        int sample = args.Length > 0 ? int.Parse(args[0]) : 400;
        foreach (var root in NbtTests.BedrockRoots())
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                if (!BedrockWorld.LooksLikeBedrockWorld(dir)) { Program.Info($"(not a world) {dir}"); continue; }
                var sw = Stopwatch.StartNew();
                BedrockWorld w;
                try { w = BedrockWorld.Open(dir); }
                catch (Exception ex) { Program.Check(false, $"{dir}: open failed: {ex.Message}"); continue; }
                using (w)
                {
                    Console.WriteLine($"== {Path.GetFileName(dir)} \"{w.Name}\": {w.FormatDescription}; {w.Db.Count:N0} keys; opened in {sw.ElapsedMilliseconds} ms");
                    Program.Info($"compression: {string.Join(", ", w.Db.CompressionCounts.Select(kv => $"{kv.Key}={kv.Value}"))}; tables {w.Db.Manifest.Files.Count}; warnings {w.Db.Warnings.Count}; corrupt log fragments {w.Db.CorruptLogFragments}");
                    foreach (var warn in w.Db.Warnings.Take(3)) Program.Info("  warning: " + warn);
                    var subVersions = new Dictionary<int, int>();
                    var tags = new Dictionary<string, int>();
                    foreach (var key in w.Db.Keys())
                    {
                        if (BedrockKeys.TryParseChunkKey(key, out var ck)) tags[ck.Tag.ToString()] = tags.GetValueOrDefault(ck.Tag.ToString()) + 1;
                        else
                        {
                            string d = BedrockKeys.Describe(key);
                            string kind = d.StartsWith("actorprefix") ? "actorprefix" : d.StartsWith("digp") ? "digp" : d.StartsWith("player_") ? "player_*" : d.StartsWith("map_") ? "map_*" : d.StartsWith("VILLAGE_") ? "VILLAGE_*" : d.Length > 24 ? "other" : d;
                            tags[kind] = tags.GetValueOrDefault(kind) + 1;
                        }
                    }
                    Program.Info("keys: " + string.Join(", ", tags.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")));
                    foreach (var dim in w.Dimensions.Cast<BedrockDimension>())
                    {
                        int ok = 0, bad = 0;
                        var versions = new Dictionary<int, int>();
                        foreach (var pos in dim.ChunkPositions.Take(sample))
                        {
                            try
                            {
                                foreach (var key in dim.KeysAt(pos))
                                    if (BedrockKeys.TryParseChunkKey(key, out var ck) && ck.Tag == ChunkTag.SubChunkPrefix)
                                    {
                                        var v = w.Db.Get(key);
                                        if (v is { Length: > 0 }) subVersions[v[0]] = subVersions.GetValueOrDefault(v[0]) + 1;
                                    }
                                var cv = dim.ChunkVersion(pos) ?? -1;
                                versions[cv] = versions.GetValueOrDefault(cv) + 1;
                                var col = dim.LoadChunk(pos)!;
                                foreach (var s in col.Sections.Values) _ = s.Indices;
                                ok++;
                            }
                            catch (Exception ex) { bad++; if (bad < 4) Program.Info($"   {pos}: {ex.Message}"); }
                        }
                        Program.Check(bad == 0, $"{Path.GetFileName(dir)} {dim.DisplayName}: {ok} chunks decoded");
                        Program.Info($"{dim.DisplayName}: {dim.ChunkPositions.Count:N0} chunks; chunk versions {string.Join(", ", versions.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"))}");
                    }
                    Program.Info("sub-chunk versions: " + string.Join(", ", subVersions.OrderBy(kv => kv.Key).Select(kv => $"v{kv.Key}={kv.Value}")));
                }
            }
    }

    /// <summary>bedrockrender &lt;world dir&gt; [radius]</summary>
    public static void Render(string[] args)
    {
        Directory.CreateDirectory(WorldTests.OutDir);
        using var w = BedrockWorld.Open(args[0]);
        int radius = args.Length > 1 ? int.Parse(args[1]) : 32;
        Console.WriteLine($"== {w.Name}: {w.FormatDescription}");
        foreach (var (k, v) in w.Summary()) Program.Info($"{k}: {v}");
        long sx = w.LevelRoot.GetLong("SpawnX") >> 4, sz = w.LevelRoot.GetLong("SpawnZ") >> 4;
        foreach (var dim in w.Dimensions)
        {
            var sw = Stopwatch.StartNew();
            var opt = new MapRenderOptions();
            if (dim.Id.Contains("nether")) opt.StartY = 100;
            var r = dim.Id.Contains("overworld")
                ? DimensionRenderer.Render(dim, opt, (int)sx - radius, (int)sz - radius, (int)sx + radius, (int)sz + radius)
                : DimensionRenderer.Render(dim, opt, -radius, -radius, radius, radius);
            string file = Path.Combine(WorldTests.OutDir, "bedrock_" + string.Concat(w.Name.Where(char.IsLetterOrDigit)) + "_" + dim.DisplayName.Replace(" ", "") + ".png");
            if (r.Width > 0) File.WriteAllBytes(file, PngWriter.Encode(r.Pixels, r.Width, r.Height));
            Program.Info($"{dim.DisplayName}: rendered {r.Rendered} ({r.Failed} failed) {r.Width}x{r.Height} in {sw.Elapsed.TotalSeconds:0.0}s -> {file}");
            Program.Check(r.Failed == 0, $"{dim.DisplayName}: all chunks rendered");
        }
        var unknown = Enumerable.Range(0, BlockState.Count).Select(BlockState.FromId)
            .Where(s => s.MapColorResolved && !s.IsAir && BlockColors.IsGuessed(s)).Select(s => s.ToString()).Distinct().ToList();
        if (unknown.Count > 0) Program.Info("blocks without a colour: " + string.Join(", ", unknown.Take(80)));
    }

    /// <summary>bedrockwrite &lt;world dir&gt; : copies the world, edits the copy and verifies the database.</summary>
    public static void Write(string[] args)
    {
        string src = args[0];
        string dst = Path.Combine(Path.GetTempPath(), "mss-bedrock-test", Path.GetFileName(src));
        if (Directory.Exists(dst)) Directory.Delete(dst, true);
        CopyDir(src, dst);
        Console.WriteLine($"== write test on copy {dst}");

        Dictionary<string, byte[]> snapshot;
        int deleted;
        using (var w = BedrockWorld.Open(dst))
        {
            snapshot = w.Db.Keys().ToDictionary(k => Convert.ToHexString(k), k => w.Db.Get(k)!);
            // 1. Append-log edit: change a level value stored in the db (player) and add a custom key.
            w.Db.Put("mss_test_key"u8.ToArray(), "hello"u8.ToArray());
            w.Save(CommitMode.AppendLog);
        }
        using (var w = BedrockWorld.Open(dst))
        {
            Program.Check(w.Db.Get("mss_test_key"u8.ToArray()) is byte[] v && v.AsSpan().SequenceEqual("hello"u8), "append-log write is visible after reopening");
            Program.Check(w.Db.Count == snapshot.Count + 1, $"key count after append ({w.Db.Count} vs {snapshot.Count + 1})");
            // 2. Delete chunks far from spawn + compact rewrite
            var dim = w.Dimensions[0];
            var victims = dim.ChunkPositions.OrderByDescending(p => Math.Abs(p.X) + Math.Abs(p.Z)).Take(dim.ChunkPositions.Count / 4).ToList();
            deleted = victims.Count;
            dim.DeleteChunks(victims);
            w.Db.Delete("mss_test_key"u8.ToArray());
            var sw = Stopwatch.StartNew();
            w.Save(CommitMode.Compact);
            Program.Info($"compacted after deleting {deleted} chunks in {sw.ElapsedMilliseconds} ms");
        }
        using (var w = BedrockWorld.Open(dst))
        {
            Program.Check(w.Db.Warnings.Count == 0 && w.Db.CorruptLogFragments == 0, "compacted database has no warnings: " + string.Join("; ", w.Db.Warnings.Take(3)));
            Program.Check(w.Db.Get("mss_test_key"u8.ToArray()) == null, "deleted key is gone");
            int same = 0, missing = 0, differ = 0;
            var now = w.Db.Keys().ToDictionary(k => Convert.ToHexString(k), k => k);
            foreach (var (hex, value) in snapshot)
            {
                if (!now.TryGetValue(hex, out var key)) { missing++; continue; }
                if (w.Db.Get(key)!.AsSpan().SequenceEqual(value)) same++; else differ++;
            }
            Program.Check(differ == 0, $"surviving values are byte-identical ({same} same, {differ} differ, {missing} removed)");
            Program.Check(now.Count == same, $"no unexpected keys ({now.Count} keys)");
            Program.Info($"database files: {string.Join(", ", Directory.EnumerateFiles(Path.Combine(dst, "db")).Select(Path.GetFileName))}");
            foreach (var d in w.Dimensions) foreach (var p in d.ChunkPositions.Take(200)) d.LoadChunk(p);
            Program.Check(true, "chunks load from the compacted database");
        }
    }

    /// <summary>bloom : rebuilds every stored bloom filter from the table keys and compares bytes.</summary>
    public static void Bloom(string[] args)
    {
        int tables = 0, same = 0, noFilter = 0;
        foreach (var root in NbtTests.BedrockRoots())
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                string db = Path.Combine(dir, "db");
                if (!Directory.Exists(db)) continue;
                foreach (var file in Directory.EnumerateFiles(db, "*.ldb"))
                {
                    using var t = new TableReader(file, 0);
                    var meta = t.ReadBlock(t.MetaIndex);
                    BlockHandle? fh = null;
                    foreach (var e in TableReader.BlockEntries(meta))
                    {
                        if (System.Text.Encoding.ASCII.GetString(e.Key) != "filter." + BloomFilter.PolicyName) continue;
                        int p = 0;
                        fh = BlockHandle.Decode(meta.AsSpan(e.ValueOffset, e.ValueLength), ref p);
                    }
                    tables++;
                    if (fh == null) { noFilter++; continue; }
                    var stored = t.ReadBlock(fh.Value);
                    var fb = new FilterBlockBuilder();
                    long end = 0;
                    foreach (var h in t.DataBlocks())
                    {
                        fb.StartBlock((long)h.Offset);
                        foreach (var e in TableReader.BlockEntries(t.ReadBlock(h))) fb.AddKey(e.Key.AsSpan(0, e.Key.Length - 8).ToArray());
                        end = (long)(h.Offset + h.Size) + TableFormat.BlockTrailerSize;
                    }
                    // LevelDB calls StartBlock again after flushing the last data block.
                    fb.StartBlock(end);
                    if (fb.Finish().AsSpan().SequenceEqual(stored)) same++;
                    else Program.Info($"filter differs: {file}");
                }
            }
        Program.Check(same == tables - noFilter && tables > 0, $"rebuilt bloom filters match the game's: {same}/{tables - noFilter} ({noFilter} tables without a filter)");
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
