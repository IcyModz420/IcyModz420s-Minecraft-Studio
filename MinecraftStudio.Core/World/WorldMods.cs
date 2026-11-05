// MinecraftStudio.Core/World/WorldMods.cs: which mods, add-ons and data packs a world needs, and what it holds of each.
// Owns: ModUse, ModsReport, WorldMods. Everything whose name is not the game's own (byg:soapstone, twilightforest:naga,
// mypack:ruby_ore) is counted by its namespace in every dimension: blocks, block entities, mobs and other entities (riders
// too), items in containers, on mobs and in every player's inventory and ender chest, biomes, and a dimension of its own.
// What level.dat says (Forge's mod list and number tables, the server brands that saved it), the world's data packs or
// behaviour packs that bring a namespace, and the mods on this PC that have it come from WorldMods.Sources.cs. Only reads.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Packs;

namespace MinecraftStudio.Core.World;

/// <summary>What one mod, add-on or data pack — one namespace — has in a world, and where it can come from.</summary>
public sealed class ModUse
{
    public ModUse(string ns) => Namespace = ns;

    /// <summary>"byg", "twilightforest" — or "" for numbered blocks the world doesn't name (an old mod's, unknown which).</summary>
    public string Namespace { get; }
    /// <summary>Full names with how many there are (biomes: in how many chunks).</summary>
    public Dictionary<string, long> Blocks { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> BlockEntities { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> Entities { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> Items { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> Biomes { get; } = new(StringComparer.Ordinal);
    /// <summary>Dimensions that are its own (by their id), and those that are a mod's without saying whose but hold mostly its things.</summary>
    public List<string> Dimensions { get; } = [];
    public List<string> LikelyDimensions { get; } = [];
    /// <summary>The chunks that hold its blocks, block entities or entities, by dimension id.</summary>
    public Dictionary<string, HashSet<ChunkPos>> Chunks { get; } = new(StringComparer.Ordinal);
    /// <summary>In level.dat's mod list (Forge), with the version it gives there.</summary>
    public bool Listed { get; set; }
    public string? ListedVersion { get; set; }
    /// <summary>What Forge's number tables in level.dat hold of it: "blocks" → 140, "items" → 210 …</summary>
    public Dictionary<string, int> Registered { get; } = new(StringComparer.Ordinal);
    /// <summary>The world's data pack (Java) or behaviour/resource pack (Bedrock) that brings it, if one does.</summary>
    public string? Pack { get; set; }
    /// <summary>That pack in words: the data pack "x.zip" in this world.</summary>
    public string? PackWhere { get; set; }
    /// <summary>Mods on this PC that have it, the best match first.</summary>
    public List<InstalledMod> OnThisPc { get; } = [];
    /// <summary>The first of them is in the game folder the world itself is in (or its server's).</summary>
    public bool Beside { get; set; }

    static string ModsFolder(string where) => where.StartsWith("this world's", StringComparison.Ordinal) ? "the mods folder of " + where : where + "'s mods folder";

    public long BlockCount => Blocks.Values.Sum();
    public long Total => BlockCount + BlockEntities.Values.Sum() + Entities.Values.Sum() + Items.Values.Sum();
    /// <summary>Something of it is in the world (not only listed or registered).</summary>
    public bool Found => Total > 0 || Biomes.Count > 0 || Dimensions.Count > 0 || LikelyDimensions.Count > 0;
    public int ChunkCount => Chunks.Values.Sum(c => c.Count);

    /// <summary>What to call it: the installed mod's name, else the pack's, else its namespace.</summary>
    public string Title => Namespace.Length == 0 ? "Numbered blocks the world doesn't name"
        : OnThisPc.FirstOrDefault() is { } m ? $"{m.Name} ({Namespace})" : Pack != null ? $"{Pack} ({Namespace})" : Namespace;

    /// <summary>Where it comes from, in words.</summary>
    public string Source
    {
        get
        {
            if (OnThisPc.FirstOrDefault() is { } m)
                return $"on this PC: {m.Describe()}, in {ModsFolder(m.Where)}" + (Beside ? " (the game folder this world is in)" : "") + (OnThisPc.Count > 1 ? $"; {OnThisPc.Count - 1:N0} more on this PC" : "");
            if (PackWhere != null) return "from " + PackWhere;
            if (Namespace.Length == 0) return "an old mod's blocks: the world keeps only their numbers";
            return Listed ? "in the world's mod list; not found on this PC" : "not found on this PC";
        }
    }

    /// <summary>"1 chunk", "2 chunks", "1 data pack"; in "kinds of entity" or "jobs for villagers" the first word is the one counted.</summary>
    public static string Count(long n, string plural)
    {
        if (n != 1) return $"{n:N0} {plural}";
        var words = plural.Split(' ');
        int which = words.Length > 2 && words[1] is "of" or "for" ? 0 : words.Length - 1;
        string w = words[which];
        words[which] = w.EndsWith("ies", StringComparison.Ordinal) ? w[..^3] + "y" : w.EndsWith('s') ? w[..^1] : w;
        return "1 " + string.Join(' ', words);
    }

    /// <summary>A name as the list shows it: "byg:lush_dirt" → "lush dirt".</summary>
    public static string Short(string name) => (name.IndexOf(':') is int i and >= 0 ? name[(i + 1)..] : name).Replace('_', ' ');

    static string List(Dictionary<string, long> d, int most, string unit = "") =>
        string.Join(", ", d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(most).Select(kv => unit.Length == 0 ? $"{kv.Value:N0} {Short(kv.Key)}" : $"{Short(kv.Key)} ({kv.Value:N0} {unit})"))
        + (d.Count > most ? $" and {d.Count - most:N0} more" : "");

    /// <summary>Its lines in the report: what of it is where (the most common of each kind, or every one).</summary>
    public IEnumerable<string> Lines(bool all)
    {
        int most = all ? int.MaxValue : 8;
        if (Listed) yield return "In level.dat's mod list" + (ListedVersion is { Length: > 0 } v && v != "NONE" ? $" (version {v})." : ".");
        if (Dimensions.Count > 0) yield return "Its own dimension" + (Dimensions.Count == 1 ? ": " : "s: ") + string.Join(", ", Dimensions) + ".";
        if (LikelyDimensions.Count > 0) yield return "Most likely its dimension (the world doesn't say whose; most of what is in it is this mod's): " + string.Join(", ", LikelyDimensions) + ".";
        if (Blocks.Count > 0) yield return $"Blocks: {BlockCount:N0} ({Count(Blocks.Count, "kinds")}) in {Count(ChunkCount, "chunks")} — {List(Blocks, most)}.";
        if (BlockEntities.Count > 0) yield return $"Block entities: {List(BlockEntities, most)}.";
        if (Entities.Count > 0) yield return $"Mobs and other entities: {List(Entities, most)}.";
        if (Items.Count > 0) yield return $"Items (in chests, on mobs, with the players): {List(Items, most)}.";
        if (Biomes.Count > 0) yield return $"Biomes: {List(Biomes, most, "chunks")}.";
        if (Registered.Count > 0) yield return "Forge's tables in level.dat know " + string.Join(", ", Registered.OrderByDescending(kv => kv.Value).Select(kv => Count(kv.Value, kv.Key))) + " of it.";
    }
}

/// <summary>Every mod, add-on and data pack a world has things from, and what saved it.</summary>
public sealed class ModsReport
{
    public string World { get; set; } = "";
    /// <summary>Most of it first; mods only listed in level.dat (nothing of them found) last.</summary>
    public List<ModUse> Mods { get; } = [];
    /// <summary>level.dat's ServerBrands (Java 1.16+): every kind of game that saved it — "vanilla", "fabric", "forge", "paper" …</summary>
    public List<string> Brands { get; } = [];
    /// <summary>"Forge 36.1.23", "NeoForge 21.1.77" — when level.dat names it.</summary>
    public string? Loader { get; set; }
    /// <summary>The world's own packs that are on (names), Bedrock's behaviour and resource packs and Java's data packs.</summary>
    public List<string> Packs { get; } = [];
    public bool Bedrock { get; set; }
    public int ChunksRead { get; set; }
    public List<string> Warnings { get; } = [];

    public IEnumerable<ModUse> Found => Mods.Where(m => m.Found);

    /// <summary>The lines about what saved the world.</summary>
    public IEnumerable<string> About()
    {
        if (Loader != null) yield return $"Saved by {Loader}.";
        // The brands the loader's own line already names are not said twice.
        var brands = Brands.Where(b => Loader == null || !Loader.StartsWith(b, StringComparison.OrdinalIgnoreCase)).ToList();
        if (brands.Count > 0)
        {
            static string Brand(string b) => b switch
            {
                "vanilla" => "the game without mods", "fabric" => "Fabric", "forge" => "Forge", "neoforge" => "NeoForge", "quilt" => "Quilt",
                "paper" => "Paper", "spigot" => "Spigot", "craftbukkit" => "CraftBukkit", "purpur" => "Purpur", "folia" => "Folia", _ => b,
            };
            yield return (Loader == null ? "Saved by " : "Also saved by ") + string.Join(", ", brands.Select(Brand)) + " (level.dat keeps a list of what saved it).";
        }
        if (Packs.Count > 0) yield return (Bedrock ? "Packs it uses: " : "Data packs that are on: ") + string.Join(", ", Packs) + ".";
    }

    /// <summary>What happens to these things when the world is opened without them.</summary>
    public string Without => Bedrock
        ? "Opened without its pack, Bedrock can't show an add-on's blocks and mobs."
        : "Opened without a mod, the game turns its blocks into air, leaves out its mobs, block entities and items, and its biomes become ordinary ones; a dimension of its own stays on disk, unused. Keep a backup before opening it without them.";

    /// <summary>"2 mods and 1 data pack", "3 add-ons": what was found (a Java namespace a pack of the world brings is a data pack's).</summary>
    public string Counted()
    {
        var found = Found.ToList();
        if (Bedrock) return ModUse.Count(found.Count, "add-ons");
        int packs = found.Count(m => m.Pack != null), mods = found.Count - packs;
        return mods > 0 && packs > 0 ? $"{ModUse.Count(mods, "mods")} and {ModUse.Count(packs, "data packs")}" : packs > 0 ? ModUse.Count(packs, "data packs") : ModUse.Count(mods, "mods");
    }

    /// <summary>The whole report as text (the window's "Copy" and mcstudio mods).</summary>
    public string Text(bool all = true)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Mods and add-ons in ").Append(World).Append("\r\n");
        foreach (var line in About()) sb.Append(line).Append("\r\n");
        var found = Found.ToList();
        if (found.Count == 0) sb.Append($"\r\nNothing from a mod, an add-on or a data pack was found in {ChunksRead:N0} chunks: every block, mob, item and biome is the game's own.\r\n");
        else sb.Append($"\r\n{Counted()} found in {ModUse.Count(ChunksRead, "chunks")}.{(Bedrock || Mods.Any(m => m.Found && m.Pack == null) ? " " + Without : "")}\r\n");
        foreach (var m in Mods)
        {
            sb.Append("\r\n").Append(m.Title).Append(" — ").Append(m.Source).Append("\r\n");
            foreach (var line in m.Lines(all)) sb.Append("  ").Append(line).Append("\r\n");
        }
        foreach (var w in Warnings) sb.Append("\r\n! ").Append(w);
        return sb.ToString();
    }
}

public static partial class WorldMods
{
    /// <summary>The ids in level.dat's mod lists that are the game and the loader themselves, not mods.</summary>
    static readonly HashSet<string> NotMods = new(StringComparer.OrdinalIgnoreCase) { "minecraft", "mcp", "FML", "forge", "neoforge", "fabricloader", "java" };

    static readonly HashSet<string> GameDimensions = new(StringComparer.Ordinal) { "minecraft:overworld", "minecraft:the_nether", "minecraft:the_end" };

    /// <summary>
    /// Every namespace a world holds things of, with what level.dat says, the world's packs and the mods given (from
    /// InstalledMods.Find; none: nothing is matched). Reads every chunk of every dimension; only reads.
    /// </summary>
    public static ModsReport Read(SaveWorld world, IReadOnlyList<InstalledMod>? installed = null, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        var report = new ModsReport { World = world.Name, Bedrock = world is BedrockWorld };
        var uses = new Dictionary<string, ModUse>(StringComparer.Ordinal);
        var (numbers, biomeNumbers) = LevelDat(world, report, uses);
        var counter = new Counter(uses, numbers);
        foreach (var dim in world.Dimensions)
        {
            string id = CanonicalReader.DimensionId(dim);
            var reader = new CanonicalReader(world, dim) { ModBiomeNumbers = biomeNumbers };
            // Region by region, so neighbouring chunks are read one after another.
            var positions = dim.ChunkPositions.Where(reader.Exists).OrderBy(p => p.X >> 5).ThenBy(p => p.Z >> 5).ThenBy(p => p.X).ThenBy(p => p.Z).ToList();
            int done = 0;
            foreach (var pos in positions)
            {
                cancel.ThrowIfCancellationRequested();
                if (done++ % 64 == 0) progress?.Report($"Reading {dim.DisplayName}: {done - 1:N0} of {positions.Count:N0} chunks");
                if (reader.Get(pos) is not { } c) continue;
                counter.Chunk(c, id);
                report.ChunksRead++;
            }
            report.Warnings.AddRange(reader.Warnings.Take(10));
            if (reader.Warnings.Count > 10) report.Warnings.Add($"{reader.Warnings.Count - 10} more damaged parts in {dim.DisplayName}.");
            Dimension(id, counter, uses);
        }
        progress?.Report("Reading the players");
        try
        {
            foreach (var p in WorldInfoReader.Read(world).Players) counter.Player(p);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or IO.SaveFormatException) { report.Warnings.Add("The players could not be read: " + e.Message); }
        progress?.Report("Looking at the world's packs");
        WorldPackSources(world, report, uses);
        if (installed != null) Match(world, uses, installed);
        report.Mods.AddRange(uses.Values.OrderByDescending(m => m.Found).ThenByDescending(m => m.Total).ThenByDescending(m => m.Biomes.Count + m.Dimensions.Count)
            .ThenBy(m => m.Namespace, StringComparer.Ordinal));
        return report;
    }

    static ModUse Use(Dictionary<string, ModUse> uses, string ns) => uses.TryGetValue(ns, out var u) ? u : uses[ns] = new ModUse(ns);

    /// <summary>A dimension's id: a mod's own by its namespace; one without a namespace (Forge 1.12's DIM7) the mod most of its things are from.</summary>
    static void Dimension(string id, Counter counter, Dictionary<string, ModUse> uses)
    {
        if (GameDimensions.Contains(id)) return;
        int colon = id.IndexOf(':');
        if (colon > 0 && id[..colon] != "minecraft") { Use(uses, id[..colon]).Dimensions.Add(id); return; }
        if (colon > 0) return;
        var most = counter.InDimension(id);
        if (most != null) Use(uses, most).LikelyDimensions.Add(id);
    }

    /// <summary>Counts one chunk at a time; the totals go into the uses.</summary>
    sealed class Counter(Dictionary<string, ModUse> uses, IReadOnlyDictionary<int, string>? numbers)
    {
        // Per dimension, how much of each namespace is in it (to tell whose a dimension without a namespace is).
        readonly Dictionary<(string Dim, string Ns), long> _perDimension = [];
        // What each palette state is for counting: its modded name, or null for the game's own (cached by state).
        readonly Dictionary<int, string?> _blockNames = [];
        string _dim = "";
        ChunkPos _pos;

        public string? InDimension(string dim) =>
            _perDimension.Where(kv => kv.Key.Dim == dim).OrderByDescending(kv => kv.Value).Select(kv => kv.Key.Ns).FirstOrDefault();

        static string? Ns(string id)
        {
            int colon = id.IndexOf(':');
            if (colon <= 0) return null;
            string ns = id[..colon];
            // "lce:block_298": a console block this program has no name for — the console's own, never a mod's (consoles take none).
            return ns is "minecraft" or "lce" ? null : ns;
        }

        // A block's name to count it under: a mod's name, or (numbered worlds) what Forge's table says the number was.
        string? BlockName(BlockState s)
        {
            if (_blockNames.TryGetValue(s.Id, out var known)) return known;
            string? name = Ns(s.Name) != null ? s.Name : null;
            if (name == null && s.BaseName.StartsWith("legacy_block_", StringComparison.Ordinal) && int.TryParse(s.BaseName.AsSpan("legacy_block_".Length), out int number))
                name = numbers != null && numbers.TryGetValue(number, out var registered) ? (Ns(registered) != null ? registered : null) : $":block {number}";
            return _blockNames[s.Id] = name;
        }

        void Count(Func<ModUse, Dictionary<string, long>> kind, string name, long count, bool inChunk)
        {
            // ":block 256" — a number nobody named: the unknown mod's.
            string ns = name.StartsWith(':') ? "" : Ns(name)!;
            var use = Use(uses, ns);
            var d = kind(use);
            d[name] = d.GetValueOrDefault(name) + count;
            if (!inChunk) return;
            (use.Chunks.TryGetValue(_dim, out var set) ? set : use.Chunks[_dim] = []).Add(_pos);
            _perDimension[(_dim, ns)] = _perDimension.GetValueOrDefault((_dim, ns)) + count;
        }

        public void Chunk(CanonicalChunk c, string dim)
        {
            _dim = dim;
            _pos = new ChunkPos(c.X, c.Z);
            foreach (var (_, section) in c.Sections)
            {
                var palette = section.Palette;
                string?[]? names = null;
                for (int i = 0; i < palette.Length; i++)
                    if (!palette[i].IsAir && BlockName(palette[i]) is string n) (names ??= new string?[palette.Length])[i] = n;
                if (names == null) continue;
                if (section.Indices is not { } indices)
                {
                    if (names[0] is string only) Count(u => u.Blocks, only, 4096, true);
                    continue;
                }
                var counts = new int[palette.Length];
                foreach (var index in indices) if (index < counts.Length) counts[index]++;
                for (int i = 0; i < names.Length; i++)
                    if (names[i] is string n && counts[i] > 0) Count(u => u.Blocks, n, counts[i], true);
            }
            HashSet<string>? biomes = null;
            foreach (var cells in c.Biomes.Values)
                foreach (var b in cells) if (b != null && Ns(b) != null) (biomes ??= []).Add(b);
            foreach (var b in c.ColumnBiomes ?? []) if (b != null && Ns(b) != null) (biomes ??= []).Add(b);
            foreach (var b in biomes ?? []) { var use = Use(uses, Ns(b)!); use.Biomes[b] = use.Biomes.GetValueOrDefault(b) + 1; }
            foreach (var be in c.BlockEntities)
            {
                if (be.GetString("id") is string id && Ns(id) != null) Count(u => u.BlockEntities, id, 1, true);
                Stacks(be, true);
            }
            foreach (var e in c.Entities) Entity(e);
        }

        void Entity(NbtCompound e)
        {
            if (e.GetString("id") is string id && Ns(id) != null) Count(u => u.Entities, id, 1, true);
            foreach (var (key, value) in e)
                if (key != "Passengers") Stacks(value, true);
            foreach (var rider in e.GetList("Passengers")?.OfType<NbtCompound>() ?? []) Entity(rider);
        }

        public void Player(CanonicalPlayer p)
        {
            foreach (var key in new[] { "Inventory", "EnderItems", "equipment" })
                if (p.Data[key] is { } tag) Stacks(tag, false);
        }

        // Every item stack inside a tag (a compound with an id and a count, the canonical form), and what it holds.
        void Stacks(NbtTag tag, bool inChunk)
        {
            switch (tag)
            {
                case NbtCompound c when c.GetString("id") is string id && c["count"] is { IsNumeric: true } count:
                    if (Ns(id) != null) Count(u => u.Items, id, Math.Max(1, count.AsLong), inChunk);
                    foreach (var (key, value) in c)
                        if (key != "id") Stacks(value, inChunk);
                    break;
                case NbtCompound c:
                    foreach (var (_, value) in c) Stacks(value, inChunk);
                    break;
                case NbtList l:
                    foreach (var value in l) Stacks(value, inChunk);
                    break;
            }
        }
    }
}
