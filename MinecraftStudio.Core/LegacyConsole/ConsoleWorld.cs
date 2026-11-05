// MinecraftStudio.Core/LegacyConsole/ConsoleWorld.cs: A Legacy Console Edition world.
// Owns: ConsoleWorld.
using System.Buffers.Binary;
using System.Text.RegularExpressions;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.LegacyConsole;

/// <summary>A Legacy Console Edition world.</summary>
public sealed class ConsoleWorld : SaveWorld
{
    readonly List<WorldDimension> _dims = [];
    readonly List<PlayerRecord> _players = [];

    ConsoleWorld(string path) { FilePath = path; }

    public string FilePath { get; }
    public ConsoleArchive Archive { get; private set; } = null!;
    public ConsoleWrapper Wrapper { get; private set; }
    public ConsolePlatform Platform { get; private set; }
    public StfsPackage? Package { get; private set; }
    public StfsPackage.Entry? PackageEntry { get; private set; }
    public NbtFile? Level { get; private set; }
    public List<string> Warnings { get; } = [];
    /// <summary>Split region files beside the main file (PS4 / Xbox One / Switch).</summary>
    public Dictionary<string, string> SplitRegionFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public override Edition Edition => Edition.LegacyConsole;
    public override string Name =>
        Package?.DisplayName is { Length: > 0 } dn ? dn : Level?.RootCompound.GetCompound("Data")?.GetString("LevelName") ?? Path.GetFileNameWithoutExtension(FilePath);
    public override string Location => FilePath;
    public override NbtFile? LevelData => Level;
    public override IReadOnlyList<WorldDimension> Dimensions => _dims;
    public override IReadOnlyList<PlayerRecord> Players => _players;
    public override bool IsModified => _dims.Any(d => d.IsModified) || LevelModified;
    public bool LevelModified { get; set; }

    public override string FormatDescription =>
        $"Legacy Console Edition ({Platform.DisplayName()}, save version {Archive.Version} = {ConsoleArchive.Era(Archive.Version)}, {Wrapper})";

    public static ConsoleWorld Open(string path, IProgress<string>? progress = null)
    {
        var w = new ConsoleWorld(path);
        byte[] d = File.ReadAllBytes(path);
        string magic = d.Length >= 4 ? System.Text.Encoding.ASCII.GetString(d, 0, 4) : "";
        if (magic is "CON " or "LIVE" or "PIRS")
        {
            progress?.Report("Reading Xbox 360 package");
            var pkg = new StfsPackage(d);
            w.Package = pkg;
            var entry = pkg.Files().FirstOrDefault(f => f.Name.Equals("savegame.dat", StringComparison.OrdinalIgnoreCase))
                ?? throw new SaveFormatException("The package does not contain savegame.dat.");
            w.PackageEntry = entry;
            d = pkg.Extract(entry, out int bad);
            if (bad > 0) w.Warnings.Add($"{bad} blocks of savegame.dat failed their SHA-1 check.");
            if (!pkg.HeaderHashValid) w.Warnings.Add("The package header hash does not match.");
        }
        progress?.Report("Decompressing save");
        if (!ConsoleSave.TryDecode(d, out var archive, out var wrapper, out var platform))
            throw new SaveFormatException("Not a Legacy Console Edition save (no known wrapper decoded to a valid archive).");
        w.Archive = archive!;
        w.Wrapper = w.Package != null ? ConsoleWrapper.Stfs : wrapper;
        w.Platform = platform == ConsolePlatform.Raw ? (archive!.LittleEndian ? ConsolePlatform.PS4 : ConsolePlatform.Xbox360) : platform;
        w.Platform = RefinePlatform(path, w.Platform);
        if (archive!.Warning != null) w.Warnings.Add(archive.Warning);
        w.FindSplitRegions();
        w.Load();
        return w;
    }

