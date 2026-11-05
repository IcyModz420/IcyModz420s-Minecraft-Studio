// MinecraftStudio.Core/Bedrock/BedrockWorld.cs: A Bedrock Edition world folder (level.dat + LevelDB "db"
// folder), Pocket Edition 0.9 onwards.
// Owns: BedrockWorld.
using System.Buffers.Binary;
using System.Text;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.LevelDb;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Bedrock;

/// <summary>A Bedrock Edition world folder (level.dat + LevelDB "db" folder), Pocket Edition 0.9 onwards.</summary>
public sealed class BedrockWorld : SaveWorld
{
    readonly List<WorldDimension> _dims = [];
    readonly List<PlayerRecord> _players = [];
    readonly Dictionary<int, Dictionary<ChunkPos, List<byte[]>>> _chunkKeys = [];

    BedrockWorld(string folder, NbtFile level, LevelDatabase db)
    {
        Folder = folder;
        Level = level;
        Db = db;
    }

    public string Folder { get; }
    public NbtFile Level { get; }
    public LevelDatabase Db { get; }
    public NbtCompound LevelRoot => Level.RootCompound;

    public override Edition Edition => Edition.Bedrock;
    public override string Name
    {
        get
        {
            string txt = Path.Combine(Folder, "levelname.txt");
            string? fromDat = LevelRoot.GetString("LevelName");
            if (!string.IsNullOrWhiteSpace(fromDat)) return fromDat;
            return File.Exists(txt) ? File.ReadAllText(txt).Trim() : Path.GetFileName(Folder);
        }
    }
    public override string Location => Folder;
    public override NbtFile? LevelData => Level;
    public override IReadOnlyList<WorldDimension> Dimensions => _dims;
    public override IReadOnlyList<PlayerRecord> Players => _players;
    public override bool IsModified => Db.HasChanges;

    public string? LastOpenedVersion => LevelRoot.GetList("lastOpenedWithVersion") is NbtList l && l.Count > 0
        ? string.Join(".", l.Select(t => t.IsNumeric ? t.AsLong : 0).Take(l.Count >= 5 && l[4].AsLong == 0 ? 4 : 5)) : null;

    public override string FormatDescription
    {
        get
        {
            string ver = LastOpenedVersion ?? "(version unknown)";
            int storage = (int)LevelRoot.GetLong("StorageVersion", Level.HeaderVersion ?? 0);
            return $"Bedrock {ver} (LevelDB, storage version {storage})";
        }
    }

    public static bool LooksLikeBedrockWorld(string folder) =>
        File.Exists(Path.Combine(folder, "level.dat")) && Directory.Exists(Path.Combine(folder, "db")) && File.Exists(Path.Combine(folder, "db", "CURRENT"));

