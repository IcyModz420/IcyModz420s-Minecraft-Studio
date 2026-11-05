// McTests/DowngradeTests.cs: downgrade: undoes the Bedrock upgrade schemas for every canonical state and
// several game versions.
// Owns: DowngradeTests.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

/// <summary>downgrade: undoes the Bedrock upgrade schemas for every canonical state and several game versions.</summary>
static class DowngradeTests
{
    static int V(int major, int minor, int patch) => BedrockBlocks.MakeVersion(major, minor, patch, 0);

    static string Show((string Name, Dictionary<string, NbtTag> States)? d) =>
        d is var (n, st) ? n + "{" + string.Join(",", st.OrderBy(k => k.Key).Select(kv => $"{kv.Key}={BedrockBlocks.TagText(kv.Value)}({kv.Value.Type})")) + "}" : "(none)";

    public static void Run(string[] args)
    {
        var games = new (string Name, int Version)[]
        {
            ("1.21.40", V(1, 21, 40)), ("1.21.0", V(1, 21, 0)), ("1.20.0", V(1, 20, 0)), ("1.19.40", V(1, 19, 40)), ("1.18.30", V(1, 18, 30)),
            ("1.18.0", V(1, 18, 0)), ("1.16.210", V(1, 16, 210)), ("1.16.0", V(1, 16, 0)), ("1.13.0", V(1, 13, 0)),
        };
        var all = BedrockBlocks.CanonicalStates.ToList();
        foreach (var (name, v) in games)
        {
            int ok = 0, changed = 0;
            var failures = new List<string>();
            foreach (var s in all)
            {
                var d = BedrockBlocks.Downgrade(s, v);
                if (d == null) { if (failures.Count < 6) failures.Add(s.ToString()); continue; }
                ok++;
                if (d.Value.Name != s.Name) changed++;
            }
            Program.Info($"{name}: palette version {BedrockBlocks.VersionText(BedrockBlocks.StateVersionFor(v))}, {ok:N0}/{all.Count:N0} states have an older form ({changed:N0} renamed); failures e.g. {string.Join("  ", failures)}");
            Program.Check(ok > all.Count * 0.97, $"{name}: at least 97% of states downgrade");
        }
        foreach (var (state, game, version) in new[]
        {
            ("minecraft:white_wool", "1.19.0", V(1, 19, 0)), ("minecraft:granite", "1.20.0", V(1, 20, 0)), ("minecraft:stone", "1.20.0", V(1, 20, 0)),
            ("minecraft:poppy", "1.20.0", V(1, 20, 0)), ("minecraft:short_grass", "1.20.0", V(1, 20, 0)), ("minecraft:oak_log", "1.19.0", V(1, 19, 0)),
            ("minecraft:stripped_oak_log", "1.19.0", V(1, 19, 0)), ("minecraft:dirt", "1.20.0", V(1, 20, 0)), ("minecraft:grass_block", "1.19.0", V(1, 19, 0)),
            ("minecraft:cobblestone_wall", "1.20.0", V(1, 20, 0)), ("minecraft:oak_planks", "1.20.0", V(1, 20, 0)), ("minecraft:smooth_stone_slab", "1.20.0", V(1, 20, 0)),
            ("minecraft:light_block_7", "1.21.0", V(1, 21, 0)), ("minecraft:infested_stone", "1.21.0", V(1, 21, 0)), ("minecraft:red_concrete", "1.19.0", V(1, 19, 0)),
            ("minecraft:oak_fence", "1.21.0", V(1, 21, 0)), ("minecraft:chest", "1.16.0", V(1, 16, 0)), ("minecraft:stone_bricks", "1.20.0", V(1, 20, 0)), ("minecraft:white_stained_glass", "1.19.0", V(1, 19, 0)), ("minecraft:tube_coral_block", "1.20.0", V(1, 20, 0)), ("minecraft:oak_sapling", "1.20.0", V(1, 20, 0)), ("minecraft:sandstone", "1.20.0", V(1, 20, 0)), ("minecraft:white_terracotta", "1.19.0", V(1, 19, 0)), ("minecraft:mossy_cobblestone_wall", "1.20.0", V(1, 20, 0)), ("minecraft:mangrove_log", "1.20.0", V(1, 20, 0)),
        })
        {
            var s = BedrockBlocks.StatesOf(state).FirstOrDefault();
            if (s == null) { Program.Info($"{state}: not canonical"); continue; }
            Program.Info($"{s} @ {game} -> {Show(BedrockBlocks.Downgrade(s, version))}");
        }
    }
}