    static ConsolePlatform RefinePlatform(string path, ConsolePlatform p)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (p is ConsolePlatform.PS4 or ConsolePlatform.XboxOne or ConsolePlatform.Switch)
        {
            if (Directory.Exists(Path.Combine(dir, "sce_sys"))) return ConsolePlatform.PS4;
            string stem = Path.Combine(dir, Path.GetFileNameWithoutExtension(path));
            if (Directory.Exists(stem + ".sub") || File.Exists(stem + ".ext")) return ConsolePlatform.Switch;
            if (File.Exists(Path.Combine(dir, "wd_displayname.txt")) || File.Exists(Path.Combine(dir, "containers.index"))) return ConsolePlatform.XboxOne;
        }
        if (p == ConsolePlatform.WiiU && File.Exists(path + ".ext")) return ConsolePlatform.WiiU;
        return p;
    }

    static readonly Regex SplitName = new(@"^GAMEDATA_000([0-2])([0-9a-f]{2})([0-9a-f]{2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    void FindSplitRegions()
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        var folders = new List<string> { dir };
        string sub = Path.Combine(dir, Path.GetFileNameWithoutExtension(FilePath) + ".sub");
        if (Directory.Exists(sub)) folders.Add(sub);
        foreach (var folder in folders)
            foreach (var f in Directory.EnumerateFiles(folder, "GAMEDATA_000*"))
            {
                var m = SplitName.Match(Path.GetFileName(f));
                if (!m.Success) continue;
                int dim = m.Groups[1].Value[0] - '0';
                int rx = (sbyte)Convert.ToByte(m.Groups[2].Value, 16), rz = (sbyte)Convert.ToByte(m.Groups[3].Value, 16);
                string prefix = dim switch { 1 => "DIM-1", 2 => "DIM1/", _ => "" };
                SplitRegionFiles[$"{prefix}r.{rx}.{rz}.mcr"] = f;
            }
    }

    void Load()
    {
        _dims.Clear();
        _players.Clear();
        var level = Archive.Find("level.dat");
        if (level != null && level.Data.Length > 0)
        {
            try { Level = NbtFile.Parse(level.Data, NbtFormat.Java); }
            catch (Exception ex) { Warnings.Add("level.dat could not be read: " + ex.Message); }
        }
        foreach (var (prefix, id, name) in new[] { ("", "minecraft:overworld", "Overworld"), ("DIM-1", "minecraft:the_nether", "The Nether"), ("DIM1/", "minecraft:the_end", "The End") })
        {
            var regions = new Dictionary<(int, int), string>();
            var pattern = new Regex("^" + Regex.Escape(prefix) + @"r\.(-?\d+)\.(-?\d+)\.mcr$", RegexOptions.IgnoreCase);
            foreach (var f in Archive.Files)
            {
                var m = pattern.Match(f.Name);
                if (m.Success && f.Data.Length > 0) regions[(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))] = f.Name;
            }
            foreach (var (fileName, path) in SplitRegionFiles)
            {
                var m = pattern.Match(fileName);
                if (m.Success) regions[(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))] = fileName;
            }
            if (regions.Count > 0) _dims.Add(new ConsoleDimension(this, id, name, regions));
        }
        foreach (var f in Archive.Files)
        {
            bool isPlayer = f.Name.StartsWith("players/", StringComparison.OrdinalIgnoreCase) || f.Name.StartsWith("players_", StringComparison.OrdinalIgnoreCase);
            if (!isPlayer || f.Data.Length == 0) continue;
            try
            {
                var nbt = NbtFile.Parse(f.Data, NbtFormat.Java);
                if (nbt.Root is NbtCompound c) _players.Add(new PlayerRecord(Path.GetFileNameWithoutExtension(f.Name.Replace("players_", "players/")), f.Name, c));
            }
            catch { }
        }
    }

    /// <summary>Region bytes (from the archive or a split file).</summary>
    internal byte[] ReadRegionBytes(string name)
    {
        if (SplitRegionFiles.TryGetValue(name, out var path))
        {
            byte[] raw = File.ReadAllBytes(path);
            int dsize = BinaryPrimitives.ReadInt32LittleEndian(raw);
            return ConsoleRle.DecodeSplit(raw.AsSpan(4), dsize);
        }
        return Archive.Find(name)?.Data ?? [];
    }

    internal void WriteRegionBytes(string name, byte[] data)
    {
        if (SplitRegionFiles.TryGetValue(name, out var path))
        {
            var w = new ByteWriter(data.Length / 2 + 8);
            w.I32LE(data.Length);
            w.Bytes(ConsoleRle.EncodeSplit(data));
            SafeFile.WriteAllBytes(path, w.WrittenSpan);
            return;
        }
        var entry = Archive.Find(name);
        if (entry == null) Archive.Files.Add(entry = new ConsoleArchive.FileEntry { Name = name });
        entry.Data = data;
        entry.Modified = ConsoleArchive.Now1601Ms();
    }

    public override IEnumerable<(string Key, string Value)> Summary()
    {
        yield return ("Name", Name);
        yield return ("Format", FormatDescription);
        var d = Level?.RootCompound.GetCompound("Data");
        if (d != null)
        {
            if (d.GetLongOrNull("RandomSeed") is long seed) yield return ("Seed", seed.ToString());
            if (d.GetLongOrNull("GameType") is long gt) yield return ("Game mode", gt switch { 0 => "Survival", 1 => "Creative", 2 => "Adventure", _ => gt.ToString() });
            if (d.GetLongOrNull("XZSize") is long xz) yield return ("World size", $"{xz} chunks ({xz * 16} blocks) — " + (xz switch { 54 => "Classic", 64 => "Small", 192 => "Medium", 320 => "Large", _ => "custom" }));
            if (d.GetLongOrNull("SpawnX") is long sx) yield return ("Spawn", $"{sx}, {d.GetLong("SpawnY")}, {d.GetLong("SpawnZ")}");
            if (d.GetLongOrNull("Time") is long t) yield return ("World age", $"{t / 24000.0:0.#} days");
        }
        if (Package != null) yield return ("Package", $"{Package.Magic.Trim()} package, profile {Package.ProfileId}, title {Package.TitleId:X8}");
        yield return ("Archive", $"{Archive.Files.Count} files, version {Archive.Version} (min {Archive.MinVersion})");
        yield return ("Dimensions", string.Join(", ", _dims.Select(x => $"{x.DisplayName} ({x.ChunkPositions.Count} chunks)")));
        yield return ("Players", _players.Count.ToString());
        if (Warnings.Count > 0) yield return ("Warnings", string.Join(" ", Warnings));
    }

    public void SetLevel(NbtFile level)
    {
        Level = level;
        LevelModified = true;
    }

    /// <summary>Encodes the archive with the original wrapper and writes it back (and rebuilds the STFS package).</summary>
    public override void Save()
    {
        foreach (var d in _dims.OfType<ConsoleDimension>()) d.FlushRegions();
        if (LevelModified && Level != null)
        {
            var e = Archive.Find("level.dat");
            if (e != null) { e.Data = Level.ToBytes(); e.Modified = ConsoleArchive.Now1601Ms(); }
            LevelModified = false;
        }
        byte[] bytes = Encode();
        if (Wrapper == ConsoleWrapper.Stfs && Package != null)
        {
            var e = PackageEntry!;
            bytes = Package.Rebuild([("savegame.dat", bytes, e.UpdateTime, e.AccessTime)]);
        }
        SafeFile.WriteAllBytes(FilePath, bytes);
    }

    /// <summary>The save file bytes (without the STFS package) in the original wrapper.</summary>
    public byte[] Encode() => Wrap(Archive.ToBytes(), Wrapper);

    /// <summary>Compresses an archive the way <paramref name="wrapper"/> stores it.</summary>
    public static byte[] Wrap(byte[] arc, ConsoleWrapper wrapper)
    {
        var w = new ByteWriter(arc.Length / 2 + 64);
        switch (wrapper)
        {
            case ConsoleWrapper.Stfs:
            case ConsoleWrapper.X360Dat:
                {
                    byte[] lzx = XMemLzx.Compress(arc);
                    w.U32BE((uint)(8 + lzx.Length));
                    w.U32BE(0);
                    w.U32BE((uint)arc.Length);
                    w.Bytes(lzx);
                    break;
                }
            case ConsoleWrapper.Mcs:
                w.U32BE(0);
                w.U32BE((uint)arc.Length);
                w.Bytes(XMemLzx.Compress(arc));
                break;
            case ConsoleWrapper.ZlibBigEndian:
                w.U32BE(0);
                w.U32BE((uint)arc.Length);
                w.Bytes(Compression.ZlibCompress(arc));
                break;
            case ConsoleWrapper.ZlibLittleEndian:
                w.U32LE(0);
                w.U32LE((uint)arc.Length);
                w.Bytes(Compression.ZlibCompress(arc));
                break;
            case ConsoleWrapper.Ps3Deflate:
                w.U32BE(0);
                w.U32BE((uint)arc.Length);
                w.U32BE((uint)arc.Length);
                w.Bytes(Compression.Deflate(arc));
                break;
            case ConsoleWrapper.VitaRle:
                w.U32LE(0);
                w.U32LE((uint)arc.Length);
                w.Bytes(ConsoleRle.EncodeVita(arc));
                break;
            default:
                return arc;
        }
        return w.ToArray();
    }
}
