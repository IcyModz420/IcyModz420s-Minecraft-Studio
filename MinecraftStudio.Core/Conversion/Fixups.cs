// MinecraftStudio.Core/Conversion/Fixups.cs: Shared helpers for changing states inside a canonical chunk.
// Owns: StateEdit, LegacyFixups, BedrockFixups.
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>Shared helpers for changing states inside a canonical chunk.</summary>
static class StateEdit
{
    public static readonly string[] Colors = ["white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray", "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"];

    public static BlockState With(BlockState s, string key, string value) =>
        BlockState.Get(s.Name, s.PropertyPairs().Where(p => p.Key != key).Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).Append(new(key, value)));

    public static BlockState Rename(BlockState s, string name) => JavaBlocks.Canonicalize(BlockState.Get(name, s.PropertyPairs().Select(p => new KeyValuePair<string, string>(p.Key, p.Value))));

    public static (int X, int Y, int Z)? Position(NbtCompound te)
    {
        if (te.GetLongOrNull("x") is long x && te.GetLongOrNull("y") is long y && te.GetLongOrNull("z") is long z) return ((int)x, (int)y, (int)z);
        return null;
    }

    /// <summary>Local position inside the chunk, or null when the block entity lies outside it.</summary>
    public static (int X, int Y, int Z)? Local(CanonicalChunk c, NbtCompound te)
    {
        if (Position(te) is not var (x, y, z)) return null;
        if (x >> 4 != c.X || z >> 4 != c.Z) return null;
        return (x & 15, y, z & 15);
    }

    public static string BaseId(string id)
    {
        id = id.StartsWith("minecraft:", StringComparison.Ordinal) ? id[10..] : id;
        return id.ToLowerInvariant();
    }
}

/// <summary>
/// Fix-ups for numeric (pre-1.13 Java, Legacy Console) chunks, where the flattening table alone cannot know the
/// final state: colours and types kept in block entities, and properties kept on the other half of a two-block block.
/// </summary>
public static class LegacyFixups
{
    static readonly string[] SkullTypes = ["skeleton", "wither_skeleton", "zombie", "player", "creeper", "dragon", "piglin"];

    static string SkullName(int type, bool wall)
    {
        string t = SkullTypes[Math.Clamp(type, 0, SkullTypes.Length - 1)];
        string kind = t is "skeleton" or "wither_skeleton" ? "skull" : "head";
        return $"minecraft:{t}_{(wall ? "wall_" : "")}{kind}";
    }

    // 1.12 flower pot contents (item name or numeric id, data) -> potted block.
    static string? PottedName(NbtCompound te)
    {
        string item;
        int data = te.GetInt("Data");
        if (te["Item"] is NbtString s) item = StateEdit.BaseId(s.Value);
        else if (te["Item"] is { IsNumeric: true } n) item = n.AsLong switch
        {
            6 => "sapling", 31 => "tallgrass", 32 => "deadbush", 37 => "yellow_flower", 38 => "red_flower", 39 => "brown_mushroom",
            40 => "red_mushroom", 81 => "cactus", _ => "",
        };
        else return null;
        string? plant = item switch
        {
            "red_flower" => data switch { 1 => "blue_orchid", 2 => "allium", 3 => "azure_bluet", 4 => "red_tulip", 5 => "orange_tulip", 6 => "white_tulip", 7 => "pink_tulip", 8 => "oxeye_daisy", _ => "poppy" },
            "yellow_flower" => "dandelion",
            "sapling" => (new[] { "oak", "spruce", "birch", "jungle", "acacia", "dark_oak" })[Math.Clamp(data, 0, 5)] + "_sapling",
            "red_mushroom" => "red_mushroom",
            "brown_mushroom" => "brown_mushroom",
            "cactus" => "cactus",
            "deadbush" => "dead_bush",
            "tallgrass" => data == 2 ? "fern" : null,
            "air" or "" => null,
            _ => null,
        };
        return plant == null ? "minecraft:flower_pot" : "minecraft:potted_" + plant;
    }

