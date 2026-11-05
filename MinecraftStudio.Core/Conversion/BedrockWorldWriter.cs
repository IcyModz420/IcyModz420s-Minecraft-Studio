// MinecraftStudio.Core/Conversion/BedrockWorldWriter.cs: Writes a Bedrock or Pocket Edition world of any
// LevelDB era: 1.18+ sub-chunks with Data3D biomes and actor storage, 1.13–1.17 state palettes, 1.2.13–1.12
// {name, val} palettes, 1.1–1.2 numeric sub-chunks and the 0.9–0.16 LegacyTerrain.
// Owns: BedrockWorldWriter.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.LevelDb;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Writes a Bedrock or Pocket Edition world of any LevelDB era: 1.18+ sub-chunks with Data3D biomes and actor storage,
/// 1.13–1.17 state palettes, 1.2.13–1.12 {name, val} palettes, 1.1–1.2 numeric sub-chunks and the 0.9–0.16
/// LegacyTerrain. Blocks, biomes and items are written the way the target version stores them: newer blocks first
/// become the closest block that version has, then the block-state upgrade schemas are undone. Overworld chunks from
/// 0–255 sources written for 1.18+ are stored as 1.17 chunks so the game extends them below y=0 itself.
/// </summary>
public sealed partial class BedrockWorldWriter : IWorldWriter
{
    const int OldChunkVersion = 22; // 1.16.210 – 1.17.x

    readonly string _folder;
    readonly ConversionTarget _target;
    readonly ConversionOptions _options;
    readonly LevelDbBuilder _db;
    readonly List<string> _notes = [];
    /// <summary>Older targets: the game version whose blocks, items and biomes are written; 0 = newest (nothing to undo).</summary>
    readonly int _game;
    readonly int _stateVersion;
    readonly Dictionary<int, Entry> _entries = [];
    readonly HashSet<string> _stoneFallbacks = [];
    int _dim;
    int _chunks;
    uint _actorCounter = 1; // 1 is the host player (HostId)
    Guid? _hostUuid;

    /// <summary>The host player's actor id, fixed so pets can point at the player before it is written.</summary>
    static long HostId => ((long)Session << 32) | 1;

    long NewActorId() => ((long)Session << 32) | ++_actorCounter;

    /// <summary>Pets of the host follow the converted host; other owners keep a stable id.</summary>
    long OwnerId(Guid owner) => _hostUuid == owner ? HostId : Entities.BedrockIdForUuid(owner);

    public void Begin(WorldInfo info) => _hostUuid = _options.ConvertPlayers ? info.Players.FirstOrDefault()?.Uuid : null;

    /// <summary>The Java content version of this target (mob and block entity types it has).</summary>
    int Content => _target.ContentDataVersion > 0 ? _target.ContentDataVersion : Terrain == BedrockTerrain.States ? int.MaxValue : 1343;
    const int Session = -1; // actor ids: (-1 << 32) | counter; level.dat worldStartCount keeps the game's ids below this

    /// <summary>How one canonical Java state is stored by this target.</summary>
    sealed record Entry(string Key, byte[] Palette, int Id, int Data, bool Water, bool Air);

    public BedrockWorldWriter(string folder, ConversionTarget target, ConversionOptions options)
    {
        _folder = folder;
        _target = target;
        _options = options;
        var v = target.BedrockVersion;
        _game = target.BedrockChunkVersion >= 42 || v.Length < 3 ? 0 : BedrockBlocks.MakeVersion(v[0], v[1], v[2], 0);
        _stateVersion = _game == 0 ? BedrockBlocks.CurrentVersion : BedrockBlocks.StateVersionFor(_game);
        Directory.CreateDirectory(folder);
        _db = new LevelDbBuilder(Path.Combine(folder, "db"));
    }

    public IReadOnlyList<string> Notes => _notes;

    BedrockTerrain Terrain => _target.BedrockTerrain;
    int ChunkVersion => _target.BedrockChunkVersion;
    bool ModernHeight => ChunkVersion >= 39;
    bool WaterLayer => Terrain is BedrockTerrain.States or BedrockTerrain.NameVal;

    public bool SupportsDimension(string id) => Supports(_target, id);

    /// <summary>Pocket Edition before 1.0 had no End.</summary>
    public static bool Supports(ConversionTarget target, string id) =>
        id is "minecraft:overworld" or "minecraft:the_nether" || id == "minecraft:the_end" && target.BedrockTerrain != BedrockTerrain.LegacyTerrain;

    public void BeginDimension(string id) => _dim = id switch { "minecraft:the_nether" => 1, "minecraft:the_end" => 2, _ => 0 };
    public void EndDimension() { }

    (int Min, int Max) Range(bool modern) => _dim switch
    {
        1 => (0, 7),
        2 => (0, Terrain == BedrockTerrain.LegacyTerrain ? 7 : 15),
        _ => modern ? (-4, 19) : (0, Terrain == BedrockTerrain.LegacyTerrain ? 7 : 15),
    };

