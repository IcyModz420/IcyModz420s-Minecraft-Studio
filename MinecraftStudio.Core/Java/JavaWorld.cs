// MinecraftStudio.Core/Java/JavaWorld.cs: a Java Edition save (Anvil/McRegion/Alpha): its dimensions, region files, level.dat and players.
// Owns: JavaStorage, JavaWorld.
using System.Buffers.Binary;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Java;

public enum JavaStorage
{
    Unknown,
    /// <summary>Infdev/Alpha/early Beta: one file per chunk in base-36 folders.</summary>
    Alpha,
    /// <summary>Beta 1.3 - 1.1: .mcr region files.</summary>
    McRegion,
    /// <summary>1.2+: .mca region files.</summary>
    Anvil,
}

/// <summary>A Java Edition world folder (Infdev 2010 onwards).</summary>
public sealed class JavaWorld : SaveWorld
{
    readonly List<WorldDimension> _dimensions = [];
    readonly List<PlayerRecord> _players = [];

    JavaWorld(string folder, NbtFile? level)
    {
        Folder = folder;
        Level = level;
    }

    public string Folder { get; }
    public NbtFile? Level { get; }
    public NbtCompound? Data => Level?.Roots.Count > 0 ? (Level.Root as NbtCompound)?.GetCompound("Data") : null;
    public JavaStorage Storage { get; private set; }

    public override Edition Edition => Edition.Java;
    public override string Name => Data?.GetString("LevelName") ?? Path.GetFileName(Folder);
    public override string Location => Folder;
    public override NbtFile? LevelData => Level;
    public override IReadOnlyList<WorldDimension> Dimensions => _dimensions;
    public override IReadOnlyList<PlayerRecord> Players => _players;

    public int? DataVersion => Data?.GetLongOrNull("DataVersion") is long v ? (int)v : null;
    public string? VersionName => Data?.GetCompound("Version")?.GetString("Name");
    /// <summary>Data.version: 19132 McRegion, 19133 Anvil.</summary>
    public int? StorageVersion => Data?.GetLongOrNull("version") is long v ? (int)v : null;

    public long? Seed
    {
        get
        {
            var d = Data;
            if (d?.GetCompound("WorldGenSettings")?.GetLongOrNull("seed") is long s) return s;
            if (d?.GetLongOrNull("RandomSeed") is long r) return r;
            // 26.1 snapshot 6+: world generation settings moved to their own file.
            string wgs = Path.Combine(Folder, "data", "minecraft", "world_gen_settings.dat");
            if (File.Exists(wgs))
            {
                try
                {
                    var root = NbtFile.Load(wgs).RootCompound;
                    var data = root.GetCompound("data") ?? root;
                    return data.GetLongOrNull("seed");
                }
                catch { }
            }
            return null;
        }
    }

    /// <summary>26.1+ layout: every dimension under dimensions/minecraft/…, players under players/data.</summary>
    public bool IsNewLayout => Directory.Exists(Path.Combine(Folder, "dimensions", "minecraft", "overworld"));

