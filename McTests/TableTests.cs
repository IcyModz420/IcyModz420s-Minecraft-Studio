// McTests/TableTests.cs: TableTests.
// Owns: TableTests.
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

static class TableTests
{
    /// <summary>tables : checks the built-in legacy id switch against the flattening table and that colour/biome tables load.</summary>
    public static void Run(string[] args)
    {
        string csv = Path.Combine(TestPaths.Repo, "MinecraftStudio.Core", "Data", "flattening.csv");
        int ok = 0, bad = 0;
        foreach (var line in File.ReadLines(csv).Skip(1))
        {
            var p = Biomes_SplitCsv(line);
            if (p.Count < 4 || !int.TryParse(p[0], out int id)) continue;
            string expected = p[3].Trim();
            foreach (int data in ExpandData(p[2]))
            {
                string mine = BlockState.Parse(LegacyBlocks.JavaName(id, data)).Name;
                // 1.13 names that were renamed later (grass -> short_grass, grass_path -> dirt_path, stone_slab -> smooth_stone_slab)
                string norm = expected switch
                {
                    "minecraft:grass" => "minecraft:short_grass",
                    "minecraft:grass_path" => "minecraft:dirt_path",
                    "minecraft:stone_slab" => "minecraft:smooth_stone_slab",
                    "minecraft:sign" => "minecraft:oak_sign",
                    "minecraft:wall_sign" => "minecraft:oak_wall_sign",
                    _ => expected,
                };
                if (mine == norm) ok++;
                else { bad++; if (bad <= 40) Program.Info($"{id}:{data} expected {expected} got {mine}"); }
            }
        }
        Program.Check(bad == 0, $"legacy id table agrees with flattening.csv ({ok} ok, {bad} differ)");

        int colored = 0, total = 0;
        foreach (var line in File.ReadLines(Path.Combine(TestPaths.Repo, "MinecraftStudio.Core", "Data", "block_colors.csv")).Skip(1))
        {
            total++;
            var s = BlockState.Get(line.Split(',')[0]);
            if (BlockColors.Lookup(s).Color != 0xFFB040B0u) colored++;
        }
        Program.Check(colored == total, $"every block in the colour table resolves ({colored}/{total})");
        Program.Check(Biomes.All.Count >= 90, $"biome table loaded ({Biomes.All.Count} biomes)");
        Program.Check(Biomes.NameOf(1, BiomeIdScheme.Java) == "plains" && Biomes.NameOf(21, BiomeIdScheme.Bedrock) == "jungle", "biome ids resolve per edition");
        Program.Info($"cherry_grove tint {Biomes.Get("minecraft:cherry_grove").Grass:X6}, swamp {Biomes.Get("swamp").Grass:X6}, bedrock id 6 = {Biomes.NameOf(6, BiomeIdScheme.Bedrock)}");
    }

    static IEnumerable<int> ExpandData(string spec)
    {
        foreach (var part in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Contains('-'))
            {
                var ab = part.Split('-');
                for (int i = int.Parse(ab[0]); i <= int.Parse(ab[1]); i++) yield return i;
            }
            else if (int.TryParse(part, out int v)) yield return v;
        }
    }

    static List<string> Biomes_SplitCsv(string line)
    {
        var list = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool q = false;
        foreach (char ch in line)
        {
            if (ch == '"') q = !q;
            else if (ch == ',' && !q) { list.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        list.Add(sb.ToString());
        return list;
    }
}
