// MinecraftStudio.Core/Packs/PackCheck.Data.cs: what the game would ignore or refuse in a Java data pack.
// Part of PackCheck (main part in PackCheck.cs). Owns: DataPack — the pack format and the releases that read it (the same
// table new packs are made from), folder names of the version (functions, recipes, loot_tables … before 1.21; function,
// recipe, loot_table … from it, tags too), functions whose lines start with / (the function then fails to load), macro
// lines ($) before 1.20.2, first words that are no command of the game (a typo, or another edition's command), and the
// load and tick tags naming functions the pack doesn't have.
using System.Text;
using System.Text.Json;

namespace MinecraftStudio.Core.Packs;

public static partial class PackCheck
{
    static readonly (string Plural, string Singular)[] DataFolders =
    [
        ("functions", "function"), ("loot_tables", "loot_table"), ("recipes", "recipe"), ("advancements", "advancement"),
        ("structures", "structure"), ("predicates", "predicate"), ("item_modifiers", "item_modifier"),
    ];

    static readonly (string Plural, string Singular)[] TagFolders =
    [
        ("functions", "function"), ("items", "item"), ("blocks", "block"), ("entity_types", "entity_type"), ("fluids", "fluid"), ("game_events", "game_event"),
    ];

    // Every command of Java Edition's releases (a first word outside it is a typo or another edition's command).
    static readonly HashSet<string> JavaCommands = new(StringComparer.Ordinal)
    {
        "advancement", "attribute", "ban", "ban-ip", "banlist", "bossbar", "clear", "clone", "damage", "data", "datapack", "debug",
        "defaultgamemode", "deop", "dialog", "difficulty", "effect", "enchant", "execute", "experience", "fetchprofile", "fill", "fillbiome",
        "forceload", "function", "gamemode", "gamerule", "give", "help", "item", "jfr", "kick", "kill", "list", "locate", "loot", "me",
        "msg", "op", "pardon", "pardon-ip", "particle", "perf", "place", "playsound", "publish", "random", "recipe", "reload", "replaceitem",
        "return", "ride", "rotate", "save-all", "save-off", "save-on", "say", "schedule", "scoreboard", "seed", "setblock", "setidletimeout",
        "setworldspawn", "spawnpoint", "spectate", "spreadplayers", "stop", "stopsound", "stopwatch", "summon", "swing", "tag", "team",
        "teammsg", "teleport", "tell", "tellraw", "test", "tick", "time", "title", "tm", "tp", "transfer", "trigger", "version", "w",
        "waypoint", "weather", "whitelist", "worldborder", "xp",
    };

    /// <summary>The data pack's own checks (after the ones every Java pack gets).</summary>
    static void DataPack(Dictionary<string, Func<byte[]>> files, string root, int format, List<string> data, PackReport r)
    {
        var known = PackTemplates.DataPackFormats.LastOrDefault(f => f.Major == format);
        var newest = PackTemplates.DataPackFormats[^1];
        r.Kind = format < 0 ? "Java data pack (no version)" : known != null ? $"Java data pack, pack format {known.Text}: made for {known.Versions}" : $"Java data pack, pack format {format}";
        if (format >= 0 && known == null)
            r.Findings.Add(new(PackSeverity.Warning, format > newest.Major
                ? $"Pack format {format} is newer than any release known here (the newest is {newest.Text}, {newest.Versions})."
                : $"Pack format {format} is not one a release of the game uses: the game lists it as made for another version."));
        bool singular = format >= 48;

        // Folder names of the version: data/<namespace>/<folder>/… and data/<namespace>/tags/<kind>/….
        var wrongEra = new List<string>();
        string? wrongName = null;
        foreach (var d in data)
        {
            var parts = d.Split('/');
            if (parts.Length < 4) continue;
            var (folder, kinds) = parts[2] == "tags" && parts.Length >= 5 ? (parts[3], TagFolders) : (parts[2], DataFolders);
            foreach (var (plural, one) in kinds)
                if (singular ? folder == plural : folder == one)
                {
                    wrongEra.Add(d);
                    wrongName ??= singular ? $"{plural} → {one}" : $"{one} → {plural}";
                }
        }
        Group(r, PackSeverity.Problem, singular
            ? $"Folders named as before 1.21 ({wrongName}): this pack's version reads the singular names (function, recipe, loot_table, advancement, tags/function, tags/item …), so these are ignored"
            : $"Folders named as from 1.21 ({wrongName}): this pack's version reads the plural names (functions, recipes, loot_tables, advancements, tags/functions, tags/items …), so these are ignored", wrongEra);

        // Functions: no leading /, macros only from 1.20.2, and commands the game knows.
        var slash = new List<string>();
        var macro = new List<string>();
        var unknown = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var f in data.Where(d => d.EndsWith(".mcfunction", StringComparison.OrdinalIgnoreCase)))
        {
            string text;
            try { text = Encoding.UTF8.GetString(files[root + f]()); } catch (IOException) { continue; }
            int n = 0;
            foreach (var raw in text.Split('\n'))
            {
                n++;
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.EndsWith('\\')) continue; // a line carried on (1.20.2+): its first word is checked where it starts
                if (line[0] == '/') { slash.Add($"{f} line {n}"); continue; }
                if (line[0] == '$')
                {
                    if (format is >= 0 and < 18) macro.Add($"{f} line {n}");
                    line = line[1..].TrimStart();
                }
                string first = line.Split(' ', 2)[0];
                if (first.Length > 0 && !JavaCommands.Contains(first) && unknown.Count < 50)
                {
                    if (!unknown.TryGetValue(first, out var at)) unknown[first] = at = [];
                    at.Add($"{f} line {n}");
                }
            }
        }
        Group(r, PackSeverity.Problem, "Commands starting with /: in a function that is an error, so the whole function does not load", slash);
        Group(r, PackSeverity.Problem, "Macro lines ($) came in 1.20.2: this pack's version can't load these functions", macro);
        foreach (var (word, at) in unknown)
            Group(r, PackSeverity.Warning, $"\"{word}\" is not a command of Java Edition (a typo, or another edition's command): the function does not load", at);

        // The load and tick tags run functions: each one the pack names in its own namespaces should be in it.
        var namespaces = data.Select(d => d.Split('/')).Where(p => p.Length > 2).Select(p => p[1]).ToHashSet(StringComparer.Ordinal);
        string fn = singular ? "function" : "functions";
        foreach (var tag in new[] { "load", "tick" })
        {
            string path = root + $"data/minecraft/tags/{fn}/{tag}.json";
            if (!files.ContainsKey(path)) continue;
            using var doc = Json(files, path, r, comments: false);
            if (doc?.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) continue;
            foreach (var v in values.EnumerateArray())
            {
                string? id = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Object && v.TryGetProperty("id", out var i) ? i.GetString() : null;
                if (id == null || id.StartsWith('#') || id.Split(':') is not [var ns, var name] || !namespaces.Contains(ns)) continue;
                if (!files.ContainsKey(root + $"data/{ns}/{fn}/{name}.mcfunction"))
                    r.Findings.Add(new(PackSeverity.Problem, $"The {tag} tag runs {id}, which is not in the pack (data/{ns}/{fn}/{name}.mcfunction)", path[root.Length..]));
            }
        }
    }
}
