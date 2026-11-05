// MinecraftStudio.Core/Conversion/Entities.ToJava.cs: to java for Entities.
// Part of Entities (main part in Entities.cs).
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public static partial class Entities
{
    // ---------------------------------------------------------------- canonical -> Java target

    public static NbtCompound? ToJava(NbtCompound canonical, int dv) => ToJava(canonical, dv, null);

    /// <summary>
    /// The entity in the layout of data version <paramref name="dv"/>, or null when the version can't have it (reported).
    /// Riders who can't stay seated (on what the version lacks, or past the first before 1.9) get off into
    /// <paramref name="setDown"/>, to be written as entities of their own; without it they are reported lost.
    /// </summary>
    public static NbtCompound? ToJava(NbtCompound canonical, int dv, List<NbtCompound>? setDown)
    {
        string id = canonical.GetString("id") ?? "";
        var c = (NbtCompound)canonical.Clone();
        // Types the version lacks: the closest type it has, or reported (with what they carried rescued).
        int content = dv >= 1451 ? dv : 1343;
        if (id == "minecraft:glow_item_frame" && !JavaKnown(id, content))
        {
            ConversionLog.Note(LossKind.Entity, "glow item frame", $"became an item frame: {TargetName(dv)} has no glow item frames");
            id = "minecraft:item_frame";
        }
        if (id == "minecraft:cat" && !JavaKnown(id, content))
        {
            // Before 1.14 cats were tamed ocelots (CatType 1 tuxedo, 2 tabby, 3 siamese).
            bool tame = canonical["Owner"] is NbtIntArray || canonical.GetBool("Tame");
            int catType = Bare(canonical.GetString("variant") ?? "") switch
            {
                "black" or "all_black" => 1, "siamese" or "white" or "ragdoll" or "british_shorthair" => 3, _ => 2,
            };
            c.SetInt("CatType", tame ? catType : 0);
            c.Remove("variant");
            ConversionLog.Note(LossKind.Entity, "cat", $"became {(tame ? "a tamed" : "an")} ocelot: before Java 1.14 cats were ocelots");
            id = "minecraft:ocelot";
        }
        bool boat = id.EndsWith("_boat") || id.EndsWith("_raft") || id is "minecraft:boat" or "minecraft:chest_boat";
        if (!boat && !JavaKnown(id, content)) return Dropped(canonical, dv, setDown, $"{TargetName(dv)} has no {ConversionLog.Friendly(id)}");
        // Bedrock add-on mobs have no Java counterpart (Java mods are kept: the world may be played with them).
        if (!id.StartsWith("minecraft:", StringComparison.Ordinal) && ConversionLog.Current?.SourceIsBedrock == true)
            return Dropped(canonical, dv, setDown, "Java has no Bedrock add-on mobs");
        // A release before 1.13 has only the kinds it had then (the game drops the others without a word).
        if (OldRelease(dv) is double oldRelease && Added.TryGetValue(id, out double added) && oldRelease < added)
            return Dropped(canonical, dv, setDown, $"{TargetName(dv)} has no {ConversionLog.Friendly(id)}");
        if (dv < 4080 && boat && id is not ("minecraft:boat" or "minecraft:chest_boat"))
        {
            string wood = id.Replace("minecraft:", "").Replace("_chest", "").Replace("_boat", "").Replace("_raft", "");
            if (!WoodKnown(wood, dv))
            {
                ConversionLog.Note(LossKind.Entity, ConversionLog.Friendly(id), $"became an oak boat: {TargetName(dv)} has no {wood.Replace('_', ' ')} boats");
                wood = "oak";
            }
            c.SetString("Type", wood);
            id = id.Contains("chest") ? "minecraft:chest_boat" : "minecraft:boat";
        }
        if (id == "minecraft:chest_boat" && !JavaKnown(id, content))
        {
            var carried = CarriedItems(canonical);
            bool rescued = carried.Count == 0 || ConversionLog.Rescue("Items from a boat with a chest", carried);
            ConversionLog.Note(LossKind.Entity, "boat with a chest", $"became a boat: {TargetName(dv)} has no chest boats"
                + (carried.Count == 0 ? "" : rescued ? "; its items are in the rescue chests at the spawn" : "; its items were lost"), lost: !rescued);
            c.Remove("Items");
            id = "minecraft:boat";
        }
        string modern = id;
        // Before 1.14 a villager's job is numbers; before 1.11 some kinds are one kind with a type number.
        if (dv < VillagerDataSince) OldJob(c, id, dv, OldRelease(dv) ?? (dv >= 1451 ? 1.13 : ConsoleForms));
        if (dv < KindsSplit) id = OldKind(c, id, dv, OldRelease(dv) ?? ConsoleForms);
        if (dv < 1451) id = LegacyJavaId(id, dv >= 819, dv);
        else foreach (var (oldName, newName, since) in Renames.Reverse()) if (id == newName && dv < since) id = oldName;
        // Paintings, frames and leash knots in the version's own fields (art names, facing, block).
        if (Hanging.ToJava(c, canonical, dv, ConversionLog.Current?.Target is { Kind: TargetKind.Java } t ? t.LegacyLevel : LegacyLevel.Release_1_12) is string cannot)
            return Dropped(canonical, dv, setDown, cannot, itselfAsItem: modern is "minecraft:item_frame" or "minecraft:painting");
        c.SetString("id", id);
        if (canonical.GetCompound("Item") is NbtCompound item) c["Item"] = dv >= 1451 ? Items.ToJava(item, dv) : Items.ToLegacyJava(item, dv, dv < 100);
        if (canonical.GetList("Items") is NbtList items)
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var it in items.OfType<NbtCompound>())
            {
                var j = dv >= 1451 ? Items.ToJava(it, dv) : Items.ToLegacyJava(it, dv, dv < 100);
                j.SetByte("Slot", (sbyte)it.GetInt("Slot"));
                list.Add(j);
            }
            c["Items"] = list;
        }
        if (canonical.GetString("CustomName") is string cn)
            c["CustomName"] = dv >= 1451 ? TextComponents.ToJavaTag(cn, dv) : TextComponents.ToPlainTag(cn);
        EquipmentToJava(c, canonical.GetString("id") ?? "", dv);
        if (canonical.GetCompound("Offers")?.GetList("Recipes") is NbtList offers)
        {
            var recipes = new NbtList(NbtType.Compound);
            foreach (var r in offers.OfType<NbtCompound>())
            {
                var copy = (NbtCompound)r.Clone();
                foreach (var key in new[] { "buy", "buyB", "sell" })
                    if (r.GetCompound(key) is NbtCompound it) copy[key] = JavaItem(it, dv);
                recipes.Add(copy);
            }
            c["Offers"] = new NbtCompound { { "Recipes", recipes } };
        }
        if (canonical.GetList("Inventory") is NbtList inventory)
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var it in inventory.OfType<NbtCompound>())
            {
                var j = JavaItem(it, dv);
                if (it.ContainsKey("Slot")) j.SetByte("Slot", (sbyte)it.GetInt("Slot"));
                list.Add(j);
            }
            c["Inventory"] = list;
        }
        if (canonical.GetCompound("item") is NbtCompound projectile && modern is "minecraft:trident" or "minecraft:arrow" or "minecraft:spectral_arrow")
        {
            c.Remove("item");
            if (dv >= 3819) c["item"] = JavaItem(projectile, dv);
            else if (modern == "minecraft:trident") c["Trident"] = JavaItem(projectile, dv);
        }
        if (canonical.GetList("Passengers") is NbtList riders)
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var r in riders.OfType<NbtCompound>()) if (ToJava(r, dv, setDown) is NbtCompound jr) list.Add(jr);
            c["Passengers"] = list;
        }
        if (canonical["Owner"] is NbtIntArray { Value.Length: 4 } owner && dv < 2515)
        {
            c.SetString("OwnerUUID", Players.UuidFromInts(owner.Value).ToString());
            c.Remove("Owner");
        }
        if (id == "minecraft:cat" && dv < 3105 && canonical.GetString("variant") is string catVariant)
        {
            c.SetInt("CatType", Math.Max(0, Array.IndexOf(JavaCatTypes, Bare(catVariant))));
            c.Remove("variant");
        }
        // Consoles name an entity "ent" and 32 hex digits; Java keeps the same 128 bits as numbers (consoles get theirs back).
        if (canonical.GetString("UUID") is string named && ConsoleUuid(named) is int[] parsed && ConversionLog.Current?.Target.Kind != TargetKind.LegacyConsole)
        {
            c.Remove("UUID");
            if (dv >= 2515) c["UUID"] = new NbtIntArray(parsed);
            else
            {
                c.SetLong("UUIDMost", ((long)parsed[0] << 32) | (uint)parsed[1]);
                c.SetLong("UUIDLeast", ((long)parsed[2] << 32) | (uint)parsed[3]);
            }
        }
        else if (canonical["UUID"] is NbtIntArray { Value.Length: 4 } uuid && dv < 2515)
        {
            var u = uuid.Value;
            c.SetLong("UUIDMost", ((long)u[0] << 32) | (uint)u[1]);
            c.SetLong("UUIDLeast", ((long)u[2] << 32) | (uint)u[3]);
            c.Remove("UUID");
        }
        else if (!c.ContainsKey("UUID") && dv >= 2515) c["UUID"] = RandomUuid();
        if (dv < 1451 && c["Health"] is NbtFloat hf && dv < 169) c.SetShort("Health", (short)Math.Round(hf.Value));
        // Potion effects as the version keeps them (numbered before 1.20.2); an enderman's block in the version's form.
        Effects.ToJava(c, dv, ConversionLog.Friendly(modern));
        if (modern == "minecraft:enderman") CarriedToJava(c, canonical, dv);
        // Drop chances, fall distance, beds, phantom/turtle/vex fields: the old names before 1.21.5.
        EntityFields.ToJava(c, dv);
        // Before 1.9 the rider keeps what it rides: the stack turned over, its first rider outermost.
        return dv < Players.MountsKept ? Turned(c, dv, setDown) : c;
    }


    /// <summary>Java data version each entity type first appeared in (types older than 1.13 are not listed).</summary>
    static readonly Dictionary<string, int> Since = new(StringComparer.Ordinal)
    {
        ["minecraft:turtle"] = 1519, ["minecraft:phantom"] = 1519, ["minecraft:cod"] = 1519, ["minecraft:salmon"] = 1519, ["minecraft:pufferfish"] = 1519,
        ["minecraft:tropical_fish"] = 1519, ["minecraft:drowned"] = 1519, ["minecraft:dolphin"] = 1519, ["minecraft:trident"] = 1519,
        ["minecraft:cat"] = 1952, ["minecraft:fox"] = 1952, ["minecraft:panda"] = 1952, ["minecraft:pillager"] = 1952, ["minecraft:ravager"] = 1952,
        ["minecraft:wandering_trader"] = 1952, ["minecraft:trader_llama"] = 1952, ["minecraft:bee"] = 2225,
        ["minecraft:hoglin"] = 2566, ["minecraft:piglin"] = 2566, ["minecraft:strider"] = 2566, ["minecraft:zoglin"] = 2566, ["minecraft:piglin_brute"] = 2578,
        ["minecraft:axolotl"] = 2724, ["minecraft:glow_squid"] = 2724, ["minecraft:goat"] = 2724, ["minecraft:glow_item_frame"] = 2724, ["minecraft:marker"] = 2724,
        ["minecraft:allay"] = 3105, ["minecraft:frog"] = 3105, ["minecraft:tadpole"] = 3105, ["minecraft:warden"] = 3105, ["minecraft:chest_boat"] = 3105,
        ["minecraft:block_display"] = 3337, ["minecraft:item_display"] = 3337, ["minecraft:text_display"] = 3337, ["minecraft:interaction"] = 3337,
        ["minecraft:camel"] = 3463, ["minecraft:sniffer"] = 3463, ["minecraft:armadillo"] = 3837,
        ["minecraft:breeze"] = 3953, ["minecraft:bogged"] = 3953, ["minecraft:wind_charge"] = 3953, ["minecraft:breeze_wind_charge"] = 3953,
        ["minecraft:ominous_item_spawner"] = 3953, ["minecraft:creaking"] = 4189, ["minecraft:happy_ghast"] = 4435,
        ["minecraft:copper_golem"] = 4554, ["minecraft:mannequin"] = 4554,
        ["minecraft:nautilus"] = 4671, ["minecraft:zombie_nautilus"] = 4671, ["minecraft:camel_husk"] = 4671, ["minecraft:parched"] = 4671,
    };

    /// <summary>Whether an entity type exists in a Java data version (or a Bedrock target's Java content version).</summary>
    public static bool JavaKnown(string id, int dv) => !Since.TryGetValue(id, out int since) || dv >= since;

    /// <summary>Boat woods by the data version that added them.</summary>
    static bool WoodKnown(string wood, int dv) => wood switch
    {
        "mangrove" => dv >= 3105,
        "cherry" or "bamboo" => dv >= 3463,
        "pale_oak" => dv >= 4189,
        _ => true,
    };

    static string TargetName(int dv) => ConversionLog.Current?.Target.Name ?? $"data version {dv}";

    /// <summary>Items an entity holds (frame item, chest and inventory contents, equipment, saddle, decor).</summary>
    public static List<NbtCompound> CarriedItems(NbtCompound e)
    {
        var items = new List<NbtCompound>();
        if (e.GetCompound("Item") is NbtCompound item) items.Add(item);
        foreach (var key in new[] { "Items", "Inventory", "ArmorItems", "HandItems" })
            items.AddRange(e.GetList(key)?.OfType<NbtCompound>() ?? []);
        foreach (var (_, v) in e.GetCompound("equipment") ?? []) if (v is NbtCompound it) items.Add(it);
        foreach (var key in new[] { "SaddleItem", "DecorItem", "ArmorItem", "body_armor_item" })
            if (e.GetCompound(key) is NbtCompound it) items.Add(it);
        return items.Where(i => !ConversionLog.IsEmptyItem(i)).ToList();
    }

    /// <summary>
    /// An entity the target cannot hold: what it carried (and, for frames, the frame) goes to the rescue chests at the
    /// spawn, and the loss is reported with its position. Riders still on it go with it: counted, what they carry rescued
    /// too (a writer that can set them down takes them off first). Returns null.
    /// </summary>
    public static NbtCompound? Drop(NbtCompound canonical, string why, bool itselfAsItem = false)
    {
        string id = canonical.GetString("id") ?? "?";
        var items = CarriedItems(canonical);
        if (itselfAsItem) items.Add(new NbtCompound { { "id", new NbtString(id) }, { "count", new NbtInt(1) } });
        var riders = AllRiders(canonical).ToList();
        foreach (var r in riders) items.AddRange(CarriedItems(r));
        var (x, y, z) = Position(canonical) ?? (0, 0, 0);
        string name = ConversionLog.Friendly(id);
        if (canonical.GetString("CustomName") is string cn && TextComponents.ToPlain(cn) is { Length: > 0 } plain) name += $" named \"{plain}\"";
        if (riders.Count > 0) name += $" with {riders.Count} rider{(riders.Count == 1 ? "" : "s")} on it";
        bool rescued = items.Count > 0 && ConversionLog.Rescue($"Items from a {ConversionLog.Friendly(id)}", items);
        if (canonical.GetString("LootTable") != null) why += " (unopened loot)";
        string whose = riders.Count > 0 ? "they" : "it";
        ConversionLog.Note(LossKind.Entity, name,
            why + (items.Count == 0 ? "" : rescued ? $"; what {whose} carried is in the rescue chests at the spawn" : $"; what {whose} carried was lost"),
            1 + riders.Count, lost: true, where: $"{x:0}, {y:0}, {z:0}");
        return null;
    }

    /// <summary>A console entity name ("ent" and 32 hex digits) as the four ints of its 128 bits, or null.</summary>
    static int[]? ConsoleUuid(string text)
    {
        if (text.Length != 35 || !text.StartsWith("ent", StringComparison.Ordinal)) return null;
        var ints = new int[4];
        for (int i = 0; i < 4; i++)
            if (!uint.TryParse(text.AsSpan(3 + i * 8, 8), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out uint part)) return null;
            else ints[i] = (int)part;
        return ints;
    }

    public static NbtIntArray RandomUuid()
    {
        var g = Guid.NewGuid().ToByteArray();
        return new NbtIntArray([BitConverter.ToInt32(g, 0), BitConverter.ToInt32(g, 4), BitConverter.ToInt32(g, 8), BitConverter.ToInt32(g, 12)]);
    }
}
