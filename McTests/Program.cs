// McTests/Program.cs: Program.
// Owns: Program, NbtTests, RegionTests.
using System.Diagnostics;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;

static partial class Program
{
    static int failures, passes;

    public static void Check(bool ok, string what)
    {
        if (ok) passes++;
        else { failures++; Console.WriteLine($"  [FAIL] {what}"); }
    }

    public static void Info(string s) => Console.WriteLine("  " + s);

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "nbt";
        var sw = Stopwatch.StartNew();
        switch (mode)
        {
            case "nbt": NbtTests.Run(args.Skip(1).ToArray()); break;
            case "regions": RegionTests.Run(args.Skip(1).ToArray()); break;
            case "render": WorldTests.Render(args.Skip(1).ToArray()); break;
            case "javaworlds": WorldTests.AllJava(args.Skip(1).ToArray()); break;
            case "bedrock": BedrockTests.All(args.Skip(1).ToArray()); break;
            case "bedrockrender": BedrockTests.Render(args.Skip(1).ToArray()); break;
            case "bedrockwrite": BedrockTests.Write(args.Skip(1).ToArray()); break;
            case "keys": KeyDump.Run(args.Skip(1).ToArray()); break;
            case "bloom": BedrockTests.Bloom(args.Skip(1).ToArray()); break;
            case "tables": TableTests.Run(args.Skip(1).ToArray()); break;
            case "downloads": DownloadTests.Run(args.Skip(1).ToArray()); break;
            case "console": ConsoleTests.Run(args.Skip(1).ToArray()); break;
            case "consolewrite": ConsoleTests.Write(args.Skip(1).ToArray()); break;
            case "stfs": ConsoleTests.Stfs(args.Skip(1).ToArray()); break;
            case "rawdat": ConsoleTests.RawDat(args.Skip(1).ToArray()); break;
            case "chunkkeys": KeyDump.Chunk(args.Skip(1).ToArray()); break;
            case "keystats": KeyDump.Stats(args.Skip(1).ToArray()); break;
            case "unfinished": UnfinishedTests.Run(); break;
            case "check": CheckTests.Run(); break;
            case "shape": KeyDump.Shape(args.Skip(1).ToArray()); break;
            case "playerkeys": KeyDump.PlayerKeys(args.Skip(1).ToArray()); break;
            case "playerground": KeyDump.PlayerGround(args.Skip(1).ToArray()); break;
            case "inspectbedrock": InspectTests.Bedrock(args.Skip(1).ToArray()); break;
            case "inspectconsole": InspectTests.Console_(args.Skip(1).ToArray()); break;
            case "convert": ConvertTests.Run(args.Skip(1).ToArray()); break;
            case "leafdebug": LeafDebug.Run(args.Skip(1).ToArray()); break;
            case "leafdebug2": LeafDebug.RoundTrip(args.Skip(1).ToArray()); break;
            case "mapdebug": MapDebug.Run(args.Skip(1).ToArray()); break;
            case "mapdebugb": MapDebug.Bedrock(args.Skip(1).ToArray()); break;
            case "lceids": LceIds.Run(args.Skip(1).ToArray()); break;
            case "downgrade": DowngradeTests.Run(args.Skip(1).ToArray()); break;
            case "matrix": MatrixTests.Run(args.Skip(1).ToArray()); break;
            case "prune": PruneTests.Run(args.Skip(1).ToArray()); break;
            case "fidelity": FidelityTests.Run(args.Skip(1).ToArray()); break;
            case "be": BeDump.Run(args.Skip(1).ToArray()); break;
            case "restore": RestoreTests.Run(args.Skip(1).ToArray()); break;
            case "skins": SkinTests.Run(args.Skip(1).ToArray()); break;
            case "textures": TexturePackTests.Run(args.Skip(1).ToArray()); break;
            case "renderspeed": RenderSpeed.Run(args.Skip(1).ToArray()); break;
            case "rescue": RescueTests.Run(args.Skip(1).ToArray()); break;
            case "inventory": InventoryTests.Run(args.Skip(1).ToArray()); break;
            case "structure": StructureTests.Run(args.Skip(1).ToArray()); break;
            case "backup": AndroidBackupTests.Run(args.Skip(1).ToArray()); break;
            case "identity": IdentityTests.Run(args.Skip(1).ToArray()); break;
            case "packs": PackTests.Run(args.Skip(1).ToArray()); break;
            case "paint": PaintEngineTests.Run(args.Skip(1).ToArray()); break;
            case "mobs": MobTests.Run(args.Skip(1).ToArray()); break;
            case "animations": AnimationTests.Run(args.Skip(1).ToArray()); break;
            case "hardcore": HardcoreTests.Run(args.Skip(1).ToArray()); break;
            case "search": SearchTests.Run(args.Skip(1).ToArray()); break;
            case "schematics": SchematicTests.Run(args.Skip(1).ToArray()); break;
            case "biomes": BiomeTests.Run(args.Skip(1).ToArray()); break;
            case "gamerules": GameRuleTests.Run(args.Skip(1).ToArray()); break;
            case "packcheck": PackCheckTests.Run(args.Skip(1).ToArray()); break;
            case "share": ShareTests.Run(args.Skip(1).ToArray()); break;
            case "hanging": HangingTests.Run(args.Skip(1).ToArray()); break;
            case "turn": TurnTests.Run(args.Skip(1).ToArray()); break;
            case "server": ServerTests.Run(args.Skip(1).ToArray()); break;
            case "replace": ReplaceTests.Run(args.Skip(1).ToArray()); break;
            case "give": GiveTests.Run(args.Skip(1).ToArray()); break;
            case "mapart": MapArtTests.Run(args.Skip(1).ToArray()); break;
            case "templates": TemplateTests.Run(args.Skip(1).ToArray()); break;
            case "fields": EntityFieldTests.Run(args.Skip(1).ToArray()); break;
            case "census": CensusTests.Run(args.Skip(1).ToArray()); break;
            case "mods": ModsTests.Run(args.Skip(1).ToArray()); break;
            case "compare": CompareTests.Run(args.Skip(1).ToArray()); break;
            case "model": ModelTests.Run(args.Skip(1).ToArray()); break;
            case "blockmodels": BlockModelTests.Run(args.Skip(1).ToArray()); break;
            case "features": FeatureTests.Run(args.Skip(1).ToArray()); break;
            case "biomepaint": BiomePaintTests.Run(args.Skip(1).ToArray()); break;
            case "move": MoveTests.Run(args.Skip(1).ToArray()); break;
            case "lag": LagTests.Run(args.Skip(1).ToArray()); break;
            case "slime": SlimeTests.Run(args.Skip(1).ToArray()); break;
            case "terrain": TerrainTests.Run(args.Skip(1).ToArray()); break;
            case "structuremap": StructureMapTests.Run(args.Skip(1).ToArray()); break;
            case "riding": RidingTests.Run(args.Skip(1).ToArray()); break;
            case "variants": VariantTests.Run(args.Skip(1).ToArray()); break;
            case "worldfiles": WorldFileTests.Run(args.Skip(1).ToArray()); break;
            default: Console.WriteLine("unknown mode " + mode); return 2;
        }
        Console.WriteLine($"\n{passes} passed, {failures} failed in {sw.Elapsed.TotalSeconds:0.0}s");
        return failures == 0 ? 0 : 1;
    }
}

