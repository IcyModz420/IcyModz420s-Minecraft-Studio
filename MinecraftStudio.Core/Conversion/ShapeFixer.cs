// MinecraftStudio.Core/Conversion/ShapeFixer.cs: Recomputes the neighbour-dependent block properties that
// numeric and Bedrock sources do not store, following the game's own rules: fence/pane/wall links, stair
// corners, redstone dust links, double chests and attached stems.
// Owns: ShapeFixer.
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Recomputes the neighbour-dependent block properties that numeric and Bedrock sources do not store, following the
/// game's own rules: fence/pane/wall links, stair corners, redstone dust links, double chests and attached stems.
/// Neighbours in other chunks are read through the <see cref="CanonicalReader"/> cache.
/// </summary>
public static class ShapeFixer
{
    static readonly (string Name, int Dx, int Dz)[] Dirs = [("north", 0, -1), ("east", 1, 0), ("south", 0, 1), ("west", -1, 0)];

    static string Cw(string d) => d switch { "north" => "east", "east" => "south", "south" => "west", _ => "north" };
    static string Ccw(string d) => d switch { "north" => "west", "west" => "south", "south" => "east", _ => "north" };
    static string Opposite(string d) => d switch { "north" => "south", "south" => "north", "east" => "west", _ => "east" };
    static (int, int) Offset(string d) => d switch { "north" => (0, -1), "south" => (0, 1), "east" => (1, 0), _ => (-1, 0) };
    static char Axis(string d) => d is "north" or "south" ? 'z' : 'x';

    /// <param name="walls">Recompute wall links too (sources that stored them, like Bedrock, keep theirs).</param>
    public static void Fix(CanonicalChunk c, CanonicalReader? neighbours, bool walls) => Fix(c, neighbours == null ? null : neighbours.BlockAt, walls);

