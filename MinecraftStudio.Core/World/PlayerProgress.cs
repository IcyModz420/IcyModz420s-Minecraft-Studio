// MinecraftStudio.Core/World/PlayerProgress.cs: a Java player's statistics and advancements — read, changed, written back.
// Owns: PlayerProgress, StatLine, AdvancementLine, AdvancementCatalog. The world's own files: stats/<uuid>.json and
// advancements/<uuid>.json (under players/ in 26.1's layout). Statistics by category in 1.13+ (general, mined, crafted,
// used, broken, picked up, dropped, killed, killed by), the older flat names before; any value can be changed.
// Advancements with titles and criteria from the player's own Minecraft (the version's jar, read when needed, never
// bundled): done or not and when; marked done (every criterion with the time now, as the game writes it) or not done
// (its entry taken out). Fields this doesn't know (DataVersion, recipes) are kept. Saved through a temporary file; the
// caller backs the world up first and refuses while the game has it open.
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using MinecraftStudio.Core.Java;

namespace MinecraftStudio.Core.World;

/// <summary>One statistic: its category ("minecraft:custom", "minecraft:mined" …, or "" for a pre-1.13 name), name and value.</summary>
public sealed record StatLine(string Category, string Name, long Value)
{
    /// <summary>The value as people read it: hours for time, blocks for distance, the number otherwise.</summary>
    public string Pretty => Name switch
    {
        _ when Name.EndsWith("_time", StringComparison.Ordinal) || Name.EndsWith("play_one_minute", StringComparison.Ordinal) || Name.Contains("since_", StringComparison.Ordinal)
            => TimeSpan.FromSeconds(Value / 20.0).TotalHours >= 1 ? $"{TimeSpan.FromSeconds(Value / 20.0).TotalHours:0.0} hours" : $"{TimeSpan.FromSeconds(Value / 20.0).TotalMinutes:0} minutes",
        _ when Name.EndsWith("_one_cm", StringComparison.Ordinal) || Name.EndsWith("OneCm", StringComparison.Ordinal) => Value >= 100_000 ? $"{Value / 100_000.0:0.0} km" : $"{Value / 100.0:N0} blocks",
        _ => Value.ToString("N0", CultureInfo.InvariantCulture),
    };
}

/// <summary>One advancement: its id, title, whether it is done and when, and its criteria (those done).</summary>
public sealed record AdvancementLine(string Id, string Title, string? Description, bool Done, string? When, IReadOnlyList<string> Criteria, IReadOnlyList<string> CriteriaDone);

/// <summary>Every advancement of a version: title, description and criteria, from that version's jar.</summary>
public sealed class AdvancementCatalog
{
    public sealed record Entry(string Id, string Title, string? Description, string[] Criteria, bool Hidden);

    public Dictionary<string, Entry> Entries { get; } = new(StringComparer.Ordinal);
    public string Version { get; private set; } = "";

    /// <summary>
    /// The advancements a world can have: those of the installed Java version closest to the world's (none when no Java
    /// Minecraft is installed) and those of the world's own data packs.
    /// </summary>
    public static AdvancementCatalog ForWorld(JavaWorld world)
    {
        var catalog = new AdvancementCatalog();
        var jars = Packs.GameFolders.JavaJars();
        if (jars.Count > 0)
        {
            string? want = world.VersionName;
            // The world's own version, else the newest installed one that is not newer than it (so no advancement the world's
            // version lacks is offered), else the oldest installed (the list is newest first).
            int[]? Key(string? v) => v != null && v.Split('.') is { Length: >= 2 } p && p.All(x => int.TryParse(x, out _)) ? p.Select(int.Parse).ToArray() : null;
            int Compare(int[] a, int[] b)
            {
                for (int i = 0; i < Math.Max(a.Length, b.Length); i++) { int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0; if (x != y) return x.CompareTo(y); }
                return 0;
            }
            var mine = Key(want);
            var jar = jars.FirstOrDefault(j => j.Version == want).Jar
                ?? (mine == null ? null : jars.FirstOrDefault(j => Key(j.Version) is { } k && Compare(k, mine) <= 0).Jar)
                ?? (mine == null ? jars[0].Jar : jars.LastOrDefault(j => Key(j.Version) != null).Jar ?? jars[0].Jar);
            try { catalog = FromJar(jar, Path.GetFileNameWithoutExtension(jar)); }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { }
        }
        catalog.AddDataPacks(world.Folder);
        return catalog;
    }