    public static void Apply(CanonicalChunk c, IReadOnlyList<NbtCompound> tileEntities)
    {
        foreach (var te in tileEntities)
        {
            if (StateEdit.Local(c, te) is not var (x, y, z)) continue;
            var block = c.GetBlock(x, y, z);
            string id = StateEdit.BaseId(te.GetString("id") ?? "");
            switch (id)
            {
                case "bed" when block.BaseName.EndsWith("_bed", StringComparison.Ordinal):
                    {
                        int color = te.GetInt("color", 14) & 15;
                        c.SetBlock(x, y, z, StateEdit.Rename(block, $"minecraft:{StateEdit.Colors[color]}_bed"));
                        break;
                    }
                case "banner" when block.BaseName.EndsWith("_banner", StringComparison.Ordinal):
                    {
                        // 1.8-1.12 banners store the dye damage (0 = black); wool order is the reverse.
                        int color = 15 - (te.GetInt("Base") & 15);
                        bool wall = block.BaseName.Contains("wall_");
                        c.SetBlock(x, y, z, StateEdit.Rename(block, $"minecraft:{StateEdit.Colors[color]}_{(wall ? "wall_" : "")}banner"));
                        break;
                    }
                case "skull" when block.BaseName.Contains("skull") || block.BaseName.Contains("head"):
                    {
                        bool wall = block.BaseName.Contains("wall_");
                        var s = BlockState.Get(SkullName(te.GetInt("SkullType"), wall), block.PropertyPairs().Select(p => new KeyValuePair<string, string>(p.Key, p.Value)));
                        if (!wall) s = StateEdit.With(s, "rotation", (te.GetInt("Rot") & 15).ToString());
                        c.SetBlock(x, y, z, JavaBlocks.Canonicalize(s));
                        break;
                    }
                case "flowerpot" or "flower_pot" when block.BaseName.Contains("flower_pot") || block.BaseName.StartsWith("potted_", StringComparison.Ordinal):
                    if (PottedName(te) is string potted) c.SetBlock(x, y, z, JavaBlocks.Canonicalize(BlockState.Get(potted)));
                    break;
                case "music" or "noteblock" or "note_block" when block.BaseName == "note_block":
                    c.SetBlock(x, y, z, StateEdit.With(block, "note", (te.GetInt("note") % 25).ToString()));
                    break;
                case "recordplayer" or "jukebox" when block.BaseName == "jukebox":
                    if (te.ContainsKey("RecordItem") || te.GetInt("Record") > 0) c.SetBlock(x, y, z, StateEdit.With(block, "has_record", "true"));
                    break;
            }
        }
        FixHalves(c);
    }

    /// <summary>Doors and double plants store half of their state in each block; snowy grass depends on the block above.</summary>
    public static void FixHalves(CanonicalChunk c)
    {
        foreach (var (sy, s) in c.Sections.ToList())
        {
            bool interesting = s.Palette.Any(p => p.BaseName.EndsWith("_door", StringComparison.Ordinal) || p.GetProperty("half") == "upper"
                || p.BaseName is "grass_block" or "mycelium" or "podzol");
            if (!interesting) continue;
            for (int i = 0; i < 4096; i++)
            {
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                var b = c.GetBlock(x, y, z);
                if (b.BaseName.EndsWith("_door", StringComparison.Ordinal) && b.GetProperty("half") == "lower")
                {
                    var up = c.GetBlock(x, y + 1, z);
                    if (up.Name != b.Name) continue;
                    var lower = StateEdit.With(StateEdit.With(b, "hinge", up.GetProperty("hinge") ?? "left"), "powered", up.GetProperty("powered") ?? "false");
                    var upper = StateEdit.With(StateEdit.With(up, "facing", b.GetProperty("facing") ?? "north"), "open", b.GetProperty("open") ?? "false");
                    c.SetBlock(x, y, z, lower);
                    c.SetBlock(x, y + 1, z, upper);
                }
                else if (b.GetProperty("half") == "upper" && !b.BaseName.EndsWith("_door", StringComparison.Ordinal))
                {
                    var below = c.GetBlock(x, y - 1, z);
                    if (below.GetProperty("half") == "lower" && below.Name != b.Name && IsDoublePlant(below.BaseName))
                        c.SetBlock(x, y, z, StateEdit.With(below, "half", "upper"));
                }
                else if (b.BaseName is "grass_block" or "mycelium" or "podzol")
                {
                    var above = c.GetBlock(x, y + 1, z);
                    bool snowy = above.BaseName is "snow" or "snow_block" or "powder_snow";
                    if ((b.GetProperty("snowy") == "true") != snowy) c.SetBlock(x, y, z, StateEdit.With(b, "snowy", snowy ? "true" : "false"));
                }
            }
        }
    }

