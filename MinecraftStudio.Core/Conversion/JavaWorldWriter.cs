// MinecraftStudio.Core/Conversion/JavaWorldWriter.cs: Writes a Java Edition world of any era: Alpha chunk
// files, McRegion, numeric Anvil (1.2–1.12), palette Anvil (1.13–1.17) and the 1.18+ layout, including the 26.1
// folder layout.
// Owns: JavaWorldWriter.
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Writes a Java Edition world of any era: Alpha chunk files, McRegion, numeric Anvil (1.2–1.12), palette Anvil
/// (1.13–1.17) and the 1.18+ layout, including the 26.1 folder layout. Overworld chunks from 0–255 sources written for
/// 1.18+ targets are stored as 1.17.1 chunks so the game upgrades them itself (new terrain below y=0, blending).
/// </summary>
public sealed partial class JavaWorldWriter : IWorldWriter
{
    const int Upgrade117 = 2730; // 1.17.1

    readonly string _folder;
    readonly ConversionTarget _target;
    readonly ConversionOptions _options;
    readonly List<string> _notes = [];
    readonly Dictionary<string, RegionFile> _regions = new(StringComparer.OrdinalIgnoreCase);
    readonly LinkedList<string> _lru = new();
    string _dim = "minecraft:overworld";
    string _dimFolder = "";
    int _chunks;
    bool _notedCut;

    public JavaWorldWriter(string folder, ConversionTarget target, ConversionOptions options)
    {
        _folder = folder;
        _target = target;
        _options = options;
        Directory.CreateDirectory(folder);
    }

    int Dv => _target.DataVersion;
    /// <summary>
    /// The 26.1 folder layout is never written: level.dat uses the 1.20.4 layout, so 26.1+ moves the dimension folders
    /// itself while upgrading (the same path every older world takes).
    /// </summary>
    static bool NewLayout => false;
    public IReadOnlyList<string> Notes => _notes;

    public bool SupportsDimension(string id) => Supports(_target, id);

    public static bool Supports(ConversionTarget target, string id) => id switch
    {
        "minecraft:overworld" => true,
        "minecraft:the_nether" => target.JavaFormat != JavaFormat.Alpha || target.LegacyLevel >= LegacyLevel.Alpha_1_2,
        "minecraft:the_end" => target.JavaFormat >= JavaFormat.AnvilNumeric || target.LegacyLevel >= LegacyLevel.Release_1_0,
        _ => target.DataVersion >= 2566,
    };

    public void BeginDimension(string id)
    {
        _dim = id;
        if (NewLayout)
        {
            var (ns, path) = SplitId(id);
            _dimFolder = Path.Combine(_folder, "dimensions", ns, path);
        }
        else
        {
            _dimFolder = id switch
            {
                "minecraft:overworld" => _folder,
                "minecraft:the_nether" => Path.Combine(_folder, "DIM-1"),
                "minecraft:the_end" => Path.Combine(_folder, "DIM1"),
                _ => CustomFolder(id),
            };
        }
        Directory.CreateDirectory(_dimFolder);
    }

    /// <summary>A data-pack or mod dimension's folder: dimensions/namespace/path, under its name as written (see <see cref="OutputDimensionId"/>).</summary>
    string CustomFolder(string id)
    {
        var (ns, path) = CleanId(id);
        if ($"{ns}:{path}" != (id.Contains(':') ? id : "minecraft:" + id))
            _notes.Add($"The dimension {id} is saved as {ns}:{path} (dimension names are lower case since Java 1.16); the game shows it only with the mod or data pack that adds it.");
        return Path.Combine(new[] { _folder, "dimensions", ns }.Concat(path.Split('/')).ToArray());
    }

    /// <summary>
    /// The id a dimension has in the written world. Names since 1.16 are lower case letters, digits and _ - . / only, so an
    /// old mod dimension like DIM7 is written as minecraft:dim7; the vanilla three keep theirs.
    /// </summary>
    public static string OutputDimensionId(string id)
    {
        if (id is "minecraft:overworld" or "minecraft:the_nether" or "minecraft:the_end") return id;
        var (ns, path) = CleanId(id);
        return $"{ns}:{path}";
    }

