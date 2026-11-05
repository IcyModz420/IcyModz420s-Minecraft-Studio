// MinecraftStudio.Core/Conversion/ConsoleBlocks.cs: Legacy Console block values. Ids below 256 are Java 1.12
// ids (already canonical through the flattening table).
// Owns: ConsoleBlocks.
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Legacy Console block values. Ids below 256 are Java 1.12 ids (already canonical through the flattening table).
/// The Update Aquatic builds (TU69+, save version 11+, 16-bit section chunks) store newer blocks as ids from 256 up;
/// no public table exists, so these were identified from real saves (placement, stacking and data patterns). Their
/// data values follow Bedrock's legacy meanings, so they decode through Bedrock's 1.12 block table.
/// </summary>
public static class ConsoleBlocks
{
    static readonly Dictionary<int, string> Extended = new()
    {
        [257] = "minecraft:pumpkin",        // uncarved pumpkin (grass below turns to dirt), data = facing
        [258] = "minecraft:kelp",           // stacked in ocean columns, data = age
        [259] = "minecraft:coral_block",    // reef blocks, data = colour | 8 dead
        [263] = "minecraft:coral",          // reef plants, data = colour
        [264] = "minecraft:coral_fan",      // floor fans, data = colour | 8 direction
        [265] = "minecraft:coral_fan_dead", // (by position in the sequence; not seen in sample saves)
        [266] = "minecraft:coral_fan_hang", // wall fans tube/brain: type bit, dead bit, direction
        [267] = "minecraft:coral_fan_hang2",// wall fans bubble/fire
        [268] = "minecraft:coral_fan_hang3",// wall fans horn
        [270] = "minecraft:seagrass",       // data 0 single, 2 tall bottom, 1 tall top
        [271] = "minecraft:sea_pickle",     // data = count - 1 | 4 dead
        [272] = "minecraft:bubble_column",  // columns above magma (data 1 = drag down) / soul sand
    };

    /// <summary>Console ids for blocks the reverse direction can write on TU69+ (Bedrock legacy name → console id).</summary>
    public static IReadOnlyDictionary<int, string> ExtendedIds => Extended;

    public static BlockState ToCanonical(BlockState s)
    {
        if (!s.Name.StartsWith("lce:block_", StringComparison.Ordinal)) return s;
        if (!int.TryParse(s.Name.AsSpan(10), out int id) || !Extended.TryGetValue(id, out var bedrockName)) return s;
        int data = int.TryParse(s.GetProperty("data"), out int d) ? d : 0;
        var bedrock = BedrockBlocks.FromLegacyName(bedrockName, data);
        if (bedrock == null) return s;
        var java = CanonicalReader.BedrockToJava(bedrock);
        if (s.GetProperty("waterlogged") == "true" && java.GetProperty("waterlogged") == "false") java = StateEdit.With(java, "waterlogged", "true");
        return java;
    }
}
