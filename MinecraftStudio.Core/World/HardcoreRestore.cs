// MinecraftStudio.Core/World/HardcoreRestore.cs: bringing a hardcore world back after its player died, so it can be
// played on (still hardcore).
// Owns: HardcoreRestore — which editions have hardcore (Java Edition: Data.hardcore; Bedrock: IsHardcore; the others have
// none), which players are dead (a hardcore death leaves them in Spectator; Java may also keep them on the death screen
// with no health), a safe place to stand (beside their own bed or respawn point, else on the surface near the world
// spawn: not in lava, water, fire or inside blocks — a spectator can be anywhere, also underground), and restoring a
// player there: Survival, full health, out of the death screen. Moving any player to safe ground (MoveTo / MoveAndSave:
// a stuck or lost player, with what a Java or console player rides) uses the same rules, in every world that keeps its
// players: Java, Bedrock, the consoles, Pocket 0.x and Indev. Players are edited in memory; the caller backs the world up
// and saves them (PlayerStore).
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

public static class HardcoreRestore
{
    /// <summary>
    /// Whether a player of this world can be moved to safe ground: every world whose players are kept and saved — Java,
    /// Bedrock, the consoles, Pocket 0.x and Indev (Classic levels keep no player; the 3DS's players aren't read).
    /// </summary>
    public static bool CanMove(SaveWorld world) => PlayerStore.CanSave(world) && world is JavaWorld or BedrockWorld or ConsoleWorld or PocketLegacyWorld or FiniteWorld { Indev: not null };

    /// <summary>
    /// How far above the feet a world keeps its players' height: Bedrock and Pocket 0.x at the eyes (1.62); Indev is written
    /// at the eyes too (a real Indev save stands at 33.62 on a block top; a spot is only taken with two more blocks of room,
    /// so a game that read it as the feet would drop them 1.62 blocks, harmlessly); Java and the consoles at the feet.
    /// </summary>
    public static double EyeHeight(SaveWorld world) => world is BedrockWorld or PocketLegacyWorld or FiniteWorld ? 1.62 : 0;

    /// <summary>Whether this kind of world can be hardcore at all (Java Edition and Bedrock Edition).</summary>
    public static bool HasHardcore(SaveWorld world) => world is JavaWorld or BedrockWorld;

    /// <summary>Whether the world is a hardcore world.</summary>
    public static bool IsHardcore(SaveWorld world) => world switch
    {
        JavaWorld j => j.Data?.GetLongOrNull("hardcore") is long h && h != 0,
        BedrockWorld b => b.LevelRoot.GetLongOrNull("IsHardcore") is long h && h != 0,
        _ => false,
    };

    /// <summary>The players a hardcore death has left spectating (or, in Java, still on the death screen).</summary>
    public static List<PlayerRecord> Dead(SaveWorld world)
    {
        if (!IsHardcore(world)) return [];
        return world.Players.Where(p => IsDead(world, p.Data)).ToList();
    }

    static bool IsDead(SaveWorld world, NbtCompound p) => world switch
    {
        // Spectator is 3 in Java; Bedrock writes it as 6.
        JavaWorld => p.GetLongOrNull("playerGameType") == WorldFlags.Spectator || p["Health"] is { IsNumeric: true } hp && hp.AsDouble <= 0,
        BedrockWorld => p.GetLongOrNull("PlayerGameMode") == 6 || p.GetLongOrNull("Dead") is long d && d != 0,
        _ => false,
    };

    /// <summary>Where a restored player will stand (the feet's block), and why there (for the message).</summary>
    public readonly record struct Spot(int X, int Y, int Z, string Where)
    {
        public override string ToString() => $"{X}, {Y}, {Z} ({Where})";
    }