    // ---------------------------------------------------------------- block mapping

    static readonly ConcurrentDictionary<int, (BlockState Block, bool Water)> StateCache = new();

    static bool InherentlyWet(BlockState j) => j.BaseName is "seagrass" or "tall_seagrass" or "kelp" or "kelp_plant" or "bubble_column";

    /// <summary>Newest Bedrock state for a canonical Java state, and whether the water layer must hold water.</summary>
    public static (BlockState Block, bool Water) ToBedrock(BlockState java) => StateCache.GetOrAdd(java.Id, _ =>
    {
        if (java.IsAir) return (BlockState.Get("minecraft:air"), false);
        // Frame blocks placed for Java item frame entities are already Bedrock states.
        if (java.Name is "minecraft:frame" or "minecraft:glow_frame") return (BedrockBlocks.Fit(java), false);
        bool water = java.GetProperty("waterlogged") == "true" || InherentlyWet(java);
        var s = java;
        for (int guard = 0; guard < 10; guard++)
        {
            if (s.IsAir) return (BlockState.Get("minecraft:air"), water);
            if (BedrockJavaMap.ToBedrock(s) is BlockState b) return (BedrockBlocks.Fit(b), water);
            var next = Substitutes.Next(s, BedrockJavaMap.HasJavaBlock, Substitutes.PlainCubes);
            if (next == null || next == s) break;
            s = next;
        }
        return (BlockState.Get("minecraft:stone"), water);
    });

    static readonly BlockState Water = BlockState.Get("minecraft:water", "liquid_depth=0");

    Entry Convert(BlockState java)
    {
        if (_entries.TryGetValue(java.Id, out var e)) return e;
        // Add-on blocks of a Bedrock world whose packs come along: written by name (default states).
        if (Terrain == BedrockTerrain.States && ConversionLog.Current?.KeepAddOns == true && !java.Name.StartsWith("minecraft:", StringComparison.Ordinal))
            return _entries[java.Id] = new Entry(java.Name, BedrockBlocks.PaletteEntryBytes(java.Name, new Dictionary<string, NbtTag>(), _stateVersion), 0, 0, false, false);
        bool frame = java.Name is "minecraft:frame" or "minecraft:glow_frame";
        bool water = WaterLayer && !java.IsAir && (java.GetProperty("waterlogged") == "true" || InherentlyWet(java));
        // Older targets: the closest block the matching Java version has (Bedrock gained blocks alongside Java).
        var content = java;
        if (!java.IsAir && !frame)
        {
            if (_target.ContentDataVersion > 0)
                content = JavaBlocks.Canonicalize(JavaBlocks.ToVersion(java, _target.ContentDataVersion), _target.ContentDataVersion);
            else if (Terrain is BedrockTerrain.NameValSingle or BedrockTerrain.Numeric or BedrockTerrain.LegacyTerrain)
            {
                // Before 1.4: the blocks of the matching Java release, through its numeric ids.
                var (id, data) = JavaBlocks.ToLegacy(java, _target.LegacyLevel);
                content = JavaBlocks.FromLegacy(id, data);
            }
        }
        e = Terrain switch
        {
            BedrockTerrain.States => StatesEntry(content, water),
            _ => LegacyEntry(content, water),
        };
        _entries[java.Id] = e;
        return e;
    }

    Entry StatesEntry(BlockState java, bool water)
    {
        var current = ToBedrock(java).Block;
        bool air = current.Name == "minecraft:air";
        if (_game == 0) return new Entry(current.ToString(), BedrockBlocks.PaletteEntryBytes(current), 0, 0, water, air);
        var s = java;
        for (int guard = 0; guard < 8; guard++)
        {
            if (DowngradeOrClosest(ToBedrock(s).Block) is var (name, states))
                return new Entry(name + "|" + string.Join(",", states.OrderBy(k => k.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + BedrockBlocks.TagText(kv.Value))),
                    BedrockBlocks.PaletteEntryBytes(name, states, _stateVersion), 0, 0, water, air);
            var next = Substitutes.Next(s, n => DowngradeOrClosest(ToBedrock(BlockState.Get(n)).Block) != null, Substitutes.PlainCubes);
            if (next == null || next == s) break;
            s = next;
        }
        _stoneFallbacks.Add(current.Name);
        var stone = DowngradeOrClosest(BlockState.Get("minecraft:stone")) ?? ("minecraft:stone", new Dictionary<string, NbtTag>());
        return new Entry("minecraft:stone", BedrockBlocks.PaletteEntryBytes(stone.Name, stone.States, _stateVersion), 0, 0, water, false);
    }

