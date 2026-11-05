// MinecraftStudio.Core/Conversion/Entities.cs: Entities across editions. Canonical form = newest Java entity
// NBT (namespaced id, Pos/Motion doubles, Rotation floats, Health float, canonical items, canonical JSON text).
// Owns: Entities.
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Entities across editions. Canonical form = newest Java entity NBT (namespaced id, Pos/Motion doubles,
/// Rotation floats, Health float, canonical items, canonical JSON text). Fields a target does not know are dropped;
/// entities a target cannot represent (Bedrock agents, Java item frames on Bedrock...) are skipped.
/// </summary>
public static partial class Entities
{
    // Pre-1.11 Java ids (CamelCase) -> 1.11 names.
    static readonly Dictionary<string, string> Old = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AreaEffectCloud"] = "area_effect_cloud", ["ArmorStand"] = "armor_stand", ["Arrow"] = "arrow", ["Bat"] = "bat", ["Boat"] = "boat",
        ["ThrownExpBottle"] = "xp_bottle", ["Blaze"] = "blaze", ["CaveSpider"] = "cave_spider", ["Chicken"] = "chicken", ["Cow"] = "cow",
        ["Creeper"] = "creeper", ["DragonFireball"] = "dragon_fireball", ["ThrownEgg"] = "egg", ["EnderCrystal"] = "ender_crystal",
        ["EnderDragon"] = "ender_dragon", ["ThrownEnderpearl"] = "ender_pearl", ["Enderman"] = "enderman", ["Endermite"] = "endermite",
        ["EyeOfEnderSignal"] = "eye_of_ender_signal", ["XPOrb"] = "xp_orb", ["FallingSand"] = "falling_block", ["Fireball"] = "fireball",
        ["FireworksRocketEntity"] = "fireworks_rocket", ["Ghast"] = "ghast", ["Giant"] = "giant", ["Guardian"] = "guardian", ["EntityHorse"] = "horse",
        ["VillagerGolem"] = "villager_golem", ["ItemFrame"] = "item_frame", ["Item"] = "item", ["LeashKnot"] = "leash_knot",
        ["LightningBolt"] = "lightning_bolt", ["LavaSlime"] = "magma_cube", ["MinecartRideable"] = "minecart", ["MinecartChest"] = "chest_minecart",
        ["MinecartCommandBlock"] = "commandblock_minecart", ["MinecartFurnace"] = "furnace_minecart", ["MinecartHopper"] = "hopper_minecart",
        ["MinecartSpawner"] = "spawner_minecart", ["MinecartTNT"] = "tnt_minecart", ["MushroomCow"] = "mooshroom", ["Ozelot"] = "ocelot",
        ["Painting"] = "painting", ["Pig"] = "pig", ["PolarBear"] = "polar_bear", ["Rabbit"] = "rabbit", ["Sheep"] = "sheep",
        ["ShulkerBullet"] = "shulker_bullet", ["Shulker"] = "shulker", ["Silverfish"] = "silverfish", ["Skeleton"] = "skeleton", ["Slime"] = "slime",
        ["SmallFireball"] = "small_fireball", ["SnowMan"] = "snowman", ["Snowball"] = "snowball", ["SpectralArrow"] = "spectral_arrow",
        ["Spider"] = "spider", ["ThrownPotion"] = "potion", ["Squid"] = "squid", ["PrimedTnt"] = "tnt", ["Villager"] = "villager", ["Witch"] = "witch",
        ["WitherBoss"] = "wither", ["WitherSkull"] = "wither_skull", ["Wolf"] = "wolf", ["Zombie"] = "zombie", ["PigZombie"] = "zombie_pigman",
    };

    // Renames after 1.11 (old -> new, data version).
    static readonly (string Old, string New, int Since)[] Renames =
    [
        ("minecraft:xp_orb", "minecraft:experience_orb", 1451), ("minecraft:xp_bottle", "minecraft:experience_bottle", 1451),
        ("minecraft:eye_of_ender_signal", "minecraft:eye_of_ender", 1451), ("minecraft:ender_crystal", "minecraft:end_crystal", 1451),
        ("minecraft:fireworks_rocket", "minecraft:firework_rocket", 1451), ("minecraft:commandblock_minecart", "minecraft:command_block_minecart", 1451),
        ("minecraft:snowman", "minecraft:snow_golem", 1451), ("minecraft:villager_golem", "minecraft:iron_golem", 1451),
        ("minecraft:evocation_fangs", "minecraft:evoker_fangs", 1451), ("minecraft:evocation_illager", "minecraft:evoker", 1451),
        ("minecraft:vindication_illager", "minecraft:vindicator", 1451), ("minecraft:illusion_illager", "minecraft:illusioner", 1451),
        ("minecraft:zombie_pigman", "minecraft:zombified_piglin", 2510),
    ];

    // Java (canonical) -> Bedrock identifiers where they differ.
    static readonly Dictionary<string, string> JavaToBedrock = new(StringComparer.Ordinal)
    {
        ["minecraft:villager"] = "minecraft:villager_v2", ["minecraft:zombie_villager"] = "minecraft:zombie_villager_v2",
        ["minecraft:zombified_piglin"] = "minecraft:zombie_pigman", ["minecraft:evoker"] = "minecraft:evocation_illager",
        ["minecraft:end_crystal"] = "minecraft:ender_crystal", ["minecraft:experience_orb"] = "minecraft:xp_orb",
        ["minecraft:experience_bottle"] = "minecraft:xp_bottle", ["minecraft:eye_of_ender"] = "minecraft:eye_of_ender_signal",
        ["minecraft:firework_rocket"] = "minecraft:fireworks_rocket", ["minecraft:trident"] = "minecraft:thrown_trident",
        ["minecraft:fishing_bobber"] = "minecraft:fishing_hook", ["minecraft:evoker_fangs"] = "minecraft:evocation_fang",
        ["minecraft:potion"] = "minecraft:splash_potion", ["minecraft:wind_charge"] = "minecraft:wind_charge_projectile",
        ["minecraft:breeze_wind_charge"] = "minecraft:breeze_wind_charge_projectile", ["minecraft:mooshroom"] = "minecraft:mooshroom",
    };

    static readonly Dictionary<string, string> BedrockToJava = JavaToBedrock.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    static readonly string[] BoatWoods = ["oak", "spruce", "birch", "jungle", "acacia", "dark_oak", "mangrove", "bamboo", "cherry", "pale_oak"];

    // Entities with no counterpart in the other edition.
    static readonly HashSet<string> BedrockOnly = new(StringComparer.Ordinal) { "minecraft:agent", "minecraft:npc", "minecraft:balloon", "minecraft:tripod_camera", "minecraft:ice_bomb", "minecraft:chalkboard", "minecraft:player" };
    static readonly HashSet<string> JavaOnly = new(StringComparer.Ordinal) { "minecraft:item_frame", "minecraft:glow_item_frame", "minecraft:marker", "minecraft:interaction", "minecraft:block_display", "minecraft:item_display", "minecraft:text_display", "minecraft:illusioner", "minecraft:giant", "minecraft:player" };

    static string Ns(string id) => id.Contains(':') ? id : "minecraft:" + id;

    /// <summary>Newest Java id for any Java id (CamelCase pre-1.11 included).</summary>
    public static string CanonicalId(string id)
    {
        if (Old.TryGetValue(id, out var o)) id = o;
        id = Ns(id);
        foreach (var (oldName, newName, _) in Renames) if (id == oldName) id = newName;
        return id;
    }

    /// <summary>Java id for a target data version (pre-1.11 CamelCase when <paramref name="namespaced"/> is false).</summary>
    public static string LegacyJavaId(string canonical, bool namespaced, int dataVersion = 1343)
    {
        string id = canonical;
        foreach (var (oldName, newName, since) in Renames.Reverse()) if (id == newName && dataVersion < since) id = oldName;
        if (id.EndsWith("_boat") || id.EndsWith("_raft")) id = id.Contains("chest") ? "minecraft:chest_boat" : "minecraft:boat";
        if (namespaced) return id;
        string b = id.Replace("minecraft:", "");
        var hit = Old.FirstOrDefault(kv => kv.Value == b);
        return hit.Key ?? b;
    }

    public static string JavaIdFromBedrock(string identifier)
    {
        identifier = Ns(identifier.Split('<')[0]);
        if (identifier == "minecraft:villager") return "minecraft:villager";
        return BedrockToJava.TryGetValue(identifier, out var j) ? j : identifier;
    }

    public static string BedrockIdentifier(string canonical)
    {
        if (canonical.EndsWith("_chest_boat") || canonical.EndsWith("_chest_raft")) return "minecraft:chest_boat";
        if (canonical.EndsWith("_boat") || canonical.EndsWith("_raft")) return "minecraft:boat";
        return JavaToBedrock.TryGetValue(canonical, out var b) ? b : canonical;
    }

    static NbtList Doubles(params double[] v) { var l = new NbtList(NbtType.Double); foreach (var d in v) l.Add(new NbtDouble(d)); return l; }
    static NbtList Floats(params float[] v) { var l = new NbtList(NbtType.Float); foreach (var d in v) l.Add(new NbtFloat(d)); return l; }

    static double[] Vec(NbtCompound e, string key, int n)
    {
        var r = new double[n];
        if (e.GetList(key) is NbtList l) for (int i = 0; i < n && i < l.Count; i++) r[i] = l[i].AsDouble;
        return r;
    }

    // ---------------------------------------------------------------- owners, equipment and variants

    const int BedrockUuidMarker = unchecked((int)0xBEDB0C00);

    /// <summary>A stable Java UUID for a Bedrock actor or player id, so pets keep pointing at their owner across editions.</summary>
    public static Guid UuidForBedrockId(long id)
    {
        var b = new byte[16];
        BitConverter.GetBytes(BedrockUuidMarker).CopyTo(b, 0);
        BitConverter.GetBytes(id).CopyTo(b, 8);
        return new Guid(b);
    }

    /// <summary>The Bedrock id behind a UUID made by <see cref="UuidForBedrockId"/>, or a stable id for any other UUID.</summary>
    public static long BedrockIdForUuid(Guid g)
    {
        var b = g.ToByteArray();
        return BitConverter.ToInt32(b, 0) == BedrockUuidMarker ? BitConverter.ToInt64(b, 8) : BitConverter.ToInt64(b, 0) ^ BitConverter.ToInt64(b, 8);
    }

    static readonly string[] Professions = ["none", "farmer", "fisherman", "shepherd", "fletcher", "librarian", "cartographer", "cleric", "armorer", "weaponsmith", "toolsmith", "butcher", "leatherworker", "mason", "nitwit"];
    static readonly string[] VillagerTypes = ["plains", "desert", "jungle", "savanna", "snow", "swamp", "taiga"];
    /// <summary>Cat variants by Bedrock number; Java 1.14–1.18 numbered them tabby-first.</summary>
    static readonly string[] BedrockCats = ["white", "black", "red", "siamese", "british_shorthair", "calico", "persian", "ragdoll", "tabby", "all_black", "jellie"];
    static readonly string[] JavaCatTypes = ["tabby", "black", "red", "siamese", "british_shorthair", "calico", "persian", "ragdoll", "white", "jellie", "all_black"];
    static readonly string[] Frogs = ["temperate", "cold", "warm"];

    static string Bare(string? id) => (id ?? "").Replace("minecraft:", "");

    static NbtCompound ItemStack(string id) => new() { { "id", new NbtString(id) }, { "count", new NbtInt(1) } };

    /// <summary>
    /// Canonical equipment is the 1.21.5+ "equipment" compound (head, chest, legs, feet, mainhand, offhand, body, saddle).
    /// Older entities keep ArmorItems/HandItems, horse ArmorItem/SaddleItem, llama DecorItem, wolf body_armor_item and the
    /// pig/strider Saddle flag.
    /// </summary>
    static void NormalizeEquipment(NbtCompound c, int dv)
    {
        var eq = new NbtCompound();
        void Put(string slot, NbtTag? t)
        {
            if (t is NbtCompound it && it.Count > 0 && Items.FromJava(it, dv) is NbtCompound ci) eq[slot] = ci;
        }
        if (c.GetCompound("equipment") is NbtCompound existing)
            foreach (var (slot, t) in existing) Put(slot, t);
        if (c.GetList("ArmorItems") is NbtList armor)
            for (int i = 0; i < 4 && i < armor.Count; i++) Put(i switch { 0 => "feet", 1 => "legs", 2 => "chest", _ => "head" }, armor[i]);
        if (c.GetList("HandItems") is NbtList hands)
        {
            if (hands.Count > 0) Put("mainhand", hands[0]);
            if (hands.Count > 1) Put("offhand", hands[1]);
        }
        // Before 1.9 one list: what it holds, then feet to head (an armour stand's armour too).
        if (c.GetList("Equipment") is NbtList old)
            for (int i = 0; i < 5 && i < old.Count; i++) Put(OldEquipment[i], old[i]);
        Put("body", c["body_armor_item"]);
        Put("body", c["ArmorItem"]);
        Put("body", c["DecorItem"]);
        Put("saddle", c["SaddleItem"]);
        if (c.GetBool("Saddle")) eq["saddle"] = ItemStack("minecraft:saddle");
        foreach (var k in new[] { "equipment", "ArmorItems", "HandItems", "Equipment", "body_armor_item", "ArmorItem", "DecorItem", "SaddleItem", "Saddle" }) c.Remove(k);
        if (eq.Count > 0) c["equipment"] = eq;
    }

    /// <summary>The slots of the pre-1.9 Equipment list (and its DropChances), in order.</summary>
    public static readonly string[] OldEquipment = ["mainhand", "feet", "legs", "chest", "head"];

    /// <summary>The first Java data version (1.9's first) with HandItems/ArmorItems instead of one Equipment list.</summary>
    public const int HandsAndArmor = 100;

    static NbtCompound JavaItem(NbtCompound canonical, int dv) => dv >= 1451 ? Items.ToJava(canonical, dv) : Items.ToLegacyJava(canonical, dv, dv < 100);

    /// <summary>The equipment layout of a Java version (see <see cref="NormalizeEquipment"/>).</summary>
    static void EquipmentToJava(NbtCompound c, string id, int dv)
    {
        if (c.GetCompound("equipment") is not NbtCompound eq) return;
        c.Remove("equipment");
        if (dv >= 4325)
        {
            var copy = new NbtCompound();
            foreach (var (slot, t) in eq) if (t is NbtCompound it) copy[slot] = Items.ToJava(it, dv);
            c["equipment"] = copy;
            return;
        }
        NbtCompound J(string slot) => eq.GetCompound(slot) is NbtCompound it ? JavaItem(it, dv) : new NbtCompound();
        // A console save gets both forms: its old chunks are read by every console since, the newest reading the lists.
        bool console = ConversionLog.Current?.Target.Kind == TargetKind.LegacyConsole;
        if (dv < HandsAndArmor)
        {
            // Before 1.9 one list, what it holds then feet to head; nothing holds anything in an off-hand.
            if (OldEquipment.Any(eq.ContainsKey)) c["Equipment"] = new NbtList { J("mainhand"), J("feet"), J("legs"), J("chest"), J("head") };
            if (!console && eq.GetCompound("offhand") is NbtCompound off && !ConversionLog.IsEmptyItem(off))
            {
                bool rescued = ConversionLog.Rescue("Items mobs were holding", [off]);
                ConversionLog.Note(LossKind.Entity, ConversionLog.Friendly(id) + " off-hand item",
                    rescued ? "moved to the rescue chests: before 1.9 nothing has an off-hand" : "lost: before 1.9 nothing has an off-hand", lost: !rescued);
            }
        }
        if (dv >= HandsAndArmor || console)
        {
            if (eq.ContainsKey("feet") || eq.ContainsKey("legs") || eq.ContainsKey("chest") || eq.ContainsKey("head"))
                c["ArmorItems"] = new NbtList { J("feet"), J("legs"), J("chest"), J("head") };
            if (eq.ContainsKey("mainhand") || eq.ContainsKey("offhand"))
                c["HandItems"] = new NbtList { J("mainhand"), J("offhand") };
        }
        if (eq.ContainsKey("body"))
        {
            if (id is "minecraft:llama" or "minecraft:trader_llama") c["DecorItem"] = J("body");
            else if (dv >= 3837) c["body_armor_item"] = J("body");
            else c["ArmorItem"] = J("body");
        }
        if (eq.ContainsKey("saddle"))
        {
            if (id is "minecraft:pig" or "minecraft:strider") c.SetByte("Saddle", 1);
            else c["SaddleItem"] = J("saddle");
        }
    }

    public static (double X, double Y, double Z)? Position(NbtCompound canonical) =>
        canonical.GetList("Pos") is { Count: 3 } p ? (p[0].AsDouble, p[1].AsDouble, p[2].AsDouble) : null;
}
