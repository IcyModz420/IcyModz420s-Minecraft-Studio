// MinecraftStudio.Core/World/LagMachines.Entities.cs: entities of LagMachines.
// Part of LagMachines (main part in LagMachines.cs). Piles in one block — minecarts, boats, armour stands, mobs past the
// game's crowding rule (24 share a block before they are squashed), item frames stacked on one face — and floods in one
// chunk: dropped items, experience orbs, falling blocks, lit TNT, projectiles, potion clouds, markers. Riders count too.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

public static partial class LagMachines
{
    /// <summary>The family an entity belongs to for piles and floods (canonical ids).</summary>
    enum Family { Minecart, Boat, ArmourStand, Mob, Frame, Item, Orb, FallingBlock, Tnt, Projectile, Cloud, Marker, Other }

    static Family FamilyOf(string id)
    {
        string n = id.StartsWith("minecraft:", StringComparison.Ordinal) ? id[10..] : id;
        if (n.EndsWith("minecart", StringComparison.Ordinal)) return Family.Minecart;
        if (n.EndsWith("_boat", StringComparison.Ordinal) || n.EndsWith("_raft", StringComparison.Ordinal) || n is "boat" or "chest_boat") return Family.Boat;
        return n switch
        {
            "armor_stand" => Family.ArmourStand,
            "item" => Family.Item,
            "experience_orb" => Family.Orb,
            "falling_block" => Family.FallingBlock,
            "tnt" => Family.Tnt,
            "item_frame" or "glow_item_frame" => Family.Frame,
            "arrow" or "spectral_arrow" or "trident" or "snowball" or "egg" or "potion" or "splash_potion" or "lingering_potion" or "experience_bottle"
                or "fireball" or "small_fireball" or "dragon_fireball" or "wither_skull" or "shulker_bullet" or "llama_spit" or "firework_rocket"
                or "wind_charge" or "breeze_wind_charge" or "ender_pearl" => Family.Projectile,
            "area_effect_cloud" => Family.Cloud,
            "marker" or "block_display" or "item_display" or "text_display" or "interaction" => Family.Marker,
            "painting" or "leash_knot" or "end_crystal" or "evoker_fangs" or "eye_of_ender" or "lightning_bolt" or "fishing_bobber" or "ominous_item_spawner" => Family.Other,
            _ => Family.Mob,
        };
    }

    /// <summary>How many of one family there are (in a chunk, or in one block), of which kinds, and where the first was.</summary>
    sealed class Tally(int x, int y, int z)
    {
        public int Count;
        public readonly int X = x, Y = y, Z = z;
        public readonly Dictionary<string, int> Kinds = new(StringComparer.Ordinal);

        public void Add(string id) { Count++; Kinds[id] = Kinds.GetValueOrDefault(id) + 1; }
    }

