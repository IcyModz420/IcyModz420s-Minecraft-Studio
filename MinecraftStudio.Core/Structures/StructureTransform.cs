// MinecraftStudio.Core/Structures/StructureTransform.cs: turning and mirroring a structure before it is saved or placed.
// Owns: StructureMirror and StructureTransform — Apply (a new structure, mirrored first and then turned 0–3 quarter turns
// clockwise seen from above, the order structure blocks use), Block (one block state: facing, axis, rotation 0–15, side
// connections, rails, stairs/doors/chests left and right, jigsaw and crafter orientation, bookshelf slots), and the
// entities: positions, yaw, motion, frame/painting/shulker facing (the block a painting hangs in follows its centre), and
// what they remember inside the structure. Block entities keep their contents (pot sherds and shelf slots swap sides).
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

/// <summary>A mirror applied to a structure (before it is turned).</summary>
public enum StructureMirror
{
    /// <summary>As it is.</summary>
    None,
    /// <summary>North and south swapped (z flipped): the structure block's "left right".</summary>
    NorthSouth,
    /// <summary>East and west swapped (x flipped): the structure block's "front back".</summary>
    EastWest,
}

public static class StructureTransform
{
    /// <summary>The horizontal directions in clockwise order seen from above.</summary>
    static readonly string[] Clockwise = ["north", "east", "south", "west"];

    /// <summary>
    /// A new structure: <paramref name="s"/> mirrored, then turned <paramref name="turns"/> quarter turns clockwise (seen
    /// from above). The structure itself is not changed; with no turn and no mirror it is returned as it is.
    /// </summary>
    public static Structure Apply(Structure s, int turns, StructureMirror mirror)
    {
        turns &= 3;
        if (turns == 0 && mirror == StructureMirror.None) return s;
        bool swap = (turns & 1) == 1;
        var t = new Structure(swap ? s.SizeZ : s.SizeX, s.SizeY, swap ? s.SizeX : s.SizeZ)
        {
            DataVersion = s.DataVersion, Author = s.Author, Origin = s.Origin,
        };
        var states = new Dictionary<int, BlockState>();
        for (int y = 0; y < s.SizeY; y++)
            for (int z = 0; z < s.SizeZ; z++)
                for (int x = 0; x < s.SizeX; x++)
                {
                    var b = s.Get(x, y, z);
                    if (!states.TryGetValue(b.Id, out var turned)) states[b.Id] = turned = Block(b, turns, mirror);
                    var (nx, nz) = Cell(x, z, s.SizeX, s.SizeZ, turns, mirror);
                    t.Set(nx, y, nz, turned);
                }

        foreach (var (index, be) in s.BlockEntities)
        {
            int x = index % s.SizeX, rem = index / s.SizeX, z = rem % s.SizeZ, y = rem / s.SizeZ;
            var (nx, nz) = Cell(x, z, s.SizeX, s.SizeZ, turns, mirror);
            var copy = (NbtCompound)be.Clone();
            if (mirror != StructureMirror.None) MirrorContents(copy, s.Get(x, y, z));
            // Counted from the corner the block entity's own coordinates (where it was cropped) imply.
            if (be["x"] is { IsNumeric: true } && be["y"] is { IsNumeric: true } && be["z"] is { IsNumeric: true })
            {
                int cx = be.GetInt("x") - x, cy = be.GetInt("y") - y, cz = be.GetInt("z") - z;
                StructurePositions.BlockEntity(copy, Inside(s, cx, cy, cz, turns, mirror));
                copy.SetInt("x", cx + nx); copy.SetInt("y", cy + y); copy.SetInt("z", cz + nz);
            }
            t.BlockEntities[t.Index(nx, y, nz)] = copy;
        }

        foreach (var e in s.Entities)
        {
            var (nx, nz) = Point(e.X, e.Z, s.SizeX, s.SizeZ, turns, mirror);
            var nbt = (NbtCompound)e.Nbt.Clone();
            var offset = Hanging.OffsetOf(e.Nbt);
            // The entity's own Pos counts from where it was cropped; it keeps counting from there.
            var (cx, cy, cz) = Entities.Position(e.Nbt) is var (px, py, pz) ? (px - e.X, py - e.Y, pz - e.Z) : (0.0, 0.0, 0.0);
            Turn(nbt, turns, mirror);
            nbt["Pos"] = new NbtList(NbtType.Double, 3) { new NbtDouble(cx + nx), new NbtDouble(cy + e.Y), new NbtDouble(cz + nz) };
            StructurePositions.Passengers(nbt, (qx, qy, qz) =>
            {
                var (rx, rz) = Point(qx - cx, qz - cz, s.SizeX, s.SizeZ, turns, mirror);
                return (cx + rx, qy, cz + rz);
            });
            StructurePositions.Entity(nbt, Inside(s, (int)Math.Round(cx), (int)Math.Round(cy), (int)Math.Round(cz), turns, mirror));
            if (Hanging.Is(nbt.GetString("id") ?? "")) Hanging.SetBlockFromCentre(nbt, offset);
            t.Entities.Add(new StructureEntity(nx, e.Y, nz, nbt));
        }
        return t;
    }