    /// <summary>
    /// Opens a damaged world as far as it can be read: level.dat from level.dat_old when it is damaged, or a minimal
    /// one; a database whose index (CURRENT, MANIFEST) is damaged, or that has damaged tables, read file by file.
    /// <paramref name="findings"/> says what was damaged and what was used instead. The database is only read.
    /// </summary>
    public static BedrockWorld OpenForRescue(string folder, List<string> findings, IProgress<string>? progress = null)
    {
        NbtFile? level = null;
        foreach (var name in new[] { "level.dat", "level.dat_old" })
        {
            string path = Path.Combine(folder, name);
            if (!File.Exists(path)) { if (name == "level.dat") findings.Add("level.dat is missing."); continue; }
            try
            {
                level = NbtFile.Load(path);
                _ = level.RootCompound;
                if (name != "level.dat") findings.Add("The world's settings were taken from level.dat_old, the copy the game keeps of level.dat.");
                break;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or SaveFormatException or ArgumentException or EndOfStreamException or UnauthorizedAccessException)
            {
                level = null;
                findings.Add($"{name} is damaged ({World.WorldRescue.Describe(e)}).");
            }
        }
        if (level == null)
        {
            // Only the name and storage version; everything else starts from the game's defaults.
            var root = new NbtCompound();
            string txt = Path.Combine(folder, "levelname.txt");
            root.SetString("LevelName", File.Exists(txt) && File.ReadAllText(txt).Trim() is { Length: > 0 } n ? n : Path.GetFileName(folder));
            root.SetInt("StorageVersion", 10);
            level = new NbtFile(root, NbtFormat.Bedrock) { HeaderVersion = 10 };
            findings.Add("No readable level.dat: the world's seed, spawn and rules start from defaults in the rescued world; the terrain does not depend on them.");
        }
        string dbDir = Path.Combine(folder, "db");
        LevelDatabase db;
        try
        {
            db = LevelDatabase.Open(dbDir, progress);
            if (db.UnreadableTables > 0 || db.CorruptLogFragments > 0)
            {
                // Parts are damaged: every file read directly can find records the index lost track of.
                var scan = LevelDatabase.OpenScan(dbDir, progress);
                if (scan.Count > db.Count)
                {
                    findings.Add($"Parts of the database are damaged: reading every file directly found {scan.Count - db.Count:N0} more records than its index.");
                    db.Dispose();
                    db = scan;
                }
                else scan.Dispose();
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or SaveFormatException or KeyNotFoundException or ArgumentException or EndOfStreamException or FormatException)
        {
            findings.Add($"The world's database index is damaged ({World.WorldRescue.Describe(e)}): every data file was read directly instead.");
            db = LevelDatabase.OpenScan(dbDir, progress);
        }
        foreach (var w in db.Warnings.Take(20)) findings.Add("Database: " + w);
        if (db.Warnings.Count > 20) findings.Add($"Database: {db.Warnings.Count - 20} more damaged parts.");
        if (db.CorruptLogFragments > 0) findings.Add($"{db.CorruptLogFragments} damaged piece(s) of the database's log were skipped: the last changes written there may be missing.");
        db.ReadOnly = true;
        var world = new BedrockWorld(folder, level, db) { OpenedForRescue = true };
        world.Index();
        return world;
    }

    public static BedrockWorld Open(string folder, IProgress<string>? progress = null)
    {
        var level = NbtFile.Load(Path.Combine(folder, "level.dat"));
        var db = LevelDatabase.Open(Path.Combine(folder, "db"), progress);
        var w = new BedrockWorld(folder, level, db);
        w.Index();
        return w;
    }

    void Index()
    {
        _chunkKeys.Clear();
        var generated = new Dictionary<int, HashSet<ChunkPos>>();
        foreach (var key in Db.Keys())
        {
            if (BedrockKeys.TryParseChunkKey(key, out var ck))
            {
                if (!_chunkKeys.TryGetValue(ck.Dimension, out var dim)) _chunkKeys[ck.Dimension] = dim = [];
                if (!dim.TryGetValue(ck.Pos, out var list)) dim[ck.Pos] = list = [];
                list.Add(key);
                // A chunk counts as generated only when it has a version record; others are structure pre-placement data.
                if (ck.IsVersion || ck.Tag == ChunkTag.LegacyTerrain)
                {
                    if (!generated.TryGetValue(ck.Dimension, out var g)) generated[ck.Dimension] = g = [];
                    g.Add(ck.Pos);
                }
            }
        }
        // Keep existing dimension objects (the UI holds them) and refresh their indexes in place.
        foreach (var d in _chunkKeys.Keys.OrderBy(d => d))
        {
            var existing = _dims.OfType<BedrockDimension>().FirstOrDefault(x => x.Dimension == d);
            if (existing != null) { existing.Reset(_chunkKeys[d], generated.GetValueOrDefault(d) ?? []); continue; }
            string id = d switch { 0 => "minecraft:overworld", 1 => "minecraft:the_nether", 2 => "minecraft:the_end", _ => d.ToString() };
            string name = d switch { 0 => "Overworld", 1 => "The Nether", 2 => "The End", _ => $"Dimension {d}" };
            _dims.Add(new BedrockDimension(this, d, id, name, _chunkKeys[d], generated.GetValueOrDefault(d) ?? []));
        }
        foreach (var dim in _dims.OfType<BedrockDimension>().Where(x => !_chunkKeys.ContainsKey(x.Dimension))) dim.Reset([], []);
        _dims.Sort((a, b) => ((BedrockDimension)a).Dimension.CompareTo(((BedrockDimension)b).Dimension));
        LoadPlayers();
    }

    // A player already loaded keeps its record (the pages hold it, and its data is what was just saved: players are only
    // written from their records), so a save never leaves one page editing a copy that another page replaced. The list
    // itself changes only when players come or go, so saving players one by one while going through the list is safe.
    void LoadPlayers()
    {
        var known = new Dictionary<string, PlayerRecord>(StringComparer.Ordinal);
        foreach (var p in _players) known.TryAdd(p.Source, p);
        var list = new List<PlayerRecord>();
        foreach (var key in Db.Keys())
        {
            if (!BedrockKeys.IsPrintable(key)) continue;
            string k = Encoding.ASCII.GetString(key);
            if (k != "~local_player" && !k.StartsWith("player_", StringComparison.Ordinal)) continue;
            if (known.TryGetValue(k, out var had)) { list.Add(had); continue; }
            try
            {
                var value = Db.Get(key);
                if (value == null) continue;
                var c = NbtBinary.ReadCompound(value, NbtFormat.Bedrock);
                list.Add(new PlayerRecord(k == "~local_player" ? "Local player" : k, k, c));
            }
            catch { }
        }
        if (list.SequenceEqual(_players)) return;
        _players.Clear();
        _players.AddRange(list);
    }

    /// <summary>Reads a database value as one Bedrock NBT compound.</summary>
    public NbtCompound? ReadNbt(string key)
    {
        var v = Db.Get(Encoding.ASCII.GetBytes(key));
        return v == null ? null : NbtBinary.ReadCompound(v, NbtFormat.Bedrock);
    }

    public void WriteNbt(string key, NbtCompound value) => Db.Put(Encoding.ASCII.GetBytes(key), NbtBinary.Serialize(value, NbtFormat.Bedrock));

    public void SavePlayer(PlayerRecord p) => Db.Put(Encoding.ASCII.GetBytes(p.Source), NbtBinary.Serialize(p.Data, NbtFormat.Bedrock));

    public override IEnumerable<(string Key, string Value)> Summary()
    {
        var d = LevelRoot;
        yield return ("Name", Name);
        yield return ("Format", FormatDescription);
        if (d.GetLongOrNull("RandomSeed") is long seed) yield return ("Seed", seed.ToString());
        if (d.GetLongOrNull("GameType") is long gt) yield return ("Game mode", gt switch { 0 => "Survival", 1 => "Creative", 2 => "Adventure", 3 => "Spectator", 5 => "Default", _ => gt.ToString() });
        if (d.GetLongOrNull("Difficulty") is long diff) yield return ("Difficulty", diff switch { 0 => "Peaceful", 1 => "Easy", 2 => "Normal", 3 => "Hard", _ => diff.ToString() });
        if (d.ContainsKey("IsHardcore")) yield return ("Hardcore", d.GetBool("IsHardcore") ? "Yes" : "No");
        if (d.GetLongOrNull("LastPlayed") is long lp && lp > 0) yield return ("Last played", DateTimeOffset.FromUnixTimeSeconds(lp).LocalDateTime.ToString("g"));
        if (d.GetLongOrNull("Time") is long t) yield return ("World age", $"{t / 24000.0:0.#} days ({t:N0} ticks)");
        if (d.GetLongOrNull("SpawnX") is long sx) yield return ("Spawn", $"{sx}, {(d.GetLong("SpawnY") == 32767 ? "surface" : d.GetLong("SpawnY").ToString())}, {d.GetLong("SpawnZ")}");
        yield return ("Database", $"{Db.Count:N0} records, {Db.Manifest.Files.Count} tables, compression {string.Join("/", Db.CompressionCounts.Keys)}");
        yield return ("Dimensions", string.Join(", ", _dims.Select(x => $"{x.DisplayName} ({x.ChunkPositions.Count:N0} chunks)")));
        yield return ("Players", _players.Count.ToString());
        if (Db.Warnings.Count > 0) yield return ("Warnings", string.Join("; ", Db.Warnings.Take(5)));
    }

    public void SaveLevelDat()
    {
        if (OpenedForRescue) throw new InvalidOperationException(RescueReadOnly);
        string path = Path.Combine(Folder, "level.dat");
        Level.Save(path);
        string name = LevelRoot.GetString("LevelName") ?? "";
        File.WriteAllText(Path.Combine(Folder, "levelname.txt"), name);
    }

    public override void Save() => Save(CommitMode.AppendLog);

    public void Save(CommitMode mode, IProgress<string>? progress = null)
    {
        Db.Commit(mode, progress);
        Index();
    }

    public override void Dispose()
    {
        Db.Dispose();
        base.Dispose();
    }

    internal Dictionary<ChunkPos, List<byte[]>> KeysOf(int dim) => _chunkKeys.TryGetValue(dim, out var d) ? d : [];
}