    /// <summary>The downgraded state, or the nearest other state of the same block that has an older form.</summary>
    (string Name, Dictionary<string, NbtTag> States)? DowngradeOrClosest(BlockState current)
    {
        if (BedrockBlocks.Downgrade(current, _game) is { } d) return d;
        var want = current.PropertyPairs().ToList();
        foreach (var s in BedrockBlocks.StatesOf(current.Name).OrderByDescending(s => s.PropertyPairs().Count(want.Contains)))
            if (BedrockBlocks.Downgrade(s, _game) is { } d2) return d2;
        return null;
    }

    /// <summary>Numeric targets: the 1.12 id/meta (and the {name, val} name); ids above 255 do not fit before 1.2.13.</summary>
    (int Id, int Meta, string Name)? Legacy(BlockState java)
    {
        var current = ToBedrock(java).Block;
        if (current.Name == "minecraft:air") return (0, 0, "minecraft:air");
        if (BedrockBlocks.ToLegacy(current) is not var (id, meta, name)) return null;
        if (Terrain is BedrockTerrain.Numeric or BedrockTerrain.LegacyTerrain && id > 255) return null;
        return (id, meta, name);
    }

    Entry LegacyEntry(BlockState java, bool water)
    {
        var s = java;
        for (int guard = 0; guard < 8; guard++)
        {
            if (Legacy(s) is var (id, meta, name))
                return new Entry(name + ":" + meta, Terrain is BedrockTerrain.NameVal or BedrockTerrain.NameValSingle ? BedrockBlocks.NameValEntryBytes(name, meta) : [],
                    id, meta, water, id == 0);
            var next = Substitutes.Next(s, n => Legacy(BlockState.Get(n)) != null, Substitutes.PlainCubes);
            if (next == null || next == s) break;
            s = next;
        }
        _stoneFallbacks.Add(java.Name);
        return new Entry("minecraft:stone:0", BedrockBlocks.NameValEntryBytes("minecraft:stone", 0), 1, 0, water, false);
    }

    Entry WaterEntry() => Terrain == BedrockTerrain.States
        ? (_game == 0
            ? new Entry("water", BedrockBlocks.PaletteEntryBytes(Water), 0, 0, false, false)
            : DowngradeOrClosest(Water) is var (n, st)
                ? new Entry("water", BedrockBlocks.PaletteEntryBytes(n, st, _stateVersion), 0, 0, false, false)
                : new Entry("water", BedrockBlocks.PaletteEntryBytes(Water), 0, 0, false, false))
        : new Entry("water", BedrockBlocks.NameValEntryBytes("minecraft:water", 0), 9, 0, false, false);

    Entry? _air, _water;
    Entry AirEntry => _air ??= Convert(BlockState.Get("minecraft:air"));
    Entry WaterLayerEntry => _water ??= WaterEntry();

    // ---------------------------------------------------------------- level.dat and players

    public void Finish(WorldInfo info, IProgress<string>? progress = null)
    {
        if (_options.ConvertPlayers && info.Players.Count > 0)
        {
            var player = Players.ToBedrock(info.Players[0], HostId);
            DowngradeItems(player);
            _db.Put(Encoding.ASCII.GetBytes("~local_player"), NbtBinary.Serialize(player, NbtFormat.Bedrock));
        }
        foreach (var (key, value) in Maps.BedrockRecords(info.Maps)) _db.Put(key, value);
        if (info.Maps.Count > 0) _notes.Add($"Converted {info.Maps.Count} map{(info.Maps.Count == 1 ? "" : "s")} (map art).");
        progress?.Report("Writing the Bedrock database");
        _db.Finish(progress);
        var level = BedrockLevelData.Build(info, _target);
        var file = new NbtFile { Format = NbtFormat.Bedrock, HeaderVersion = _target.BedrockStorageVersion };
        file.Roots.Add(new NamedTag("", level));
        file.Save(Path.Combine(_folder, "level.dat"));
        File.WriteAllText(Path.Combine(_folder, "levelname.txt"), info.Name);
        _notes.Add($"Wrote {_chunks:N0} chunks and {_db.RecordCount:N0} database records.");
        if (_game != 0) _notes.Add($"Blocks, items and biomes were written the way {_target.VersionName} stores them; newer blocks became the closest block that version has.");
        if (_stoneFallbacks.Count > 0)
            _notes.Add($"{_stoneFallbacks.Count} block type{(_stoneFallbacks.Count == 1 ? "" : "s")} had no counterpart and became stone: {string.Join(", ", _stoneFallbacks.Take(8))}{(_stoneFallbacks.Count > 8 ? "…" : "")}");
        if (Terrain is BedrockTerrain.Numeric or BedrockTerrain.LegacyTerrain && _options.ConvertEntities)
            _notes.Add("Versions before 1.6 stored mobs by number; converted mobs may not appear there.");
    }

    public void Dispose() => _db.Dispose();
}
