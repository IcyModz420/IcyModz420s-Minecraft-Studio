// MinecraftStudio.Core/World/WorldSearch.cs: finding things in a world of any edition — blocks, items, mobs and other
// entities, and text — without changing anything.
// Owns: SearchKind, SearchHit, SearchOptions, SearchResult, WorldSearch. Chunks are read through CanonicalReader, so every
// edition answers in the newest Java names (diamond_ore, shulker_box …). Items are found wherever they are kept: in
// containers (and in shulker boxes or bundles inside them, at any depth), item frames, dropped on the ground, in mobs'
// hands and armour, chest minecarts, donkeys and llamas, villagers, and every player's inventory and ender chest. Text:
// signs, command blocks, names, books, loot tables — anything written in a block entity, an entity or a player.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

public enum SearchKind { Block, Item, Entity, Text }

/// <summary>
/// One thing found: what (a name, or the text), where (block coordinates), in what, and how many. Dimension is set only
/// for something found in another dimension than the one searched (a player's things, wherever the player is);
/// OnPlayer for what a player carries (it is in no chunk).
/// </summary>
public sealed record SearchHit(SearchKind Kind, string What, int X, int Y, int Z, string Where, long Count, string? Dimension = null, bool OnPlayer = false);

public sealed class SearchOptions
{
    /// <summary>
    /// Part of a name or of a text ("diamond", "minecraft:elytra", "spawner", "hello"); case and "minecraft:" do not matter.
    /// A * stands for anything in a name: "*_ore" is every ore, "*" alone everything (block counts of an area, every mob).
    /// </summary>
    public string Query { get; init; } = "";
    /// <summary>The whole name must match: "diamond" finds diamonds, not diamond ore or diamond swords.</summary>
    public bool WholeName { get; init; }
    public bool Blocks { get; init; } = true;
    public bool Items { get; init; } = true;
    public bool Entities { get; init; } = true;
    public bool Text { get; init; } = true;
    /// <summary>The players' inventories and ender chests too (wherever the players are).</summary>
    public bool Players { get; init; } = true;
    /// <summary>Only these chunks (the map's selection); null for the whole dimension.</summary>
    public IReadOnlyCollection<ChunkPos>? Chunks { get; init; }
    /// <summary>Block positions listed at most (the counts are always complete).</summary>
    public int MaxBlockHits { get; init; } = 2000;
    /// <summary>Things listed at most in all.</summary>
    public int MaxHits { get; init; } = 20000;
    /// <summary>Only pets: mobs that were tamed or have an owner (their owner is named, when a player of the world).</summary>
    public bool PetsOnly { get; init; }
}

public sealed class SearchResult
{
    public List<SearchHit> Hits { get; } = [];
    /// <summary>Every matching name with how many there are — complete, even when the list of hits was cut short.</summary>
    public Dictionary<string, long> BlockCounts { get; } = [];
    public Dictionary<string, long> ItemCounts { get; } = [];
    public Dictionary<string, long> EntityCounts { get; } = [];
    public int TextCount { get; set; }
    public int ChunksRead { get; set; }
    public int ChunksTotal { get; set; }
    /// <summary>More was found than is listed (the counts still have everything).</summary>
    public bool Cut { get; set; }
    public List<string> Warnings { get; } = [];
}

public static class WorldSearch
{
    /// <summary>Searches one dimension (and the players, when asked). Only reads.</summary>
    public static SearchResult Run(SaveWorld world, WorldDimension dim, SearchOptions options, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        var r = new SearchResult();
        var s = new Searcher(options, r, CanonicalReader.DimensionId(dim), Owners(world));
        if (s.Empty) return r;
        var reader = new CanonicalReader(world, dim);
        // Region by region, so neighbouring chunks are read one after another.
        var positions = (options.Chunks ?? (IReadOnlyCollection<ChunkPos>)dim.ChunkPositions).Where(reader.Exists).Distinct()
            .OrderBy(p => p.X >> 5).ThenBy(p => p.Z >> 5).ThenBy(p => p.X).ThenBy(p => p.Z).ToList();
        r.ChunksTotal = positions.Count;
        foreach (var pos in positions)
        {
            cancel.ThrowIfCancellationRequested();
            if (r.ChunksRead % 64 == 0) progress?.Report($"Searched {r.ChunksRead:N0} of {positions.Count:N0} chunks of {dim.DisplayName}");
            r.ChunksRead++;
            if (reader.Get(pos) is not { } c) continue;
            s.Chunk(c);
        }
        if (options.Players)
        {
            try
            {
                foreach (var p in WorldInfoReader.Read(world).Players) s.Player(p);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or SaveFormatException) { r.Warnings.Add("The players could not be read: " + e.Message); }
        }
        r.Warnings.AddRange(reader.Warnings.Take(20));
        if (reader.Warnings.Count > 20) r.Warnings.Add($"{reader.Warnings.Count - 20} more damaged parts.");
        return r;
    }

