// MinecraftStudio.Core/Structures/StructureMaterials.cs: what a structure takes to build — its material list.
// Owns: StructureMaterials and MaterialLine. Every block counted as the item that places it: a wall torch is a torch, a
// door or a bed counts once (not per half), a double slab is two slabs, four candles are four, a potted fern is a pot and
// a fern, a crop its seeds, still water a bucket; what can't be placed (portals, fire, moving pistons, flowing water) is
// left out. Paintings, item frames and armour stands count too. Text() lines up the list to copy; Csv() is for a sheet.
using System.Globalization;
using System.Text;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

/// <summary>One kind of item a structure needs, and how many.</summary>
public sealed record MaterialLine(string Item, long Count)
{
    /// <summary>The item in plain words ("oak planks").</summary>
    public string Name => WorldSearch.Nice(Item);

    /// <summary>How many fit in one inventory slot.</summary>
    public int StackSize => StructureMaterials.StackSize(Item);

    /// <summary>The count in stacks: "3 stacks + 12", "1 stack", "40".</summary>
    public string Stacks
    {
        get
        {
            int size = StackSize;
            if (size <= 1 || Count < size) return Count.ToString("N0", CultureInfo.CurrentCulture);
            long stacks = Count / size, rest = Count % size;
            return $"{stacks:N0} stack{(stacks == 1 ? "" : "s")}{(rest > 0 ? $" + {rest}" : "")}{(size != 64 ? $" (of {size})" : "")}";
        }
    }
}

public static class StructureMaterials
{
    /// <summary>Blocks no item places (or that the game makes itself): left out of the list.</summary>
    static readonly HashSet<string> NotPlaced = new(StringComparer.Ordinal)
    {
        "air", "cave_air", "void_air", "structure_void", "moving_piston", "piston_head", "nether_portal", "end_portal", "end_gateway",
        "fire", "soul_fire", "bubble_column", "frosted_ice", "frogspawn",
    };

    /// <summary>Blocks whose item has another name.</summary>
    static readonly Dictionary<string, string> ItemOf = new(StringComparer.Ordinal)
    {
        ["redstone_wire"] = "redstone", ["tripwire"] = "string", ["cocoa"] = "cocoa_beans", ["carrots"] = "carrot", ["potatoes"] = "potato",
        ["beetroots"] = "beetroot_seeds", ["wheat"] = "wheat_seeds", ["melon_stem"] = "melon_seeds", ["attached_melon_stem"] = "melon_seeds",
        ["pumpkin_stem"] = "pumpkin_seeds", ["attached_pumpkin_stem"] = "pumpkin_seeds", ["torchflower_crop"] = "torchflower_seeds",
        ["pitcher_crop"] = "pitcher_pod", ["sweet_berry_bush"] = "sweet_berries", ["cave_vines"] = "glow_berries", ["cave_vines_plant"] = "glow_berries",
        ["kelp_plant"] = "kelp", ["weeping_vines_plant"] = "weeping_vines", ["twisting_vines_plant"] = "twisting_vines", ["bamboo_sapling"] = "bamboo",
        ["powder_snow"] = "powder_snow_bucket", ["big_dripleaf_stem"] = "big_dripleaf", ["tall_seagrass"] = "seagrass",
        ["farmland"] = "dirt", ["dirt_path"] = "dirt", ["wall_torch"] = "torch",
        ["water_cauldron"] = "cauldron", ["lava_cauldron"] = "cauldron", ["powder_snow_cauldron"] = "cauldron",
        ["potted_azalea_bush"] = "azalea", ["potted_flowering_azalea_bush"] = "flowering_azalea",
    };

    /// <summary>Two-block-tall blocks: only their lower half is counted.</summary>
    static readonly HashSet<string> TallPlants = new(StringComparer.Ordinal)
    {
        "sunflower", "lilac", "rose_bush", "peony", "tall_grass", "large_fern", "pitcher_plant", "tall_seagrass", "small_dripleaf",
    };

