// MinecraftStudio.Core/Structures/StructurePositions.cs: the block positions entities and block entities remember.
// Owns: StructurePositions — a bee's hive and flower, a villager's bed and job site, a turtle's home, a leash's knot, a
// vex's bounds… (the canonical fields VerticalShift also moves), each passed through a function. A structure that is
// moved, turned or mirrored takes along what pointed inside it; what pointed outside it stays where it was. The block a
// painting or frame hangs in is Hanging's (worked out from its centre); an end gateway's exit is never moved.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Structures;

public static class StructurePositions
{
    /// <summary>A remembered block position's new place, or null to leave it as it is.</summary>
    public delegate (int X, int Y, int Z)? Map(int x, int y, int z);

    /// <summary>Entity tags holding an x, y, z triple of ints (beds, turtle homes, vex bounds, shulker attachment, old arrows).</summary>
    static readonly (string X, string Y, string Z)[] Triples =
    [
        ("SleepingX", "SleepingY", "SleepingZ"), ("HomePosX", "HomePosY", "HomePosZ"), ("TravelPosX", "TravelPosY", "TravelPosZ"),
        ("BoundX", "BoundY", "BoundZ"), ("APX", "APY", "APZ"), ("xTile", "yTile", "zTile"),
    ];
    /// <summary>Tags holding an {X, Y, Z} compound (a leash may hold a UUID instead, and is then left alone).</summary>
    static readonly string[] XyzCompounds = ["HivePos", "FlowerPos", "PatrolTarget", "WanderTarget", "BeamTarget", "Leash"];
    /// <summary>Tags holding an int[3] position.</summary>
    static readonly string[] IntArrays = ["sleeping_pos", "hive_pos", "flower_pos", "patrol_target", "wander_target", "beam_target", "anchor_pos", "bound_pos", "leash", "home_pos"];

    /// <summary>An entity (canonical Java form), its brain's memories and its passengers.</summary>
    public static void Entity(NbtCompound e, Map map)
    {
        Fields(e, map);
        // Villagers' job site, home and meeting point.
        if (e.GetCompound("Brain")?.GetCompound("memories") is NbtCompound memories)
            foreach (var (_, m) in memories)
                if (m is NbtCompound mem && mem.GetCompound("value") is NbtCompound v) Array3(v, "pos", map);
        foreach (var p in e.GetList("Passengers")?.OfType<NbtCompound>() ?? []) Entity(p, map);
    }

    /// <summary>A block entity (canonical Java form) and the bees inside a hive.</summary>
    public static void BlockEntity(NbtCompound be, Map map)
    {
        Fields(be, map);
        foreach (var bee in be.GetList("Bees")?.OfType<NbtCompound>() ?? [])
            if (bee.GetCompound("EntityData") is NbtCompound data) Entity(data, map);
        foreach (var bee in be.GetList("bees")?.OfType<NbtCompound>() ?? [])
            if (bee.GetCompound("entity_data") is NbtCompound data) Entity(data, map);
    }

    /// <summary>The riders' own positions (a rider is saved where it sat), moved with <paramref name="move"/>.</summary>
    public static void Passengers(NbtCompound e, Func<double, double, double, (double X, double Y, double Z)> move)
    {
        foreach (var p in e.GetList("Passengers")?.OfType<NbtCompound>() ?? [])
        {
            if (Entities.Position(p) is var (x, y, z))
            {
                var (nx, ny, nz) = move(x, y, z);
                p["Pos"] = new NbtList(NbtType.Double, 3) { new NbtDouble(nx), new NbtDouble(ny), new NbtDouble(nz) };
            }
            Passengers(p, move);
        }
    }

    static void Fields(NbtCompound t, Map map)
    {
        foreach (var (kx, ky, kz) in Triples)
            if (t[kx] is { IsNumeric: true } x && t[ky] is { IsNumeric: true } y && t[kz] is { IsNumeric: true } z
                && map((int)Math.Floor(x.AsDouble), (int)Math.Floor(y.AsDouble), (int)Math.Floor(z.AsDouble)) is var (nx, ny, nz))
            {
                t[kx] = Same(x, nx, x.AsDouble - Math.Floor(x.AsDouble));
                t[ky] = Same(y, ny, y.AsDouble - Math.Floor(y.AsDouble));
                t[kz] = Same(z, nz, z.AsDouble - Math.Floor(z.AsDouble));
            }
        foreach (var key in XyzCompounds)
            if (t.GetCompound(key) is NbtCompound p && p["X"] is { IsNumeric: true } x && p["Y"] is { IsNumeric: true } y && p["Z"] is { IsNumeric: true } z
                && map((int)x.AsLong, (int)y.AsLong, (int)z.AsLong) is var (nx, ny, nz))
            {
                p["X"] = Same(x, nx, 0); p["Y"] = Same(y, ny, 0); p["Z"] = Same(z, nz, 0);
            }
        foreach (var key in IntArrays) Array3(t, key, map);
    }

    static void Array3(NbtCompound t, string key, Map map)
    {
        if (t[key] is NbtIntArray { Value.Length: 3 } a && map(a.Value[0], a.Value[1], a.Value[2]) is var (x, y, z))
            t[key] = new NbtIntArray([x, y, z]);
    }

    /// <summary>The new whole number (plus the old fraction, for the few doubles) in the tag's own type.</summary>
    static NbtTag Same(NbtTag old, int value, double fraction) => old switch
    {
        NbtDouble => new NbtDouble(value + fraction),
        NbtFloat => new NbtFloat((float)(value + fraction)),
        NbtLong => new NbtLong(value),
        NbtShort => new NbtShort((short)value),
        NbtByte => new NbtByte((sbyte)value),
        _ => new NbtInt(value),
    };
}