    /// <summary>The same, with the blocks outside the chunk given by <paramref name="blockAt"/> (absolute x, y, z; null = air).</summary>
    public static void Fix(CanonicalChunk c, Func<int, int, int, BlockState?>? blockAt, bool walls)
    {
        BlockState At(int lx, int y, int lz)
        {
            if (lx is >= 0 and < 16 && lz is >= 0 and < 16) return c.GetBlock(lx, y, lz);
            return blockAt?.Invoke(c.X * 16 + lx, y, c.Z * 16 + lz) ?? BlockState.Air;
        }
        FixLeaves(c, At);

        var chestsDone = new HashSet<(int, int, int)>();
        foreach (var (sy, s) in c.Sections.ToList())
        {
            bool any = false;
            foreach (var p in s.Palette)
                if (Kind(p) != ShapeKind.None) { any = true; break; }
            if (!any) continue;
            for (int i = 0; i < 4096; i++)
            {
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                var b = c.GetBlock(x, y, z);
                var kind = Kind(b);
                if (kind == ShapeKind.None) continue;
                BlockState n = b;
                switch (kind)
                {
                    case ShapeKind.Fence:
                        foreach (var (d, dx, dz) in Dirs) n = StateEdit.With(n, d, FenceConnects(b, At(x + dx, y, z + dz), d) ? "true" : "false");
                        break;
                    case ShapeKind.Pane:
                        foreach (var (d, dx, dz) in Dirs) n = StateEdit.With(n, d, PaneConnects(At(x + dx, y, z + dz), d) ? "true" : "false");
                        break;
                    case ShapeKind.Wall when walls:
                        {
                            int count = 0;
                            var links = new Dictionary<string, bool>();
                            foreach (var (d, dx, dz) in Dirs) { bool l = WallConnects(At(x + dx, y, z + dz), d); links[d] = l; if (l) count++; }
                            foreach (var (d, _, _) in Dirs) n = StateEdit.With(n, d, links[d] ? "low" : "none");
                            bool straight = count == 2 && (links["north"] && links["south"] || links["east"] && links["west"]);
                            bool above = !At(x, y + 1, z).IsAir;
                            n = StateEdit.With(n, "up", straight && !above ? "false" : "true");
                            break;
                        }
                    case ShapeKind.Stairs:
                        n = StateEdit.With(b, "shape", StairShape(b, x, y, z, At));
                        break;
                    case ShapeKind.Wire:
                        n = WireShape(b, x, y, z, At);
                        break;
                    case ShapeKind.Chest:
                        if (b.GetProperty("type") == "single" && !chestsDone.Contains((x, y, z)))
                        {
                            string f = b.GetProperty("facing") ?? "north";
                            foreach (var (side, type, other) in new[] { (Cw(f), "left", "right"), (Ccw(f), "right", "left") })
                            {
                                var (dx, dz) = Offset(side);
                                var nb = At(x + dx, y, z + dz);
                                if (nb.Name != b.Name || nb.GetProperty("facing") != f || nb.GetProperty("type") != "single") continue;
                                n = StateEdit.With(b, "type", type);
                                chestsDone.Add((x, y, z));
                                if (x + dx is >= 0 and < 16 && z + dz is >= 0 and < 16)
                                {
                                    c.SetBlock(x + dx, y, z + dz, StateEdit.With(nb, "type", other));
                                    chestsDone.Add((x + dx, y, z + dz));
                                }
                                break;
                            }
                        }
                        break;
                    case ShapeKind.Note:
                        n = StateEdit.With(b, "instrument", Instrument(At(x, y - 1, z), At(x, y + 1, z)));
                        break;
                    case ShapeKind.Vine:
                        {
                            // Generated vines hang on their side faces; only a vine with no side face is attached above.
                            bool sides = Dirs.Any(d => b.GetProperty(d.Name) == "true");
                            n = StateEdit.With(b, "up", sides ? "false" : "true");
                            break;
                        }
                    case ShapeKind.Mushroom:
                        {
                            // Like generated mushrooms: outer faces show the cap skin, undersides show pores, stems have no ends.
                            bool stem = b.BaseName == "mushroom_stem";
                            foreach (var (d, dx, dz) in Dirs) n = StateEdit.With(n, d, At(x + dx, y, z + dz).Name != b.Name ? "true" : "false");
                            n = StateEdit.With(n, "up", !stem && At(x, y + 1, z).Name != b.Name ? "true" : "false");
                            n = StateEdit.With(n, "down", "false");
                            break;
                        }
                    case ShapeKind.Stem:
                        if (b.GetProperty("age") == "7")
                        {
                            string fruit = b.BaseName == "pumpkin_stem" ? "pumpkin" : "melon";
                            foreach (var (d, dx, dz) in Dirs)
                                if (At(x + dx, y, z + dz).BaseName == fruit)
                                {
                                    n = JavaBlocks.Canonicalize(BlockState.Get("minecraft:attached_" + b.BaseName, $"facing={d}"));
                                    break;
                                }
                        }
                        break;
                }
                if (n != b) c.SetBlock(x, y, z, n);
            }
        }
    }

    static bool IsLog(BlockState s)
    {
        string b = s.BaseName;
        return b.EndsWith("_log") || b.EndsWith("_wood") || b.EndsWith("_stem") && b is not ("pumpkin_stem" or "melon_stem" or "mushroom_stem" or "big_dripleaf_stem")
            || b.EndsWith("_hyphae");
    }

    static bool IsLeaves(BlockState s) => s.BaseName.EndsWith("_leaves", StringComparison.Ordinal) && s.GetProperty("distance") != null;