    /// <summary>The material list: every item the structure takes, most first.</summary>
    public static List<MaterialLine> Count(Structure s)
    {
        var perState = new Dictionary<BlockState, long>();
        foreach (var b in s.AllBlocks) if (!b.IsAir) perState[b] = perState.GetValueOrDefault(b) + 1;
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (state, n) in perState)
            foreach (var (item, each) in ItemsFor(state)) totals[item] = totals.GetValueOrDefault(item) + n * each;
        foreach (var e in s.Entities)
        {
            string id = e.Nbt.GetString("id") ?? "";
            if (id is "minecraft:painting" or "minecraft:item_frame" or "minecraft:glow_item_frame" or "minecraft:armor_stand") totals[id] = totals.GetValueOrDefault(id) + 1;
        }
        return totals.Select(kv => new MaterialLine(kv.Key, kv.Value)).OrderByDescending(l => l.Count).ThenBy(l => l.Item, StringComparer.Ordinal).ToList();
    }

    /// <summary>The items that place one block in this state (none for what no item places).</summary>
    public static IEnumerable<(string Item, int Count)> ItemsFor(BlockState b)
    {
        string n = b.BaseName, ns = b.Name.Length > n.Length ? b.Name[..^n.Length] : "";
        if (b.IsAir || NotPlaced.Contains(n)) yield break;
        // Liquids: a bucket per source; flowing water and lava come from the sources.
        if (n is "water" or "lava")
        {
            if (b.GetProperty("level") is null or "0") yield return (ns + n + "_bucket", 1);
            yield break;
        }
        // Only one half of what stands two blocks tall.
        if (n.EndsWith("_door", StringComparison.Ordinal) && b.GetProperty("half") == "upper") yield break;
        if (TallPlants.Contains(n) && b.GetProperty("half") == "upper") yield break;
        if (n.EndsWith("_bed", StringComparison.Ordinal) && b.GetProperty("part") == "head") yield break;
        if (n.StartsWith("potted_", StringComparison.Ordinal))
        {
            yield return (ns + "flower_pot", 1);
            yield return (ns + (ItemOf.TryGetValue(n, out var plant) ? plant : n[7..]), 1);
            yield break;
        }
        if (n.EndsWith("candle_cake", StringComparison.Ordinal))
        {
            yield return (ns + "cake", 1);
            yield return (ns + (n == "candle_cake" ? "candle" : n[..^5]), 1);
            yield break;
        }
        string item = ItemOf.TryGetValue(n, out var renamed) ? renamed
            // Wall-mounted forms are placed with the standing item: oak_wall_sign → oak_sign, skeleton_wall_skull → skeleton_skull.
            : n.Contains("_wall_", StringComparison.Ordinal) ? n.Replace("_wall_", "_")
            : n;
        int count = 1;
        if (n.EndsWith("_slab", StringComparison.Ordinal) && b.GetProperty("type") == "double") count = 2;
        foreach (var key in new[] { "candles", "pickles", "eggs", "flower_amount", "segment_amount", "layers" })
            if (b.GetProperty(key) is string v && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) && k > 0) { count = k; break; }
        yield return (ns + item, count);
    }

    /// <summary>How many of an item fit in one slot (beds, cakes and filled buckets 1; signs and banners 16; most 64).</summary>
    public static int StackSize(string item)
    {
        string n = item.StartsWith("minecraft:", StringComparison.Ordinal) ? item[10..] : item;
        if (n.EndsWith("_bed", StringComparison.Ordinal) || n is "cake" or "water_bucket" or "lava_bucket" or "powder_snow_bucket") return 1;
        if (n.EndsWith("_sign", StringComparison.Ordinal) || n.EndsWith("_banner", StringComparison.Ordinal) || n is "armor_stand") return 16;
        return 64;
    }

    /// <summary>The list as lined-up text: count, stacks, item.</summary>
    public static string Text(IReadOnlyList<MaterialLine> lines, string? title = null)
    {
        var sb = new StringBuilder();
        if (title != null) sb.AppendLine(title).AppendLine();
        int width = lines.Count == 0 ? 1 : lines.Max(l => l.Count.ToString("N0", CultureInfo.CurrentCulture).Length);
        foreach (var l in lines)
            sb.Append(l.Count.ToString("N0", CultureInfo.CurrentCulture).PadLeft(width)).Append("  ").Append(l.Name)
              .Append(l.Count >= l.StackSize && l.StackSize > 1 ? $"  ({l.Stacks})" : "").AppendLine();
        sb.AppendLine().Append($"{lines.Count:N0} kinds, {lines.Sum(l => l.Count):N0} items in all");
        return sb.ToString();
    }

    /// <summary>The list as CSV (item id, name, count, stacks), for a spreadsheet.</summary>
    public static string Csv(IReadOnlyList<MaterialLine> lines)
    {
        var sb = new StringBuilder("Item,Name,Count,Stacks\r\n");
        foreach (var l in lines)
            sb.Append(l.Item).Append(',').Append(l.Name).Append(',').Append(l.Count.ToString(CultureInfo.InvariantCulture)).Append(",\"").Append(l.Stacks.Replace("\"", "'")).Append("\"\r\n");
        return sb.ToString();
    }
}