    /// <summary>Remembered positions inside the structure's box (counted from <paramref name="cx"/>, …) turn with it.</summary>
    static StructurePositions.Map Inside(Structure s, int cx, int cy, int cz, int turns, StructureMirror mirror) => (x, y, z) =>
    {
        int lx = x - cx, ly = y - cy, lz = z - cz;
        if (!s.InRange(lx, ly, lz)) return null;
        var (nx, nz) = Cell(lx, lz, s.SizeX, s.SizeZ, turns, mirror);
        return (cx + nx, y, cz + nz);
    };

    /// <summary>Where block cell (x, z) of a structure sizeX × sizeZ lands.</summary>
    public static (int X, int Z) Cell(int x, int z, int sizeX, int sizeZ, int turns, StructureMirror mirror)
    {
        if (mirror == StructureMirror.NorthSouth) z = sizeZ - 1 - z;
        else if (mirror == StructureMirror.EastWest) x = sizeX - 1 - x;
        return (turns & 3) switch
        {
            1 => (sizeZ - 1 - z, x),
            2 => (sizeX - 1 - x, sizeZ - 1 - z),
            3 => (z, sizeX - 1 - x),
            _ => (x, z),
        };
    }

    /// <summary>Where a point (x, z) inside a structure sizeX × sizeZ (counted from its low corner) lands.</summary>
    public static (double X, double Z) Point(double x, double z, double sizeX, double sizeZ, int turns, StructureMirror mirror)
    {
        if (mirror == StructureMirror.NorthSouth) z = sizeZ - z;
        else if (mirror == StructureMirror.EastWest) x = sizeX - x;
        return (turns & 3) switch
        {
            1 => (sizeZ - z, x),
            2 => (sizeX - x, sizeZ - z),
            3 => (z, sizeX - x),
            _ => (x, z),
        };
    }

    /// <summary>A direction name after the mirror and the turn ("up", "down" and anything else stay).</summary>
    public static string Direction(string d, int turns, StructureMirror mirror)
    {
        if (mirror == StructureMirror.NorthSouth) d = d switch { "north" => "south", "south" => "north", _ => d };
        else if (mirror == StructureMirror.EastWest) d = d switch { "east" => "west", "west" => "east", _ => d };
        int i = Array.IndexOf(Clockwise, d);
        return i < 0 ? d : Clockwise[(i + turns) & 3];
    }

    /// <summary>A yaw in degrees (0 south, 90 west, 180 north, 270 east) after the mirror and the turn.</summary>
    public static float Yaw(float yaw, int turns, StructureMirror mirror)
    {
        double y = mirror switch { StructureMirror.NorthSouth => 180 - yaw, StructureMirror.EastWest => -yaw, _ => yaw };
        y += 90 * (turns & 3);
        y %= 360;
        if (y < 0) y += 360;
        return (float)y;
    }

    // ---------------------------------------------------------------- block states

