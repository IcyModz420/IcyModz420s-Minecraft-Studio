// MinecraftStudio.Core/Packs/InstalledMods.cs: the Java mods on this PC — every mod jar in the game folders (the official
// launcher's, TLauncher's, every Prism, MultiMC, CurseForge, Modrinth, GDLauncher and ATLauncher instance) and in any
// other mods folder given (a server's), with what each jar says about itself.
// Owns: InstalledMod, InstalledMods. A jar is only read, never run or unpacked: fabric.mod.json (Fabric), quilt.mod.json
// (Quilt), META-INF/neoforge.mods.toml (NeoForge), META-INF/mods.toml (Forge 1.13+), mcmod.info (Forge 1.12 and older),
// and the namespaces in its assets/ and data/ folders — the names its blocks, items and mobs carry in a world.
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MinecraftStudio.Core.Packs;

/// <summary>
/// One mod in a jar: its id, name, version, loader and the game version it asks for; the jar, the game folder whose
/// mods folder holds it and that folder's launcher label; the namespaces of its assets (Namespaces) and those only in
/// its data (DataNamespaces); Disabled for a jar a launcher switched off (".jar.disabled").
/// </summary>
public sealed record InstalledMod(string Id, string Name, string Version, string Loader, string? GameVersion, string File, string GameFolder,
    string Where, IReadOnlyList<string> Namespaces, IReadOnlyList<string> DataNamespaces, bool Disabled)
{
    /// <summary>"Oh The Biomes You'll Go 2.0.0.7 (Fabric, for 1.19)".</summary>
    public string Describe()
    {
        var about = new List<string> { Loader };
        if (GameVersion is { Length: > 0 } g) about.Add("for " + g);
        if (Disabled) about.Add("switched off");
        return $"{Name}{(Version.Length > 0 ? " " + Version : "")} ({string.Join(", ", about)})";
    }

    /// <summary>How well it answers for a namespace: 0 it is the mod's id, 1 its assets, 2 only its data, -1 not at all.</summary>
    public int Match(string ns) => string.Equals(Id, ns, StringComparison.OrdinalIgnoreCase) ? 0 : Namespaces.Contains(ns) ? 1 : DataNamespaces.Contains(ns) ? 2 : -1;
}

public static class InstalledMods
{
    /// <summary>The most jars read in one mods folder, and the most bytes of one file read out of a jar.</summary>
    const int MaxJars = 3000, MaxFile = 1 << 20;

    // A jar is read once while it stays the same (path, size and time).
    static readonly ConcurrentDictionary<(string Path, long Length, DateTime Time), List<InstalledMod>> Cache = new();

