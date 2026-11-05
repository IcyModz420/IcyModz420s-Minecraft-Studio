// MinecraftStudio/UI/MapMarks.cs: what a world map marks — the world spawn (on the overworld), the players in a dimension,
// and where each last died (Java 1.19+ LastDeathLocation; Bedrock's DeathPosition, written once a player has died).
// Owns: MapMarks — shared by the world tab's map and the map pickers of the crop and place windows.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.UI;

static class MapMarks
{
    /// <summary>The world spawn, marked only on the overworld's map.</summary>
    public static (int X, int Z)? Spawn(SaveWorld world, WorldDimension? d)
    {
        if (d == null || d.Id != "minecraft:overworld") return null;
        NbtCompound? data = world.LevelData?.Roots.FirstOrDefault()?.Tag as NbtCompound;
        if (data?.GetCompound("Data") is NbtCompound jd) data = jd;
        if (data?.GetLongOrNull("SpawnX") is long x && data.GetLongOrNull("SpawnZ") is long z) return ((int)x, (int)z);
        if (data?.GetCompound("spawn")?.GetInts("pos") is { Length: 3 } p) return (p[0], p[2]);
        if (world is PocketLegacyWorld { RawLevel: { } raw }) return (raw.SpawnX, raw.SpawnZ);
        if (world is Core.Legacy.FiniteWorld { Classic: { } c }) return (c.SpawnX, c.SpawnZ);
        if (world is Core.Legacy.FiniteWorld { Indev: { } i }) return (i.Spawn.X, i.Spawn.Z);
        return null;
    }

    /// <summary>Every player standing in the dimension: a short name and where they are.</summary>
    public static List<(string Name, double X, double Z)> Players(SaveWorld world, WorldDimension? d)
    {
        var list = new List<(string, double, double)>();
        if (d == null) return list;
        foreach (var p in world.Players)
        {
            var pos = p.Data.GetList("Pos");
            if (pos == null || pos.Count < 3) continue;
            var dimTag = p.Data["Dimension"] ?? p.Data["DimensionId"];
            string dimId = dimTag switch
            {
                NbtString s => s.Value,
                { IsNumeric: true } n => n.AsLong switch { -1 => "minecraft:the_nether", 1 when world is not BedrockWorld => "minecraft:the_end", 1 => "minecraft:the_nether", 2 => "minecraft:the_end", _ => "minecraft:overworld" },
                _ => "minecraft:overworld",
            };
            if (dimId != d.Id) continue;
            string name = p.Name.Length > 18 ? p.Name[..18] + "…" : p.Name;
            list.Add((name, pos[0].AsDouble, pos[2].AsDouble));
        }
        return list;
    }

    /// <summary>Where each player last died, in this dimension: "Steve died here" and the block.</summary>
    public static List<(string Name, double X, double Z)> Deaths(SaveWorld world, WorldDimension? d)
    {
        var list = new List<(string, double, double)>();
        if (d == null) return list;
        foreach (var p in world.Players)
        {
            string? dimId = null;
            int x = 0, z = 0;
            if (p.Data.GetCompound("LastDeathLocation") is { } last && last.GetInts("pos") is { Length: 3 } at) { dimId = last.GetString("dimension"); x = at[0]; z = at[2]; }
            else if (p.Data.GetLongOrNull("DeathPositionX") is long bx && p.Data.GetLongOrNull("DeathPositionZ") is long bz)
            {
                dimId = p.Data.GetInt("DeathDimension") switch { 1 => "minecraft:the_nether", 2 => "minecraft:the_end", _ => "minecraft:overworld" };
                (x, z) = ((int)bx, (int)bz);
            }
            if (dimId != d.Id) continue;
            string name = p.Name.Length > 18 ? p.Name[..18] + "…" : p.Name;
            list.Add((name + " died here", x + 0.5, z + 0.5));
        }
        return list;
    }
}