    /// <summary>
    /// Leaf distance to the nearest log (1–6, 7 = none), computed like the game does. Sources that do not store it
    /// (Bedrock, pre-1.13) would otherwise leave every natural leaf at 7, and the game would make them decay.
    /// </summary>
    static void FixLeaves(CanonicalChunk c, Func<int, int, int, BlockState> at)
    {
        if (!c.Sections.Values.Any(s => s.Palette.Any(IsLeaves))) return;
        const int R = 6, W = 16 + 2 * R;
        int minY = c.Sections.Keys.Min() * 16 - R, maxY = c.Sections.Keys.Max() * 16 + 15 + R;
        int H = maxY - minY + 1;
        var dist = new byte[W * W * H];
        int Idx(int x, int y, int z) => ((y - minY) * W + (z + R)) * W + (x + R);
        var queue = new Queue<(int X, int Y, int Z)>();
        for (int y = minY; y <= maxY; y++)
            for (int z = -R; z < 16 + R; z++)
                for (int x = -R; x < 16 + R; x++)
                {
                    var b = at(x, y, z);
                    int i = Idx(x, y, z);
                    if (IsLog(b)) { dist[i] = 0; queue.Enqueue((x, y, z)); }
                    else dist[i] = IsLeaves(b) ? (byte)7 : (byte)255;
                }
        while (queue.Count > 0)
        {
            var (x, y, z) = queue.Dequeue();
            int d = dist[Idx(x, y, z)] + 1;
            if (d > 6) continue;
            foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
            {
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if (nx < -R || nx >= 16 + R || nz < -R || nz >= 16 + R || ny < minY || ny > maxY) continue;
                int ni = Idx(nx, ny, nz);
                if (dist[ni] == 255 || dist[ni] <= d) continue; // not leaves, a log, or already closer
                dist[ni] = (byte)d;
                queue.Enqueue((nx, ny, nz));
            }
        }
        foreach (var (sy, s) in c.Sections.ToList())
        {
            if (!s.Palette.Any(IsLeaves)) continue;
            for (int i = 0; i < 4096; i++)
            {
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                var b = s.Indices == null ? s.Palette[0] : s.Palette[s.Indices[i]];
                if (!IsLeaves(b)) continue;
                string want = Math.Clamp((int)dist[Idx(x, y, z)], 1, 7).ToString();
                if (b.GetProperty("distance") != want) c.SetBlock(x, y, z, StateEdit.With(b, "distance", want));
            }
        }
    }

    /// <summary>Note block instrument from the block below (or a mob head above), as the game derives it.</summary>
    static string Instrument(BlockState below, BlockState above)
    {
        string a = above.BaseName;
        if (Substitutes.IsMobHead(a))
            return a switch
            {
                _ when a.StartsWith("zombie") => "zombie", _ when a.StartsWith("wither_skeleton") => "wither_skeleton", _ when a.StartsWith("skeleton") => "skeleton",
                _ when a.StartsWith("creeper") => "creeper", _ when a.StartsWith("dragon") => "dragon", _ when a.StartsWith("piglin") => "piglin", _ => "custom_head",
            };
        string b = below.BaseName;
        return b switch
        {
            "gold_block" => "bell", "clay" => "flute", "packed_ice" => "chime", "bone_block" => "xylophone", "iron_block" => "iron_xylophone",
            "soul_sand" => "cow_bell", "pumpkin" => "didgeridoo", "emerald_block" => "bit", "hay_block" => "banjo", "glowstone" => "pling",
            _ when b.EndsWith("_wool") => "guitar",
            _ when b is "sand" or "red_sand" or "gravel" or "suspicious_sand" or "suspicious_gravel" || b.EndsWith("concrete_powder") => "snare",
            _ when b.Contains("glass") || b is "sea_lantern" or "beacon" => "hat",
            _ when b.EndsWith("_planks") || b.EndsWith("_log") || b.EndsWith("_wood") || b.EndsWith("_stem") || b.EndsWith("_hyphae") || b is "bookshelf" or "chest" or "trapped_chest"
                or "barrel" or "crafting_table" or "note_block" or "jukebox" or "loom" or "lectern" or "composter" || b.EndsWith("_sign") || b.EndsWith("_fence") => "bass",
            _ when b is "stone" or "cobblestone" or "netherrack" or "obsidian" or "bricks" or "end_stone" or "deepslate" || b.EndsWith("_ore") || b.Contains("stone") || b.Contains("brick")
                || b.EndsWith("terracotta") || b.Contains("concrete") || b.Contains("quartz") || b.Contains("prismarine") || b.Contains("basalt") => "basedrum",
            _ => "harp",
        };
    }