    /// <summary>One block state mirrored, then turned.</summary>
    public static BlockState Block(BlockState b, int turns, StructureMirror mirror)
    {
        turns &= 3;
        if (b.Properties.Length == 0 || (turns == 0 && mirror == StructureMirror.None)) return b;
        bool bookshelf = mirror != StructureMirror.None && b.Name == "minecraft:chiseled_bookshelf";
        var props = new List<KeyValuePair<string, string>>();
        foreach (var (k, v) in b.PropertyPairs())
        {
            string key = k, value = v;
            switch (k)
            {
                case "north" or "east" or "south" or "west":
                    key = Direction(k, turns, mirror);
                    break;
                case "facing":
                    value = Direction(v, turns, mirror);
                    break;
                case "axis" when (turns & 1) == 1:
                    value = v switch { "x" => "z", "z" => "x", _ => v };
                    break;
                case "rotation" when int.TryParse(v, out int r):
                    value = Rotation16(r, turns, mirror).ToString();
                    break;
                case "orientation":
                    value = string.Join('_', v.Split('_').Select(part => Direction(part, turns, mirror)));
                    break;
                case "shape" when v.Contains("north") || v.Contains("south") || v.Contains("east") || v.Contains("west"):
                    value = RailShape(v, turns, mirror);
                    break;
                default:
                    // Left and right (door hinges, chest halves, stair corners, shelf chains) swap in a mirror.
                    if (mirror != StructureMirror.None) value = Hand(v);
                    if (bookshelf && k.StartsWith("slot_", StringComparison.Ordinal)) key = BookshelfSlotKey(k);
                    break;
            }
            props.Add(new(key, value));
        }
        return BlockState.Get(b.Name, props);
    }

    /// <summary>A 16-step rotation (signs, banners, heads: 0 south, 4 west, 8 north, 12 east) mirrored, then turned.</summary>
    static int Rotation16(int r, int turns, StructureMirror mirror)
    {
        r &= 15;
        r = mirror switch { StructureMirror.NorthSouth => (24 - r) % 16, StructureMirror.EastWest => (16 - r) % 16, _ => r };
        return (r + 4 * turns) & 15;
    }

    /// <summary>A rail shape (north_south, ascending_east, south_west …) mirrored and turned, in the game's own spelling.</summary>
    static string RailShape(string v, int turns, StructureMirror mirror)
    {
        if (v.StartsWith("ascending_", StringComparison.Ordinal)) return "ascending_" + Direction(v[10..], turns, mirror);
        var parts = v.Split('_');
        if (parts.Length != 2) return v;
        string a = Direction(parts[0], turns, mirror), b = Direction(parts[1], turns, mirror);
        bool aNs = a is "north" or "south", bNs = b is "north" or "south";
        if (aNs && bNs) return "north_south";
        if (!aNs && !bNs) return "east_west";
        return aNs ? $"{a}_{b}" : $"{b}_{a}";
    }

    static string Hand(string v) => v switch
    {
        "left" => "right",
        "right" => "left",
        _ when v.EndsWith("_left", StringComparison.Ordinal) => v[..^5] + "_right",
        _ when v.EndsWith("_right", StringComparison.Ordinal) => v[..^6] + "_left",
        _ => v,
    };

    /// <summary>A chiseled bookshelf's slots, seen from the front (0 1 2 above 3 4 5), swap columns in a mirror.</summary>
    static string BookshelfSlotKey(string key) => key switch
    {
        "slot_0_occupied" => "slot_2_occupied",
        "slot_2_occupied" => "slot_0_occupied",
        "slot_3_occupied" => "slot_5_occupied",
        "slot_5_occupied" => "slot_3_occupied",
        _ => key,
    };

