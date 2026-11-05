// MinecraftStudio.Core/World/WorldRepair.cs: Writes a repaired copy of a world opened for rescue, in the
// world's own format: nothing is converted.
// Owns: WorldRepair.
using System.Diagnostics;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LevelDb;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

/// <summary>
/// Writes a repaired copy of a world opened for rescue, in the world's own format: nothing is converted. Region files
/// are rebuilt from every chunk that can be read, byte for byte (with the copies the rescue found outside where the
/// headers point), a Bedrock database is rebuilt from every record that can be read, and every other file that can be
/// read is copied as it is, so modded blocks, data packs and anything else the game or a mod stored stay as they were.
/// A damaged NBT file is replaced by the copy the game keeps of it (name.dat_old) when that one is readable. The copy
/// is then opened the normal way and compared with what was read from the damaged world.
/// </summary>
public static class WorldRepair
{
    /// <summary>Java and Bedrock worlds can be repaired in their own format; other saves are rescued by converting them.</summary>
    public static bool CanRepair(SaveWorld world) => world is JavaWorld or BedrockWorld;

    public static ConversionReport Write(SaveWorld world, string output, string? worldName = null, IProgress<ConversionProgress>? progress = null, CancellationToken cancel = default)
    {
        output = Path.GetFullPath(output);
        string source = Path.GetFullPath(world.Location).TrimEnd('\\');
        if (output.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase) || string.Equals(output.TrimEnd('\\'), source, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The repaired copy cannot go inside the damaged world.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException($"The folder is not empty: {output}");
        if (worldName != null && worldName.Trim().Length == 0) worldName = null;
        var clock = Stopwatch.StartNew();
        var job = new Job(source, output, worldName?.Trim(), progress, cancel);
        Directory.CreateDirectory(output);
        switch (world)
        {
            case JavaWorld j: job.Java(j); break;
            case BedrockWorld b: job.Bedrock(b); break;
            default: throw new NotSupportedException("A repaired copy can be made of Java and Bedrock worlds; other saves are rescued by converting them.");
        }
        job.Report.Elapsed = clock.Elapsed;
        return job.Report;
    }

    sealed class Job(string source, string output, string? worldName, IProgress<ConversionProgress>? progress, CancellationToken cancel)
    {
        public ConversionReport Report { get; } = new() { OpenPath = output };
        readonly List<(string From, string To)> _copied = [];
        readonly List<string> _badRegions = [];
        // Java: per dimension (its region folder in the copy), the kind of region file it reads (.mca, or .mcr before
        // 1.2) and the chunk positions written to those files, to compare with the reopened copy.
        readonly Dictionary<string, (string Extension, HashSet<ChunkPos> Positions)> _expected = new(StringComparer.OrdinalIgnoreCase);
        int _unlisted, _older, _entityChunks, _poiChunks, _otherChunks, _alphaChunks;
        // Java before Beta 1.3: one file per chunk.
        HashSet<string> _alphaFiles = new(StringComparer.OrdinalIgnoreCase);

        void Stage(string stage, int done = 0, int total = 0) => progress?.Report(new ConversionProgress { Stage = stage, Done = done, Total = total });

        // ------------------------------------------------------------------ Java

        public void Java(JavaWorld j)
        {
            var known = new Dictionary<string, RegionRecovery>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in j.Dimensions.OfType<JavaRegionDimension>())
            {
                // Chunks are compared per dimension; region files anywhere else (a backup inside the world) are kept too.
                _expected[Path.GetFullPath(Path.Combine(output, Path.GetRelativePath(source, Path.GetFullPath(d.RegionFolder))))] = (d.Extension, []);
                foreach (var (file, rec) in d.Recoveries) known[Path.GetFullPath(file)] = rec;
            }
            _alphaFiles = j.Dimensions.OfType<AlphaDimension>().SelectMany(d => d.ChunkFiles).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList();
            for (int n = 0; n < files.Count; n++)
            {
                cancel.ThrowIfCancellationRequested();
                Stage("Writing the repaired copy", n, files.Count);
                string from = files[n], rel = Path.GetRelativePath(source, from), name = Path.GetFileName(from);
                string ext = Path.GetExtension(from).ToLowerInvariant(), to = Path.Combine(output, rel);
                // The game makes session.lock; a big chunk's .mcc file is written again with its region.
                if (name.Equals("session.lock", StringComparison.OrdinalIgnoreCase) || ext == ".mcc") continue;
                if (rel.Equals("level.dat", StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (ext is ".mca" or ".mcr" && RegionFile.TryParseName(name, out int rx, out int rz)) Region(from, to, rel, rx, rz, known);
                else if (ext == ".dat") Dat(from, to, rel);
                else Copy(from, to, rel);
            }
            JavaLevelDat(j);
            Report.Chunks += _alphaChunks;
            if (_unlisted > 0) Report.Notes.Add($"{WorldRescue.Count(_unlisted, "chunk the region headers had lost was", "chunks the region headers had lost were")} found in the files and kept.");
            if (_older > 0) Report.Notes.Add($"{WorldRescue.Count(_older, "damaged chunk was replaced by an older copy", "damaged chunks were replaced by older copies")} still in the files; the latest changes there may be missing.");
            Report.Notes.Add($"Wrote {Report.Chunks:N0} chunks byte for byte" + (_entityChunks > 0 ? $", the mobs of {_entityChunks:N0} chunks" : "") +
                             (_poiChunks > 0 ? $", the points of interest of {_poiChunks:N0} chunks" : "") +
                             (_otherChunks > 0 ? $", {_otherChunks:N0} chunks in region files the game does not read (old McRegion files, copies kept in the world)" : "") +
                             $" and {_copied.Count:N0} other files. Nothing was converted.");
            VerifyJava(j);
        }

        void Region(string from, string to, string rel, int rx, int rz, Dictionary<string, RegionRecovery> known)
        {
            RegionFile region;
            int unreadable;
            try { region = RegionFile.OpenTolerant(from, out unreadable); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Lose(LossKind.Block, rel, $"could not be read ({e.Message}); its chunks are missing from the copy");
                return;
            }
            if (unreadable > 0) Report.Warnings.Add($"{rel}: {unreadable * 4:N0} KB could not be read from the disk; chunks there count as damaged.");
            string folder = Path.GetFileName(Path.GetDirectoryName(from)) ?? "";
            var content = folder.ToLowerInvariant() switch { "entities" => RegionContent.Entities, "poi" => RegionContent.PointsOfInterest, _ => RegionContent.Terrain };
            var rec = known.TryGetValue(from, out var k) && k.Stamp == RegionRecovery.StampOf(from) ? k : region.Recover(content, cancel);
            // A region with only its header is normal (all its chunks were deleted); data past it that yields nothing is not.
            if (rec.Chunks.Count == 0 && rec.Lost == 0 && region.FileLength > 2 * RegionFile.SectorSize)
                Report.Warnings.Add($"{rel}: no readable chunk was found in it (it may have been emptied or overwritten).");
            if (rec.Lost > 0)
            {
                if (content == RegionContent.Entities) Lose(LossKind.Entity, $"Mobs and items of damaged chunks in {rel}", "no readable copy; those chunks have no mobs", rec.Lost);
                else if (content == RegionContent.PointsOfInterest) Lose(LossKind.World, $"Points of interest of damaged chunks in {rel}", "no readable copy; the game finds beds, workstations and portals again from the blocks", rec.Lost);
                else Lose(LossKind.Block, $"Damaged chunks in {rel}", "no readable copy; left out (the game generates them again)", rec.Lost);
            }
            if (content == RegionContent.Terrain)
            {
                _unlisted += rec.Unlisted;
                _older += rec.Older;
                // Only the files a dimension reads count as its chunks: a world from before 1.2 keeps its old
                // McRegion files next to the Anvil ones, and a world may hold copies of region files elsewhere.
                if (_expected.TryGetValue(Path.GetFullPath(Path.GetDirectoryName(to)!), out var dim) && string.Equals(Path.GetExtension(from), dim.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    Report.Chunks += rec.Chunks.Count;
                    Report.FailedChunks += rec.Lost;
                    foreach (int i in rec.Chunks.Keys) dim.Positions.Add(new ChunkPos(rx * 32 + (i & 31), rz * 32 + (i >> 5)));
                }
                else _otherChunks += rec.Chunks.Count;
            }
            else if (content == RegionContent.Entities) _entityChunks += rec.Chunks.Count;
            else _poiChunks += rec.Chunks.Count;
            if (rec.Chunks.Count == 0) return;

            var repaired = RegionFile.CreateEmpty(to, rx, rz);
            var written = new Dictionary<int, RawChunk>();
            foreach (var (i, copy) in rec.Chunks)
            {
                var raw = region.ReadCopy(i, copy);
                repaired.SetRaw(i, raw);
                written[i] = raw;
            }
            repaired.Save(to);
            // The written file must read back with exactly these chunks, byte for byte.
            var check = RegionFile.Open(to);
            bool same = check.ChunkIndexes().Count() == written.Count && written.All(w =>
            {
                try { return check.GetRaw(w.Key) is RawChunk r && r.CompressionType == w.Value.CompressionType && r.Payload.AsSpan().SequenceEqual(w.Value.Payload); }
                catch (Exception e) when (e is IOException or SaveFormatException) { return false; }
            });
            if (!same) _badRegions.Add(rel);
        }

        /// <summary>
        /// A .dat file: NBT (players, maps, raids, scoreboard, chunks before Beta 1.3) is copied when it reads, else
        /// from the copy the game keeps of it (.dat_old). A file that is not NBT at all (some mods and servers keep
        /// other data in .dat files) is copied as it is, unless it is empty or all zeros: what a crash while writing
        /// leaves behind.
        /// </summary>
        void Dat(string from, string to, string rel)
        {
            bool chunk = _alphaFiles.Contains(from);
            if (ReadableNbt(from, java: true))
            {
                if (Copy(from, to, rel) && chunk) _alphaChunks++;
                return;
            }
            if (!chunk && !StartsWithGzip(from) && !IsBlank(from)) { Copy(from, to, rel); return; }
            string old = from + "_old";
            if (File.Exists(old) && ReadableNbt(old, java: true))
            {
                if (Copy(old, to, rel) && chunk) _alphaChunks++;
                Report.Notes.Add($"{rel} was damaged: the copy the game keeps of it ({Path.GetFileName(old)}) was used.");
                return;
            }
            if (chunk)
            {
                Report.FailedChunks++;
                Lose(LossKind.Block, $"Damaged chunk {rel}", "left out (the game generates it again)");
                return;
            }
            var kind = rel.StartsWith("playerdata", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("players", StringComparison.OrdinalIgnoreCase) ? LossKind.Player : LossKind.World;
            Lose(kind, rel, kind == LossKind.Player ? "damaged and left out: that player starts again with an empty inventory" : "damaged and left out (the game starts it again)");
        }

        void JavaLevelDat(JavaWorld j)
        {
            string to = Path.Combine(output, "level.dat");
            foreach (var name in new[] { "level.dat", "level.dat_old" })
            {
                string path = Path.Combine(source, name);
                if (!File.Exists(path) || !ReadableNbt(path, java: true)) continue;
                if (worldName != null && worldName != j.Name)
                {
                    var file = NbtFile.Load(path);
                    file.RootCompound.GetCompound("Data")!.SetString("LevelName", worldName);
                    file.Save(to);
                }
                else File.Copy(path, to);
                if (name != "level.dat") Report.Notes.Add("level.dat was damaged: the copy the game keeps of it (level.dat_old) was used.");
                return;
            }
            // Nothing readable: a new level.dat for the version the chunks were written by. The host player is not
            // written into it, so the game loads them from playerdata, which is copied as it is.
            var info = WorldInfoReader.Read(j);
            info.Name = worldName ?? j.Name;
            var target = TargetCatalog.ClosestTo(j);
            var level = new NbtFile { Format = NbtFormat.Java, Compression = CompressionKind.Gzip };
            level.Roots.Add(new NamedTag("", new NbtCompound { { "Data", JavaLevelData.Build(info, target) } }));
            level.Save(to);
            Report.Notes.Add($"No readable level.dat: a new one was written for {target.Name}. The seed, spawn and game rules start from defaults" +
                             (info.Seed != 0 ? " (the seed was found in the world's other files)" : "") + "; the terrain already there is kept, but new terrain may not line up with it.");
        }

        void VerifyJava(JavaWorld j)
        {
            Stage("Checking the repaired copy", 0, 1);
            SaveWorld? reopened = null;
            try
            {
                try { reopened = WorldLoader.Open(output); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Report.Checks.Add(new VerifyCheck("Opens", false, e.Message));
                    return;
                }
                bool java = reopened is JavaWorld rj && rj.Data != null;
                Report.Checks.Add(new VerifyCheck("Opens", java, java ? "opens as a Java world with its level.dat" : $"opens as {reopened.FormatDescription}"));
                if (reopened is JavaWorld copy)
                {
                    var problems = new List<string>();
                    foreach (var (folder, (_, expected)) in _expected)
                    {
                        var dim = copy.Dimensions.OfType<JavaRegionDimension>().FirstOrDefault(d => string.Equals(Path.GetFullPath(d.RegionFolder), folder, StringComparison.OrdinalIgnoreCase));
                        if (dim == null)
                        {
                            if (expected.Count > 0) problems.Add($"{Path.GetRelativePath(output, folder)}: not found as a dimension");
                            continue;
                        }
                        var have = dim.ChunkPositions.ToHashSet();
                        if (!have.SetEquals(expected)) problems.Add($"{dim.DisplayName}: {have.Count:N0} chunks, {expected.Count:N0} written");
                    }
                    // Worlds from before Beta 1.3 keep one file per chunk.
                    int alpha = copy.Dimensions.OfType<AlphaDimension>().Sum(d => d.ChunkPositions.Count);
                    if (alpha != _alphaChunks) problems.Add($"{alpha:N0} chunk files, {_alphaChunks:N0} written");
                    Report.Checks.Add(new VerifyCheck("Chunks", problems.Count == 0, problems.Count == 0 ? $"every dimension lists the {Report.Chunks:N0} chunks written" : string.Join("; ", problems)));
                }
            }
            finally { reopened?.Dispose(); }
            Report.Checks.Add(new VerifyCheck("Region files", _badRegions.Count == 0, _badRegions.Count == 0 ? "every chunk reads back byte for byte" : "differ after writing: " + string.Join(", ", _badRegions.Take(5))));
            VerifyCopies();
        }

        // ------------------------------------------------------------------ Bedrock

        public void Bedrock(BedrockWorld b)
        {
            string dbFolder = Path.Combine(source, "db") + "\\";
            var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                .Where(f => !Path.GetFullPath(f).StartsWith(dbFolder, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var from in files)
            {
                cancel.ThrowIfCancellationRequested();
                string rel = Path.GetRelativePath(source, from), to = Path.Combine(output, rel);
                if (rel.Equals("level.dat", StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                Copy(from, to, rel);
            }

            var keys = b.Db.Keys();
            var written = new bool[keys.Count];
            int count = 0, unreadable = 0;
            using (var builder = new LevelDbBuilder(Path.Combine(output, "db")))
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    if ((i & 4095) == 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        Stage("Writing the repaired database", i, keys.Count);
                    }
                    byte[]? value;
                    try { value = b.Db.Get(keys[i]); }
                    catch (Exception e) when (e is IOException or InvalidDataException or SaveFormatException) { unreadable++; continue; }
                    if (value == null) continue;
                    builder.Put(keys[i], value);
                    written[i] = true;
                    count++;
                }
                Stage("Writing the repaired database", 1, 1);
                builder.Finish();
            }
            if (unreadable > 0) Lose(LossKind.World, "Database records", "could not be read and were left out", unreadable);
            BedrockLevelDat(b);
            Report.Chunks = b.Dimensions.Sum(d => d.ChunkPositions.Count);
            Report.Notes.Add($"Wrote {count:N0} database records ({Report.Chunks:N0} chunks) byte for byte and {_copied.Count:N0} other files. Nothing was converted.");
            VerifyBedrock(b, keys, written, count, unreadable > 0);
        }

        void BedrockLevelDat(BedrockWorld b)
        {
            string to = Path.Combine(output, "level.dat");
            foreach (var name in new[] { "level.dat", "level.dat_old" })
            {
                string path = Path.Combine(source, name);
                if (!File.Exists(path) || !ReadableNbt(path, java: false)) continue;
                if (worldName != null && worldName != b.Name)
                {
                    var file = NbtFile.Load(path);
                    file.RootCompound.SetString("LevelName", worldName);
                    file.Save(to);
                    WriteLevelName(worldName);
                }
                else File.Copy(path, to);
                if (name != "level.dat") Report.Notes.Add("level.dat was damaged: the copy the game keeps of it (level.dat_old) was used.");
                return;
            }
            var info = WorldInfoReader.Read(b);
            info.Name = worldName ?? b.Name;
            var target = TargetCatalog.ClosestTo(b);
            var level = new NbtFile { Format = NbtFormat.Bedrock, HeaderVersion = target.BedrockStorageVersion };
            level.Roots.Add(new NamedTag("", BedrockLevelData.Build(info, target)));
            level.Save(to);
            WriteLevelName(info.Name);
            Report.Notes.Add($"No readable level.dat: a new one was written for {target.Name}. The seed, spawn and game rules start from defaults; the terrain already there is kept, but new terrain may not line up with it.");
        }

        /// <summary>levelname.txt with the world's (new) name; it is no longer a copy of the damaged world's.</summary>
        void WriteLevelName(string name)
        {
            string path = Path.Combine(output, "levelname.txt");
            File.WriteAllText(path, name);
            _copied.RemoveAll(c => string.Equals(c.To, path, StringComparison.OrdinalIgnoreCase));
        }

        void VerifyBedrock(BedrockWorld b, List<byte[]> keys, bool[] written, int count, bool recordsLeftOut)
        {
            Stage("Checking the repaired copy", 0, 1);
            try
            {
                using var db = LevelDatabase.Open(Path.Combine(output, "db"));
                int missing = 0, different = 0;
                for (int i = 0; i < keys.Count; i++)
                {
                    if (!written[i]) continue;
                    if ((i & 4095) == 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        Stage("Checking the repaired copy", i, keys.Count);
                    }
                    var copy = db.Get(keys[i]);
                    if (copy == null) { missing++; continue; }
                    if (!copy.AsSpan().SequenceEqual(b.Db.Get(keys[i]))) different++;
                }
                bool ok = missing == 0 && different == 0 && db.Count == count && db.Warnings.Count == 0;
                Report.Checks.Add(new VerifyCheck("Database", ok, ok ? $"all {count:N0} records read back byte for byte"
                    : $"{missing:N0} missing, {different:N0} different, {db.Count:N0} records for {count:N0} written" + (db.Warnings.Count > 0 ? "; " + db.Warnings[0] : "")));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Report.Checks.Add(new VerifyCheck("Database", false, e.Message));
            }
            SaveWorld? reopened = null;
            try
            {
                try { reopened = WorldLoader.Open(output); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Report.Checks.Add(new VerifyCheck("Opens", false, e.Message));
                    return;
                }
                Report.Checks.Add(new VerifyCheck("Opens", reopened is BedrockWorld, reopened is BedrockWorld ? "opens as a Bedrock world with its level.dat" : $"opens as {reopened.FormatDescription}"));
                var problems = new List<string>();
                foreach (var d in b.Dimensions)
                {
                    var other = reopened.Dimensions.FirstOrDefault(x => x.Id == d.Id);
                    var have = other?.ChunkPositions.ToHashSet() ?? [];
                    // Records that could not be read can take chunks with them (those are listed as lost).
                    bool ok = have.SetEquals(d.ChunkPositions) || recordsLeftOut && have.IsSubsetOf(d.ChunkPositions);
                    if (!ok) problems.Add($"{d.DisplayName}: {have.Count:N0} chunks, {d.ChunkPositions.Count:N0} read");
                }
                Report.Checks.Add(new VerifyCheck("Chunks", problems.Count == 0, problems.Count == 0 ? $"every dimension lists the {Report.Chunks:N0} chunks read" : string.Join("; ", problems)));
            }
            finally { reopened?.Dispose(); }
            VerifyCopies();
        }

        // ------------------------------------------------------------------ files

        bool Copy(string from, string to, string rel)
        {
            try
            {
                File.Copy(from, to);
                _copied.Add((from, to));
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Lose(LossKind.World, rel, $"could not be read ({e.Message}) and was left out");
                try { if (File.Exists(to)) File.Delete(to); } catch (IOException) { }
                return false;
            }
        }

        void VerifyCopies()
        {
            var differ = new List<string>();
            for (int i = 0; i < _copied.Count; i++)
            {
                if ((i & 63) == 0) cancel.ThrowIfCancellationRequested();
                var (from, to) = _copied[i];
                if (!FilesEqual(from, to)) differ.Add(Path.GetRelativePath(output, to));
            }
            Report.Checks.Add(new VerifyCheck("Other files", differ.Count == 0, differ.Count == 0 ? $"all {_copied.Count:N0} copied exactly" : "differ after copying: " + string.Join(", ", differ.Take(5))));
        }

        void Lose(LossKind kind, string what, string outcome, long count = 1) =>
            Report.Losses.Add(new LossEntry { Kind = kind, What = what, Outcome = outcome, Lost = true, Count = count });
    }

    /// <summary>Empty or nothing but zeros: what is left of a file the game was writing when the computer went off.</summary>
    static bool IsBlank(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            var buffer = new byte[1 << 16];
            int n;
            while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
                if (buffer.AsSpan(0, n).ContainsAnyExcept((byte)0)) return false;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    static bool StartsWithGzip(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return fs.ReadByte() == 0x1F && fs.ReadByte() == 0x8B;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Whether an NBT file reads completely (a Java level.dat must also have its Data section).</summary>
    static bool ReadableNbt(string path, bool java)
    {
        try
        {
            var file = NbtFile.Load(path);
            if (file.Roots.Count == 0) return false;
            return !Path.GetFileName(path).StartsWith("level.dat", StringComparison.OrdinalIgnoreCase) || (java ? file.RootCompound.GetCompound("Data") != null : file.RootCompound != null);
        }
        catch (Exception e) when (e is not OperationCanceledException) { return false; }
    }

    static bool FilesEqual(string a, string b)
    {
        try
        {
            using var x = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            using var y = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            if (x.Length != y.Length) return false;
            var bx = new byte[1 << 16];
            var by = new byte[1 << 16];
            int n;
            while ((n = x.Read(bx, 0, bx.Length)) > 0)
            {
                y.ReadExactly(by, 0, n);
                if (!bx.AsSpan(0, n).SequenceEqual(by.AsSpan(0, n))) return false;
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
