// MinecraftStudio.Core/Packs/TextureNames.cs: The names one texture has in every edition and version: its Java
// file names by version range (block/… from 1.13, blocks/… before), its tile in the old terrain.png atlas, and
// its Bedrock file names.
// Owns: TextureNames.
using System.Reflection;

namespace MinecraftStudio.Core.Packs;

/// <summary>
/// The names one texture has in every edition and version: its Java file names by version range (block/… from 1.13,
/// blocks/… before), its tile in the old terrain.png atlas, and its Bedrock file names. Built from the games' own files
/// (tools/texture-map) and shipped as Data/texture_names.csv. A texture is known by its shared name: the newest Java
/// name, or "bedrock:…" for one only Bedrock has.
/// </summary>
public sealed class TextureNames
{
    /// <summary>
    /// One name of a texture. For Bedrock files, <see cref="Role"/> marks derived pictures: "carried" (the inventory
    /// picture, sometimes the texture times one <see cref="Tint"/>) and "opaque" (leaves for fast graphics).
    /// </summary>
    public sealed record Row(string Shared, string Platform, string Name, string From, string To, string Evidence, string Note, string Role = "", string Tint = "");

    /// <summary>A Bedrock file a texture is written to, and how.</summary>
    public sealed record BedrockTarget(string Name, string Role, uint? Tint, string Evidence);

    readonly List<Row> _rows = [];
    readonly Dictionary<string, List<Row>> _byShared = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<Row>> _byJava = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<Row>> _byBedrock = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<int, List<Row>> _byAtlas = [];
    /// <summary>For each Bedrock file, the texture with the best claim on it (null when two claim it equally).</summary>
    Dictionary<string, string?>? _bedrockOwner;

    public IReadOnlyList<Row> Rows => _rows;
    public IEnumerable<string> SharedNames => _byShared.Keys;

    static readonly Lazy<TextureNames> _default = new(() =>
    {
        var asm = typeof(TextureNames).Assembly;
        using var s = asm.GetManifestResourceStream("MinecraftStudio.Data.texture_names.csv");
        return s == null ? new TextureNames() : Load(new StreamReader(s));
    });

    /// <summary>The table shipped with the program (empty if it is missing).</summary>
    public static TextureNames Default => _default.Value;