    static bool IsDoublePlant(string b) => b is "sunflower" or "lilac" or "tall_grass" or "large_fern" or "rose_bush" or "peony";
}

/// <summary>Fix-ups for Bedrock chunks: colours, plants and notes kept in Bedrock block entities.</summary>
public static class BedrockFixups
{
    /// <summary>Java entity for an item frame at a block (frames are entities on Java).</summary>
    public static NbtCompound ItemFrameEntity(bool glow, int x, int y, int z, int facing, NbtCompound? item, int rotation, float dropChance)
    {
        var (nx, ny, nz) = facing switch { 0 => (0, -1, 0), 1 => (0, 1, 0), 2 => (0, 0, -1), 3 => (0, 0, 1), 4 => (-1, 0, 0), _ => (1, 0, 0) };
        const double d = 0.46875;
        var e = new NbtCompound();
        e.SetString("id", glow ? "minecraft:glow_item_frame" : "minecraft:item_frame");
        e["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(x + 0.5 - nx * d), new NbtDouble(y + 0.5 - ny * d), new NbtDouble(z + 0.5 - nz * d) };
        e["Motion"] = new NbtList(NbtType.Double) { new NbtDouble(0), new NbtDouble(0), new NbtDouble(0) };
        float yaw = facing switch { 2 => 180, 4 => 90, 5 => 270, _ => 0 }, pitch = facing switch { 0 => 90, 1 => -90, _ => 0 };
        e["Rotation"] = new NbtList(NbtType.Float) { new NbtFloat(yaw), new NbtFloat(pitch) };
        e.SetByte("Facing", (sbyte)facing);
        e.SetInt("TileX", x); e.SetInt("TileY", y); e.SetInt("TileZ", z);
        e.SetByte("ItemRotation", (sbyte)(rotation & 7));
        e.SetFloat("ItemDropChance", dropChance);
        e.SetByte("Fixed", 0);
        e.SetByte("Invisible", 0);
        if (item != null) e["Item"] = item;
        return e;
    }

    public static void Apply(CanonicalChunk c, IReadOnlyList<NbtCompound> blockEntities)
    {
        // Item frames: the block becomes air and an entity holds the item.
        var frameData = blockEntities.Where(be => be.GetString("id") is "ItemFrame" or "GlowItemFrame")
            .Select(be => (Pos: StateEdit.Position(be), Be: be)).Where(t => t.Pos != null).ToDictionary(t => t.Pos!.Value, t => t.Be);
        foreach (var (sy, s) in c.Sections.ToList())
        {
            if (!s.Palette.Any(p => p.Name is "minecraft:frame" or "minecraft:glow_frame")) continue;
            for (int i = 0; i < 4096; i++)
            {
                var b = s.Indices == null ? s.Palette[0] : s.Palette[s.Indices[i]];
                if (b.Name is not ("minecraft:frame" or "minecraft:glow_frame")) continue;
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                int wx = c.X * 16 + x, wz = c.Z * 16 + z;
                int facing = int.TryParse(b.GetProperty("facing_direction"), out int f) ? f : 2;
                NbtCompound? item = null;
                int rot = 0;
                float drop = 1;
                if (frameData.TryGetValue((wx, y, wz), out var be))
                {
                    if (be.GetCompound("Item") is NbtCompound bi) item = Items.FromBedrock(bi);
                    rot = (int)Math.Round(be.GetDouble("ItemRotation") / 45.0);
                    drop = (float)be.GetDouble("ItemDropChance", 1);
                }
                c.Entities.Add(ItemFrameEntity(b.Name == "minecraft:glow_frame", wx, y, wz, facing, item, rot, drop));
                c.SetBlock(x, y, z, BlockState.Air);
            }
        }
        foreach (var be in blockEntities)
        {
            if (StateEdit.Local(c, be) is not var (x, y, z)) continue;
            var block = c.GetBlock(x, y, z);
            switch (be.GetString("id"))
            {
                case "Chest" when block.BaseName is "chest" or "trapped_chest" && be["pairx"] is { IsNumeric: true } px && be["pairz"] is { IsNumeric: true } pz:
                    {
                        // Java: a LEFT half has its partner clockwise of the facing (north-facing: to the east).
                        int dx = (int)px.AsLong - (c.X * 16 + x), dz = (int)pz.AsLong - (c.Z * 16 + z);
                        var (lx, lz) = (block.GetProperty("facing") ?? "north") switch { "north" => (1, 0), "east" => (0, 1), "south" => (-1, 0), _ => (0, -1) };
                        string? type = (dx, dz) == (lx, lz) ? "left" : (dx, dz) == (-lx, -lz) ? "right" : null;
                        if (type != null) c.SetBlock(x, y, z, StateEdit.With(block, "type", type));
                        break;
                    }
                case "Bed" when block.BaseName.EndsWith("_bed", StringComparison.Ordinal):
                    c.SetBlock(x, y, z, StateEdit.Rename(block, $"minecraft:{StateEdit.Colors[be.GetInt("color", 14) & 15]}_bed"));
                    break;
                case "Banner" when block.BaseName.EndsWith("_banner", StringComparison.Ordinal):
                    {
                        // Bedrock banner Base uses the dye order (0 = black), like Java 1.12.
                        int color = 15 - (be.GetInt("Base") & 15);
                        bool wall = block.BaseName.Contains("wall_");
                        c.SetBlock(x, y, z, StateEdit.Rename(block, $"minecraft:{StateEdit.Colors[color]}_{(wall ? "wall_" : "")}banner"));
                        break;
                    }
                case "Skull" when !block.BaseName.Contains("wall_") && block.GetProperty("rotation") != null:
                    {
                        float deg = (float)(be["Rotation"]?.AsDouble ?? 0);
                        int rot = ((int)Math.Round(deg / 22.5) % 16 + 16) % 16;
                        c.SetBlock(x, y, z, StateEdit.With(block, "rotation", rot.ToString()));
                        break;
                    }
                case "FlowerPot" when block.BaseName == "flower_pot" || block.BaseName.StartsWith("potted_", StringComparison.Ordinal):
                    if (be.GetCompound("PlantBlock") is NbtCompound plant)
                    {
                        var java = CanonicalReader.BedrockToJava(BedrockBlocks.FromPaletteNbt(plant));
                        string potted = "minecraft:potted_" + java.BaseName;
                        if (JavaBlockData.Latest.Blocks.ContainsKey(potted)) c.SetBlock(x, y, z, JavaBlocks.Canonicalize(BlockState.Get(potted)));
                    }
                    break;
                case "Music" when block.BaseName == "note_block":
                    c.SetBlock(x, y, z, StateEdit.With(block, "note", (be.GetInt("note") % 25).ToString()));
                    break;
                case "Jukebox" when block.BaseName == "jukebox":
                    if (be.ContainsKey("RecordItem")) c.SetBlock(x, y, z, StateEdit.With(block, "has_record", "true"));
                    break;
            }
        }
        FixPlantColumns(c);
    }

    // Java splits these plants into a tip block and a body block; Bedrock uses one block for both.
    static readonly (string Tip, string Body, int Grow)[] Columns =
    [
        ("kelp", "kelp_plant", 1), ("twisting_vines", "twisting_vines_plant", 1), ("weeping_vines", "weeping_vines_plant", -1), ("cave_vines", "cave_vines_plant", -1),
    ];

    public static void FixPlantColumns(CanonicalChunk c)
    {
        foreach (var (sy, s) in c.Sections.ToList())
        {
            if (!s.Palette.Any(p => Columns.Any(col => p.BaseName == col.Tip || p.BaseName == col.Body))) continue;
            for (int i = 0; i < 4096; i++)
            {
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                var b = c.GetBlock(x, y, z);
                foreach (var (tip, body, grow) in Columns)
                {
                    if (b.BaseName != tip && b.BaseName != body) continue;
                    var next = c.GetBlock(x, y + grow, z);
                    bool isBody = next.BaseName == tip || next.BaseName == body;
                    string want = "minecraft:" + (isBody ? body : tip);
                    if (b.Name == want) break;
                    var props = b.PropertyPairs().Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).ToList();
                    var n = JavaBlocks.Canonicalize(BlockState.Get(want, props));
                    c.SetBlock(x, y, z, n);
                    break;
                }
            }
        }
    }
}
