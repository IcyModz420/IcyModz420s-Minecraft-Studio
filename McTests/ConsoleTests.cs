// McTests/ConsoleTests.cs: ConsoleTests.
// Owns: ConsoleTests.
using System.Diagnostics;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

static class ConsoleTests
{
    static IEnumerable<string> Files()
    {
        // Console saves of your own to test with: one file or folder a line in TestData/console-saves.txt (not part of the
        // repository); a folder gives its .mcs, Save*.bin and savegame.dat files.
        string list = Path.Combine(DownloadTests.Root, "console-saves.txt");
        if (File.Exists(list))
            foreach (var line in File.ReadAllLines(list).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
            {
                if (File.Exists(line)) yield return line;
                else if (Directory.Exists(line))
                    foreach (var f in Directory.EnumerateFiles(line, "*", SearchOption.AllDirectories))
                    {
                        string name = Path.GetFileName(f);
                        if (name.EndsWith(".mcs", StringComparison.OrdinalIgnoreCase) || name.Equals("savegame.dat", StringComparison.OrdinalIgnoreCase)
                            || name.StartsWith("Save", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                            yield return f;
                    }
            }
        string lce = Path.Combine(DownloadTests.Root, "lce");
        if (Directory.Exists(lce))
            foreach (var f in Directory.EnumerateFiles(lce, "*.dat", SearchOption.AllDirectories))
                if (!f.Contains("decompressed", StringComparison.OrdinalIgnoreCase)) yield return f;
    }

    /// <summary>console : opens every console save, checks LZX/STFS/archive round trips, decodes all chunks.</summary>
    /// <summary>
    /// A console chunk's NBT edited and saved (the NBT editor's Save): on copies of the Xbox 360 TU70 save (binary chunks,
    /// the NBT after the blocks) and the TU0 - 4 save (whole-NBT chunks), a chest's first stack changed; reopened: that chunk's
    /// NBT is the edited one, its blocks and a neighbour chunk exactly as before; refused while the world has other unsaved
    /// changes.
    /// </summary>
    static void ChunkNbtSave()
    {
        Console.WriteLine("== Console chunk NBT edited and saved");
        foreach (var (label, source) in new[]
        {
            ("Xbox 360 TU70", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat")),
            ("Xbox 360 TU0 - 4", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_0033_upgraded", "savegame_upgraded.dat")),
        })
        {
            if (!File.Exists(source)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            string dir = Path.Combine(TestPaths.Output, "console-chunknbt", string.Concat(label.Where(char.IsLetterOrDigit)));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, Path.GetFileName(source));
            File.Copy(source, copy);
            ChunkPos at = default, beside = default;
            MinecraftStudio.Core.Nbt.NbtCompound? edited = null;
            ChunkColumn? blocksBefore = null, besideBefore = null;
            MinecraftStudio.Core.Nbt.NbtCompound? besideNbt = null;
            using (var w = WorldLoader.Open(copy))
            {
                var dim = (ConsoleDimension)w.Dimensions.First(d => MinecraftStudio.Core.Conversion.CanonicalReader.DimensionId(d) == "minecraft:overworld");
                Program.Check(dim.CanSaveChunkNbt, $"{label}: a chunk's NBT can be saved");
                foreach (var pos in dim.ChunkPositions.OrderBy(p => p.X).ThenBy(p => p.Z))
                {
                    var nbt = dim.LoadChunkNbt(pos);
                    var tiles = (nbt?.GetCompound("Level") ?? nbt)?.GetList("TileEntities")?.OfType<MinecraftStudio.Core.Nbt.NbtCompound>();
                    if (tiles?.FirstOrDefault(t => t.GetList("Items") is { Count: > 0 }) is not { } chest) continue;
                    var stack = chest.GetList("Items")!.OfType<MinecraftStudio.Core.Nbt.NbtCompound>().First();
                    stack.SetByte("Count", (sbyte)(stack.GetInt("Count") % 63 + 1));
                    edited = nbt;
                    at = pos;
                    break;
                }
                if (edited == null) { Program.Check(false, $"{label}: a chunk with a filled chest"); continue; }
                beside = dim.ChunkPositions.First(p => p != at);
                blocksBefore = dim.LoadChunk(at);
                besideBefore = dim.LoadChunk(beside);
                besideNbt = dim.LoadChunkNbt(beside);
                // Another unsaved change in the world: refused, nothing written.
                byte[] fileBefore = File.ReadAllBytes(copy);
                dim.DeleteChunks([beside]);
                bool refused = false;
                try { dim.SaveChunkNbt(at, edited); } catch (InvalidOperationException) { refused = true; }
                dim.RestoreChunks([beside]);
                Program.Check(refused && File.ReadAllBytes(copy).AsSpan().SequenceEqual(fileBefore), $"{label}: refused while the world has another unsaved change, the save untouched");
                dim.SaveChunkNbt(at, edited);
            }
            using (var w = WorldLoader.Open(copy))
            {
                var dim = w.Dimensions.First(d => MinecraftStudio.Core.Conversion.CanonicalReader.DimensionId(d) == "minecraft:overworld");
                var now = dim.LoadChunkNbt(at);
                bool same(ChunkColumn? a, ChunkColumn? b) => a != null && b != null && Enumerable.Range(0, 128).All(y => Enumerable.Range(0, 256).All(i => a.GetBlock(i & 15, y, i >> 4) == b.GetBlock(i & 15, y, i >> 4)));
                Program.Check(now != null && now.ContentEquals(edited!), $"{label}: chunk {at}'s NBT reads back as edited (a chest's stack count)");
                Program.Check(same(blocksBefore, dim.LoadChunk(at)), $"{label}: its blocks exactly as before");
                Program.Check(same(besideBefore, dim.LoadChunk(beside)) && dim.LoadChunkNbt(beside) is { } nb && besideNbt != null && nb.ContentEquals(besideNbt), $"{label}: chunk {beside} beside it exactly as before");
            }
        }
    }

    public static void Run(string[] args)
    {
        Directory.CreateDirectory(WorldTests.OutDir);
        ChunkNbtSave();
        // LZX against the published decompressed files
        string lce = Path.Combine(DownloadTests.Root, "lce");
        if (Directory.Exists(lce))
        {
            foreach (var dec in Directory.EnumerateFiles(lce, "*decompressed*.dat", SearchOption.AllDirectories))
            {
                string kind = dec.Contains("upgraded") ? "upgraded" : "original";
                var comp = Directory.EnumerateFiles(lce, $"savegame_{kind}.dat", SearchOption.AllDirectories).FirstOrDefault();
                if (comp == null) continue;
                Program.Check(ConsoleSave.TryDecode(File.ReadAllBytes(comp), out var arc, out _, out _), $"{kind}: decodes");
                var expected = File.ReadAllBytes(dec);
                var mine = arc!.ToBytes();
                var decoded = XMemLzx.Decompress(File.ReadAllBytes(comp), 12, expected.Length, out _);
                Program.Check(decoded.AsSpan().SequenceEqual(expected), $"0033 {kind}: LZX output byte-identical to the published decompressed file");
                Program.Check(mine.AsSpan().SequenceEqual(expected), $"0033 {kind}: archive re-serialises byte-identically");
            }
        }
        // LZX encoder round trip on synthetic data
        var rnd = new Random(7);
        foreach (int size in new[] { 1, 100, 32768, 32769, 100_000, 300_000 })
        {
            var data = new byte[size];
            for (int i = 0; i < size; i++) data[i] = (byte)(i % 13 == 0 ? rnd.Next(256) : (i / 37) & 0xFF);
            var c = XMemLzx.Compress(data);
            var back = XMemLzx.Decompress(c, 0, size, out _);
            Program.Check(back.AsSpan().SequenceEqual(data), $"LZX encoder round trip {size} bytes -> {c.Length}");
        }
        var rand = new byte[200_000];
        rnd.NextBytes(rand);
        Program.Check(XMemLzx.Decompress(XMemLzx.Compress(rand), 0, rand.Length, out _).AsSpan().SequenceEqual(rand), "LZX encoder round trip (random data)");

        foreach (var path in Files())
        {
            if (!File.Exists(path)) continue;
            string label = path.Length > 70 ? "…" + path[^70..] : path;
            var sw = Stopwatch.StartNew();
            ConsoleWorld w;
            try { w = (ConsoleWorld)ConsoleSave.Open(path); }
            catch (Exception ex) { Program.Check(false, $"{label}: open failed: {ex.Message}"); continue; }
            Console.WriteLine($"== {label}: {w.FormatDescription} [{sw.ElapsedMilliseconds} ms]");
            foreach (var (k, v) in w.Summary()) Program.Info($"{k}: {v}");
            if (w.Package != null)
                Program.Check(w.Package.HeaderHashValid && w.Package.TopHashValid, $"{label}: STFS header and top hashes valid");
            var formats = new Dictionary<string, int>();
            foreach (var dim in w.Dimensions.Cast<ConsoleDimension>())
            {
                int ok = 0, bad = 0;
                foreach (var pos in dim.ChunkPositions)
                {
                    try
                    {
                        var c = dim.LoadConsoleChunk(pos)!;
                        string f = c.Format + (c.Version > 0 ? $" v{c.Version}" : "");
                        formats[f] = formats.GetValueOrDefault(f) + 1;
                        foreach (var s in c.Column.Sections.Values) _ = s.Indices;
                        if (c.X != pos.X || c.Z != pos.Z) Program.Info($"   chunk at {pos} says {c.X},{c.Z}");
                        ok++;
                    }
                    catch (Exception ex) { bad++; if (bad <= 3) Program.Info($"   {pos}: {ex.GetType().Name}: {ex.Message}"); }
                }
                Program.Check(bad == 0, $"{label} {dim.DisplayName}: {ok} chunks decoded");
                var opt = new MapRenderOptions();
                if (dim.Id.Contains("nether")) opt.StartY = 100;
                var r = DimensionRenderer.Render(dim, opt);
                string file = Path.Combine(WorldTests.OutDir, "lce_" + string.Concat(Path.GetFileNameWithoutExtension(path).Where(char.IsLetterOrDigit)) + "_" + dim.DisplayName.Replace(" ", "") + ".png");
                if (r.Width > 0) File.WriteAllBytes(file, PngWriter.Encode(r.Pixels, r.Width, r.Height));
            }
            Program.Info("chunk formats: " + string.Join(", ", formats.Select(kv => $"{kv.Key}={kv.Value}")));

            // Re-encoding: the wrapper + archive re-read gives identical files
            var encoded = w.Encode();
            Program.Check(ConsoleSave.TryDecode(encoded, out var arc2, out _, out _) && arc2!.Files.Count == w.Archive.Files.Count
                && arc2.Files.Zip(w.Archive.Files).All(p => p.First.Name == p.Second.Name && p.First.Data.AsSpan().SequenceEqual(p.Second.Data)),
                $"{label}: re-encoded save ({w.Wrapper}) decodes to the same archive ({encoded.Length:N0} bytes vs {new FileInfo(path).Length:N0})");
            w.Dispose();
        }
    }

    /// <summary>rawdat &lt;file&gt; : tries the X360 savegame.dat path step by step.</summary>
    public static void RawDat(string[] args)
    {
        var d = File.ReadAllBytes(args[0]);
        Console.WriteLine($"len {d.Length} head {MinecraftStudio.Core.IO.BinaryUtil.Hex(d, 24)}");
        uint L = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(d);
        uint ds = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(8));
        Console.WriteLine($"L={L} (file-4={d.Length - 4}) dsize={ds}");
        var dec = XMemLzx.Decompress(d, 12, (int)ds, out int end);
        Console.WriteLine($"LZX -> {dec.Length}, end {end}");
        uint to = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(dec);
        int cnt = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(dec.AsSpan(4));
        Console.WriteLine($"table 0x{to:X} count {cnt} ver {dec[8]:X2}{dec[9]:X2} {dec[10]:X2}{dec[11]:X2}; end-based 0x{dec.Length - cnt * 144:X}");
        try { var a = ConsoleArchive.Parse(dec, false); Console.WriteLine($"parsed {a.Files.Count} files; warning {a.Warning}"); }
        catch (Exception ex) { Console.WriteLine("parse failed: " + ex.Message); }
        Console.WriteLine($"Validate: {ConsoleArchive.Validate(dec, false)}; TryDecode: {ConsoleSave.TryDecode(d, out var arc, out var wr, out var pl)} {wr} {pl}");
        try { var w = ConsoleSave.Open(args[0]); Console.WriteLine("Open ok: " + w.FormatDescription); }
        catch (Exception ex) { Console.WriteLine("Open failed: " + ex); }
    }

    /// <summary>stfs &lt;file&gt; : dumps an STFS package and its savegame.dat header.</summary>
    public static void Stfs(string[] args)
    {
        var pkg = new StfsPackage(File.ReadAllBytes(args[0]));
        Console.WriteLine($"{pkg.Magic} '{pkg.DisplayName}' title {pkg.TitleId:X8} alloc {pkg.AllocatedBlocks} unalloc {pkg.UnallocatedBlocks} sep {pkg.BlockSeparation} top level {pkg.TopLevel} header ok {pkg.HeaderHashValid} top ok {pkg.TopHashValid}");
        foreach (var f in pkg.Files())
        {
            var data = pkg.Extract(f, out int bad);
            Console.WriteLine($"  {f.Name,-30} size {f.Size,9} blocks {f.Blocks,5} start {f.StartBlock,5} consecutive {f.Consecutive} bad {bad}  head {MinecraftStudio.Core.IO.BinaryUtil.Hex(data, 24)}");
            if (f.Name == "savegame.dat")
            {
                uint L = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data);
                uint ds = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
                Console.WriteLine($"  L={L} dsize={ds}");
                try
                {
                    var dec = XMemLzx.Decompress(data, 12, (int)ds, out int end);
                    Console.WriteLine($"  LZX ok -> {dec.Length} bytes, end {end}; archive head {MinecraftStudio.Core.IO.BinaryUtil.Hex(dec, 16)}");
                    try { var a = ConsoleArchive.Parse(dec, false); Console.WriteLine($"  archive v{a.Version}: {a.Files.Count} files: {string.Join(", ", a.Files.Take(8).Select(x => x.Name))}"); }
                    catch (Exception ex) { Console.WriteLine("  archive parse failed: " + ex.Message); }
                    uint to = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(dec);
                    int cnt = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(dec.AsSpan(4));
                    Console.WriteLine($"  table at 0x{to:X} ({cnt} entries), data length 0x{dec.Length:X}, table end 0x{to + cnt * 144:X}");
                    for (int i = 0; i < cnt; i++)
                    {
                        int at = (int)to + i * 144;
                        if (at + 144 > dec.Length) { Console.WriteLine($"   #{i} beyond end"); break; }
                        string nm = System.Text.Encoding.BigEndianUnicode.GetString(dec, at, 128).Split('\0')[0];
                        uint sz = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(dec.AsSpan(at + 128));
                        uint of = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(dec.AsSpan(at + 132));
                        Console.WriteLine($"   #{i,2} {nm,-28} size {sz,9} off 0x{of:X}  raw {MinecraftStudio.Core.IO.BinaryUtil.Hex(dec.AsSpan(at, 16), 16)}");
                    }
                }
                catch (Exception ex) { Console.WriteLine("  LZX failed: " + ex.Message); }
            }
        }
    }

    /// <summary>consolewrite : prunes a copy of each console save and checks the result.</summary>
    public static void Write(string[] args)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mss-console-test");
        Directory.CreateDirectory(tmp);
        foreach (var path in Files())
        {
            if (!File.Exists(path)) continue;
            string copy = Path.Combine(tmp, Path.GetFileName(Path.GetDirectoryName(path)!) + "_" + Path.GetFileName(path));
            File.Copy(path, copy, true);
            var w = (ConsoleWorld)ConsoleSave.Open(copy);
            var dim = (ConsoleDimension)w.Dimensions[0];
            int before = dim.ChunkPositions.Count;
            var victims = dim.ChunkPositions.OrderByDescending(p => Math.Abs(p.X) + Math.Abs(p.Z)).Take(before / 3).ToList();
            var keepSample = dim.ChunkPositions.Except(victims).Take(20).ToDictionary(p => p, p => dim.ChunkBytes(p)!);
            dim.DeleteChunks(victims);
            var sw = Stopwatch.StartNew();
            w.Save();
            long ms = sw.ElapsedMilliseconds;
            var w2 = (ConsoleWorld)ConsoleSave.Open(copy);
            var dim2 = (ConsoleDimension)w2.Dimensions[0];
            Program.Check(dim2.ChunkPositions.Count == before - victims.Count, $"{Path.GetFileName(copy)}: {victims.Count} chunks pruned ({before} -> {dim2.ChunkPositions.Count}), saved in {ms} ms");
            Program.Check(keepSample.All(kv => dim2.ChunkBytes(kv.Key)!.AsSpan().SequenceEqual(kv.Value)), $"{Path.GetFileName(copy)}: kept chunks are unchanged");
            if (w2.Package != null) Program.Check(w2.Package.HeaderHashValid && w2.Package.TopHashValid && w2.Warnings.Count == 0, $"{Path.GetFileName(copy)}: rebuilt STFS package hashes are valid ({string.Join("; ", w2.Warnings)})");
        }
    }
}