    public static TextureNames Load(TextReader reader)
    {
        var t = new TextureNames();
        string? header = reader.ReadLine();
        while (header != null && header.StartsWith('#')) header = reader.ReadLine();
        if (header == null) return t;
        var cols = SplitCsv(header).Select(c => c.Trim().ToLowerInvariant()).ToList();
        int Col(string name) => cols.IndexOf(name);
        int cShared = Col("canonical"), cPlatform = Col("platform"), cName = Col("name"), cFrom = Col("from"), cTo = Col("to"), cEvidence = Col("evidence"), cNote = Col("note"),
            cRole = Col("role"), cTint = Col("tint");
        if (cShared < 0 || cPlatform < 0 || cName < 0) throw new InvalidDataException("texture_names.csv needs canonical, platform and name columns.");
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var f = SplitCsv(line);
            string Get(int i) => i >= 0 && i < f.Count ? f[i].Trim() : "";
            var row = new Row(Get(cShared), Get(cPlatform).ToLowerInvariant(), Get(cName), Get(cFrom), Get(cTo), Get(cEvidence), Get(cNote), Get(cRole).ToLowerInvariant(), Get(cTint));
            if (row.Shared.Length == 0 || row.Name.Length == 0) continue;
            t.Add(row);
        }
        return t;
    }

    void Add(Row row)
    {
        _rows.Add(row);
        Bucket(_byShared, row.Shared).Add(row);
        switch (row.Platform)
        {
            case "java": Bucket(_byJava, row.Name).Add(row); break;
            case "bedrock": Bucket(_byBedrock, row.Name).Add(row); break;
            case "java_atlas":
                if (row.Name.StartsWith("terrain:") && int.TryParse(row.Name[8..], out int i)) Bucket(_byAtlas, i).Add(row);
                break;
        }
    }

    static List<Row> Bucket<TK>(Dictionary<TK, List<Row>> d, TK key) where TK : notnull
    {
        if (!d.TryGetValue(key, out var list)) d[key] = list = [];
        return list;
    }

    static bool InRange(Row r, JavaRelease release) =>
        VersionOrder.Overlap(r.From, r.To, release.First, release.Last);

    /// <summary>
    /// The Java file names (under textures/, without .png) of a texture for a release line. A line that spans a rename
    /// gets every name used in it (an extra file is harmless; a missing one shows the default texture).
    /// </summary>
    public List<string> JavaNames(string shared, JavaRelease release) =>
        _byShared.TryGetValue(shared, out var rows) ? rows.Where(r => r.Platform == "java" && InRange(r, release)).Select(r => r.Name).Distinct().ToList() : [];

    /// <summary>Whether the table knows this texture (also old-only "java:…" and Bedrock-only "bedrock:…" ones).</summary>
    public bool IsKnown(string shared) => _byShared.ContainsKey(shared);

    /// <summary>Whether a Java file name (under textures/, without .png) is in the table.</summary>
    public bool IsKnownJava(string name) => _byJava.ContainsKey(name);

    /// <summary>Whether a Bedrock file name (under textures/, without extension) is in the table.</summary>
    public bool IsKnownBedrock(string name) => _byBedrock.ContainsKey(name);

    /// <summary>
    /// Every texture a Java file stands for in a release: old versions used one file for several of today's textures
    /// (the command block's sides, the quartz block's top and bottom …).
    /// </summary>
    public List<string> FromJavaAll(string name, JavaRelease? release)
    {
        if (!_byJava.TryGetValue(name, out var rows)) return [];
        var fit = release == null ? [] : rows.Where(r => InRange(r, release)).Select(r => r.Shared).Distinct().ToList();
        return fit.Count > 0 ? fit : rows.Select(r => r.Shared).Distinct().ToList();
    }

    /// <summary>How sure the table is that a texture has this name (higher is surer).</summary>
    public int Confidence(string shared, string platform, string name) =>
        _byShared.TryGetValue(shared, out var rows) ? rows.Where(r => r.Platform == platform && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(r => EvidenceRank(r.Evidence)).DefaultIfEmpty(0).Max() : 0;

    /// <summary>The shared name of a Java texture file (the release helps where a name was reused), or null.</summary>
    public string? FromJava(string name, JavaRelease? release)
    {
        if (!_byJava.TryGetValue(name, out var rows)) return null;
        var fit = release == null ? null : rows.FirstOrDefault(r => InRange(r, release));
        return (fit ?? rows[^1]).Shared;
    }

    /// <summary>Bedrock file names (under textures/, without extension) of a texture, its main pictures first.</summary>
    public List<string> BedrockNames(string shared) =>
        _byShared.TryGetValue(shared, out var rows) ? rows.Where(r => r.Platform == "bedrock").OrderBy(r => r.Role.Length > 0).Select(r => r.Name).Distinct().ToList() : [];

    /// <summary>
    /// The Bedrock files a texture is written to: only files it has the best claim on (Bedrock reuses one picture for
    /// a few blocks that happened to look alike, and an edit of one must not change the other), main pictures first.
    /// </summary>
    public List<BedrockTarget> BedrockTargets(string shared)
    {
        if (!_byShared.TryGetValue(shared, out var rows)) return [];
        var owners = BedrockOwners();
        return rows.Where(r => r.Platform == "bedrock" && owners.TryGetValue(r.Name, out var o) && o == shared)
            .OrderBy(r => r.Role.Length > 0).ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => new BedrockTarget(r.Name, r.Role, ParseTint(r.Tint), r.Evidence))
            .DistinctBy(t => t.Name).ToList();
    }

    /// <summary>
    /// The texture a Bedrock file holds: the one with the best claim on it. Derived pictures (inventory and opaque
    /// ones) and files two textures claim equally have none.
    /// </summary>
    public string? FromBedrock(string name)
    {
        if (!BedrockOwners().TryGetValue(name, out var owner) || owner == null) return null;
        return _byBedrock[name].Any(r => r.Shared == owner && r.Role.Length > 0) ? null : owner;
    }

    /// <summary>Whether a Bedrock file is a derived picture (inventory or opaque) of a texture.</summary>
    public bool IsDerivedBedrock(string name) => _byBedrock.TryGetValue(name, out var rows) && rows.Any(r => r.Role.Length > 0);

    static uint? ParseTint(string tint) =>
        tint.Length == 7 && tint[0] == '#' && uint.TryParse(tint.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v | 0xFF000000u : null;

    static int EvidenceRank(string evidence) => evidence switch
    {
        "pixels+model" => 5,
        "model" or "manual" => 4,
        "pixels" => 3,
        _ => 1,
    };

    /// <summary>Which texture owns each Bedrock file: a file named like the texture wins, then the strongest evidence.</summary>
    Dictionary<string, string?> BedrockOwners()
    {
        if (_bedrockOwner != null) return _bedrockOwner;
        var owners = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, rows) in _byBedrock)
        {
            string file = name[(name.LastIndexOf('/') + 1)..];
            int Score(Row r)
            {
                string javaFile = r.Shared[(r.Shared.LastIndexOf('/') + 1)..];
                bool sameName = javaFile.Equals(file, StringComparison.OrdinalIgnoreCase) || r.Shared.StartsWith("bedrock:");
                return (sameName ? 10 : 0) + EvidenceRank(r.Evidence);
            }
            var best = rows.GroupBy(r => r.Shared).Select(g => (Shared: g.Key, Score: g.Max(Score))).OrderByDescending(x => x.Score).ToList();
            owners[name] = best.Count == 1 || best[0].Score > best[1].Score ? best[0].Shared : null;
        }
        return _bedrockOwner = owners;
    }

    /// <summary>The tile of terrain.png (Java up to 1.4.7) a texture fills, or null.</summary>
    public int? AtlasIndex(string shared, JavaRelease release)
    {
        if (!_byShared.TryGetValue(shared, out var rows)) return null;
        var r = rows.FirstOrDefault(r => r.Platform == "java_atlas" && InRange(r, release));
        return r != null && int.TryParse(r.Name.AsSpan(8), out int i) ? i : null;
    }

    public string? FromAtlas(int index, JavaRelease release) =>
        _byAtlas.TryGetValue(index, out var rows) ? (rows.FirstOrDefault(r => InRange(r, release)) ?? rows[^1]).Shared : null;

    /// <summary>Notes the table keeps about a texture on a platform (tinting, sizes …).</summary>
    public IEnumerable<string> NotesFor(string shared, string platform) =>
        _byShared.TryGetValue(shared, out var rows) ? rows.Where(r => r.Platform == platform && r.Note.Length > 0).Select(r => r.Note).Distinct() : [];

    /// <summary>A readable name for lists: "oak_planks" → "Oak planks"; Bedrock-only ones keep their file name.</summary>
    public static string Display(string shared)
    {
        string n = shared.StartsWith("bedrock:") ? shared[8..] : shared.StartsWith("java:") ? shared[5..] : shared;
        // Blocks and items can share a name (nether sprouts, kelp …): items say so.
        bool item = n.StartsWith("item/") || n.StartsWith("items/");
        n = n[(n.LastIndexOf('/') + 1)..].Replace('_', ' ');
        if (n.Length == 0) return shared;
        return char.ToUpperInvariant(n[0]) + n[1..] + (item ? " (item)" : "");
    }

    /// <summary>One CSV line: commas separate, double quotes protect commas and quotes ("").</summary>
    static List<string> SplitCsv(string line)
    {
        var list = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { list.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        list.Add(sb.ToString());
        return list;
    }
}
