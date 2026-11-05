// MinecraftStudio.Core/Conversion/Entities.FromBedrock.cs: from bedrock for Entities.
// Part of Entities (main part in Entities.cs).
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public static partial class Entities
{
    // ---------------------------------------------------------------- Bedrock -> canonical

    public static NbtCompound? FromBedrock(NbtCompound e)
    {
        string? identifier = e.GetString("identifier");
        if (identifier == null && e["id"] is { IsNumeric: true } nid) identifier = BedrockNumericEntity((int)(nid.AsLong & 0xFF));
        if (identifier == null) return null;
        string id = JavaIdFromBedrock(identifier);
        if (BedrockOnly.Contains(id)) return null;
        var c = new NbtCompound();
        if (id is "minecraft:boat" or "minecraft:chest_boat")
        {
            int v = e.GetInt("Variant");
            string wood = BoatWoods[Math.Clamp(v, 0, BoatWoods.Length - 1)];
            id = $"minecraft:{wood}{(id.Contains("chest") ? "_chest" : "")}_{(wood == "bamboo" ? "raft" : "boat")}";
        }
        c.SetString("id", id);
        var pos = Vec(e, "Pos", 3);
        // Bedrock stores the eye-ish position for some mobs; the feet position is close enough for conversion.
        c["Pos"] = Doubles(pos[0], pos[1], pos[2]);
        var motion = Vec(e, "Motion", 3);
        c["Motion"] = Doubles(motion[0], motion[1], motion[2]);
        var rot = Vec(e, "Rotation", 2);
        c["Rotation"] = Floats((float)rot[0], (float)rot[1]);
        c.SetByte("OnGround", (sbyte)(e.GetBool("OnGround") ? 1 : 0));
        c.SetDouble("fall_distance", e.GetDouble("FallDistance"));
        foreach (var a in e.GetList("Attributes")?.OfType<NbtCompound>() ?? [])
            if (a.GetString("Name") == "minecraft:health") c.SetFloat("Health", (float)a.GetDouble("Current", 20));
        if (e.GetString("CustomName") is string cn && cn.Length > 0)
        {
            c.SetString("CustomName", TextComponents.FromPlain(cn));
            c.SetByte("CustomNameVisible", (sbyte)(e.GetBool("CustomNameVisible") ? 1 : 0));
        }
        if (e.GetBool("IsBaby")) { c.SetInt("Age", -24000); c.SetByte("IsBaby", 1); }
        if (e.GetBool("Persistent")) c.SetByte("PersistenceRequired", 1);
        if (e.GetBool("Sheared")) c.SetByte("Sheared", 1);
        if (id == "minecraft:sheep") c.SetByte("Color", (sbyte)(e.GetInt("Color") & 15));
        // Tamed animals: owner (as the UUID the converted player gets), sitting, collar.
        if (e.GetBool("IsTamed"))
        {
            c.SetByte("Tame", 1);
            if (e.GetLongOrNull("OwnerNew") is long owner && owner != -1) c["Owner"] = new NbtIntArray(Players.UuidToInts(UuidForBedrockId(owner)));
        }
        if (e.GetBool("Sitting")) c.SetByte("Sitting", 1);
        if (id is "minecraft:wolf" or "minecraft:cat" && e["Color"] is { IsNumeric: true } collar) c.SetByte("CollarColor", (sbyte)(collar.AsLong & 15));
        int variant = e.GetInt("Variant"), mark = e.GetInt("MarkVariant");
        switch (id)
        {
            case "minecraft:cat": c.SetString("variant", "minecraft:" + BedrockCats[Math.Clamp(variant, 0, BedrockCats.Length - 1)]); break;
            // The block an enderman carries, as a Bedrock block ({name, states}): canonical under its Java name.
            case "minecraft:enderman" when e.GetCompound("carriedBlock") is NbtCompound carried && carried.GetString("name") is string carriedName && carriedName != "minecraft:air"
                && CanonicalReader.BedrockToJava(BedrockBlocks.FromPaletteNbt(carried)) is { IsAir: false } held:
                c["carriedBlockState"] = held.ToJavaNbt();
                break;
            case "minecraft:horse": c.SetInt("Variant", (variant & 255) | (mark << 8)); break;
            case "minecraft:llama" or "minecraft:trader_llama" or "minecraft:parrot" or "minecraft:axolotl": c.SetInt("Variant", variant); break;
            case "minecraft:rabbit": c.SetInt("RabbitType", variant); break;
            case "minecraft:fox": c.SetString("Type", variant == 1 ? "snow" : "red"); break;
            case "minecraft:mooshroom": c.SetString("Type", variant == 1 ? "brown" : "red"); break;
            case "minecraft:frog": c.SetString("variant", "minecraft:" + Frogs[Math.Clamp(variant, 0, Frogs.Length - 1)]); break;
            case "minecraft:villager" or "minecraft:zombie_villager":
                c["VillagerData"] = new NbtCompound
                {
                    { "profession", new NbtString("minecraft:" + Professions[Math.Clamp(variant, 0, Professions.Length - 1)]) },
                    { "level", new NbtInt(Math.Clamp(e.GetInt("TradeTier") + 1, 1, 5)) },
                    { "type", new NbtString("minecraft:" + VillagerTypes[Math.Clamp(mark, 0, VillagerTypes.Length - 1)]) },
                };
                if (e.GetLongOrNull("TradeExperience") is long xp) c.SetInt("Xp", (int)xp);
                if (e.GetCompound("Offers")?.GetList("Recipes") is NbtList recipes) c["Offers"] = new NbtCompound { { "Recipes", OffersFromBedrock(recipes) } };
                break;
        }
        if (e.GetBool("Chested")) c.SetByte("ChestedHorse", 1);
        if (e["Strength"] is { IsNumeric: true } strength) c.SetInt("Strength", (int)strength.AsLong);
        var equipment = new NbtCompound();
        bool horseLike = id is "minecraft:horse" or "minecraft:llama" or "minecraft:trader_llama" or "minecraft:wolf" or "minecraft:donkey" or "minecraft:mule" or "minecraft:zombie_horse" or "minecraft:skeleton_horse";
        if (e.GetList("Armor") is NbtList bedrockArmor)
            for (int i = 0; i < 4 && i < bedrockArmor.Count; i++)
                if (bedrockArmor[i] is NbtCompound it && Items.FromBedrock(it) is NbtCompound piece)
                    equipment[horseLike ? "body" : i switch { 0 => "head", 1 => "chest", 2 => "legs", _ => "feet" }] = piece;
        if (e.GetList("Mainhand")?.FirstOrDefault() is NbtCompound mh && Items.FromBedrock(mh) is NbtCompound main) equipment["mainhand"] = main;
        if (e.GetList("Offhand")?.FirstOrDefault() is NbtCompound oh && Items.FromBedrock(oh) is NbtCompound off) equipment["offhand"] = off;
        if (e.GetBool("Saddled")) equipment["saddle"] = ItemStack("minecraft:saddle");
        if (equipment.Count > 0) c["equipment"] = equipment;
        if (id == "minecraft:armor_stand" && e.GetCompound("Pose")?.GetInt("PoseIndex") is int poseIndex && poseIndex != 0)
            c["Pose"] = ArmorStandPose(poseIndex);
        if (e.GetCompound("Item") is NbtCompound item && Items.FromBedrock(item) is NbtCompound ci) c["Item"] = ci;
        if (id == "minecraft:item" && !c.ContainsKey("Item")) return null;
        if (e.GetList("ChestItems") is NbtList chest)
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var it in chest.OfType<NbtCompound>())
                if (Items.FromBedrock(it) is NbtCompound x) { x.SetByte("Slot", (sbyte)it.GetInt("Slot")); list.Add(x); }
            c["Items"] = list;
        }
        // Paintings (art, facing, the block they hang in) and leash knots.
        Hanging.FromBedrock(c, e);
        if (id == "minecraft:falling_block" && e.GetCompound("FallingBlock") is NbtCompound fb)
            c["BlockState"] = CanonicalReader.BedrockToJava(BedrockBlocks.FromPaletteNbt(fb)).ToJavaNbt();
        return c;
    }

    static NbtCompound EmptyBedrockItem() => new() { { "Count", new NbtByte(0) }, { "Damage", new NbtShort(0) }, { "Name", new NbtString("") }, { "WasPickedUp", new NbtByte(0) } };

    /// <summary>Java trades (buy, buyB, sell, uses, xp …) → Bedrock (buyA/buyCountA, traderExp …).</summary>
    static NbtList OffersToBedrock(NbtList recipes)
    {
        var list = new NbtList(NbtType.Compound);
        foreach (var r in recipes.OfType<NbtCompound>())
        {
            var b = new NbtCompound();
            if (r.GetCompound("buy") is NbtCompound buy) { b["buyA"] = Items.ToBedrock(buy); b.SetInt("buyCountA", Items.Count(buy)); }
            if (r.GetCompound("buyB") is NbtCompound buyB && Items.Id(buyB) != "minecraft:air") { b["buyB"] = Items.ToBedrock(buyB); b.SetInt("buyCountB", Items.Count(buyB)); }
            if (r.GetCompound("sell") is NbtCompound sell) b["sell"] = Items.ToBedrock(sell);
            b.SetInt("maxUses", r.GetInt("maxUses", 12));
            b.SetInt("uses", r.GetInt("uses"));
            b.SetInt("traderExp", r.GetInt("xp", 1));
            b.SetByte("rewardExp", (sbyte)(r.GetBool("rewardExp", true) ? 1 : 0));
            b.SetFloat("priceMultiplierA", (float)r.GetDouble("priceMultiplier", 0.05));
            b.SetFloat("priceMultiplierB", 0);
            b.SetInt("demand", r.GetInt("demand"));
            b.SetInt("tier", 0);
            list.Add(b);
        }
        return list;
    }

    static NbtList OffersFromBedrock(NbtList recipes)
    {
        var list = new NbtList(NbtType.Compound);
        foreach (var r in recipes.OfType<NbtCompound>())
        {
            var j = new NbtCompound();
            NbtCompound? Stack(string key, string countKey)
            {
                if (r.GetCompound(key) is not NbtCompound it || Items.FromBedrock(it) is not NbtCompound ci) return null;
                if (r.GetLongOrNull(countKey) is long n && n > 0) ci.SetInt("count", (int)n);
                return ci;
            }
            if (Stack("buyA", "buyCountA") is NbtCompound buy) j["buy"] = buy;
            if (Stack("buyB", "buyCountB") is NbtCompound buyB) j["buyB"] = buyB;
            if (Stack("sell", "") is NbtCompound sell) j["sell"] = sell;
            if (!j.ContainsKey("buy") || !j.ContainsKey("sell")) continue;
            j.SetInt("maxUses", r.GetInt("maxUses", 12));
            j.SetInt("uses", r.GetInt("uses"));
            j.SetInt("xp", r.GetInt("traderExp", 1));
            j.SetByte("rewardExp", (sbyte)(r.GetBool("rewardExp", true) ? 1 : 0));
            j.SetFloat("priceMultiplier", (float)r.GetDouble("priceMultiplierA", 0.05));
            j.SetInt("demand", r.GetInt("demand"));
            j.SetInt("specialPrice", 0);
            list.Add(j);
        }
        return list;
    }

    /// <summary>Bedrock armour stands have 13 preset poses; Java stores limb rotations. Only the default pose maps exactly.</summary>
    static int ArmorStandPoseIndex(NbtCompound? javaPose) => javaPose is { Count: > 0 } ? 1 : 0;

    static NbtCompound ArmorStandPose(int index)
    {
        static NbtList R(float x, float y, float z) => new(NbtType.Float) { new NbtFloat(x), new NbtFloat(y), new NbtFloat(z) };
        // "No pose" (1) and the presets: arms raised a little so it reads as a pose in Java.
        return index == 1
            ? new NbtCompound { { "LeftArm", R(0, 0, 0) }, { "RightArm", R(0, 0, 0) } }
            : new NbtCompound { { "LeftArm", R(-10, 0, -10) }, { "RightArm", R(-15, 0, 10) }, { "Head", R(0, 0, 0) } };
    }

    static string? BedrockNumericEntity(int id) => id switch
    {
        10 => "minecraft:chicken", 11 => "minecraft:cow", 12 => "minecraft:pig", 13 => "minecraft:sheep", 14 => "minecraft:wolf", 15 => "minecraft:villager",
        16 => "minecraft:mooshroom", 17 => "minecraft:squid", 18 => "minecraft:rabbit", 19 => "minecraft:bat", 20 => "minecraft:iron_golem",
        21 => "minecraft:snow_golem", 22 => "minecraft:ocelot", 23 => "minecraft:horse", 32 => "minecraft:zombie", 33 => "minecraft:creeper",
        34 => "minecraft:skeleton", 35 => "minecraft:spider", 36 => "minecraft:zombie_pigman", 37 => "minecraft:slime", 38 => "minecraft:enderman",
        39 => "minecraft:silverfish", 40 => "minecraft:cave_spider", 41 => "minecraft:ghast", 42 => "minecraft:magma_cube", 43 => "minecraft:blaze",
        44 => "minecraft:zombie_villager", 45 => "minecraft:witch", 64 => "minecraft:item", 65 => "minecraft:tnt", 66 => "minecraft:falling_block",
        83 => "minecraft:painting", 84 => "minecraft:minecart", 90 => "minecraft:boat",
        _ => null,
    };
}
