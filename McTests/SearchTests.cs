// McTests/SearchTests.cs: "search" — finding things in a world, on every kind of world that takes a placed structure.
// Owns: SearchTests — a chest (diamonds, and a shulker box holding an elytra), a sign with words on it and a named armour
// stand are placed into a copy of each world (the real placement path); then WorldSearch must find each one where it
// was put, in the right container, with the right count; a search limited to the chest's chunk still finds it, one
// limited to a chunk far away does not; block counts match a count made block by block. The originals are not touched.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

static class SearchTests
{
    static readonly (string Label, string Path)[] Worlds =
    [
        ("Java 1.13", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_13")),
        ("Java 1.12.2", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_12_2")),
        ("Bedrock 1.18", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla")),
        ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
        ("Xbox 360 TU70", Path.Combine(TestPaths.Data, "lce", "mcsaves_x360_fgh_tu70", "savegame.dat")),
        ("PE 0.x", Path.Combine(TestPaths.Data, "pymclevel", "testfiles", "PocketWorld")),
    ];

    public static void Run(string[] args)
    {
        foreach (var (label, path) in Worlds.Where(w => args.Length == 0 || args.Any(a => w.Label.Contains(a, StringComparison.OrdinalIgnoreCase))))
        {
            Console.WriteLine("== " + label);
            One(label, path);
        }
        if (args.Length == 0) Pets();
    }

    /// <summary>
    /// Pets and villagers: into a copy of the Java 1.16.5 sample, a wolf called Rex tamed by the world's own player far from
    /// them, a cat tamed by someone not in the world and a librarian with two trades. Find names the owners ("only pets"
    /// lists just the two) and the librarian's trades; bringing the pets home moves only Rex, to beside his owner, once.
    /// </summary>
    static void Pets()
    {
        Console.WriteLine("== Pets and villagers");
        string java = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(java)) return;
        string work = Path.Combine(TestPaths.Output, "search-pets");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        try
        {
            string copyPath, owner, dimId;
            (double X, double Y, double Z) at;
            ChunkPos far;
            using (var w = WorldLoader.Open(java))
            {
                // The pets go where the world's player is (this sample's players stand in its custom dimension).
                var player = WorldInfoReader.Read(w).Players.First();
                dimId = player.Dimension;
                var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == dimId);
                owner = WorldSearch.Owners(w).GetValueOrDefault(player.Uuid, player.Name);
                var pos = player.Data.GetList("Pos")!;
                at = (pos[0].AsDouble, pos[1].AsDouble, pos[2].AsDouble);
                var home = new ChunkPos((int)Math.Floor(at.X) >> 4, (int)Math.Floor(at.Z) >> 4);
                far = dim.ChunkPositions.OrderByDescending(p => Math.Abs(p.X - home.X) + Math.Abs(p.Z - home.Z)).First();
                var ownerUuid = Players.UuidToInts(player.Uuid);
                NbtCompound Mob(string id, int n, int[]? ownedBy, string? name, ChunkPos c)
                {
                    var e = new NbtCompound
                    {
                        ["id"] = new NbtString(id),
                        ["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(c.X * 16 + 4 + n), new NbtDouble(90), new NbtDouble(c.Z * 16 + 8) },
                        ["Motion"] = new NbtList(NbtType.Double) { new NbtDouble(0), new NbtDouble(0), new NbtDouble(0) },
                        ["Rotation"] = new NbtList(NbtType.Float) { new NbtFloat(0), new NbtFloat(0) },
                        ["UUID"] = new NbtIntArray([0x5157, 0x7a11, 0x1e57, n]),
                        ["Health"] = new NbtFloat(8),
                    };
                    if (ownedBy != null) { e["Owner"] = new NbtIntArray(ownedBy); e.SetByte("Tame", 1); }
                    if (name != null) e.SetString("CustomName", "{\"text\":\"" + name + "\"}");
                    return e;
                }
                var villager = Mob("minecraft:villager", 3, null, null, far);
                villager["VillagerData"] = new NbtCompound { ["profession"] = new NbtString("minecraft:librarian"), ["level"] = new NbtInt(2), ["type"] = new NbtString("minecraft:plains") };
                NbtCompound Stack(string id, int count) => new() { ["id"] = new NbtString(id), ["count"] = new NbtInt(count) };
                villager["Offers"] = new NbtCompound
                {
                    ["Recipes"] = new NbtList(NbtType.Compound)
                    {
                        new NbtCompound { ["buy"] = Stack("minecraft:paper", 24), ["sell"] = Stack("minecraft:emerald", 1) },
                        new NbtCompound { ["buy"] = Stack("minecraft:emerald", 9), ["sell"] = Stack("minecraft:bookshelf", 1) },
                    },
                };
                var r = StructurePlacement.EditIntoCopy(w, dimId, [far], c =>
                {
                    c.Entities.Add(Mob("minecraft:wolf", 1, ownerUuid, "Rex", far));
                    c.Entities.Add(Mob("minecraft:cat", 2, [11, 22, 33, 44], null, far));
                    c.Entities.Add(villager);
                }, Path.Combine(work, "with-pets"));
                Program.Check(r.Verified, "a wolf, a cat and a librarian put into a copy");
                copyPath = r.OpenPath;
            }
            using (var w = WorldLoader.Open(copyPath))
            {
                var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == dimId);
                var pets = WorldSearch.Run(w, dim, new SearchOptions { Query = "*", Blocks = false, Items = false, Text = false, Players = false, PetsOnly = true });
                var wolf = pets.Hits.FirstOrDefault(h => h.What == "wolf");
                var cat = pets.Hits.FirstOrDefault(h => h.What == "cat");
                Program.Check(pets.Hits.Count == 2 && wolf != null && wolf.Where.Contains($"tamed by {owner}") && wolf.Where.Contains("named \"Rex\"") && cat != null && cat.Where.Contains("not in this world"),
                    $"only pets: the wolf (\"{wolf?.Where}\") and the cat (\"{cat?.Where}\"), {pets.Hits.Count} in all");
                var villagers = WorldSearch.Run(w, dim, new SearchOptions { Query = "villager", Blocks = false, Items = false, Text = false, Players = false });
                var lib = villagers.Hits.FirstOrDefault(h => h.Where.Contains("librarian"));
                Program.Check(lib != null && lib.Where.Contains("level 2") && lib.Where.Contains("2 trades") && lib.Where.Contains("bookshelf for 9 emerald"), $"a villager shows its job and trades (\"{lib?.Where}\")");
                var moves = PetsHome.Plan(w, dim);
                Program.Check(moves.Count == 1 && moves[0].Name == "Rex" && moves[0].Owner == owner, $"bringing pets home: only Rex goes, to {owner} ({moves.Count} planned)");
                var home = PetsHome.BringIntoCopy(w, dim, moves, Path.Combine(work, "home"));
                Program.Check(home.Verified && home.Notes.Any(n => n.Contains("Rex")), $"Rex brought home in a copy, checked ({string.Join(" | ", home.Notes)})");
                using var back = WorldLoader.Open(home.OpenPath);
                var bd = back.Dimensions.First(d => CanonicalReader.DimensionId(d) == dimId);
                var found = WorldSearch.Run(back, bd, new SearchOptions { Query = "wolf", Blocks = false, Items = false, Text = false, Players = false }).Hits;
                var cats = WorldSearch.Run(back, bd, new SearchOptions { Query = "cat", WholeName = true, Blocks = false, Items = false, Text = false, Players = false }).Hits;
                Program.Check(found.Count == 1 && Math.Abs(found[0].X - at.X) <= 4 && Math.Abs(found[0].Z - at.Z) <= 4 && cats.Count == 1 && cats[0].X >> 4 == far.X,
                    $"in the copy Rex is once, beside {owner} (at {found.FirstOrDefault()?.X}, {found.FirstOrDefault()?.Z}; the player at {at.X:0}, {at.Z:0}); the stranger's cat stayed");
            }
        }
        catch (Exception ex) { Program.Check(false, $"pets threw {ex.GetType().Name}: {ex.Message}"); }
        finally { try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { } }
    }

    static NbtCompound Item(int slot, string id, int count, NbtCompound? components = null)
    {
        var c = new NbtCompound { ["Slot"] = new NbtByte((sbyte)slot), ["id"] = new NbtString(id), ["count"] = new NbtInt(count) };
        if (components != null) c["components"] = components;
        return c;
    }

    static Structure Things()
    {
        var s = new Structure(3, 1, 1) { DataVersion = 4189 };
        s.Set(0, 0, 0, BlockState.Get("minecraft:chest", "facing=north,type=single,waterlogged=false"));
        var shulker = new NbtCompound
        {
            ["minecraft:container"] = new NbtList(NbtType.Compound)
            {
                new NbtCompound { ["slot"] = new NbtInt(0), ["item"] = new NbtCompound { ["id"] = new NbtString("minecraft:elytra"), ["count"] = new NbtInt(1) } },
            },
        };
        s.BlockEntities[s.Index(0, 0, 0)] = new NbtCompound
        {
            ["id"] = new NbtString("minecraft:chest"),
            ["Items"] = new NbtList(NbtType.Compound) { Item(0, "minecraft:diamond", 5), Item(1, "minecraft:shulker_box", 1, shulker) },
        };
        s.Set(1, 0, 0, BlockState.Get("minecraft:oak_sign", "rotation=0,waterlogged=false"));
        NbtList Lines(string first) => new(NbtType.String) { new NbtString("{\"text\":\"" + first + "\"}"), new NbtString("\"\""), new NbtString("\"\""), new NbtString("\"\"") };
        s.BlockEntities[s.Index(1, 0, 0)] = new NbtCompound
        {
            ["id"] = new NbtString("minecraft:sign"),
            ["front_text"] = new NbtCompound { ["messages"] = Lines("Find me 42"), ["color"] = new NbtString("black"), ["has_glowing_text"] = new NbtByte(0) },
            ["back_text"] = new NbtCompound { ["messages"] = Lines(""), ["color"] = new NbtString("black"), ["has_glowing_text"] = new NbtByte(0) },
            ["is_waxed"] = new NbtByte(0),
        };
        s.Entities.Add(new StructureEntity(2.5, 0, 0.5, new NbtCompound
        {
            ["id"] = new NbtString("minecraft:armor_stand"),
            ["CustomName"] = new NbtString("{\"text\":\"Smoke stand\"}"),
        }));
        return s;
    }

    static void One(string label, string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) { Program.Info($"(skipped {label}: not in TestData)"); return; }
        string outDir = Path.Combine(TestPaths.Output, "search", string.Concat(label.Where(char.IsLetterOrDigit)) + DateTime.Now.ToString("HHmmss"));
        int ox, oy, oz;
        PlacementResult placed;
        try
        {
            using var world = WorldLoader.Open(path);
            var dim = Overworld(world);
            var info = WorldInfoReader.Read(world);
            var stored = dim.ChunkPositions.ToHashSet();
            var spawn = new ChunkPos(info.SpawnX >> 4, info.SpawnZ >> 4);
            var chunk = stored.Contains(spawn) ? spawn : stored.OrderBy(p => Math.Abs((long)p.X - spawn.X) + Math.Abs((long)p.Z - spawn.Z)).First();
            (ox, oz) = (chunk.X * 16 + 2, chunk.Z * 16 + 2);
            var (minY, maxY) = StructurePlacement.Height(world, "minecraft:overworld");
            oy = Math.Clamp(info.SpawnY is > 0 and < 30000 ? info.SpawnY : 64, minY + 1, maxY - 2);
            placed = StructurePlacement.PlaceIntoCopy(world, Things(), "minecraft:overworld", ox, oy, oz, pasteAir: true, outDir);
        }
        catch (Exception e) { Program.Check(false, $"{label}: placing the things threw {e.GetType().Name}: {e.Message}"); return; }

        using var copy = WorldLoader.Open(placed.OpenPath);
        var over = Overworld(copy);
        Program.Check(placed.Verified, $"{label}: the placement checked out ({string.Join("; ", placed.Problems)})");
        if (copy is MinecraftStudio.Core.Bedrock.PocketLegacyWorld { RawLevel: not null })
        {
            // Pocket Edition 0.1 has nowhere to keep chest contents, signs or mobs: placing says so instead of dropping them quietly.
            Program.Check(placed.Notes.Any(n => n.Contains("Pocket Edition 0.1")), $"{label}: placing says the chest contents and sign stay out ({string.Join("; ", placed.Notes)})");
            Program.Check(WorldSearch.Run(copy, over, new SearchOptions { Query = "chest", WholeName = true }).Hits.Any(h => h.Kind == SearchKind.Block && (h.X, h.Y, h.Z) == (ox, oy, oz)), $"{label}: the chest block is found");
            return;
        }
        SearchResult Find(string q, bool whole = false, IReadOnlyCollection<ChunkPos>? chunks = null) =>
            WorldSearch.Run(copy, over, new SearchOptions { Query = q, WholeName = whole, Chunks = chunks });

        var diamonds = Find("diamond");
        var inChest = diamonds.Hits.Where(h => h.Kind == SearchKind.Item && h.What == "diamond" && (h.X, h.Y, h.Z) == (ox, oy, oz)).ToList();
        Program.Check(inChest.Count == 1 && inChest[0].Count == 5 && inChest[0].Where == "chest",
            $"{label}: 5 diamonds found in the chest at {ox}, {oy}, {oz} ({string.Join("; ", inChest.Select(h => $"{h.Count} in {h.Where}"))})");
        Program.Info($"{label}: \"diamond\": {diamonds.Hits.Count} hits in {diamonds.ChunksRead} chunks — blocks {Counts(diamonds.BlockCounts)}; items {Counts(diamonds.ItemCounts)}");

        var elytra = Find("elytra").Hits.Where(h => h.Kind == SearchKind.Item && (h.X, h.Y, h.Z) == (ox, oy, oz)).ToList();
        Program.Check(elytra.Count == 1 && elytra[0].Where.EndsWith("shulker box in chest"), $"{label}: the elytra found in the shulker box in the chest ({string.Join("; ", elytra.Select(h => h.Where))})");

        var words = Find("find me 42").Hits.Where(h => h.Kind == SearchKind.Text).ToList();
        Program.Check(words.Any(h => (h.X, h.Y, h.Z) == (ox + 1, oy, oz) && h.What.Contains("Find me 42")), $"{label}: the sign's words found at {ox + 1}, {oy}, {oz} ({string.Join("; ", words.Select(h => $"\"{h.What}\" at {h.X}, {h.Y}, {h.Z} in {h.Where}"))})");

        var stands = Find("armor_stand", whole: true).Hits.Where(h => h.Kind == SearchKind.Entity).ToList();
        Program.Check(stands.Any(h => (h.X, h.Y, h.Z) == (ox + 2, oy, oz) && h.Where.Contains("Smoke stand")), $"{label}: the named armour stand found ({string.Join("; ", stands.Select(h => $"{h.X}, {h.Y}, {h.Z} {h.Where}"))})");

        // Only the chest's chunk: still there; only a chunk far away: not there.
        var here = new[] { new ChunkPos(ox >> 4, oz >> 4) };
        Program.Check(Find("diamond", chunks: here).Hits.Any(h => h.Kind == SearchKind.Item && h.Where == "chest"), $"{label}: found when only the chest's chunk is searched");
        var far = over.ChunkPositions.OrderByDescending(p => Math.Abs((long)p.X - (ox >> 4)) + Math.Abs((long)p.Z - (oz >> 4))).Take(1).ToArray();
        Program.Check(!Find("diamond", chunks: far).Hits.Any(h => h.Kind == SearchKind.Item && h.Where == "chest" && (h.X, h.Y, h.Z) == (ox, oy, oz)), $"{label}: not found when only a chunk far away is searched");

        // Block counts agree with a count made block by block over the same chunks.
        var chests = Find("chest", whole: true);
        long counted = 0;
        var reader = new CanonicalReader(copy, over);
        foreach (var pos in over.ChunkPositions)
            if (reader.Get(pos) is { } c)
                foreach (var (_, section) in c.Sections)
                    for (int i = 0; i < 4096; i++)
                        if (section.Get(i & 15, i >> 8, (i >> 4) & 15).BaseName == "chest") counted++;
        Program.Check(chests.BlockCounts.GetValueOrDefault("chest") == counted && counted >= 1 && chests.Hits.Any(h => h.Kind == SearchKind.Block && (h.X, h.Y, h.Z) == (ox, oy, oz)),
            $"{label}: chest blocks counted {chests.BlockCounts.GetValueOrDefault("chest")} = {counted} counted one by one, the placed one among them");
        if (chests.Warnings.Count > 0) Program.Info($"{label}: warnings: {string.Join("; ", chests.Warnings.Take(3))}");

        // Patterns: *_ore is every ore and nothing else; * alone counts everything of the kinds asked for.
        var ores = WorldSearch.Run(copy, over, new SearchOptions { Query = "*_ore", Items = false, Entities = false, Text = false, Players = false });
        Program.Check(ores.BlockCounts.Count > 0 && ores.BlockCounts.Keys.All(k => k.EndsWith("_ore")), $"{label}: *_ore counts only ores ({Counts(ores.BlockCounts)})");
        var mobs = WorldSearch.Run(copy, over, new SearchOptions { Query = "*", Blocks = false, Items = false, Text = false, Players = false });
        Program.Check(mobs.EntityCounts.ContainsKey("armor_stand"), $"{label}: * counts every entity ({Counts(mobs.EntityCounts)})");
        var oneChunk = WorldSearch.Run(copy, over, new SearchOptions { Query = "*", Items = false, Entities = false, Text = false, Players = false, Chunks = here });
        long blocks = oneChunk.BlockCounts.Values.Sum();
        Program.Check(oneChunk.BlockCounts.ContainsKey("chest") && blocks > 0 && !oneChunk.BlockCounts.ContainsKey("air"), $"{label}: * counts the blocks of one chunk ({blocks:N0} blocks of {oneChunk.BlockCounts.Count} kinds, no air)");
        Program.Check(Find("find*").Hits.All(h => h.Kind != SearchKind.Text), $"{label}: a * finds no text (words are looked for as they are)");
    }

    static string Counts(Dictionary<string, long> d) => d.Count == 0 ? "none" : string.Join(", ", d.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key} {kv.Value:N0}"));

    static WorldDimension Overworld(SaveWorld w) => w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
}