    static void Entities(CanonicalChunk c, List<Hit> hits)
    {
        var flood = new Dictionary<Family, Tally>();
        // Piles: per family, per 1×2×1 cell (a pile pushes itself up a little).
        var cells = new Dictionary<(Family, int, int, int), Tally>();
        var faces = new Dictionary<(int, int, int, long), int>();
        void Count(NbtCompound e)
        {
            string id = EntityId(e);
            var f = FamilyOf(id);
            if (e.GetList("Pos") is { Count: 3 } p)
            {
                int x = (int)Math.Floor(p[0].AsDouble), y = (int)Math.Floor(p[1].AsDouble), z = (int)Math.Floor(p[2].AsDouble);
                if (!flood.TryGetValue(f, out var fl)) flood[f] = fl = new Tally(x, y, z);
                fl.Add(id);
                if (f is Family.Minecart or Family.Boat or Family.ArmourStand or Family.Mob or Family.FallingBlock)
                {
                    var key = (f, x, y >> 1, z);
                    if (!cells.TryGetValue(key, out var cell)) cells[key] = cell = new Tally(x, y, z);
                    cell.Add(id);
                }
                if (f == Family.Frame)
                {
                    var face = (e.GetInt("TileX"), e.GetInt("TileY"), e.GetInt("TileZ"), e.GetLongOrNull("Facing") ?? e.GetLongOrNull("facing") ?? -1);
                    faces[face] = faces.GetValueOrDefault(face) + 1;
                }
            }
            foreach (var rider in e.GetList("Passengers")?.OfType<NbtCompound>() ?? []) Count(rider);
        }
        foreach (var e in c.Entities) Count(e);

        foreach (var ((f, x, _, z), t) in cells)
        {
            int limit = f switch
            {
                Family.Mob => Limits.MobsInOneBlock, Family.Minecart => Limits.MinecartsInOneBlock, Family.Boat => Limits.BoatsInOneBlock,
                Family.FallingBlock => Limits.FallingBlocksInOneBlock, _ => Limits.ArmourStandsInOneBlock,
            };
            int n = t.Count, y = t.Y;
            if (n < limit) continue;
            var (kind, what) = f switch
            {
                Family.Minecart => (LagKind.MinecartStack, $"{n:N0} minecarts in one block"),
                Family.Boat => (LagKind.BoatStack, $"{n:N0} boats in one block"),
                Family.ArmourStand => (LagKind.ArmourStandStack, $"{n:N0} armour stands in one block"),
                // Sand that caves in falls one block per column; a machine drops it in one place, again and again.
                Family.FallingBlock => (LagKind.FallingBlocks, $"{n:N0} falling blocks (sand, gravel, anvils …) in one block"),
                _ => (LagKind.MobPile, Many(n, t.Kinds, "mobs") + " in one block (the game squashes more than 24)"),
            };
            hits.Add(new Hit(kind, x, y, z, (x, y, z, x, y + 1, z), n, what));
        }
        foreach (var ((x, y, z, _), n) in faces)
            if (n >= Limits.FramesOnOneFace) hits.Add(new Hit(LagKind.StackedFrames, x, y, z, Around(x, y, z), n, $"{n:N0} item frames on one block face (only one fits)"));

        foreach (var (f, t) in flood)
        {
            (int Limit, LagKind Kind)? rule = f switch
            {
                Family.Item => (Limits.DroppedItemsInOneChunk, LagKind.DroppedItems),
                Family.Orb => (Limits.OrbsInOneChunk, LagKind.ExperienceOrbs),
                Family.FallingBlock => (Limits.FallingBlocksInOneChunk, LagKind.FallingBlocks),
                Family.Tnt => (Limits.LitTntInOneChunk, LagKind.LitTnt),
                Family.Projectile => (Limits.ProjectilesInOneChunk, LagKind.Projectiles),
                Family.Cloud => (Limits.CloudsInOneChunk, LagKind.PotionClouds),
                Family.Marker => (Limits.MarkersInOneChunk, LagKind.Markers),
                Family.Mob => (Limits.MobsInOneChunk, LagKind.MobPile),
                Family.Minecart => (Limits.MinecartsInOneChunk, LagKind.MinecartStack),
                _ => null,
            };
            int n = t.Count;
            if (rule is not { } r || n < r.Limit) continue;
            string what = f switch
            {
                Family.Item => $"{n:N0} dropped items in one chunk ({Named(ItemKinds(c), 3)})",
                Family.Orb => $"{n:N0} experience orbs in one chunk",
                Family.FallingBlock => $"{n:N0} falling blocks (sand, gravel, anvils …) in one chunk",
                Family.Tnt => $"{n:N0} lit TNT in one chunk",
                Family.Projectile => Many(n, t.Kinds, "projectiles") + " in one chunk",
                Family.Cloud => $"{n:N0} lingering potion clouds in one chunk",
                Family.Marker => Many(n, t.Kinds, "markers and displays") + " in one chunk",
                Family.Mob => Many(n, t.Kinds, "mobs") + " in one chunk",
                _ => $"{n:N0} minecarts in one chunk",
            };
            int bx = c.X * 16, bz = c.Z * 16;
            hits.Add(new Hit(r.Kind, bx + 8, t.Y, bz + 8, (bx, t.Y, bz, bx + 15, t.Y, bz + 15), n, what));
        }
    }

    // A pile or flood in words: "312 chickens", or "330 mobs (312 chickens, 18 cows)" when it is of several kinds.
    static string Many(int n, Dictionary<string, int> kinds, string noun) =>
        kinds.Count == 1 ? $"{n:N0} {Plural(WorldSearch.Nice(kinds.Keys.First()))}" : $"{n:N0} {noun} ({Most(kinds, 2)})";

    static string Most(Dictionary<string, int> kinds, int most) =>
        string.Join(", ", kinds.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal).Take(most).Select(k => $"{k.Value:N0} {Plural(WorldSearch.Nice(k.Key))}"));

    // What things are, most first, by name ("cobblestone 1,250, dirt 1,250"): for items and blocks, whose names are no nouns to count.
    static string Named(Dictionary<string, int> kinds, int most) =>
        string.Join(", ", kinds.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal).Take(most).Select(k => $"{WorldSearch.Nice(k.Key)} {k.Value:N0}"));

    static string Plural(string s) => s == "tnt" ? "lit TNT" : s.EndsWith('s') ? s : s.EndsWith('y') && !s.EndsWith("ey") ? s[..^1] + "ies" : s + "s";

    // The items lying in the chunk by kind (what a flood of dropped items is).
    static Dictionary<string, int> ItemKinds(CanonicalChunk c)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in c.Entities)
            if (EntityId(e) == "minecraft:item" && e.GetCompound("Item")?.GetString("id") is string item)
                d[item] = d.GetValueOrDefault(item) + 1;
        return d;
    }

    /// <summary>The entity id, namespaced.</summary>
    static string EntityId(NbtCompound e)
    {
        string id = e.GetString("id") ?? "?";
        return id.Contains(':') ? id : "minecraft:" + id;
    }
}