    /// <summary>
    /// Where a dead player can safely stand: beside their own bed or respawn point when it is in the overworld (as the
    /// game would respawn them), else on the surface near the world spawn. Safe means solid, harmless ground (not liquid,
    /// fire, cactus, magma, a bed, a tree top …) with room for the body above (air, or plants and covers one walks
    /// through). Null when there is no such place (the area is not generated, or is all water or lava): then nothing is changed.
    /// </summary>
    public static Spot? SafeSpot(SaveWorld world, PlayerRecord? player = null, bool besideBed = true)
    {
        if (Overworld(world) is not { } area) return null;
        var need = RoomFor(world, player);
        // Their own bed or respawn point: beside it, a little up or down, nearest first (the game looks around a bed too).
        if (besideBed && player != null && RespawnPoint(world, player.Data) is { } home)
            for (int r = 0; r <= 3; r++)
                foreach (var (x, z) in Ring(home.X, home.Z, r))
                {
                    if (area.Column(x, z) is not { } col) continue;
                    foreach (int dy in (int[])[0, 1, -1, 2, -2])
                        if (SafeGround(col.GetBlock(x & 15, home.Y + dy - 1, z & 15), area.BedrockNames) && Room(area, x, home.Y + dy, z, need))
                            return new Spot(x, home.Y + dy, z, "by their bed or respawn point");
                }
        // Else on the surface near the world spawn.
        var info = WorldInfoReader.Read(world);
        return OnSurface(area, info.SpawnX, info.SpawnZ, need, "near the world spawn");
    }

    /// <summary>
    /// Safe ground on the surface near (<paramref name="x"/>, <paramref name="z"/>) in the overworld, nearest first, with room
    /// for the player (and what they ride, see <see cref="RoomFor"/>); null when none.
    /// </summary>
    public static Spot? SafeSpotNear(SaveWorld world, int x, int z, PlayerRecord? player = null) =>
        Overworld(world) is { } area ? OnSurface(area, x, z, RoomFor(world, player), $"near {x}, {z}") : null;

    /// <summary>How far out from the middle a search for safe ground looks (blocks), nearest columns first.</summary>
    public const int SearchReach = 32;

    static Spot? OnSurface(Area area, int x, int z, (int Height, int Reach) need, string where)
    {
        for (int r = 0; r <= SearchReach; r++)
            foreach (var (cx, cz) in Ring(x, z, r))
            {
                if (area.Column(cx, cz) is not { } col || col.Top(cx & 15, cz & 15, s => Cover(s, area.BedrockNames)) is not { } top || !SafeGround(top.State, area.BedrockNames)) continue;
                if (Room(area, cx, top.Y + 1, cz, need)) return new Spot(cx, top.Y + 1, cz, where);
            }
        return null;
    }

    // The overworld (else the first dimension), its chunks each read once: a search looks at the same chunks many times.
    static Area? Overworld(SaveWorld world) =>
        (world.Dimensions.FirstOrDefault(d => d.Id is "minecraft:overworld" or "overworld" or "0") ?? world.Dimensions.FirstOrDefault()) is { } d ? new Area(d, world is BedrockWorld or PocketLegacyWorld) : null;

    sealed class Area(WorldDimension dimension, bool bedrockNames)
    {
        readonly Dictionary<ChunkPos, ChunkColumn?> _chunks = [];

        /// <summary>Its blocks carry Bedrock's names (<see cref="BaseName"/>).</summary>
        public bool BedrockNames => bedrockNames;

        public ChunkColumn? Column(int x, int z)
        {
            var pos = new ChunkPos(x >> 4, z >> 4);
            if (!_chunks.TryGetValue(pos, out var c)) _chunks[pos] = c = SafeLoad(dimension, pos);
            return c;
        }
    }

    /// <summary>
    /// What a Java or console player rides, kept with the player and put back under them when they join: from 1.9 the
    /// bottom of the stack in RootVehicle (its riders in Passengers); before 1.9 and on the consoles the one right under
    /// them in "Riding" (what that rides in its own "Riding").
    /// </summary>
    public static NbtCompound? Mount(NbtCompound? player) => player?["RootVehicle"] is NbtCompound { } rv && rv["Entity"] is NbtCompound e ? e : player?["Riding"] as NbtCompound;

