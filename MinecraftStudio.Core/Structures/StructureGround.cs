// Structures/StructureGround.cs: the height to place a structure at so that it stands on the ground.
// Owns: StructureGround — from the top block of each column under its footprint (what a map draws): a plant, flower, snow
// layer or torch stands on the ground (one block lower; a tall plant's top two), tree tops (leaves, logs, huge mushrooms)
// are left out, water counts as it is. The middle height (median) of the rest wins, so a few trees or a hole neither lift
// nor sink it. The structure's bottom layer then takes the place of the ground's top block.
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public static class StructureGround
{
    /// <summary>
    /// The ground's height under a footprint from each column's top block and its height (block null: not known), or null
    /// when there are no columns. <paramref name="bedrockNames"/>: the blocks carry Bedrock's names (its "grass" is the grass block).
    /// </summary>
    public static int? GroundY(IEnumerable<(int Y, BlockState? Top)> columns, bool bedrockNames)
    {
        var ground = new List<int>();
        var all = new List<int>();
        foreach (var (y, top) in columns)
        {
            all.Add(y);
            if (top == null) { ground.Add(y); continue; }
            if (HardcoreRestore.Cover(top, bedrockNames)) { ground.Add(y - (UpperHalf(top) ? 2 : 1)); continue; }
            if (TreeTop(HardcoreRestore.BaseName(top, bedrockNames))) continue;
            ground.Add(y);
        }
        // Under trees only: their tops are all there is to go by.
        var use = ground.Count > 0 ? ground : all;
        if (use.Count == 0) return null;
        use.Sort();
        return use[(use.Count - 1) / 2];
    }

    /// <summary>Whether a block is the ground itself, not a plant, cover or tree top standing on it (air is not ground).</summary>
    public static bool IsGround(BlockState s, bool bedrockNames) =>
        !s.IsAir && !HardcoreRestore.Cover(s, bedrockNames) && !TreeTop(HardcoreRestore.BaseName(s, bedrockNames));

    // The top half of a plant two blocks tall (Java half=upper, Bedrock upper_block_bit).
    static bool UpperHalf(BlockState s)
    {
        foreach (var (k, v) in s.PropertyPairs())
            if (k == "half" && v == "upper" || k == "upper_block_bit" && v is "true" or "1") return true;
        return false;
    }

    // Leaves, logs and stems, huge mushrooms, bamboo and cacti: what stands up out of the ground rather than being it.
    static bool TreeTop(string n) =>
        n.Contains("leaves", StringComparison.Ordinal) || n.EndsWith("_log", StringComparison.Ordinal) || n.EndsWith("_wood", StringComparison.Ordinal)
        || n.EndsWith("_stem", StringComparison.Ordinal) || n.EndsWith("_hyphae", StringComparison.Ordinal) || n.EndsWith("mushroom_block", StringComparison.Ordinal)
        || n is "log" or "log2" or "bamboo" or "cactus" or "mangrove_roots" or "chorus_plant" or "chorus_flower";
}