    /// <summary>The world's players by the UUID a pet's Owner holds (a Bedrock player by the UUID its id converts to).</summary>
    public static Dictionary<Guid, string> Owners(SaveWorld world)
    {
        var owners = new Dictionary<Guid, string>();
        foreach (var p in world.Players)
        {
            if (ServerPlayers.UuidOf(p) is Guid g) owners.TryAdd(g, p.Name);
            if (p.Data.GetLongOrNull("UniqueID") is long id) owners.TryAdd(Entities.UuidForBedrockId(id), p.Name);
        }
        return owners;
    }

    /// <summary>Who owns a mob (a player's name, or the start of an unknown UUID), or null when it is nobody's.</summary>
    public static string? OwnerOf(NbtCompound e, IReadOnlyDictionary<Guid, string> owners)
    {
        if (e.GetInts("Owner") is not { Length: 4 } o) return e.GetBool("Tame") ? "" : null;
        var g = Players.UuidFromInts(o);
        return owners.TryGetValue(g, out var name) ? name : "a player not in this world (" + g.ToString()[..8] + "…)";
    }

    /// <summary>A villager's job and trades in words: "librarian (level 3): enchanted book for 12 emerald, …".</summary>
    static string? Villager(NbtCompound e)
    {
        if (e.GetCompound("VillagerData") is not NbtCompound data) return null;
        string job = Nice(data.GetString("profession") ?? "none");
        var trades = e.GetCompound("Offers")?.GetList("Recipes")?.OfType<NbtCompound>().ToList() ?? [];
        string Stack(NbtCompound? s) => s == null ? "" : $"{(s.GetInt("count", s.GetInt("Count", 1)) is int n and > 1 ? n + " " : "")}{Nice(s.GetString("id") ?? "?")}";
        var words = trades.Take(3).Select(t => $"{Stack(t.GetCompound("sell"))} for {Stack(t.GetCompound("buy"))}" + (t.GetCompound("buyB") is { } b && b.GetString("id") is string bid && bid != "minecraft:air" ? " + " + Stack(b) : ""));
        return job == "none" ? null : $"{job} (level {data.GetInt("level", 1)})" + (trades.Count > 0 ? $": {trades.Count} trade{(trades.Count == 1 ? "" : "s")} — {string.Join("; ", words)}{(trades.Count > 3 ? " …" : "")}" : "");
    }

    /// <summary>A name as people say it: "minecraft:shulker_box" → "shulker box".</summary>
    public static string Nice(string id) => Bare(id).Replace('_', ' ');