    /// <summary>The world's data packs' advancements (datapacks/*, folders and zips): ids namespace:path, titles as written.</summary>
    public AdvancementCatalog AddDataPacks(string worldFolder)
    {
        string packs = Path.Combine(worldFolder, "datapacks");
        if (!Directory.Exists(packs)) return this;
        foreach (var pack in Directory.EnumerateFileSystemEntries(packs))
        {
            try
            {
                if (Directory.Exists(pack))
                {
                    foreach (var file in Directory.EnumerateFiles(Path.Combine(pack, "data"), "*.json", SearchOption.AllDirectories))
                        if (AdvancementPath(Path.GetRelativePath(pack, file).Replace('\\', '/')) is { } id) Add(id, File.ReadAllText(file), Lang);
                }
                else if (pack.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var zip = ZipFile.OpenRead(pack);
                    foreach (var e in zip.Entries)
                        if (AdvancementPath(e.FullName) is { } id) { using var r = new StreamReader(e.Open()); Add(id, r.ReadToEnd(), Lang); }
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { }
        }
        return this;
    }

    // data/<namespace>/advancement[s]/<path>.json → "namespace:path".
    static string? AdvancementPath(string path)
    {
        var parts = path.Split('/');
        if (parts.Length < 4 || parts[0] != "data" || parts[2] is not ("advancement" or "advancements") || !path.EndsWith(".json", StringComparison.Ordinal)) return null;
        return parts[1] + ":" + string.Join('/', parts.Skip(3))[..^5];
    }

    // The English names of the jar read (data packs' titles that translate a vanilla key use them).
    Dictionary<string, string> Lang { get; } = new(StringComparer.Ordinal);

    void Add(string id, string json, Dictionary<string, string> lang)
    {
        using var doc = JsonDocument.Parse(json);
        var e = doc.RootElement;
        string Text(JsonElement? t) => t switch
        {
            { ValueKind: JsonValueKind.String } s => s.GetString()!,
            { ValueKind: JsonValueKind.Object } o when o.TryGetProperty("translate", out var k) => lang.GetValueOrDefault(k.GetString() ?? "", o.TryGetProperty("fallback", out var f) ? f.GetString() ?? "" : k.GetString() ?? ""),
            { ValueKind: JsonValueKind.Object } o when o.TryGetProperty("text", out var k) => k.GetString() ?? "",
            _ => "",
        };
        string[] criteria = e.TryGetProperty("criteria", out var c) && c.ValueKind == JsonValueKind.Object ? c.EnumerateObject().Select(p => p.Name).ToArray() : [];
        bool shown = e.TryGetProperty("display", out var display);
        Entries[id] = new Entry(id, shown ? Text(display.TryGetProperty("title", out var title) ? title : null) : "", shown ? Text(display.TryGetProperty("description", out var d) ? d : null) : null,
            criteria, !shown || display.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True);
    }

    /// <summary>The advancements a client jar carries (data/minecraft/advancement[s]/ from 1.13, assets/… in 1.12) with their English titles.</summary>
    public static AdvancementCatalog FromJar(string jar, string version)
    {
        var catalog = new AdvancementCatalog { Version = version };
        using var zip = ZipFile.OpenRead(jar);
        var lang = catalog.Lang;
        if (zip.GetEntry("assets/minecraft/lang/en_us.json") is { } json)
        {
            using var s = json.Open();
            foreach (var p in JsonDocument.Parse(s).RootElement.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.String) lang[p.Name] = p.Value.GetString()!;
        }
        else if (zip.GetEntry("assets/minecraft/lang/en_us.lang") is { } old)
        {
            using var r = new StreamReader(old.Open());
            while (r.ReadLine() is string line) if (line.IndexOf('=') is int i and > 0) lang[line[..i]] = line[(i + 1)..];
        }
        foreach (var entry in zip.Entries)
        {
            string? path = null;
            foreach (var root in new[] { "data/minecraft/advancement/", "data/minecraft/advancements/", "assets/minecraft/advancements/" })
                if (entry.FullName.StartsWith(root, StringComparison.Ordinal) && entry.FullName.EndsWith(".json", StringComparison.Ordinal)) path = entry.FullName[root.Length..^5];
            if (path == null) continue;
            using var r = new StreamReader(entry.Open());
            catalog.Add("minecraft:" + path, r.ReadToEnd(), lang);
        }
        return catalog;
    }
}

public sealed class PlayerProgress
{
    readonly string _statsFile, _advancementsFile;
    JsonObject _stats, _advancements;
    readonly AdvancementCatalog? _catalog;
    // Every criterion each advancement had when the file was read: one marked not done can be marked done again even when
    // no installed Minecraft knows it (a data pack's own advancements, a PC without the game).
    readonly Dictionary<string, HashSet<string>> _criteriaSeen = new(StringComparer.Ordinal);

    PlayerProgress(string statsFile, string advancementsFile, JsonObject stats, JsonObject advancements, AdvancementCatalog? catalog)
    {
        (_statsFile, _advancementsFile, _stats, _advancements, _catalog) = (statsFile, advancementsFile, stats, advancements, catalog);
        foreach (var (id, v) in advancements)
            if ((v as JsonObject)?["criteria"] is JsonObject criteria) _criteriaSeen[id] = criteria.Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Whether anything was changed since it was read (or saved).</summary>
    public bool Changed { get; private set; }

    /// <summary>The player's files in the world's layout (they need not exist yet: a player who never played has none).</summary>
    public static (string Stats, string Advancements) Files(JavaWorld world, Guid uuid)
    {
        string root = world.IsNewLayout ? Path.Combine(world.Folder, "players") : world.Folder;
        return (Path.Combine(root, "stats", uuid + ".json"), Path.Combine(root, "advancements", uuid + ".json"));
    }

    /// <summary>A player's statistics and advancements (null when the player has no UUID to find them by).</summary>
    public static PlayerProgress? Read(JavaWorld world, PlayerRecord player, AdvancementCatalog? catalog)
    {
        if (ServerPlayers.UuidOf(player) is not Guid uuid) return null;
        var (stats, advancements) = Files(world, uuid);
        return new PlayerProgress(stats, advancements, Load(stats), Load(advancements), catalog);
    }

    static JsonObject Load(string file)
    {
        if (!File.Exists(file)) return [];
        return JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? throw new InvalidDataException($"{Path.GetFileName(file)} is not a JSON object.");
    }

    /// <summary>Every statistic, by category then name.</summary>
    public List<StatLine> Stats()
    {
        var list = new List<StatLine>();
        if (_stats["stats"] is JsonObject byCategory)
        {
            foreach (var (category, values) in byCategory)
                if (values is JsonObject o)
                    foreach (var (name, v) in o) if (Number(v) is long n) list.Add(new StatLine(category, name, n));
        }
        else
            foreach (var (name, v) in _stats) if (name != "DataVersion" && Number(v) is long n) list.Add(new StatLine("", name, n));
        return list.OrderBy(s => s.Category, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
    }

    static long? Number(JsonNode? v) => v is JsonValue j && j.TryGetValue(out long n) ? n : v is JsonValue d && d.TryGetValue(out double x) ? (long)x : null;

    /// <summary>A statistic set to a value (made when it isn't there yet).</summary>
    public void SetStat(string category, string name, long value)
    {
        if (value < 0) throw new ArgumentException("A statistic can't be below zero.");
        if (category.Length == 0) _stats[name] = value;
        else
        {
            var byCategory = _stats["stats"] as JsonObject ?? (JsonObject)(_stats["stats"] = new JsonObject());
            var o = byCategory[category] as JsonObject ?? (JsonObject)(byCategory[category] = new JsonObject());
            o[name] = value;
        }
        Changed = true;
    }

    /// <summary>
    /// Every advancement (those the version's catalog shows, and any the player has progress in), with done and criteria.
    /// Recipes (minecraft:recipes/…) are left out: the game unlocks them as items are picked up.
    /// </summary>
    public List<AdvancementLine> Advancements()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, _) in _advancements) if (id != "DataVersion") ids.Add(id);
        if (_catalog != null) foreach (var e in _catalog.Entries.Values) if (!e.Hidden) ids.Add(e.Id);
        var list = new List<AdvancementLine>();
        foreach (var id in ids)
        {
            if (id.StartsWith("minecraft:recipes/", StringComparison.Ordinal)) continue;
            var have = _advancements[id] as JsonObject;
            var doneCriteria = (have?["criteria"] as JsonObject)?.Select(kv => kv.Key).ToList() ?? [];
            string? when = (have?["criteria"] as JsonObject)?.Select(kv => kv.Value?.GetValue<string>()).Where(v => v != null).OrderBy(v => v).LastOrDefault();
            var entry = _catalog?.Entries.GetValueOrDefault(id);
            bool done = have?["done"] is JsonValue d && d.TryGetValue(out bool b) && b;
            string[] all = entry?.Criteria is { Length: > 0 } known ? known : _criteriaSeen.TryGetValue(id, out var seen) ? [.. seen] : [.. doneCriteria];
            list.Add(new AdvancementLine(id, entry is { Title.Length: > 0 } ? entry.Title : Pretty(id), entry?.Description, done, done ? when : null, all, doneCriteria));
        }
        return list.OrderBy(a => a.Id.Split('/')[0], StringComparer.Ordinal).ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string Pretty(string id)
    {
        string last = id[(id.LastIndexOf('/') + 1)..].Replace('_', ' ');
        return last.Length == 0 ? id : char.ToUpperInvariant(last[0]) + last[1..];
    }

    /// <summary>The time now as the game writes it in advancements ("2024-06-01 12:30:00 +0200").</summary>
    public static string Now()
    {
        var now = DateTimeOffset.Now;
        var o = now.Offset;
        return now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture) + (o < TimeSpan.Zero ? "-" : "+") + o.ToString("hhmm", CultureInfo.InvariantCulture);
    }

    /// <summary>An advancement marked done: every criterion met now (those it has, from the catalog or the file), done true.</summary>
    public void Grant(string id)
    {
        var have = _advancements[id] as JsonObject;
        var criteria = _catalog?.Entries.GetValueOrDefault(id)?.Criteria is { Length: > 0 } known ? known
            : _criteriaSeen.TryGetValue(id, out var seen) ? [.. seen] : [];
        if (criteria.Length == 0) throw new ArgumentException($"{Pretty(id)}: what it takes is not known here (no Minecraft of this version is installed to read it from).");
        var met = have?["criteria"] as JsonObject ?? [];
        string now = Now();
        foreach (var c in criteria) if (met[c] == null) met[c] = now;
        _advancements[id] = new JsonObject { ["criteria"] = met.DeepClone(), ["done"] = true };
        Changed = true;
    }

    /// <summary>An advancement marked not done: its progress taken out (the game starts it again).</summary>
    public void Revoke(string id)
    {
        if (_advancements.Remove(id)) Changed = true;
    }

    /// <summary>Writes both files back (only what changed; through a temporary file each).</summary>
    public void Save(int dataVersion)
    {
        if (!Changed) return;
        foreach (var (file, json) in new[] { (_statsFile, _stats), (_advancementsFile, _advancements) })
        {
            if (json.Count == 0 && !File.Exists(file)) continue;
            if (json["DataVersion"] == null && dataVersion > 0) json["DataVersion"] = dataVersion;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temp = file + ".tmp";
            File.WriteAllText(temp, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, file, overwrite: true);
        }
        Changed = false;
    }
}