    static (string Ns, string Path) CleanId(string id)
    {
        var (ns, path) = SplitId(id);
        static string Clean(string s) => string.Concat(s.ToLowerInvariant().Select(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.' or '/' ? ch : '_'));
        // A namespace is one folder (never "." or ".."); the path may have folders but none that climb out.
        string cleanNs = Clean(ns).Replace("/", "_"), cleanPath = Clean(path).Trim('/');
        if (cleanNs.Length == 0 || cleanNs.Trim('.').Length == 0) cleanNs = "minecraft";
        if (cleanPath.Length == 0 || cleanPath.Split('/').Any(p => p is "" or "." or ".."))
            cleanPath = "dimension_" + string.Concat(path.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return (cleanNs, cleanPath);
    }

    static (string Ns, string Path) SplitId(string id)
    {
        int c = id.IndexOf(':');
        return c < 0 ? ("minecraft", id) : (id[..c], id[(c + 1)..]);
    }

    bool IsOverworld => _dim == "minecraft:overworld";
    bool HasSky => _dim != "minecraft:the_nether";

    (int Min, int Max) SectionRange(int chunkDv) =>
        chunkDv >= 2844 && IsOverworld ? (-4, 19) : (0, _target.JavaFormat is JavaFormat.Alpha or JavaFormat.McRegion ? 7 : 15);

    public void WriteChunk(CanonicalChunk c)
    {
        // Empty chunks are written too: they were generated (e.g. End void), and a missing chunk would be generated anew.
        _chunks++;
        bool modernTarget = _target.JavaFormat == JavaFormat.AnvilContainers;
        bool keepsHeight = modernTarget && (!IsOverworld || c.HasExtendedHeight || !_options.LetGameExtendOldTerrain);
        if (!keepsHeight)
        {
            var (minS, maxS) = SectionRange(modernTarget ? Upgrade117 : Dv);
            var fitted = c.FitHeight(minS, maxS, bedrockFloor: _dim != "minecraft:the_end");
            if (fitted != c && !_notedCut) { _notes.Add($"Blocks outside y {minS * 16}..{maxS * 16 + 15} cannot be stored in {_target.Name}; they were cut and a bedrock floor was added where the ground continued down."); _notedCut = true; }
            c = fitted;
        }
        switch (_target.JavaFormat)
        {
            case JavaFormat.Alpha:
                {
                    var nbt = BuildFlat(c);
                    string path = AlphaDimension.ChunkPath(_dimFolder, c.X, c.Z);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var file = new NbtFile { Format = NbtFormat.Java, Compression = CompressionKind.Gzip };
                    file.Roots.Add(new NamedTag("", nbt));
                    file.Save(path);
                    return;
                }
            case JavaFormat.McRegion:
                PutChunk("region", ".mcr", c.X, c.Z, BuildFlat(c));
                return;
            case JavaFormat.AnvilNumeric:
                PutChunk("region", ".mca", c.X, c.Z, BuildAnvilNumeric(c));
                return;
            case JavaFormat.AnvilPalette:
                PutChunk("region", ".mca", c.X, c.Z, BuildPalette(c, Dv, Dv));
                WriteEntities(c, Dv);
                return;
            default:
                if (IsOverworld && !c.HasExtendedHeight && _options.LetGameExtendOldTerrain)
                {
                    // The game turns 1.17 chunks into 1.18 ones itself: new deepslate/caves below y=0 and smooth blending.
                    PutChunk("region", ".mca", c.X, c.Z, BuildPalette(c, Upgrade117, Dv));
                    WriteEntities(c, Upgrade117);
                }
                else
                {
                    PutChunk("region", ".mca", c.X, c.Z, BuildModern(c));
                    WriteEntities(c, Dv);
                }
                return;
        }
    }

    // ---------------------------------------------------------------- region files

    void PutChunk(string sub, string ext, int x, int z, NbtCompound nbt)
    {
        var region = Region(sub, ext, x >> 5, z >> 5);
        region.SetChunk(RegionFile.Index(x & 31, z & 31), nbt, CompressionKind.Zlib);
    }

    RegionFile Region(string sub, string ext, int rx, int rz)
    {
        string dir = Path.Combine(_dimFolder, sub);
        string path = Path.Combine(dir, $"r.{rx}.{rz}{ext}");
        if (_regions.TryGetValue(path, out var r))
        {
            _lru.Remove(path);
            _lru.AddFirst(path);
            return r;
        }
        Directory.CreateDirectory(dir);
        r = File.Exists(path) ? RegionFile.Open(path) : RegionFile.CreateEmpty(path, rx, rz);
        _regions[path] = r;
        _lru.AddFirst(path);
        while (_lru.Count > 6) Flush(_lru.Last!.Value);
        return r;
    }

    void Flush(string path)
    {
        if (_regions.Remove(path, out var r) && r.IsModified) r.Save(path, deleteIfEmpty: true);
        _lru.Remove(path);
    }

    public void EndDimension()
    {
        foreach (var path in _regions.Keys.ToList()) Flush(path);
    }

    // ---------------------------------------------------------------- block mapping

    BlockState MapFlattened(BlockState canonical, int chunkDv)
    {
        var s = JavaBlocks.ToVersion(canonical, Dv);
        return chunkDv < Dv ? JavaBlocks.Downgrade(s, chunkDv) : s;
    }

    static NbtCompound PaletteEntry(BlockState s, int dv)
    {
        var c = new NbtCompound();
        bool newKeys = dv >= 5009;
        c.SetString(newKeys ? "id" : "Name", s.Name);
        if (s.Properties.Length > 0)
        {
            var p = new NbtCompound();
            foreach (var (k, v) in s.PropertyPairs()) p.SetString(k, v);
            c[newKeys ? "properties" : "Properties"] = p;
        }
        return c;
    }

    // ---------------------------------------------------------------- block entities and entities

    NbtList BlockEntityList(CanonicalChunk c, int dv, Func<BlockState, BlockState> map)
    {
        var list = new NbtList(NbtType.Compound);
        if (!_options.ConvertBlockEntities) return list;
        foreach (var be in c.BlockEntities)
        {
            if (StateEdit.Local(c, be) is not var (x, y, z)) continue;
            var block = map(c.GetBlock(x, y, z));
            if (BlockEntities.ToJava(be, dv, block) is NbtCompound j) list.Add(j);
        }
        return list;
    }

    NbtList LegacyBlockEntityList(CanonicalChunk c)
    {
        var list = new NbtList(NbtType.Compound);
        var have = new HashSet<(int, int, int)>();
        if (_options.ConvertBlockEntities)
            foreach (var be in c.BlockEntities)
            {
                if (StateEdit.Local(c, be) is not var (x, y, z)) continue;
                var block = c.GetBlock(x, y, z);
                string id = be.GetString("id") ?? "";
                if (BlockEntities.ToLegacyJava(be, id, Dv, block) is NbtCompound j) { list.Add(j); have.Add((x, y, z)); }
            }
        // 1.12 keeps bed colours, banner bases, skull types, pot contents and notes in block entities.
        foreach (var (sy, s) in c.Sections)
        {
            if (!s.Palette.Any(p => BlockEntities.ImpliedForLegacyJava(p, 0, 0, 0) != null)) continue;
            for (int i = 0; i < 4096; i++)
            {
                var b = s.Indices == null ? s.Palette[0] : s.Palette[s.Indices[i]];
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                if (have.Contains((x, y, z))) continue;
                if (BlockEntities.ImpliedForLegacyJava(b, c.X * 16 + x, y, c.Z * 16 + z) is not NbtCompound implied) continue;
                if (BlockEntities.ToLegacyJava(implied, implied.GetString("id")!, Dv, b) is NbtCompound j) list.Add(j);
            }
        }
        return list;
    }

    NbtList EntityList(CanonicalChunk c, int dv)
    {
        var list = new NbtList(NbtType.Compound);
        if (!_options.ConvertEntities) return list;
        // Riders that get off (on what the version lacks, or past the first before 1.9) stand beside it.
        var setDown = new List<NbtCompound>();
        foreach (var e in c.Entities)
            if (Entities.ToJava(e, dv, setDown) is NbtCompound j) list.Add(j);
        foreach (var j in setDown) list.Add(j);
        return list;
    }

    void WriteEntities(CanonicalChunk c, int chunkDv)
    {
        if (chunkDv < 2681 || c.Entities.Count == 0 || !_options.ConvertEntities) return;
        var list = EntityList(c, chunkDv);
        if (list.Count == 0) return;
        var root = new NbtCompound();
        root.SetInt("DataVersion", chunkDv);
        root["Position"] = new NbtIntArray([c.X, c.Z]);
        root["Entities"] = list;
        PutChunk("entities", ".mca", c.X, c.Z, root);
    }

    // ---------------------------------------------------------------- level.dat

    public void Finish(WorldInfo info, IProgress<string>? progress = null)
    {
        EndDimension();
        progress?.Report("Writing level.dat");
        var data = JavaLevelData.Build(info, _target);
        var file = new NbtFile { Format = NbtFormat.Java, Compression = CompressionKind.Gzip };
        file.Roots.Add(new NamedTag("", new NbtCompound { { "Data", data } }));
        file.Save(Path.Combine(_folder, "level.dat"));
        if (_options.ConvertPlayers) JavaLevelData.WritePlayers(_folder, info, _target, data);
        Maps.WriteJava(_folder, info.Maps, _target, JavaLevelData.LevelDataVersion(_target));
        if (info.Maps.Count > 0) _notes.Add($"Converted {info.Maps.Count} map{(info.Maps.Count == 1 ? "" : "s")} (map art).");
        File.WriteAllBytes(Path.Combine(_folder, "session.lock"), Dv >= 2526 ? "☃"u8.ToArray() : BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Reverse().ToArray());
        if (_chunks > 0) _notes.Add($"Wrote {_chunks:N0} chunks.");
    }

    public void Dispose() => EndDimension();
}
