// McTests/CensusTests.cs: "census" — the busiest chunks of a world (the lag finder).
// Owns: CensusTests — on Java 1.16.5, Java 1.12.2 Twilight Forest and Bedrock 1.16/1.18 sample worlds: every entity kind
// counted the same as Find counts it (riders included), the per-chunk numbers adding up to the totals, the busiest first,
// at most three kinds named per chunk, dropped items counted among the entities; nothing written.
using System.Security.Cryptography;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.World;

static class CensusTests
{
    public static void Run(string[] args)
    {
        foreach (var (label, path) in new[]
        {
            ("Java 1.16.5", Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension")),
            ("Java 1.12.2 Twilight Forest", Path.Combine(TestPaths.Data, "amulet", "java", "forge", "1_12_2_twilight_forest")),
            ("Bedrock 1.16", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_16")),
            ("Bedrock 1.18", Path.Combine(TestPaths.Data, "amulet", "bedrock", "vanilla", "1_18", "vanilla")),
        })
        {
            if (!Directory.Exists(path)) { Program.Info($"(skipped {label}: not in TestData)"); continue; }
            var before = Hashes(path);
            using (var w = WorldLoader.Open(path))
            {
                long all = 0;
                foreach (var dim in w.Dimensions)
                {
                    var census = ChunkCensus.Run(w, dim, keep: int.MaxValue);
                    var search = WorldSearch.Run(w, dim, new SearchOptions { Query = "*", Blocks = false, Items = false, Text = false, Players = false, Entities = true, MaxHits = 1_000_000 });
                    var a = census.EntityTotals.Where(kv => kv.Key != "?").ToDictionary(kv => kv.Key.Replace("minecraft:", ""), kv => kv.Value);
                    bool same = a.Count == search.EntityCounts.Count && a.All(kv => search.EntityCounts.GetValueOrDefault(kv.Key) == kv.Value);
                    long sumEntities = census.Chunks.Sum(c => (long)c.Entities), sumTiles = census.Chunks.Sum(c => (long)c.BlockEntities);
                    bool adds = sumEntities == census.EntityTotals.Values.Sum() && sumTiles == census.BlockEntityTotals.Values.Sum();
                    bool sorted = census.Chunks.Zip(census.Chunks.Skip(1)).All(p => p.First.Load >= p.Second.Load) && census.Chunks.All(c => c.Most.Count is >= 1 and <= 3 && c.Load > 0);
                    bool dropped = census.Chunks.Sum(c => (long)c.Dropped) == census.EntityTotals.Where(kv => kv.Key is "minecraft:item" or "minecraft:experience_orb").Sum(kv => kv.Value);
                    all += sumEntities + sumTiles;
                    Program.Check(same && adds && sorted && dropped, $"{label} {dim.DisplayName}: {census.EntityTotals.Values.Sum():N0} entities and {census.BlockEntityTotals.Values.Sum():N0} block entities in {census.Chunks.Count:N0} busy chunks — the same counts as Find, adding up, busiest first"
                        + (same ? "" : $" (Find: {string.Join(", ", search.EntityCounts.Take(5))}; census: {string.Join(", ", a.Take(5))})"));
                }
                Program.Info($"{label}: {all:N0} things counted");
            }
            Program.Check(before.SequenceEqual(Hashes(path)), $"{label}: nothing in the world changed");
        }
        Cleared();
    }

    /// <summary>
    /// The window's "Clear dropped items and experience orbs": a few items and orbs put into two chunks of a copy, cleared
    /// through the lag machines' clean-up into another copy — none left there, the mobs and every other chunk as they were.
    /// </summary>
    static void Cleared()
    {
        string java = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(java)) return;
        string work = Path.Combine(TestPaths.Output, "census-clear");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        try
        {
            string dirty = Path.Combine(work, "dirty"), clean = Path.Combine(work, "clean");
            List<ChunkPos> chosen;
            using (var w = WorldLoader.Open(java))
            {
                var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
                chosen = dim.ChunkPositions.OrderBy(p => Math.Abs(p.X) + Math.Abs(p.Z)).Take(2).ToList();
                var r = MinecraftStudio.Core.Structures.StructurePlacement.EditIntoCopy(w, "minecraft:overworld", chosen, c =>
                {
                    for (int i = 0; i < 12; i++)
                    {
                        var pos = new MinecraftStudio.Core.Nbt.NbtList(MinecraftStudio.Core.Nbt.NbtType.Double) { new MinecraftStudio.Core.Nbt.NbtDouble(c.X * 16 + 3 + i), new MinecraftStudio.Core.Nbt.NbtDouble(90), new MinecraftStudio.Core.Nbt.NbtDouble(c.Z * 16 + 5) };
                        var item = new MinecraftStudio.Core.Nbt.NbtCompound { ["id"] = new MinecraftStudio.Core.Nbt.NbtString(i % 3 == 0 ? "minecraft:experience_orb" : "minecraft:item"), ["Pos"] = pos, ["UUID"] = new MinecraftStudio.Core.Nbt.NbtIntArray([7, 7, c.X, i]) };
                        if (i % 3 != 0) item["Item"] = new MinecraftStudio.Core.Nbt.NbtCompound { ["id"] = new MinecraftStudio.Core.Nbt.NbtString("minecraft:cobblestone"), ["count"] = new MinecraftStudio.Core.Nbt.NbtInt(3) };
                        else item["Value"] = new MinecraftStudio.Core.Nbt.NbtShort(5);
                        c.Entities.Add(item);
                    }
                }, dirty);
                Program.Check(r.Verified, "census clear: dropped items and orbs put into two chunks of a copy");
                dirty = r.OpenPath;
            }
            Dictionary<ChunkPos, BusyChunk> Census(string path)
            {
                using var w = WorldLoader.Open(path);
                return ChunkCensus.Run(w, w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld"), keep: int.MaxValue).Chunks.ToDictionary(c => c.Pos);
            }
            var before = Census(dirty);
            using (var w = WorldLoader.Open(dirty))
            {
                string dimId = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld").Id;
                var machines = chosen.SelectMany(p => new[] { LagKind.DroppedItems, LagKind.ExperienceOrbs }.Select(k =>
                    new LagMachine(k, dimId, "", p.X * 16 + 8, 64, p.Z * 16 + 8, (p.X * 16, 64, p.Z * 16, p.X * 16 + 15, 64, p.Z * 16 + 15), 0, []))).ToList();
                var r = LagMachines.CleanIntoCopy(w, machines, clean);
                Program.Check(r.Verified && r.Notes.Any(n => n.Contains("took out")), $"census clear: the two chunks cleared in a copy ({string.Join(" | ", r.Notes)})");
                clean = r.OpenPath;
            }
            var after = Census(clean);
            bool none = chosen.All(p => !after.TryGetValue(p, out var c) || c.Dropped == 0);
            bool mobsKept = chosen.All(p => (before.TryGetValue(p, out var b) ? b.Entities - b.Dropped : 0) == (after.TryGetValue(p, out var a) ? a.Entities - a.Dropped : 0));
            bool rest = before.Where(kv => !chosen.Contains(kv.Key)).All(kv => after.TryGetValue(kv.Key, out var a)
                && a.Entities == kv.Value.Entities && a.Dropped == kv.Value.Dropped && a.BlockEntities == kv.Value.BlockEntities && a.Most.SequenceEqual(kv.Value.Most));
            Program.Check(before.Where(kv => chosen.Contains(kv.Key)).Sum(kv => kv.Value.Dropped) >= 24 && none && mobsKept && rest,
                $"census clear: no dropped item or orb left in the cleared chunks, their mobs and every other chunk as they were (before: {string.Join(", ", chosen.Select(p => before.GetValueOrDefault(p)?.Dropped ?? 0))} dropped)");
        }
        catch (Exception ex) { Program.Check(false, $"census clear threw {ex.GetType().Name}: {ex.Message}"); }
        finally { try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { } }
    }

    internal static List<string> Hashes(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(f => Path.GetRelativePath(folder, f) + " " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))).ToList();
}