static class NbtTests
{
    public static void Run(string[] args)
    {
        Console.WriteLine("== Synthetic NBT");
        Synthetic();
        Console.WriteLine("== Java NBT files (.dat)");
        string saves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "saves");
        int files = 0;
        foreach (var f in Directory.EnumerateFiles(saves, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is not (".dat" or ".dat_old" or ".nbt")) continue;
            if (Path.GetFileName(Path.GetDirectoryName(f)!).Length <= 2 && f.Contains("World1")) continue; // alpha chunk files: tested separately
            files++;
            RoundTripFile(f);
        }
        Program.Info($"{files} Java files checked");

        Console.WriteLine("== Alpha chunk files (World1)");
        int alpha = 0;
        foreach (var f in Directory.EnumerateFiles(Path.Combine(saves, "World1"), "c.*.dat", SearchOption.AllDirectories))
        {
            alpha++;
            RoundTripFile(f);
        }
        Program.Info($"{alpha} alpha chunk files checked");

        Console.WriteLine("== Bedrock level.dat files");
        int bedrock = 0;
        foreach (var root in BedrockRoots())
            foreach (var dir in Directory.EnumerateDirectories(root))
                foreach (var name in new[] { "level.dat", "level.dat_old" })
                {
                    string f = Path.Combine(dir, name);
                    if (!File.Exists(f)) continue;
                    bedrock++;
                    var file = RoundTripFile(f);
                    if (file != null) Program.Check(file.HeaderVersion != null && file.Format == NbtFormat.Bedrock, $"{f}: detected as Bedrock with header ({file.Describe()})");
                }
        Program.Info($"{bedrock} Bedrock files checked");
    }

    public static IEnumerable<string> BedrockRoots()
    {
        string users = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minecraft Bedrock", "Users");
        if (Directory.Exists(users))
            foreach (var u in Directory.EnumerateDirectories(users))
            {
                string w = Path.Combine(u, "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(w)) yield return w;
            }
        string launcher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft_bedrock", "installations");
        if (Directory.Exists(launcher))
            foreach (var inst in Directory.EnumerateDirectories(launcher))
            {
                string w = Path.Combine(inst, "packageData", "minecraftWorlds");
                if (Directory.Exists(w)) yield return w;
            }
    }

    static NbtFile? RoundTripFile(string f)
    {
        byte[] raw = File.ReadAllBytes(f);
        if (raw.Length == 0) { Program.Info($"(empty) {f}"); return null; }
        NbtFile file;
        try { file = NbtFile.Parse(raw); }
        catch (Exception ex) { Program.Check(false, $"{f}: parse failed: {ex.Message}"); return null; }
        byte[] plain = file.Compression == CompressionKind.None ? raw : Compression.Decompress(raw, file.Compression);
        byte[] again = file.ToUncompressedBytes();
        bool same = plain.AsSpan().SequenceEqual(again);
        Program.Check(same, $"{f}: re-serialized bytes identical ({file.Describe()}, {plain.Length} vs {again.Length} bytes)");
        if (!same) ReportDiff(plain, again);
        // compressed round trip parses back to the same content
        var back = NbtFile.Parse(file.ToBytes());
        Program.Check(back.Roots.Count == file.Roots.Count && back.Roots.Zip(file.Roots).All(p => p.First.Tag.ContentEquals(p.Second.Tag)), $"{f}: recompressed file parses to the same tags");
        // SNBT round trip
        foreach (var root in file.Roots)
        {
            string text = Snbt.Write(root.Tag, SnbtStyle.Pretty);
            NbtTag parsed;
            try { parsed = Snbt.Parse(text); }
            catch (Exception ex) { Program.Check(false, $"{f}: SNBT parse failed: {ex.Message}"); continue; }
            Program.Check(parsed.ContentEquals(root.Tag), $"{f}: SNBT round trip preserves the content");
            string compact = Snbt.Write(root.Tag, SnbtStyle.Compact);
            Program.Check(Snbt.Parse(compact).ContentEquals(root.Tag), $"{f}: compact SNBT round trip");
        }
        return file;
    }

    static void ReportDiff(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i])
            {
                Program.Info($"    first difference at {i}: {BinaryUtil.Hex(a.AsSpan(Math.Max(0, i - 8), Math.Min(24, a.Length - Math.Max(0, i - 8))))} vs {BinaryUtil.Hex(b.AsSpan(Math.Max(0, i - 8), Math.Min(24, b.Length - Math.Max(0, i - 8))))}");
                return;
            }
        Program.Info($"    lengths differ: {a.Length} vs {b.Length}");
    }

    static void Synthetic()
    {
        var c = new NbtCompound();
        c.SetString("ascii", "hello");
        c.SetString("unicode", "héllo wörld ☃ 😀");
        c.SetString("nul", "a\0b");
        c.SetString("quote", "it's \"quoted\" \\ \n new line");
        c.SetByte("b", -5);
        c.SetShort("s", -300);
        c.SetInt("i", int.MinValue);
        c.SetLong("l", long.MaxValue);
        c.SetFloat("f", 0.1f);
        c.SetDouble("d", -1e300);
        c.SetFloat("nan", float.NaN);
        c["ba"] = new NbtByteArray([0, 1, 255, 128]);
        c["ia"] = new NbtIntArray([1, -1, int.MaxValue]);
        c["la"] = new NbtLongArray([1, -1, long.MinValue]);
        var list = new NbtList();
        list.Add(new NbtCompound { { "x", new NbtInt(1) } });
        list.Add(new NbtCompound());
        c["list"] = list;
        c["empty"] = new NbtList(NbtType.Compound);
        c["nested"] = new NbtList { new NbtList { new NbtString("deep") } };
        c["weird key!"] = new NbtString("v");
        c["123"] = new NbtString("numeric-looking key");
        c["true"] = new NbtString("true");
        c["1.5"] = new NbtString("1.5");
        foreach (var fmt in NbtFormat.All)
        {
            byte[] bin = NbtBinary.Serialize(c, fmt, "root");
            var back = NbtBinary.ReadCompound(bin, fmt, out string name);
            Program.Check(name == "root" && back.ContentEquals(c), $"synthetic compound round-trips in {fmt.Name}");
            Program.Check(NbtBinary.Serialize(back, fmt, "root").AsSpan().SequenceEqual(bin), $"synthetic compound re-serializes identically in {fmt.Name}");
            var detected = NbtFile.Parse(bin);
            Program.Check(detected.Format == fmt || fmt == NbtFormat.LittleEndianModifiedUtf8 || fmt == NbtFormat.BedrockNetwork, $"format detection for {fmt.Name} gave {detected.Format.Name}");
        }
        string snbt = Snbt.Write(c);
        var parsed = Snbt.Parse(snbt);
        Program.Check(parsed.ContentEquals(c), "synthetic SNBT round trip (pretty)");
        Program.Check(Snbt.Parse(Snbt.Write(c, SnbtStyle.Compact)).ContentEquals(c), "synthetic SNBT round trip (compact)");
        if (!parsed.ContentEquals(c)) Console.WriteLine(snbt);
        // Invalid UTF-8 survives Bedrock round trip
        byte[] badUtf8 = [0x0A, 0, 0, 8, 1, 0, (byte)'k', 3, 0, 0xFF, 0xC3, 0x28, 0];
        var bad = NbtBinary.ReadCompound(badUtf8, NbtFormat.Bedrock);
        Program.Check(NbtBinary.Serialize(bad, NbtFormat.Bedrock).AsSpan().SequenceEqual(badUtf8), "invalid UTF-8 bytes in Bedrock strings are preserved");
        // SNBT parsing of vanilla style text
        var v = Snbt.Parse("{Count:1b,id:\"minecraft:stone\",tag:{display:{Name:'{\"text\":\"Hi\"}'}},Pos:[1.0d,2.5d,-3.0d],Rot:[0.0f,90f],Arr:[I;1,2,3],L:5L,s:3s,d:1.5,e:1e3d,big:3000000000}");
        var vc = (NbtCompound)v;
        Program.Check(vc["Count"] is NbtByte { Value: 1 }, "SNBT 1b is a byte");
        Program.Check(vc["Pos"] is NbtList { ElementType: NbtType.Double, Count: 3 }, "SNBT double list");
        Program.Check(vc["Rot"] is NbtList { ElementType: NbtType.Float }, "SNBT float list");
        Program.Check(vc["d"] is NbtDouble { Value: 1.5 }, "SNBT 1.5 is a double");
        Program.Check(vc["e"] is NbtDouble { Value: 1000 }, "SNBT 1e3d is a double");
        Program.Check(vc["big"] is NbtString, "SNBT out-of-range int becomes a string (like Minecraft)");
        Program.Check(vc.GetCompound("tag")!.GetCompound("display")!.GetString("Name") == "{\"text\":\"Hi\"}", "SNBT single-quoted string with double quotes");
        // LZ4 stream round trip
        var rnd = new Random(1);
        foreach (int size in new[] { 0, 1, 13, 100, 70000, 300000 })
        {
            var data = new byte[size];
            for (int i = 0; i < size; i++) data[i] = (byte)(i % 7 == 0 ? rnd.Next(256) : i / 100);
            var comp = Lz4.CompressJavaStream(data);
            Program.Check(Lz4.DecompressJavaStream(comp).AsSpan().SequenceEqual(data), $"LZ4 java stream round trip ({size} bytes -> {comp.Length})");
        }
        Program.Check(Crc32C.Compute("123456789"u8) == 0xE3069283, "CRC32C check value");
        Program.Check(Crc32.Compute("123456789"u8) == 0xCBF43926, "CRC32 check value");
        Program.Check(XxHash32.Compute("Nobody inspects the spammish repetition"u8, 0) == 0xE2293B2F, "xxHash32 check value");
    }
}