    enum ShapeKind { None, Fence, Pane, Wall, Stairs, Wire, Chest, Stem, Note, Vine, Mushroom }

    static readonly Dictionary<int, ShapeKind> KindCache = [];

    static ShapeKind Kind(BlockState s)
    {
        lock (KindCache)
            if (KindCache.TryGetValue(s.Id, out var k)) return k;
        string b = s.BaseName;
        var kind = b switch
        {
            _ when b.EndsWith("_fence", StringComparison.Ordinal) => ShapeKind.Fence,
            _ when b.EndsWith("glass_pane", StringComparison.Ordinal) || b == "iron_bars" || b.EndsWith("_bars", StringComparison.Ordinal) => ShapeKind.Pane,
            _ when b.EndsWith("_wall", StringComparison.Ordinal) && s.GetProperty("up") != null => ShapeKind.Wall,
            _ when b.EndsWith("_stairs", StringComparison.Ordinal) => ShapeKind.Stairs,
            "redstone_wire" => ShapeKind.Wire,
            "chest" or "trapped_chest" => ShapeKind.Chest,
            "pumpkin_stem" or "melon_stem" => ShapeKind.Stem,
            "note_block" => ShapeKind.Note,
            "vine" => ShapeKind.Vine,
            "brown_mushroom_block" or "red_mushroom_block" or "mushroom_stem" => ShapeKind.Mushroom,
            _ => ShapeKind.None,
        };
        lock (KindCache) KindCache[s.Id] = kind;
        return kind;
    }

    /// <summary>Approximation of "has a sturdy full face": an opaque collidable block that is not a partial shape.</summary>
    static bool IsSturdy(BlockState s)
    {
        if (s.IsAir) return false;
        string b = s.BaseName;
        if (b.Contains("glass", StringComparison.Ordinal) && !b.EndsWith("_pane", StringComparison.Ordinal)) return true;
        if (b.EndsWith("_slab", StringComparison.Ordinal)) return s.GetProperty("type") == "double";
        if (b.EndsWith("_stairs", StringComparison.Ordinal) || b.EndsWith("_wall", StringComparison.Ordinal) || b.EndsWith("_fence", StringComparison.Ordinal)
            || b.EndsWith("_door", StringComparison.Ordinal) || b.EndsWith("_trapdoor", StringComparison.Ordinal) || b.EndsWith("_carpet", StringComparison.Ordinal)
            || b.EndsWith("_bed", StringComparison.Ordinal) || b.Contains("chest") || b.EndsWith("_sign", StringComparison.Ordinal)) return false;
        var def = JavaBlockData.Latest.Blocks.GetValueOrDefault(s.Name);
        return def != null && def.Solid && !def.Transparent;
    }

    static bool IsConnectionException(BlockState s) =>
        s.BaseName.EndsWith("_leaves", StringComparison.Ordinal) || s.BaseName is "barrier" or "carved_pumpkin" or "jack_o_lantern" or "pumpkin" or "melon"
        || s.BaseName.EndsWith("shulker_box", StringComparison.Ordinal);

    static bool IsWoodenFence(BlockState s) => s.BaseName.EndsWith("_fence", StringComparison.Ordinal) && s.BaseName != "nether_brick_fence";

    static bool GateAligned(BlockState gate, string dir)
    {
        string f = gate.GetProperty("facing") ?? "north";
        return Axis(f) != Axis(dir);
    }

    static bool FenceConnects(BlockState fence, BlockState other, string dir)
    {
        if (other.BaseName.EndsWith("_fence", StringComparison.Ordinal))
            return IsWoodenFence(fence) == IsWoodenFence(other);
        if (other.BaseName.EndsWith("_fence_gate", StringComparison.Ordinal)) return GateAligned(other, dir);
        return !IsConnectionException(other) && IsSturdy(other);
    }

    static bool PaneConnects(BlockState other, string dir)
    {
        string b = other.BaseName;
        if (b.EndsWith("glass_pane", StringComparison.Ordinal) || b == "iron_bars" || b.EndsWith("_bars", StringComparison.Ordinal)) return true;
        if (b.EndsWith("_wall", StringComparison.Ordinal)) return true;
        return !IsConnectionException(other) && IsSturdy(other);
    }

