// MinecraftStudio.Core/World/WorldMods.Sources.cs: where a world's mods are named — level.dat (the brands that saved it,
// Forge's mod lists and number tables, the mods Forge lists among the data packs as mod:<id>), the world's own packs (a
// data pack's data/<namespace> folders, a Bedrock behaviour pack's blocks, entities and items) and the mods on this PC.
// Owns: WorldMods.LevelDat, WorldPackSources, PackNamespaces, Match, NearbyGameFolders.
using System.IO.Compression;
using System.Text.RegularExpressions;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Packs;

namespace MinecraftStudio.Core.World;

public static partial class WorldMods
{
    /// <summary>
    /// What a Java world's level.dat says: the brands that saved it, Forge's mod list (fml.LoadingModList from 1.13,
    /// FML.ModList before) with the loader's version, the mods Forge lists among the data packs, and Forge's number tables
    /// (FML/fml Registries; 1.7.10's ItemData) — how much each mod registered. Returns the names of a mod's block numbers
    /// for a world that stores blocks as numbers (before 1.13), and of its biome numbers (biomes were numbers before 1.18).
    /// </summary>
    static (IReadOnlyDictionary<int, string>? Blocks, IReadOnlyDictionary<int, string>? Biomes) LevelDat(SaveWorld world, ModsReport report, Dictionary<string, ModUse> uses)
    {
        if (world is not JavaWorld j || j.Level?.Root is not NbtCompound root) return (null, null);
        var data = j.Data;
        foreach (var b in data?.GetList("ServerBrands")?.OfType<NbtString>() ?? [])
            if (b.Value.Length > 0 && !report.Brands.Contains(b.Value, StringComparer.OrdinalIgnoreCase)) report.Brands.Add(b.Value);
        var fml = root.GetCompound("fml") ?? root.GetCompound("FML");
        string? forge = null, neo = null;
        foreach (var m in (fml?.GetList("LoadingModList") ?? fml?.GetList("ModList"))?.OfType<NbtCompound>() ?? [])
        {
            string id = m.GetString("ModId") ?? "", version = m.GetString("ModVersion") ?? "";
            if (id.Equals("forge", StringComparison.OrdinalIgnoreCase)) forge = version;
            else if (id.Equals("neoforge", StringComparison.OrdinalIgnoreCase)) neo = version;
            if (id.Length == 0 || NotMods.Contains(id)) continue;
            var use = Use(uses, id.ToLowerInvariant());
            use.Listed = true;
            use.ListedVersion = version;
        }
        report.Loader = neo != null ? "NeoForge " + neo : forge != null ? "Forge " + forge : fml != null ? "Forge" : null;
        // Forge also lists each mod's own data among the world's data packs ("mod:twilightforest").
        foreach (var p in data?.GetCompound("DataPacks")?.GetList("Enabled")?.OfType<NbtString>() ?? [])
            if (p.Value.StartsWith("mod:", StringComparison.Ordinal) && p.Value[4..] is { Length: > 0 } id && !NotMods.Contains(id) && InstalledMods.IsNamespace(id))
                Use(uses, id).Listed = true;
        Dictionary<int, string>? numbers = null, biomeNumbers = null;
        void Registered(string ns, string word)
        {
            if (ns == "minecraft" || NotMods.Contains(ns) || !InstalledMods.IsNamespace(ns)) return;
            var use = Use(uses, ns);
            use.Registered[word] = use.Registered.GetValueOrDefault(word) + 1;
        }
        if (fml?.GetCompound("Registries") is NbtCompound registries)
            foreach (var (key, value) in registries)
            {
                if (value is not NbtCompound reg || reg.GetList("ids") is not NbtList ids) continue;
                if (RegistryWord(key) is not string word) continue;
                bool blocks = key is "minecraft:blocks" or "minecraft:block", biomes = key is "minecraft:biomes" or "minecraft:worldgen/biome";
                foreach (var e in ids.OfType<NbtCompound>())
                {
                    if (e.GetString("K") is not string name || name.IndexOf(':') is not (> 0 and var colon)) continue;
                    if (blocks && e.GetLongOrNull("V") is long v) (numbers ??= [])[(int)v] = name;
                    if (biomes && name[..colon] != "minecraft" && e.GetLongOrNull("V") is long b) (biomeNumbers ??= [])[(int)b] = name;
                    Registered(name[..colon], word);
                }
            }
        // Forge for 1.7.10: one table, blocks marked with \u0001 and items with \u0002.
        foreach (var e in fml?.GetList("ItemData")?.OfType<NbtCompound>() ?? [])
        {
            if (e.GetString("K") is not { Length: > 2 } k || k[0] is not ('\u0001' or '\u0002')) continue;
            string name = k[1..];
            if (name.IndexOf(':') is not (> 0 and var colon)) continue;
            if (k[0] == '\u0001' && e.GetLongOrNull("V") is long v) (numbers ??= [])[(int)v] = name;
            Registered(name[..colon], k[0] == '\u0001' ? "blocks" : "items");
        }
        // From 1.13 (the Flattening) every block is saved by name.
        return (j.DataVersion is >= 1451 ? null : numbers, biomeNumbers);
    }