static class RegionTests
{
    public static void Run(string[] args)
    {
        string saves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "saves");
        int perFolder = args.Length > 0 ? int.Parse(args[0]) : 2;
        var compressionCounts = new Dictionary<int, int>();
        int chunks = 0, regions = 0;
        long bytes = 0;
        var sw = Stopwatch.StartNew();
        foreach (var world in Directory.EnumerateDirectories(saves))
        {
            var folders = Directory.EnumerateFiles(world, "*.mc?", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".mca", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mcr", StringComparison.OrdinalIgnoreCase))
                .GroupBy(f => Path.GetDirectoryName(f)!);
            foreach (var folder in folders)
            {
                foreach (var file in folder.OrderByDescending(f => new FileInfo(f).Length).Take(perFolder))
                {
                    regions++;
                    RegionFile region;
                    try { region = RegionFile.Open(file); }
                    catch (Exception ex) { Program.Check(false, $"{file}: open failed {ex.Message}"); continue; }
                    var issues = region.Validate(false);
                    foreach (var i in issues) Program.Info($"issue {Path.GetFileName(file)} slot {i.Index}: {i.Message}");
                    foreach (int idx in region.ChunkIndexes())
                    {
                        RawChunk? raw;
                        try { raw = region.GetRaw(idx); }
                        catch (Exception ex) { Program.Check(false, $"{file} chunk {idx}: {ex.Message}"); continue; }
                        if (raw == null) continue;
                        compressionCounts[raw.CompressionType] = compressionCounts.GetValueOrDefault(raw.CompressionType) + 1;
                        byte[] plain;
                        try { plain = raw.Decompress(); }
                        catch (Exception ex) { Program.Check(false, $"{file} chunk {idx}: decompress {ex.Message}"); continue; }
                        bytes += plain.Length;
                        chunks++;
                        try
                        {
                            var nbt = NbtBinary.ReadCompound(plain, NbtFormat.Java, out string name);
                            byte[] again = NbtBinary.Serialize(nbt, NbtFormat.Java, name);
                            Program.Check(again.AsSpan().SequenceEqual(plain), $"{file} chunk {idx}: identical re-serialization");
                        }
                        catch (Exception ex) { Program.Check(false, $"{file} chunk {idx}: NBT {ex.Message}"); }
                    }
                    // Save to a temp copy: unchanged region must keep every chunk readable and identical
                    string tmp = Path.Combine(Path.GetTempPath(), "mss-region-test", Path.GetFileName(file));
                    Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
                    region.Save(tmp, deleteIfEmpty: false);
                    var copy = RegionFile.Open(tmp);
                    bool same = region.ChunkIndexes().SequenceEqual(copy.ChunkIndexes())
                        && region.ChunkIndexes().All(i => region.GetRaw(i)!.Payload.AsSpan().SequenceEqual(copy.GetRaw(i)!.Payload) && region.GetTimestamp(i) == copy.GetTimestamp(i));
                    Program.Check(same, $"{file}: compact rewrite keeps all chunks byte-identical");
                    Program.Check(copy.WastedBytes() == 0, $"{file}: rewrite has no wasted sectors (was {region.WastedBytes()} bytes)");
                    File.Delete(tmp);
                }
            }
        }
        Program.Info($"{regions} regions, {chunks} chunks, {bytes / 1048576.0:0.0} MB of NBT in {sw.Elapsed.TotalSeconds:0.0}s");
        Program.Info("compression types: " + string.Join(", ", compressionCounts.Select(kv => $"{kv.Key}={kv.Value}")));
    }
}