    /// <summary>
    /// The room a player needs where they are put: blocks of air up from the feet, and columns out to each side that must be
    /// clear too. A player alone needs 2 and 0. A Java player riding something brings it along (<see cref="Mount"/>), so then
    /// room for the mount, its width, and them sitting on it.
    /// </summary>
    public static (int Height, int Reach) RoomFor(SaveWorld world, PlayerRecord? player)
    {
        // Indev is written at the eyes (EyeHeight): two more blocks, so it is room either way it is read.
        if (world is FiniteWorld) return (4, 0);
        if (world is not (JavaWorld or ConsoleWorld) || Mount(player?.Data) is not { } vehicle) return (2, 0);
        string id = MountKind(vehicle);
        if (id.Contains("boat") || id.Contains("raft") || id.Contains("minecart") || id == "pig") return (3, 1);
        return id switch
        {
            "horse" or "donkey" or "mule" or "skeleton_horse" or "zombie_horse" or "llama" or "trader_llama" or "strider" => (4, 1),
            "happy_ghast" => (7, 2),
            _ => (5, 1), // a camel, or anything ridden by command
        };
    }

    /// <summary>What a Java player rides, in words ("horse", "oak boat"); null when they ride nothing.</summary>
    public static string? MountName(SaveWorld world, PlayerRecord player) =>
        world is JavaWorld or ConsoleWorld && Mount(player.Data) is { } vehicle ? (MountKind(vehicle) is { Length: > 0 } kind ? WorldSearch.Nice(kind) : "mount") : null;

    // The mount's newest Java name without "minecraft:" (pre-1.11 CamelCase ids too; an old EntityHorse says its kind in Type).
    static string MountKind(NbtCompound vehicle)
    {
        string raw = vehicle.GetString("id") ?? "";
        if (raw == "EntityHorse") return vehicle.GetInt("Type") switch { 1 => "donkey", 2 => "mule", 3 => "zombie_horse", 4 => "skeleton_horse", _ => "horse" };
        string id = raw.Length == 0 ? "" : Entities.CanonicalId(raw).ToLowerInvariant();
        return id.StartsWith("minecraft:", StringComparison.Ordinal) ? id["minecraft:".Length..] : id;
    }

    /// <summary>
    /// Moves a player (in memory) to stand at the spot, in the overworld: still, not falling, not burning. What a Java player
    /// rides comes with them (else the game would seat them on it back where it was). Returns what was changed; the caller
    /// saves the player. Restore does this too, with the rest of coming back to life.
    /// </summary>
    public static List<string> MoveTo(SaveWorld world, PlayerRecord player, Spot at)
    {
        var p = player.Data;
        var done = new List<string> { $"standing at {at}" };
        switch (world)
        {
            case JavaWorld or ConsoleWorld:
                // A console player is a Java 1.x player: the feet's height, Fire -20 when not burning, Dimension 0, "Riding".
                JavaStill(p, at.X + 0.5, at.Y, at.Z + 0.5, player: true);
                if (p["RootVehicle"] is NbtCompound && Mount(p) is { } vehicle) JavaStill(vehicle, at.X + 0.5, at.Y, at.Z + 0.5, player: false);
                if (Mount(p) != null) done.Add($"their {MountName(world, player)} came with them");
                break;
            case BedrockWorld or PocketLegacyWorld:
                if (p.ContainsKey("Fire")) p["Fire"] = new NbtShort(0);
                if (p.ContainsKey("FallDistance")) p["FallDistance"] = new NbtFloat(0);
                // Bedrock and Pocket keep a player's position at eye height: 1.62 above the feet.
                p["Pos"] = new NbtList(NbtType.Float) { new NbtFloat(at.X + 0.5f), new NbtFloat(at.Y + 1.62f), new NbtFloat(at.Z + 0.5f) };
                if (p.ContainsKey("Motion")) p["Motion"] = new NbtList(NbtType.Float) { new NbtFloat(0), new NbtFloat(0), new NbtFloat(0) };
                if (p.ContainsKey("DimensionId")) p.SetInt("DimensionId", 0);
                break;
            case FiniteWorld { Indev: not null }:
                // Indev's LocalPlayer: floats, written at the eyes (EyeHeight), not falling, not burning (-20, as Java).
                if (p.ContainsKey("Fire")) p.SetNumber("Fire", -20, NbtType.Short);
                if (p.ContainsKey("FallDistance")) p["FallDistance"] = new NbtFloat(0);
                p["Pos"] = new NbtList(NbtType.Float) { new NbtFloat(at.X + 0.5f), new NbtFloat(at.Y + 1.62f), new NbtFloat(at.Z + 0.5f) };
                if (p.ContainsKey("Motion")) p["Motion"] = new NbtList(NbtType.Float) { new NbtFloat(0), new NbtFloat(0), new NbtFloat(0) };
                break;
            default:
                throw new NotSupportedException("Players can be moved in Java, Bedrock, console, Pocket Edition 0.x and Indev worlds.");
        }
        return done;
    }