    static string? RegistryWord(string key) => (key.StartsWith("minecraft:", StringComparison.Ordinal) ? key[10..] : key) switch
    {
        "blocks" or "block" => "blocks",
        "items" or "item" => "items",
        "biomes" or "worldgen/biome" => "biomes",
        "entities" or "entity_type" => "kinds of entity",
        "block_entity_type" or "tile_entity_type" => "kinds of block entity",
        "enchantments" or "enchantment" => "enchantments",
        "potions" or "mob_effect" => "effects",
        "potiontypes" or "potion" => "potions",
        "soundevents" or "sound_event" => "sounds",
        "villagerprofessions" or "villager_profession" => "jobs for villagers",
        "fluid" or "fluids" => "fluids",
        // The rest (memory kinds, stat kinds …) say little about what a mod adds.
        _ => null,
    };

    /// <summary>The world's own packs that are on (named in the report) and the namespaces they bring (given to those uses).</summary>
    static void WorldPackSources(SaveWorld world, ModsReport report, Dictionary<string, ModUse> uses)
    {
        if (world is not (JavaWorld or BedrockWorld)) return;
        List<WorldPack> packs;
        try { packs = WorldPacks.Read(world); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            report.Warnings.Add("The world's packs could not be read: " + e.Message);
            return;
        }
        foreach (var pack in packs.Where(p => p.Active && !p.BuiltIn))
        {
            report.Packs.Add(pack.Name);
            if (pack.Location == null || pack.Kind == WorldPackKind.WorldResourcePack) continue;
            string what = pack.Kind switch
            {
                WorldPackKind.DataPack => $"the data pack \"{pack.Name}\" in this world",
                WorldPackKind.BehaviorPack => $"the behaviour pack \"{pack.Name}\"{(pack.InWorld ? " in this world" : "")}",
                _ => $"the resource pack \"{pack.Name}\"{(pack.InWorld ? " in this world" : "")}",
            };
            foreach (var ns in PackNamespaces(pack.Location, pack.Kind != WorldPackKind.DataPack))
                if (uses.TryGetValue(ns, out var use) && use.Pack == null) { use.Pack = pack.Name; use.PackWhere = what; }
        }
    }

    static readonly Regex Identifier = new("\"identifier\"\\s*:\\s*\"([a-z0-9_.\\-]+):", RegexOptions.Compiled);