    /// <summary>NBT files in data/ (maps, raids, scoreboard, …).</summary>
    public IEnumerable<string> DataFiles()
    {
        string data = Path.Combine(Folder, "data");
        if (!Directory.Exists(data)) return [];
        return Directory.EnumerateFiles(data, "*.dat", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }

    public override string FormatDescription
    {
        get
        {
            string storage = Storage switch { JavaStorage.Alpha => "Alpha chunk files", JavaStorage.McRegion => "McRegion", JavaStorage.Anvil => "Anvil", _ => "no chunks" };
            string ver = VersionName ?? JavaVersions.Describe(DataVersion, Storage, StorageVersion);
            return DataVersion is int dv ? $"Java {ver} ({storage}, data version {dv})" : $"Java {ver} ({storage})";
        }
    }

    public static bool LooksLikeJavaWorld(string folder)
    {
        if (!Directory.Exists(folder)) return false;
        string level = Path.Combine(folder, "level.dat");
        if (File.Exists(level))
        {
            try
            {
                var head = new byte[2];
                using var fs = File.OpenRead(level);
                if (fs.Read(head) == 2 && head[0] == 0x1F && head[1] == 0x8B) return true;
            }
            catch { }
        }
        return Directory.Exists(Path.Combine(folder, "region")) || Directory.Exists(Path.Combine(folder, "dimensions", "minecraft", "overworld"));
    }

    /// <summary>
    /// Opens a damaged world as far as it can be read: level.dat from the copy the game keeps (level.dat_old) when the
    /// file is damaged, or none at all; every region file searched for chunks its header lost and for older copies of
    /// chunks whose data is broken. <paramref name="findings"/> says what was damaged and what was used instead.
    /// </summary>
    public static JavaWorld OpenForRescue(string folder, List<string> findings, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        NbtFile? level = null;
        foreach (var name in new[] { "level.dat", "level.dat_old" })
        {
            string path = Path.Combine(folder, name);
            if (!File.Exists(path)) { if (name == "level.dat") findings.Add("level.dat is missing."); continue; }
            try
            {
                level = NbtFile.Load(path);
                if ((level.Root as NbtCompound)?.GetCompound("Data") == null) throw new SaveFormatException("it has no Data section");
                if (name != "level.dat") findings.Add("The world's settings were taken from level.dat_old, the copy the game keeps of level.dat.");
                break;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or SaveFormatException or ArgumentException or EndOfStreamException or UnauthorizedAccessException)
            {
                level = null;
                findings.Add($"{name} is damaged ({World.WorldRescue.Describe(e)}).");
            }
        }
        if (level == null) findings.Add("No readable level.dat: the world's name, seed, spawn and rules start from defaults in the rescued world; the terrain does not depend on them.");
        var w = new JavaWorld(folder, level) { OpenedForRescue = true };
        w.Discover();
        foreach (var d in w.Dimensions.OfType<JavaRegionDimension>()) findings.AddRange(d.Rescue(cancel, progress));
        return w;
    }

    public static JavaWorld Open(string folder)
    {
        NbtFile? level = null;
        string levelPath = Path.Combine(folder, "level.dat");
        if (File.Exists(levelPath)) level = LoadLevel(levelPath);
        else if (File.Exists(levelPath + "_old")) level = LoadLevel(levelPath + "_old");
        var w = new JavaWorld(folder, level);
        w.Discover();
        return w;
    }

    /// <summary>level.dat, or an error that says it is level.dat that is damaged.</summary>
    static NbtFile LoadLevel(string path)
    {
        try { return NbtFile.Load(path); }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or SaveFormatException or ArgumentException or OverflowException)
        {
            throw new SaveFormatException($"{Path.GetFileName(path)} is damaged ({World.WorldRescue.Describe(e)}).", e);
        }
    }

    void Discover()
    {
        AddDimension(Folder, "minecraft:overworld", "Overworld");
        AddDimension(Path.Combine(Folder, "DIM-1"), "minecraft:the_nether", "The Nether");
        AddDimension(Path.Combine(Folder, "DIM1"), "minecraft:the_end", "The End");
        string custom = Path.Combine(Folder, "dimensions");
        if (Directory.Exists(custom))
        {
            // Vanilla dimensions first (26.1+ stores them here), then data-pack dimensions.
            var found = new List<(string Dir, string Id)>();
            foreach (var ns in Directory.EnumerateDirectories(custom))
                foreach (var dim in Directory.EnumerateDirectories(ns, "*", SearchOption.AllDirectories))
                {
                    if (!Directory.Exists(Path.Combine(dim, "region"))) continue;
                    found.Add((dim, Path.GetFileName(ns) + ":" + Path.GetRelativePath(ns, dim).Replace('\\', '/')));
                }
            int Order(string id) => id switch { "minecraft:overworld" => 0, "minecraft:the_nether" => 1, "minecraft:the_end" => 2, _ => 3 };
            foreach (var (dir, id) in found.OrderBy(f => Order(f.Id)).ThenBy(f => f.Id, StringComparer.Ordinal))
            {
                if (_dimensions.Any(d => d.Id == id)) continue;
                AddDimension(dir, id, DimensionName(id));
            }
        }
        // Other DIMn folders (mods, old versions)
        foreach (var d in Directory.EnumerateDirectories(Folder, "DIM*"))
        {
            string n = Path.GetFileName(d);
            if (n is "DIM-1" or "DIM1") continue;
            AddDimension(d, n, n);
        }
        LoadPlayers();
    }

    public static string DimensionName(string id) => id switch
    {
        "minecraft:overworld" => "Overworld",
        "minecraft:the_nether" => "The Nether",
        "minecraft:the_end" => "The End",
        _ => id,
    };

