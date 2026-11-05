// MinecraftStudio.Core/Conversion/VerticalShift.cs: Moves an overworld up or down by whole sections, for
// targets that are only 256 blocks high: raising a 1.18+ world by 64 keeps y −64…191 (the deep caves and the
// surface) instead of cutting everything below y 0.
// Owns: VerticalShift.
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Moves an overworld up or down by whole sections, for targets that are only 256 blocks high: raising a 1.18+ world by
/// 64 keeps y −64…191 (the deep caves and the surface) instead of cutting everything below y 0. Blocks, biomes, block
/// entities, entities (and the positions they remember), players and the world spawn move together.
/// </summary>
public static class VerticalShift
{
    /// <summary>Entity tags holding one y coordinate.</summary>
    static readonly string[] YKeys = ["TileY", "SleepingY", "HomePosY", "TravelPosY", "AY", "BoundY", "APY", "yTile", "BeamTargetY"];
    /// <summary>Entity tags holding an {X, Y, Z} compound.</summary>
    static readonly string[] XyzCompounds = ["HivePos", "FlowerPos", "PatrolTarget", "WanderTarget", "BeamTarget", "Leash", "ExitPortal"];
    /// <summary>Entity and block entity tags holding an int[3] position.</summary>
    static readonly string[] PosArrays = ["sleeping_pos", "hive_pos", "flower_pos", "patrol_target", "wander_target", "beam_target", "anchor_pos", "bound_pos", "leash", "home_pos", "exit_portal", "block_pos"];

    public static void Apply(CanonicalChunk c, int dy)
    {
        if (dy == 0) return;
        if (dy % 16 != 0) throw new ArgumentException("The shift must be a whole number of sections (a multiple of 16).", nameof(dy));
        int ds = dy / 16;
        c.SeaLevelY += dy;
        var sections = c.Sections.Values.ToList();
        c.Sections.Clear();
        foreach (var s in sections)
            c.Sections[s.Y + ds] = new BlockSection(s.Y + ds, s.Palette, s.Indices) { Biomes = s.Biomes };
        var biomes = c.Biomes.ToList();
        c.Biomes.Clear();
        foreach (var (y, b) in biomes) c.Biomes[y + ds] = b;
        foreach (var be in c.BlockEntities)
        {
            be.SetInt("y", be.GetInt("y") + dy);
            ShiftPositions(be, dy);
            // Bees in a hive remember their flower and hive.
            foreach (var bee in be.GetList("Bees")?.OfType<NbtCompound>() ?? [])
                if (bee.GetCompound("EntityData") is NbtCompound data) Entity(data, dy);
            foreach (var bee in be.GetList("bees")?.OfType<NbtCompound>() ?? [])
                if (bee.GetCompound("entity_data") is NbtCompound data) Entity(data, dy);
        }
        foreach (var e in c.Entities) Entity(e, dy);
    }

    /// <summary>One entity (canonical Java form) and its passengers.</summary>
    public static void Entity(NbtCompound e, int dy)
    {
        if (e.GetList("Pos") is NbtList { Count: 3 } pos && pos[1].IsNumeric)
            pos[1] = new NbtDouble(pos[1].AsDouble + dy);
        ShiftPositions(e, dy);
        // Villagers' job site, home and meeting point.
        if (e.GetCompound("Brain")?.GetCompound("memories") is NbtCompound memories)
            foreach (var (_, m) in memories)
                if (m is NbtCompound mem && mem.GetCompound("value") is NbtCompound v && v.GetString("dimension") is null or "minecraft:overworld")
                    ShiftArray(v, "pos", dy);
        foreach (var p in e.GetList("Passengers")?.OfType<NbtCompound>() ?? []) Entity(p, dy);
    }

    static void ShiftPositions(NbtCompound t, int dy)
    {
        foreach (var key in YKeys)
            if (t[key] is { IsNumeric: true } v) t[key] = v switch { NbtDouble d => new NbtDouble(d.Value + dy), NbtFloat f => new NbtFloat(f.Value + dy), _ => new NbtInt((int)v.AsLong + dy) };
        foreach (var key in XyzCompounds)
            if (t.GetCompound(key) is NbtCompound p && p["Y"] is { IsNumeric: true } y) p.SetInt("Y", (int)y.AsLong + dy);
        foreach (var key in PosArrays) ShiftArray(t, key, dy);
    }

    static void ShiftArray(NbtCompound t, string key, int dy)
    {
        if (t[key] is NbtIntArray { Value.Length: 3 } a) t[key] = new NbtIntArray([a.Value[0], a.Value[1] + dy, a.Value[2]]);
    }

    /// <summary>The world spawn and every player standing (or respawning) in the overworld.</summary>
    public static void Apply(WorldInfo info, int dy)
    {
        if (dy == 0) return;
        info.SpawnY += dy;
        foreach (var p in info.Players)
        {
            var d = p.Data;
            bool overworld = (d.GetString("Dimension") ?? p.Dimension) == "minecraft:overworld";
            if (overworld && d.GetList("Pos") is NbtList { Count: 3 } pos && pos[1].IsNumeric)
                pos[1] = new NbtDouble(pos[1].AsDouble + dy);
            // What they ride, kept with them, moves with them.
            if (overworld && d.GetCompound("RootVehicle")?.GetCompound("Entity") is NbtCompound mount) Entity(mount, dy);
            if (d.GetLongOrNull("SpawnY") is long sy && (d.GetString("SpawnDimension") ?? "minecraft:overworld") == "minecraft:overworld")
                d.SetInt("SpawnY", (int)sy + dy);
        }
    }

    /// <summary>
    /// The usual shift for a target: +64 when a 1.18+ overworld (blocks below y 0) goes to a version that is 256 blocks
    /// high and the user chose to keep the depths; otherwise 0.
    /// </summary>
    public static bool Applies(ConversionTarget target) => WorldConverter.TargetHeight(target, "minecraft:overworld") is (0, 255);
}