    /// <summary>Contents whose place is left or right of the block's front swap sides in a mirror.</summary>
    static void MirrorContents(NbtCompound be, BlockState block)
    {
        if (block.Name == "minecraft:chiseled_bookshelf") Slots(be, s => s switch { 0 => 2, 2 => 0, 3 => 5, 5 => 3, _ => s });
        else if (block.Name.EndsWith("_shelf", StringComparison.Ordinal)) Slots(be, s => s switch { 0 => 2, 2 => 0, _ => s });
        // A decorated pot's sherds: back, left, right, front.
        if (block.Name == "minecraft:decorated_pot" && be.GetList("sherds") is { Count: 4 } sherds)
            be["sherds"] = new NbtList([sherds[0].Clone(), sherds[2].Clone(), sherds[1].Clone(), sherds[3].Clone()]);
    }

    static void Slots(NbtCompound be, Func<int, int> map)
    {
        foreach (var item in be.GetList("Items")?.OfType<NbtCompound>() ?? [])
            if (item["Slot"] is { IsNumeric: true } slot) item.SetByte("Slot", (sbyte)map((int)slot.AsLong));
    }

    // ---------------------------------------------------------------- entities

    /// <summary>An entity's yaw, motion and facing (and its passengers') after the mirror and the turn.</summary>
    static void Turn(NbtCompound e, int turns, StructureMirror mirror)
    {
        if (e.GetList("Rotation") is { Count: 2 } rot && rot[0].IsNumeric)
            e["Rotation"] = new NbtList(NbtType.Float, 2) { new NbtFloat(Yaw((float)rot[0].AsDouble, turns, mirror)), new NbtFloat((float)rot[1].AsDouble) };
        if (e.GetList("Motion") is { Count: 3 } m && m[0].IsNumeric && m[2].IsNumeric)
        {
            var (mx, mz) = Vector(m[0].AsDouble, m[2].AsDouble, turns, mirror);
            e["Motion"] = new NbtList(NbtType.Double, 3) { new NbtDouble(mx), new NbtDouble(m[1].AsDouble), new NbtDouble(mz) };
        }
        string id = e.GetString("id") ?? "";
        if (Hanging.IsFrame(id) && e["Facing"] is { IsNumeric: true } f)
        {
            int six = (int)f.AsLong;
            e.SetByte("Facing", (sbyte)SixWay(six, turns, mirror));
            // A mirror turns the picture in the frame the other way.
            if (mirror != StructureMirror.None && e["ItemRotation"] is { IsNumeric: true } ir) e.SetByte("ItemRotation", (sbyte)((8 - (int)ir.AsLong) & 7));
        }
        if (id == "minecraft:painting" && e["facing"] is { IsNumeric: true } pf)
            e.SetByte("facing", (sbyte)(Hanging.FourWay(SixWay(Hanging.SixWay((int)pf.AsLong), turns, mirror)) ?? 0));
        if (id == "minecraft:shulker" && e["AttachFace"] is { IsNumeric: true } af) e.SetByte("AttachFace", (sbyte)SixWay((int)af.AsLong, turns, mirror));
        if (e.GetCompound("BlockState") is NbtCompound falling) e["BlockState"] = Block(BlockState.FromJavaNbt(falling), turns, mirror).ToJavaNbt();
        foreach (var p in e.GetList("Passengers")?.OfType<NbtCompound>() ?? []) Turn(p, turns, mirror);
    }

    /// <summary>A six-way direction (0 down, 1 up, 2 north, 3 south, 4 west, 5 east) after the mirror and the turn.</summary>
    static int SixWay(int six, int turns, StructureMirror mirror)
    {
        string name = six switch { 2 => "north", 3 => "south", 4 => "west", 5 => "east", _ => "" };
        if (name.Length == 0) return six;
        return Direction(name, turns, mirror) switch { "north" => 2, "south" => 3, "west" => 4, _ => 5 };
    }

    /// <summary>A horizontal vector after the mirror and the turn.</summary>
    static (double X, double Z) Vector(double x, double z, int turns, StructureMirror mirror)
    {
        if (mirror == StructureMirror.NorthSouth) z = -z;
        else if (mirror == StructureMirror.EastWest) x = -x;
        return (turns & 3) switch { 1 => (-z, x), 2 => (-x, -z), 3 => (z, -x), _ => (x, z) };
    }
}