    static string Bare(string id) => id.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase) ? id[10..] : id;

    sealed class Searcher
    {
        readonly SearchOptions _o;
        readonly SearchResult _r;
        readonly string _name, _text, _dimension;
        readonly string[]? _parts;
        string? _elsewhere;
        bool _onPlayer;
        int _blockHits;

        readonly IReadOnlyDictionary<Guid, string> _owners;

        public Searcher(SearchOptions o, SearchResult r, string dimension, IReadOnlyDictionary<Guid, string> owners)
        {
            _o = o;
            _r = r;
            _dimension = dimension;
            _owners = owners;
            _text = o.Query.Trim();
            _name = Bare(_text.ToLowerInvariant()).Replace(' ', '_');
            if (_name.Contains('*')) _parts = _name.Split('*');
        }

        public bool Empty => _name.Length == 0;

        bool Name(string id)
        {
            string b = Bare(id).ToLowerInvariant();
            if (_parts != null) return Glob(b);
            return _o.WholeName ? b == _name : b.Contains(_name, StringComparison.Ordinal);
        }

        // A name against a pattern with *: the pieces in order, the first at the start and the last at the end (unless a
        // * stands there).
        bool Glob(string name)
        {
            var parts = _parts!;
            int at = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0) continue;
                if (i == 0) { if (!name.StartsWith(part, StringComparison.Ordinal)) return false; at = part.Length; continue; }
                if (i == parts.Length - 1) return name.Length - part.Length >= at && name.EndsWith(part, StringComparison.Ordinal);
                int found = name.IndexOf(part, at, StringComparison.Ordinal);
                if (found < 0) return false;
                at = found + part.Length;
            }
            return parts[^1].Length == 0 || at == name.Length;
        }

        // Words are looked for as they are (a * finds no text).
        bool Text(string s) => _parts == null && s.Contains(_text, StringComparison.OrdinalIgnoreCase);

        void Add(SearchKind kind, string what, int x, int y, int z, string where, long count, string? dimension = null)
        {
            var counts = kind switch { SearchKind.Item => _r.ItemCounts, SearchKind.Entity => _r.EntityCounts, _ => null };
            if (counts != null) counts[what] = counts.GetValueOrDefault(what) + count;
            if (kind == SearchKind.Text) _r.TextCount++;
            if (_r.Hits.Count < _o.MaxHits) _r.Hits.Add(new SearchHit(kind, what, x, y, z, where, count, dimension ?? _elsewhere, _onPlayer));
            else _r.Cut = true;
        }

        public void Chunk(CanonicalChunk c)
        {
            if (_o.Blocks) Blocks(c);
            foreach (var be in c.BlockEntities)
            {
                int x = be.GetInt("x"), y = be.GetInt("y"), z = be.GetInt("z");
                string where = Nice(be.GetString("id") ?? "block");
                if (_o.Items) Items(be, where, x, y, z);
                if (_o.Text) Texts(be, where, x, y, z);
            }
            foreach (var e in c.Entities) Entity(e, null);
        }

        void Blocks(CanonicalChunk c)
        {
            foreach (var (sy, section) in c.Sections)
            {
                var palette = section.Palette;
                // Which states of the section match: most sections have none, and are passed over at once.
                bool[]? match = null;
                for (int i = 0; i < palette.Length; i++)
                    if (!palette[i].IsAir && Name(palette[i].BaseName)) (match ??= new bool[palette.Length])[i] = true;
                if (match == null) continue;
                if (section.Indices == null)
                {
                    // One state fills the section.
                    string only = palette[0].BaseName;
                    _r.BlockCounts[only] = _r.BlockCounts.GetValueOrDefault(only) + 4096;
                    AddBlock(only, c.X * 16, sy * 16, c.Z * 16, "a whole 16 × 16 × 16 section", 4096);
                    continue;
                }
                var indices = section.Indices;
                for (int i = 0; i < indices.Length; i++)
                {
                    int p = indices[i];
                    if (p >= match.Length || !match[p]) continue;
                    string name = palette[p].BaseName;
                    _r.BlockCounts[name] = _r.BlockCounts.GetValueOrDefault(name) + 1;
                    // Index order is y, z, x (y*256 + z*16 + x).
                    AddBlock(name, c.X * 16 + (i & 15), sy * 16 + (i >> 8), c.Z * 16 + ((i >> 4) & 15), "block", 1);
                }
            }
        }

        void AddBlock(string name, int x, int y, int z, string where, long count)
        {
            if (_blockHits >= _o.MaxBlockHits || _r.Hits.Count >= _o.MaxHits) { _r.Cut = true; return; }
            _blockHits++;
            _r.Hits.Add(new SearchHit(SearchKind.Block, name, x, y, z, where, count));
        }

        void Entity(NbtCompound e, string? riding)
        {
            string id = e.GetString("id") ?? "";
            var pos = e.GetList("Pos");
            int x = pos is { Count: 3 } ? (int)Math.Floor(pos[0].AsDouble) : 0, y = pos is { Count: 3 } ? (int)Math.Floor(pos[1].AsDouble) : 0, z = pos is { Count: 3 } ? (int)Math.Floor(pos[2].AsDouble) : 0;
            string name = Nice(id);
            string? owner = OwnerOf(e, _owners);
            if (_o.Entities && id.Length > 0 && Name(id) && (!_o.PetsOnly || owner != null))
            {
                string? custom = e.GetString("CustomName") is { Length: > 0 } cn ? TextComponents.ToPlain(cn) : null;
                var about = new List<string>();
                if (custom != null) about.Add($"named \"{custom}\"");
                if (owner != null) about.Add(owner.Length == 0 ? "tamed" : "tamed by " + owner);
                if (riding != null) about.Add("riding a " + riding);
                if (Villager(e) is string trades) about.Add(trades);
                Add(SearchKind.Entity, Bare(id), x, y, z, string.Join(", ", about), 1);
            }
            string where = id == "minecraft:item" ? "dropped on the ground" : name;
            if (_o.Items) Items(e, where, x, y, z);
            if (_o.Text) Texts(e, where, x, y, z);
            foreach (var passenger in e.GetList("Passengers")?.OfType<NbtCompound>() ?? []) Entity(passenger, name);
        }

        public void Player(CanonicalPlayer p)
        {
            var pos = p.Data.GetList("Pos");
            int x = pos is { Count: 3 } ? (int)Math.Floor(pos[0].AsDouble) : 0, y = pos is { Count: 3 } ? (int)Math.Floor(pos[1].AsDouble) : 0, z = pos is { Count: 3 } ? (int)Math.Floor(pos[2].AsDouble) : 0;
            string where = p.Dimension is "minecraft:overworld" ? "" : $" (in {Nice(p.Dimension)})";
            // What a player carries is where the player is, which may be another dimension than the one searched.
            _elsewhere = p.Dimension == _dimension ? null : p.Dimension;
            _onPlayer = true;
            try
            {
                if (_o.Items)
                {
                    if (p.Data.GetList("Inventory") is { } inventory) Walk(inventory, $"{p.Name}'s inventory{where}", x, y, z);
                    if (p.Data.GetList("EnderItems") is { } ender) Walk(ender, $"{p.Name}'s ender chest{where}", x, y, z);
                }
                if (_o.Text) Texts(p.Data, $"{p.Name}{where}", x, y, z);
            }
            finally { _elsewhere = null; _onPlayer = false; }
        }

        // Every item stack inside something (not counting what rides it: passengers are searched on their own).
        void Items(NbtCompound holder, string where, int x, int y, int z)
        {
            foreach (var (key, value) in holder)
                if (key != "Passengers") Walk(value, where, x, y, z);
        }

        // An item stack is a compound with an id and a count (the canonical form); what it holds — a shulker box's or a
        // bundle's contents — is searched as being in it.
        void Walk(NbtTag tag, string where, int x, int y, int z)
        {
            switch (tag)
            {
                case NbtCompound c when c.GetString("id") is string id && c["count"] is { IsNumeric: true } count:
                    if (Name(id)) Add(SearchKind.Item, Bare(id), x, y, z, where, Math.Max(1, count.AsLong));
                    string inside = $"{Nice(id)} in {where}";
                    foreach (var (key, value) in c)
                        if (key != "id") Walk(value, inside, x, y, z);
                    break;
                case NbtCompound c:
                    foreach (var (_, value) in c) Walk(value, where, x, y, z);
                    break;
                case NbtList l:
                    foreach (var value in l) Walk(value, where, x, y, z);
                    break;
            }
        }

        void Texts(NbtCompound holder, string where, int x, int y, int z)
        {
            foreach (var (field, text) in Strings(holder, ""))
                if (Text(text)) Add(SearchKind.Text, Snippet(text), x, y, z, $"{where}: {field}", 1);
        }

        // Every written value, with the name of the field it is in (ids and riders left out).
        static IEnumerable<(string Field, string Text)> Strings(NbtTag tag, string field)
        {
            switch (tag)
            {
                case NbtString s:
                    yield return (field, s.Value);
                    break;
                case NbtCompound c:
                    foreach (var (key, value) in c)
                    {
                        if (key is "id" or "Passengers") continue;
                        foreach (var x in Strings(value, key)) yield return x;
                    }
                    break;
                case NbtList l:
                    foreach (var value in l)
                        foreach (var x in Strings(value, field)) yield return x;
                    break;
            }
        }

        // Text as it reads in the game (a JSON text component becomes its words), kept short.
        static string Snippet(string text)
        {
            string plain = TextComponents.ToPlain(text);
            if (string.IsNullOrWhiteSpace(plain)) plain = text;
            plain = plain.Replace("\r", " ").Replace("\n", " ");
            return plain.Length > 120 ? plain[..120] + "…" : plain;
        }
    }
}