    /// <summary>
    /// The namespaces a pack brings: a Java data pack's data/&lt;namespace&gt; folders (folder or zip); a Bedrock pack's
    /// identifiers in its blocks, entities and items. The game's own namespace is left out. Only reads.
    /// </summary>
    public static SortedSet<string> PackNamespaces(string location, bool bedrock)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            if (bedrock)
            {
                if (!Directory.Exists(location)) return found;
                int files = 0;
                foreach (var sub in new[] { "blocks", "entities", "items" })
                {
                    string dir = Path.Combine(location, sub);
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
                    {
                        if (++files > 3000) break;
                        if (new FileInfo(f).Length > (1 << 20)) continue;
                        foreach (Match m in Identifier.Matches(File.ReadAllText(f))) found.Add(m.Groups[1].Value);
                    }
                }
            }
            else if (Directory.Exists(location))
            {
                string data = Path.Combine(location, "data");
                if (Directory.Exists(data)) foreach (var d in Directory.EnumerateDirectories(data)) found.Add(Path.GetFileName(d));
            }
            else if (File.Exists(location))
            {
                using var zip = ZipFile.OpenRead(location);
                var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
                // A zip whose files sit in one folder inside it has its data/ there.
                string root = names.Any(n => n.StartsWith("data/", StringComparison.Ordinal)) ? ""
                    : names.Select(n => n.IndexOf('/') is > 0 and var i ? n[..(i + 1)] : "").Where(r => r.Length > 0).Distinct().ToList() is [var only] ? only : "";
                foreach (var n in names)
                    if (n.StartsWith(root + "data/", StringComparison.Ordinal) && n.IndexOf('/', root.Length + 5) is > 0 and var end) found.Add(n[(root.Length + 5)..end]);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
        found.RemoveWhere(ns => ns == "minecraft" || !InstalledMods.IsNamespace(ns));
        return found;
    }

    /// <summary>
    /// The mods on this PC for each namespace: those whose id it is, else those whose assets hold it, else those whose data
    /// does; this world's own game folder first.
    /// </summary>
    static void Match(SaveWorld world, Dictionary<string, ModUse> uses, IReadOnlyList<InstalledMod> installed)
    {
        var near = NearbyGameFolders(world).Select(f => Path.GetFullPath(f.Folder)).ToList();
        foreach (var use in uses.Values)
        {
            if (use.Namespace.Length == 0) continue;
            var candidates = installed.Select(m => (Mod: m, Score: m.Match(use.Namespace))).Where(x => x.Score >= 0).ToList();
            if (candidates.Count == 0) continue;
            int best = candidates.Min(x => x.Score);
            use.OnThisPc.AddRange(candidates.Where(x => x.Score == best).Select(x => x.Mod)
                .OrderByDescending(m => near.Contains(Path.GetFullPath(m.GameFolder), StringComparer.OrdinalIgnoreCase)).ThenBy(m => m.Disabled)
                .ThenBy(m => m.Where, StringComparer.OrdinalIgnoreCase).DistinctBy(m => m.File, StringComparer.OrdinalIgnoreCase));
            use.Beside = near.Contains(Path.GetFullPath(use.OnThisPc[0].GameFolder), StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The game folders next to a Java world, whose mods folders were meant for it: the folder whose saves/ it is in (a
    /// launcher instance), and a server's folder (the world sits beside the server's mods folder or server.properties).
    /// </summary>
    public static List<(string Label, string Folder)> NearbyGameFolders(SaveWorld world)
    {
        var list = new List<(string, string)>();
        if (world is not JavaWorld j) return list;
        try
        {
            var parent = Directory.GetParent(Path.GetFullPath(j.Folder));
            if (parent == null) return list;
            if (parent.Name.Equals("saves", StringComparison.OrdinalIgnoreCase) && parent.Parent is { } game) list.Add(("this world's game folder", game.FullName));
            else if (Directory.Exists(Path.Combine(parent.FullName, "mods")) || File.Exists(Path.Combine(parent.FullName, "server.properties")))
                list.Add(("this world's server folder", parent.FullName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return list;
    }
}