    /// <summary>
    /// Every mod in the mods folders of this PC's Java game folders and of the extra game folders given (label, folder —
    /// a server folder, the folder a world was found in), each jar once. Damaged jars are passed over.
    /// </summary>
    public static List<InstalledMod> Find(IEnumerable<(string Label, string Folder)>? extra = null, CancellationToken cancel = default)
    {
        var list = new List<InstalledMod>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The launchers' folders first, so a jar is named after its launcher; then folders only the caller knows (a server's).
        foreach (var (label, folder) in GameFolders.Java().Concat(extra ?? []))
        {
            string mods = Path.Combine(folder, "mods");
            if (!Directory.Exists(mods)) continue;
            foreach (var jar in Jars(mods))
            {
                cancel.ThrowIfCancellationRequested();
                if (seen.Add(Path.GetFullPath(jar))) list.AddRange(Read(jar, folder, label));
            }
        }
        return list;
    }

    /// <summary>The jars of a mods folder and of the folders right inside it (Forge's mods/1.12.2/), switched-off ones too.</summary>
    static IEnumerable<string> Jars(string mods)
    {
        IEnumerable<string> Files(string dir)
        {
            try { return Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
        }
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(mods).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { dirs = []; }
        return Files(mods).Concat(dirs.SelectMany(Files)).Take(MaxJars);
    }

    /// <summary>The mods one jar holds (most hold one; a Forge jar can declare several). None when it is not a mod.</summary>
    public static List<InstalledMod> Read(string jar, string gameFolder, string where)
    {
        try
        {
            var info = new FileInfo(jar);
            var key = (info.FullName, info.Length, info.LastWriteTimeUtc);
            if (Cache.TryGetValue(key, out var known)) return known;
            var mods = ReadJar(jar, gameFolder, where);
            Cache[key] = mods;
            return mods;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException) { return []; }
    }

    static List<InstalledMod> ReadJar(string jar, string gameFolder, string where)
    {
        using var zip = ZipFile.OpenRead(jar);
        var assets = new SortedSet<string>(StringComparer.Ordinal);
        var data = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var e in zip.Entries)
        {
            string name = e.FullName.Replace('\\', '/');
            if (Namespace(name, "assets/") is string a) assets.Add(a);
            else if (Namespace(name, "data/") is string d) data.Add(d);
        }
        data.ExceptWith(assets);
        string? manifest = Text(zip, "META-INF/MANIFEST.MF");
        string? jarVersion = manifest == null ? null : Regex.Match(manifest, @"^Implementation-Version:\s*(\S+)", RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;
        bool disabled = jar.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var found = new List<(string Id, string Name, string Version, string Loader, string? Game)>();
        if (Text(zip, "fabric.mod.json") is string fabric) found.AddRange(Fabric(fabric));
        if (Text(zip, "quilt.mod.json") is string quilt) found.AddRange(Quilt(quilt));
        if (Text(zip, "META-INF/neoforge.mods.toml") is string neo) found.AddRange(ModsToml(neo, "NeoForge"));
        else if (Text(zip, "META-INF/mods.toml") is string toml) found.AddRange(ModsToml(toml, "Forge"));
        if (found.Count == 0 && Text(zip, "mcmod.info") is string mcmod) found.AddRange(McmodInfo(mcmod));
        var list = new List<InstalledMod>();
        foreach (var (id, name, version, loader, game) in found.DistinctBy(f => (f.Id, f.Loader)))
        {
            // Placeholders the build was meant to fill: the jar's own version, if its manifest says.
            string v = version.Contains("${", StringComparison.Ordinal) ? jarVersion ?? "" : version;
            list.Add(new InstalledMod(id, name.Length > 0 ? name : id, v, loader, game, jar, gameFolder, where, assets.ToList(), data.ToList(), disabled));
        }
        return list;
    }

    // "assets/byg/textures/…" → "byg" (the game's own and nonsense names left out).
    static string? Namespace(string entry, string top)
    {
        if (!entry.StartsWith(top, StringComparison.Ordinal)) return null;
        int end = entry.IndexOf('/', top.Length);
        if (end <= top.Length) return null;
        string ns = entry[top.Length..end];
        return ns != "minecraft" && IsNamespace(ns) ? ns : null;
    }

    /// <summary>A name the game accepts as a namespace: a–z, 0–9, _ - . only.</summary>
    public static bool IsNamespace(string ns) => ns.Length is > 0 and <= 64 && ns.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.');

    static string? Text(ZipArchive zip, string path)
    {
        var e = zip.GetEntry(path);
        if (e == null || e.Length > MaxFile) return null;
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return Encoding.UTF8.GetString(ms.ToArray()).TrimStart('﻿');
    }

    static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    // Some mods' files are not quite JSON (a line break inside a description): the fields are then picked out one by one.
    static string Field(string json, string key) => Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"") is { Success: true } m ? Unescape(m.Groups[1].Value) : "";

    static string Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // The versions a "depends" entry allows, as written ("1.19.x", ">=1.19", either of two).
    static string? Versions(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Array => string.Join(" or ", v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString())),
        _ => null,
    };

    static IEnumerable<(string, string, string, string, string?)> Fabric(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, Lenient);
            var root = doc.RootElement;
            string id = Str(root, "id");
            if (id.Length == 0) return [];
            string? game = root.TryGetProperty("depends", out var deps) && deps.ValueKind == JsonValueKind.Object && deps.TryGetProperty("minecraft", out var mc) ? Versions(mc) : null;
            return [(id, Str(root, "name"), Str(root, "version"), "Fabric", game)];
        }
        catch (JsonException)
        {
            string id = Field(json, "id");
            return id.Length == 0 ? [] : [(id, Field(json, "name"), Field(json, "version"), "Fabric", null)];
        }
    }

    static IEnumerable<(string, string, string, string, string?)> Quilt(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, Lenient);
            if (!doc.RootElement.TryGetProperty("quilt_loader", out var q)) return [];
            string id = Str(q, "id");
            if (id.Length == 0) return [];
            string name = q.TryGetProperty("metadata", out var meta) ? Str(meta, "name") : "";
            string? game = null;
            if (q.TryGetProperty("depends", out var deps) && deps.ValueKind == JsonValueKind.Array)
                foreach (var d in deps.EnumerateArray())
                    if (d.ValueKind == JsonValueKind.Object && Str(d, "id") == "minecraft" && d.TryGetProperty("versions", out var v)) game = Versions(v);
            return [(id, name, Str(q, "version"), "Quilt", game)];
        }
        catch (JsonException) { return []; }
    }

    static IEnumerable<(string, string, string, string, string?)> McmodInfo(string json)
    {
        var list = new List<(string, string, string, string, string?)>();
        try
        {
            using var doc = JsonDocument.Parse(json, Lenient);
            var root = doc.RootElement;
            var mods = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("modList", out var ml) && ml.ValueKind == JsonValueKind.Array ? ml : default;
            if (mods.ValueKind != JsonValueKind.Array) return list;
            foreach (var m in mods.EnumerateArray())
            {
                string id = Str(m, "modid");
                if (id.Length > 0) list.Add((id, Str(m, "name"), Str(m, "version"), "Forge", Str(m, "mcversion") is { Length: > 0 } g && !g.Contains("${", StringComparison.Ordinal) ? g : null));
            }
        }
        catch (JsonException)
        {
            string id = Field(json, "modid");
            if (id.Length > 0) list.Add((id, Field(json, "name"), Field(json, "version"), "Forge", null));
        }
        return list;
    }

    /// <summary>
    /// The mods a mods.toml declares ([[mods]] modId, version, displayName) and the game versions its minecraft dependency
    /// allows. Only the plain key = "value" lines this needs are read; multi-line text is passed over.
    /// </summary>
    internal static List<(string, string, string, string, string?)> ModsToml(string text, string loader)
    {
        var mods = new List<Dictionary<string, string>>();
        var games = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, string>? table = null;
        string? dependencyOf = null;
        var lines = text.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line[0] == '[')
            {
                string header = line.Trim('[', ']', ' ');
                dependencyOf = null;
                if (line.StartsWith("[[", StringComparison.Ordinal) && header == "mods") mods.Add(table = new Dictionary<string, string>(StringComparer.Ordinal));
                else if (header.StartsWith("dependencies.", StringComparison.Ordinal)) { table = new Dictionary<string, string>(StringComparer.Ordinal); dependencyOf = header["dependencies.".Length..].Trim('"', '\''); }
                else table = null;
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim(), value = line[(eq + 1)..].Trim();
            if (value.StartsWith("\"\"\"", StringComparison.Ordinal) || value.StartsWith("'''", StringComparison.Ordinal))
            {
                // Multi-line text: up to the line that closes it.
                string close = value[..3];
                if (value.Length < 6 || !value[3..].Contains(close, StringComparison.Ordinal))
                    while (i + 1 < lines.Length && !lines[++i].Contains(close, StringComparison.Ordinal)) { }
                continue;
            }
            if (table == null) continue;
            if (Unquote(value) is not string v) continue;
            table[key] = v;
            if (dependencyOf != null && table.TryGetValue("modId", out var dep) && dep == "minecraft" && table.TryGetValue("versionRange", out var range))
                games[dependencyOf] = range;
        }
        return mods.Where(m => m.ContainsKey("modId")).Select(m =>
        {
            string id = m["modId"];
            string? game = games.TryGetValue(id, out var g) ? g : games.Count == 1 ? games.Values.First() : null;
            if (game != null && game.Contains("${", StringComparison.Ordinal)) game = null;
            return (id, m.GetValueOrDefault("displayName") ?? "", m.GetValueOrDefault("version") ?? "", loader, game);
        }).ToList();
    }

    // "value" or 'value', with a # comment after it; null for anything else (numbers, arrays).
    static string? Unquote(string value)
    {
        if (value.Length < 2 || (value[0] != '"' && value[0] != '\'')) return null;
        char q = value[0];
        for (int i = 1; i < value.Length; i++)
        {
            // In "…" a backslash keeps the next character (\" is a quote inside the text); '…' has no escapes.
            if (q == '"' && value[i] == '\\') { i++; continue; }
            if (value[i] == q) return q == '"' ? Unescape(value[1..i]) : value[1..i];
        }
        return null;
    }

    // \" \\ \n \t \/ and \uXXXX as JSON and TOML mean them; any other backslash stays as it is (a Windows path in a text).
    static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            char n = s[i + 1];
            if (n == 'u' && i + 5 < s.Length && int.TryParse(s.AsSpan(i + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out int code))
            {
                sb.Append((char)code);
                i += 5;
                continue;
            }
            string? plain = n switch { '"' => "\"", '\\' => "\\", '/' => "/", 'n' => "\n", 't' => "\t", 'r' => "", _ => null };
            if (plain == null) { sb.Append(c); continue; }
            sb.Append(plain);
            i++;
        }
        return sb.ToString();
    }
}