    // A Java entity (and whoever rides it) standing still at the place, in the overworld, not falling, not burning.
    static void JavaStill(NbtCompound e, double x, double y, double z, bool player)
    {
        if (e.ContainsKey("FallDistance")) e["FallDistance"] = e["FallDistance"] is NbtDouble ? new NbtDouble(0) : new NbtFloat(0);
        // 1.21.5+ calls it fall_distance: without it set to 0, a fall they were in would hurt them where they land.
        if (e.ContainsKey("fall_distance")) e["fall_distance"] = new NbtDouble(0);
        // Not burning: a player's "none" is -20, other entities' -1.
        if (player) { if (e.ContainsKey("Fire")) e.SetNumber("Fire", -20, NbtType.Short); }
        else if (e.GetLongOrNull("Fire") > 0) e.SetNumber("Fire", -1, NbtType.Short);
        e["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(x), new NbtDouble(y), new NbtDouble(z) };
        if (e.ContainsKey("Motion")) e["Motion"] = new NbtList(NbtType.Double) { new NbtDouble(0), new NbtDouble(0), new NbtDouble(0) };
        // The overworld, written the way the file writes dimensions (a name since 1.16, a number before; old entities had one too).
        if (e.ContainsKey("Dimension")) e["Dimension"] = e["Dimension"] is NbtString ? new NbtString("minecraft:overworld") : new NbtInt(0);
        if (e.GetList("Passengers") is NbtList riders)
            foreach (var r in riders.OfType<NbtCompound>()) JavaStill(r, x, y, z, player: false);
        // Before 1.9 and on the consoles what it rides is inside it, and comes along too.
        if (e["Riding"] is NbtCompound under) JavaStill(under, x, y, z, player: false);
    }

    /// <summary>
    /// Moves the player to the spot (<see cref="MoveTo"/>) and saves them where they came from; the caller backs the world
    /// up first. When the save fails they are put back as they were in memory, so no later save writes half a move.
    /// </summary>
    public static List<string> MoveAndSave(SaveWorld world, PlayerRecord player, Spot at)
    {
        var before = (NbtCompound)player.Data.Clone();
        try
        {
            var done = MoveTo(world, player, at);
            PlayerStore.Save(world, player);
            return done;
        }
        catch
        {
            player.Data.Clear();
            foreach (var (k, v) in before) player.Data[k] = v;
            // Bedrock: the database holds the moved record until it is written; give it the old one back.
            if (world is BedrockWorld b) b.SavePlayer(player);
            throw;
        }
    }

    /// <summary>The player's own respawn point (a bed, or one set by command) when it is in the overworld.</summary>
    static (int X, int Y, int Z)? RespawnPoint(SaveWorld world, NbtCompound p)
    {
        static bool Overworld(string? dim) => dim is null or "" or "minecraft:overworld" or "overworld";
        switch (world)
        {
            case JavaWorld or ConsoleWorld:
                // 1.21.5 onwards: respawn {pos: [x, y, z], dimension}; before: SpawnX/Y/Z and (1.16 onwards) SpawnDimension.
                // A console player's bed is kept the old Java way.
                if (p["respawn"] is NbtCompound r)
                    return r["pos"] is NbtIntArray { Value.Length: 3 } a && Overworld(r.GetString("dimension")) ? (a.Value[0], a.Value[1], a.Value[2]) : null;
                if (p.GetLongOrNull("SpawnX") is long x && p.GetLongOrNull("SpawnY") is long y && p.GetLongOrNull("SpawnZ") is long z && Overworld(p.GetString("SpawnDimension")))
                    return ((int)x, (int)y, (int)z);
                return null;
            case BedrockWorld:
                // The bed's own block when there is one, else the point set; int.MinValue when none is set.
                if (p.GetLongOrNull("SpawnDimension") is long dim && dim != 0) return null;
                foreach (var prefix in (string[])["SpawnBlockPosition", "Spawn"])
                    if (p.GetLongOrNull(prefix + "X") is long bx && p.GetLongOrNull(prefix + "Y") is long by && p.GetLongOrNull(prefix + "Z") is long bz
                        && bx != int.MinValue && by != int.MinValue && bz != int.MinValue && by is > -128 and < 512)
                        return ((int)bx, (int)by, (int)bz);
                return null;
            default:
                return null;
        }
    }

    static IEnumerable<(int X, int Z)> Ring(int cx, int cz, int r)
    {
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == r) yield return (cx + dx, cz + dz);
    }