    void AddDimension(string folder, string id, string name)
    {
        if (!Directory.Exists(folder)) return;
        string region = Path.Combine(folder, "region");
        if (Directory.Exists(region))
        {
            bool anvil = Directory.EnumerateFiles(region, "*.mca").Any();
            bool mcr = Directory.EnumerateFiles(region, "*.mcr").Any();
            if (anvil || mcr)
            {
                // A converted world keeps its old .mcr files; the game uses .mca once they exist.
                var storage = anvil ? JavaStorage.Anvil : JavaStorage.McRegion;
                if (Storage == JavaStorage.Unknown || storage > Storage) Storage = storage;
                _dimensions.Add(new JavaRegionDimension(folder, id, name, anvil ? ".mca" : ".mcr"));
                return;
            }
        }
        if (AlphaDimension.HasChunks(folder))
        {
            if (Storage == JavaStorage.Unknown) Storage = JavaStorage.Alpha;
            _dimensions.Add(new AlphaDimension(folder, id, name));
        }
    }

    /// <summary>
    /// Reads the players again (after a player file was added or level.dat's host changed), keeping the records the pages
    /// already hold for the same files so no page edits a stale copy.
    /// </summary>
    public void ReloadPlayers()
    {
        var known = _players.ToDictionary(p => p.Source, StringComparer.OrdinalIgnoreCase);
        _players.Clear();
        LoadPlayers();
        for (int i = 0; i < _players.Count; i++)
            if (known.TryGetValue(_players[i].Source, out var had))
            {
                // level.dat's host is the compound the world holds: the kept record follows it.
                had.Data = _players[i].Data;
                _players[i] = had;
            }
    }

    void LoadPlayers()
    {
        if (Data?.GetCompound("Player") is NbtCompound local) _players.Add(new PlayerRecord("Singleplayer (level.dat)", Path.Combine(Folder, "level.dat"), local));
        foreach (var dirName in new[] { "playerdata", "players", Path.Combine("players", "data") })
        {
            string dir = Path.Combine(Folder, dirName);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.dat"))
            {
                try
                {
                    var file = NbtFile.Load(f);
                    if (file.Root is NbtCompound c) _players.Add(new PlayerRecord(Path.GetFileNameWithoutExtension(f), f, c));
                }
                catch { }
            }
        }
    }

    public override IEnumerable<(string Key, string Value)> Summary()
    {
        var d = Data;
        yield return ("Name", Name);
        yield return ("Format", FormatDescription);
        if (Seed is long seed) yield return ("Seed", seed.ToString());
        if (d == null) yield break;
        if (d.GetLongOrNull("GameType") is long gt) yield return ("Game mode", gt switch { 0 => "Survival", 1 => "Creative", 2 => "Adventure", 3 => "Spectator", _ => gt.ToString() });
        var ds = d.GetCompound("difficulty_settings");
        if (d.ContainsKey("hardcore") || ds?.ContainsKey("hardcore") == true) yield return ("Hardcore", (ds?.GetBool("hardcore") ?? d.GetBool("hardcore")) ? "Yes" : "No");
        if (d.GetLongOrNull("Difficulty") is long diff) yield return ("Difficulty", diff switch { 0 => "Peaceful", 1 => "Easy", 2 => "Normal", 3 => "Hard", _ => diff.ToString() });
        else if (ds?.GetString("difficulty") is string dname) yield return ("Difficulty", char.ToUpperInvariant(dname[0]) + dname[1..]);
        if (d.GetLongOrNull("LastPlayed") is long lp && lp > 0) yield return ("Last played", DateTimeOffset.FromUnixTimeMilliseconds(lp).LocalDateTime.ToString("g"));
        if (d.GetLongOrNull("Time") is long t) yield return ("World age", $"{t / 24000.0:0.#} days ({t:N0} ticks)");
        if (d.GetLongOrNull("SpawnX") is long sx) yield return ("Spawn", $"{sx}, {d.GetLong("SpawnY")}, {d.GetLong("SpawnZ")}");
        else if (d.GetCompound("spawn")?.GetInts("pos") is { Length: 3 } sp) yield return ("Spawn", $"{sp[0]}, {sp[1]}, {sp[2]}");
        yield return ("Dimensions", string.Join(", ", _dimensions.Select(x => x.DisplayName)));
        yield return ("Players", _players.Count.ToString());
    }

    public override void Save()
    {
        if (OpenedForRescue) throw new InvalidOperationException(RescueReadOnly);
        foreach (var d in _dimensions) if (d.IsModified) d.SaveChanges();
    }

    /// <summary>Writes the world's level.dat (settings and the single-player host); the previous one becomes level.dat_old, as the game does.</summary>
    public void SaveLevelDat()
    {
        if (OpenedForRescue) throw new InvalidOperationException(RescueReadOnly);
        if (Level == null) throw new InvalidOperationException("This world has no level.dat.");
        string path = Path.Combine(Folder, "level.dat");
        if (File.Exists(path)) File.Copy(path, path + "_old", overwrite: true);
        Level.Save(path);
    }
}