    static bool WallConnects(BlockState other, string dir)
    {
        string b = other.BaseName;
        if (b.EndsWith("_wall", StringComparison.Ordinal) && other.GetProperty("up") != null) return true;
        if (b.EndsWith("_fence_gate", StringComparison.Ordinal)) return GateAligned(other, dir);
        if (b.EndsWith("glass_pane", StringComparison.Ordinal) || b == "iron_bars") return true;
        return !IsConnectionException(other) && IsSturdy(other);
    }

    static string StairShape(BlockState s, int x, int y, int z, Func<int, int, int, BlockState> at)
    {
        string facing = s.GetProperty("facing") ?? "north", half = s.GetProperty("half") ?? "bottom";
        bool IsStairs(BlockState b) => b.BaseName.EndsWith("_stairs", StringComparison.Ordinal);
        bool CanTakeShape(string d)
        {
            var (dx, dz) = Offset(d);
            var o = at(x + dx, y, z + dz);
            return !IsStairs(o) || o.GetProperty("facing") != facing || o.GetProperty("half") != half;
        }
        var (fx, fz) = Offset(facing);
        var front = at(x + fx, y, z + fz);
        if (IsStairs(front) && front.GetProperty("half") == half)
        {
            string ff = front.GetProperty("facing") ?? "north";
            if (Axis(ff) != Axis(facing) && CanTakeShape(Opposite(ff)))
                return ff == Ccw(facing) ? "outer_left" : "outer_right";
        }
        var back = at(x - fx, y, z - fz);
        if (IsStairs(back) && back.GetProperty("half") == half)
        {
            string bf = back.GetProperty("facing") ?? "north";
            if (Axis(bf) != Axis(facing) && CanTakeShape(bf))
                return bf == Ccw(facing) ? "inner_left" : "inner_right";
        }
        return "straight";
    }

    static bool IsPowerSource(BlockState b) => b.BaseName is "redstone_torch" or "redstone_wall_torch" or "redstone_block" or "lever"
        or "daylight_detector" or "target" or "trapped_chest" or "tripwire_hook" or "detector_rail" or "lectern" or "sculk_sensor" or "calibrated_sculk_sensor"
        || b.BaseName.EndsWith("_button", StringComparison.Ordinal) || b.BaseName.EndsWith("_pressure_plate", StringComparison.Ordinal);

    static bool WireConnectsTo(BlockState other, string dir)
    {
        if (other.BaseName == "redstone_wire") return true;
        if (other.BaseName == "repeater")
        {
            string f = other.GetProperty("facing") ?? "north";
            return f == dir || f == Opposite(dir);
        }
        if (other.BaseName == "observer") return other.GetProperty("facing") == dir;
        if (other.BaseName == "comparator") return true;
        return IsPowerSource(other);
    }

    static BlockState WireShape(BlockState s, int x, int y, int z, Func<int, int, int, BlockState> at)
    {
        bool aboveSolid = IsSturdy(at(x, y + 1, z));
        var sides = new Dictionary<string, string>();
        foreach (var (d, dx, dz) in Dirs)
        {
            var nb = at(x + dx, y, z + dz);
            string v = "none";
            if (WireConnectsTo(nb, d)) v = "side";
            else if (!aboveSolid && IsSturdy(nb) && at(x + dx, y + 1, z + dz).BaseName == "redstone_wire") v = "up";
            else if (!IsSturdy(nb) && at(x + dx, y - 1, z + dz).BaseName == "redstone_wire") v = "side";
            sides[d] = v;
        }
        // A line with one link extends to the opposite side (1.16+ look); a dust with no links stays a dot.
        int linked = sides.Values.Count(v => v != "none");
        if (linked == 1)
        {
            string only = sides.First(kv => kv.Value != "none").Key;
            sides[Opposite(only)] = "side";
        }
        var n = s;
        foreach (var (d, v) in sides) n = StateEdit.With(n, d, v);
        return n;
    }
}