    static ChunkColumn? SafeLoad(WorldDimension dim, ChunkPos pos)
    {
        try { return dim.LoadChunk(pos); }
        catch (Exception e) when (e is IOException or InvalidDataException) { return null; }
    }

    // Room for the body: the blocks from the feet up are air, or something walked through — and, for what they ride, the
    // columns around too (a mount is wider than a block). A column not in the world isn't room.
    static bool Room(Area area, int x, int y, int z, (int Height, int Reach) need)
    {
        for (int dz = -need.Reach; dz <= need.Reach; dz++)
            for (int dx = -need.Reach; dx <= need.Reach; dx++)
            {
                if (area.Column(x + dx, z + dz) is not { } col) return false;
                for (int h = 0; h < need.Height; h++)
                    if (!Clear(col.GetBlock((x + dx) & 15, y + h, (z + dz) & 15), area.BedrockNames)) return false;
            }
        return true;
    }

    static bool Clear(BlockState s, bool bedrockNames) => s.IsAir || Cover(s, bedrockNames);

    /// <summary>
    /// A block's name the way the lists here spell it (Java's): a Bedrock world's blocks are read with the newest Bedrock
    /// names, whose "snow" is the full snow block (the thin layer is "snow_layer") — in Java "snow" is the layer a player
    /// walks through. (Bedrock's older "grass" for the grass block is read as "grass_block" already.)
    /// </summary>
    internal static string BaseName(BlockState s, bool bedrockNames) => bedrockNames ? BlockNames.JavaBaseName(s) : s.BaseName;

    // Plants and thin covers a player walks through and can stand in without harm (Java names, and Bedrock's older ones).
    static readonly HashSet<string> Covers = new(StringComparer.OrdinalIgnoreCase)
    {
        "grass", "short_grass", "tall_grass", "tallgrass", "fern", "large_fern", "dead_bush", "deadbush", "bush", "firefly_bush",
        "short_dry_grass", "tall_dry_grass", "leaf_litter", "pink_petals", "wildflowers", "dandelion", "poppy", "blue_orchid",
        "allium", "azure_bluet", "oxeye_daisy", "cornflower", "lily_of_the_valley", "torchflower", "sunflower", "lilac",
        "rose_bush", "peony", "pitcher_plant", "open_eyeblossom", "closed_eyeblossom", "red_flower", "yellow_flower", "double_plant",
        "brown_mushroom", "red_mushroom", "snow", "snow_layer", "wheat", "carrots", "potatoes", "beetroots", "beetroot",
        "melon_stem", "pumpkin_stem", "sugar_cane", "reeds", "glow_lichen", "sculk_vein", "hanging_roots", "crimson_roots",
        "warped_roots", "nether_sprouts", "vine", "torch", "wall_torch", "soul_torch", "soul_wall_torch", "redstone_torch",
        "redstone_wall_torch", "redstone_wire", "lever", "rail", "powered_rail", "detector_rail", "activator_rail", "golden_rail",
    };

