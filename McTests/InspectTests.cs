// McTests/InspectTests.cs: InspectTests.
// Owns: InspectTests.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class InspectTests
{
    /// <summary>inspectconsole &lt;save&gt; : archive file list, level.dat, first player, first chunk header.</summary>
    public static void Console_(string[] args)
    {
        using var w = (MinecraftStudio.Core.LegacyConsole.ConsoleWorld)MinecraftStudio.Core.LegacyConsole.ConsoleSave.Open(args[0]);
        // inspectconsole <save> entities <x> <z>: the raw entities of the Overworld chunk holding that block.
        if (args.Length > 3 && args[1] == "entities")
        {
            int bx = int.Parse(args[2]), bz = int.Parse(args[3]);
            var dim = w.Dimensions.OfType<MinecraftStudio.Core.LegacyConsole.ConsoleDimension>().First();
            var chunk = dim.LoadConsoleChunk(new MinecraftStudio.Core.World.ChunkPos(bx >> 4, bz >> 4));
            var list = chunk?.Nbt?.GetList("Entities") ?? (chunk?.Nbt?["Level"] as NbtCompound)?.GetList("Entities");
            Console.WriteLine($"chunk {bx >> 4}, {bz >> 4}: {list?.Count ?? 0} entities");
            foreach (var e in list?.OfType<NbtCompound>() ?? [])
                Console.WriteLine("  " + Snbt.Write(e, SnbtStyle.Compact) is var t && t.Length > 1500 ? t[..1500] + " …" : t);
            return;
        }
        // inspectconsole <save> features: each data/*.dat's structures (byte arrays): key, size and the first bytes.
        if (args.Length > 1 && args[1] == "features")
        {
            foreach (var f in w.Archive.Files.Where(f => f.Name.StartsWith("data/", StringComparison.Ordinal)))
            {
                NbtCompound? features = null;
                try { features = (NbtFile.Parse(f.Data, NbtFormat.Java).Roots[0].Tag as NbtCompound)?.GetCompound("data")?.GetCompound("Features"); } catch { }
                if (features == null) { Console.WriteLine($"{f.Name}: no Features"); continue; }
                Console.WriteLine($"{f.Name}: {features.Count} features");
                foreach (var (key, tag) in features.Take(3))
                    Console.WriteLine($"   {key} {tag.Type} {(tag is NbtByteArray b ? b.Value.Length + " bytes: " + MinecraftStudio.Core.IO.BinaryUtil.Hex(b.Value.AsSpan(0, Math.Min(64, b.Value.Length))) : Snbt.Write(tag, SnbtStyle.Compact)[..Math.Min(200, Snbt.Write(tag, SnbtStyle.Compact).Length)])}");
            }
            return;
        }
        // inspectconsole <save> <file in the save>: that file's first bytes and, when it is NBT, its start as SNBT.
        if (args.Length > 1 && w.Archive.Find(args[1]) is { } one)
        {
            Console.WriteLine($"{one.Name}: {one.Data.Length:N0} bytes, head {MinecraftStudio.Core.IO.BinaryUtil.Hex(one.Data.AsSpan(0, Math.Min(48, one.Data.Length)))}");
            try { Console.WriteLine(Snbt.Write(NbtFile.Parse(one.Data, NbtFormat.Java).Roots[0].Tag, SnbtStyle.Pretty) is var t && t.Length > 4000 ? t[..4000] + " …" : t); }
            catch (Exception e) { Console.WriteLine("not NBT: " + e.Message); }
            return;
        }
        Console.WriteLine($"== {w.FormatDescription}; archive v{w.Archive.Version} min {w.Archive.MinVersion}, LE={w.Archive.LittleEndian}");
        foreach (var f in w.Archive.Files) Console.WriteLine($"   {f.Name,-30} {f.Data.Length,9:N0}  {MinecraftStudio.Core.LegacyConsole.ConsoleArchive.FromFileTimeMs(f.Modified)}");
        if (w.Level != null) Console.WriteLine(Snbt.Write(w.Level.Root, SnbtStyle.Pretty));
        if (w.Players.Count > 0) Console.WriteLine("PLAYER " + w.Players[0].Name + ": " + Snbt.Write(w.Players[0].Data, SnbtStyle.Compact)[..Math.Min(2500, Snbt.Write(w.Players[0].Data, SnbtStyle.Compact).Length)]);
        foreach (var dim in w.Dimensions.OfType<MinecraftStudio.Core.LegacyConsole.ConsoleDimension>().Take(1))
        {
            var pos = dim.ChunkPositions.First();
            var raw = dim.ChunkBytes(pos)!;
            Console.WriteLine($"chunk {pos}: {raw.Length} bytes, head {MinecraftStudio.Core.IO.BinaryUtil.Hex(raw.AsSpan(0, 40))}");
            var data = dim.LoadConsoleChunk(pos)!;
            if (data.Nbt != null) Console.WriteLine("chunk NBT: " + Snbt.Write(data.Nbt, SnbtStyle.Compact)[..Math.Min(800, Snbt.Write(data.Nbt, SnbtStyle.Compact).Length)]);
        }
    }

    /// <summary>inspectbedrock &lt;world&gt; : level.dat and per-tag/version statistics of chunk records.</summary>
    public static void Bedrock(string[] args)
    {
        using var w = BedrockWorld.Open(args[0]);
        Console.WriteLine($"== {w.Name}: {w.FormatDescription}, header version {w.Level.HeaderVersion}");
        Console.WriteLine(Snbt.Write(w.LevelRoot, SnbtStyle.Pretty));
        var versions = new Dictionary<string, int>();
        var tags = new Dictionary<string, int>();
        var subVersions = new Dictionary<int, int>();
        var finalized = new Dictionary<int, int>();
        int n = 0;
        foreach (var key in w.Db.Keys())
        {
            if (!BedrockKeys.TryParseChunkKey(key, out var ck)) { if (n++ < 0) { } continue; }
            tags[ck.Tag.ToString()] = tags.GetValueOrDefault(ck.Tag.ToString()) + 1;
            var v = w.Db.Get(key);
            if (v == null) continue;
            if (ck.IsVersion && v.Length > 0) versions[$"{ck.Tag}={v[0]} dim{ck.Dimension}"] = versions.GetValueOrDefault($"{ck.Tag}={v[0]} dim{ck.Dimension}") + 1;
            if (ck.Tag == ChunkTag.SubChunkPrefix && v.Length > 0) subVersions[v[0]] = subVersions.GetValueOrDefault(v[0]) + 1;
            if (ck.Tag == ChunkTag.FinalizedState && v.Length >= 4) finalized[BitConverter.ToInt32(v)] = finalized.GetValueOrDefault(BitConverter.ToInt32(v)) + 1;
        }
        var small = new Dictionary<string, int>();
        int data3dShown = 0;
        foreach (var key in w.Db.Keys())
        {
            if (!BedrockKeys.TryParseChunkKey(key, out var ck)) continue;
            if (ck.Tag is ChunkTag.BlendingData or ChunkTag.ActorDigestVersion or ChunkTag.BiomeState or ChunkTag.GenerationSeed or ChunkTag.Checksums)
            {
                var v = w.Db.Get(key)!;
                string k = $"{ck.Tag} len{v.Length} {MinecraftStudio.Core.IO.BinaryUtil.Hex(v, 8)}";
                small[k] = small.GetValueOrDefault(k) + 1;
            }
            if (ck.Tag == ChunkTag.Data3D && data3dShown++ < 3)
            {
                var v = w.Db.Get(key)!;
                Console.WriteLine($"Data3D {ck.X},{ck.Z} len {v.Length}: heights {MinecraftStudio.Core.IO.BinaryUtil.Hex(v.AsSpan(0, 8))} biomes {MinecraftStudio.Core.IO.BinaryUtil.Hex(v.AsSpan(512), 64)}");
            }
        }
        foreach (var kv in small.OrderByDescending(k => k.Value).Take(25)) Console.WriteLine($"  {kv.Value,6} x {kv.Key}");
        Console.WriteLine("chunk versions: " + string.Join(", ", versions.OrderBy(k => k.Key).Select(kv => $"{kv.Key}:{kv.Value}")));
        Console.WriteLine("tags: " + string.Join(", ", tags.OrderBy(k => k.Key).Select(kv => $"{kv.Key}:{kv.Value}")));
        Console.WriteLine("subchunk versions: " + string.Join(", ", subVersions.Select(kv => $"v{kv.Key}:{kv.Value}")));
        Console.WriteLine("finalized: " + string.Join(", ", finalized.Select(kv => $"{kv.Key}:{kv.Value}")));
        var other = w.Db.Keys().Where(k => !BedrockKeys.TryParseChunkKey(k, out _) && BedrockKeys.IsPrintable(k)).Select(k => System.Text.Encoding.ASCII.GetString(k)).ToList();
        Console.WriteLine("named keys: " + string.Join(", ", other.Where(k => !k.StartsWith("actorprefix") && !k.StartsWith("digp")).Take(60)));
        Console.WriteLine($"actor keys: {w.Db.Keys().Count(BedrockKeys.IsActorKey)}, digp keys: {w.Db.Keys().Count(k => BedrockKeys.TryParseDigestKey(k, out _, out _, out _))}");
        // One block entity and one actor as samples
        foreach (var key in w.Db.Keys())
        {
            if (BedrockKeys.TryParseChunkKey(key, out var ck) && ck.Tag == ChunkTag.BlockEntity)
            {
                var all = NbtBinary.ReadAll(w.Db.Get(key)!, NbtFormat.Bedrock);
                foreach (var t in all.Take(3)) Console.WriteLine("BE: " + Snbt.Write(t.Tag, SnbtStyle.Compact));
                break;
            }
        }
        foreach (var key in w.Db.Keys().Where(BedrockKeys.IsActorKey).Take(2))
            Console.WriteLine("ACTOR: " + Snbt.Write(NbtBinary.ReadAll(w.Db.Get(key)!, NbtFormat.Bedrock)[0].Tag, SnbtStyle.Compact)[..Math.Min(1500, Snbt.Write(NbtBinary.ReadAll(w.Db.Get(key)!, NbtFormat.Bedrock)[0].Tag, SnbtStyle.Compact).Length)]);
    }
}