    /// <summary>A plant or thin cover a player walks through (a flower, a snow layer, a torch, a carpet …).</summary>
    internal static bool Cover(BlockState s, bool bedrockNames)
    {
        string n = BaseName(s, bedrockNames);
        return Covers.Contains(n) || n.EndsWith("_sapling", StringComparison.OrdinalIgnoreCase) || n.EndsWith("_tulip", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith("_carpet", StringComparison.OrdinalIgnoreCase) || n.EndsWith("_button", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith("_sign", StringComparison.OrdinalIgnoreCase) || n.EndsWith("_banner", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ground a player can stand on without harm (and not a tree top, a bed, a fence or a wall).</summary>
    static bool SafeGround(BlockState s, bool bedrockNames)
    {
        string n = BaseName(s, bedrockNames);
        if (s.IsAir || s.IsWaterLike || Cover(s, bedrockNames)) return false;
        if (n.Equals("bed", StringComparison.OrdinalIgnoreCase) || n.EndsWith("_bed", StringComparison.OrdinalIgnoreCase) || n.EndsWith("_wall", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var bad in (string[])["lava", "water", "fire", "cactus", "magma", "campfire", "powder_snow", "sweet_berry", "wither_rose", "pointed_dripstone",
            "bubble_column", "kelp", "seagrass", "leaves", "scaffolding", "cobweb", "web", "portal", "tnt", "fence", "lily_pad", "waterlily", "pressure_plate", "tripwire"])
            if (n.Contains(bad, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// Brings a dead player back in memory: Survival, full health, out of the death screen, standing at the spot given.
    /// Returns what was changed (for the message); the caller saves the player.
    /// </summary>
    public static List<string> Restore(SaveWorld world, PlayerRecord player, Spot at)
    {
        var p = player.Data;
        var done = new List<string>();
        switch (world)
        {
            case JavaWorld:
                if (p.ContainsKey("playerGameType")) { SetNumber(p, "playerGameType", WorldFlags.Survival); done.Add("back in Survival"); }
                if (p.ContainsKey("Health")) { p["Health"] = new NbtFloat(20f); done.Add("full health"); }
                if (p.ContainsKey("DeathTime")) p["DeathTime"] = new NbtShort(0);
                if (p.ContainsKey("HurtTime")) p["HurtTime"] = new NbtShort(0);
                if (p.ContainsKey("Air")) p["Air"] = new NbtShort(300);
                if (p.GetLongOrNull("foodLevel") is long food && food < 6) SetNumber(p, "foodLevel", 20);
                // Standing at the spot: still, not falling (the fall that killed them must not hurt again), not burning.
                done.AddRange(MoveTo(world, player, at));
                break;
            case BedrockWorld:
                if (p.ContainsKey("PlayerGameMode")) { SetNumber(p, "PlayerGameMode", WorldFlags.Survival); done.Add("back in Survival"); }
                if (p.ContainsKey("Dead")) p.SetByte("Dead", 0);
                if (p.ContainsKey("DeathTime")) p["DeathTime"] = new NbtShort(0);
                if (p.GetList("Attributes") is NbtList attributes)
                    foreach (var a in attributes.OfType<NbtCompound>().Where(a => a.GetString("Name") == "minecraft:health"))
                    {
                        a["Current"] = new NbtFloat(a["Max"] is { IsNumeric: true } max && max.AsDouble > 0 ? (float)max.AsDouble : 20f);
                        done.Add("full health");
                    }
                done.AddRange(MoveTo(world, player, at));
                break;
        }
        return done;
    }

    static void SetNumber(NbtCompound c, string name, int value)
    {
        switch (c[name])
        {
            case NbtByte: c.SetByte(name, (sbyte)value); break;
            case NbtShort: c[name] = new NbtShort((short)value); break;
            case NbtLong: c.SetLong(name, value); break;
            default: c.SetInt(name, value); break;
        }
    }
}
