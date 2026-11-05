// MinecraftStudio.Core/Conversion/Entities.ToBedrock.cs: to bedrock for Entities.
// Part of Entities (main part in Entities.cs).
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public static partial class Entities
{
    // ---------------------------------------------------------------- canonical -> Bedrock

    /// <summary>
    /// Bedrock actor; <paramref name="uniqueId"/> must be unique in the world. <paramref name="owners"/> maps an owner's
    /// UUID to the Bedrock id of that player (the converted host player keeps their pets).
    /// </summary>
    public static NbtCompound? ToBedrock(NbtCompound canonical, long uniqueId, Func<Guid, long>? owners = null)
    {
        string id = canonical.GetString("id") ?? "";
        // Frames are blocks on Bedrock (the writer places them); other Java-only types are reported.
        if (JavaOnly.Contains(id))
            return id is "minecraft:item_frame" or "minecraft:glow_item_frame" or "minecraft:player" ? null : Drop(canonical, $"Bedrock has no {ConversionLog.Friendly(id)}");
        string identifier = BedrockIdentifier(id);
        var c = new NbtCompound();
        c.SetString("identifier", identifier);
        var defs = new NbtList(NbtType.String) { new NbtString("+" + identifier) };
        bool baby = canonical.GetInt("Age") < 0 || canonical.GetBool("IsBaby");
        if (identifier is "minecraft:cow" or "minecraft:pig" or "minecraft:sheep" or "minecraft:chicken" or "minecraft:horse" or "minecraft:wolf" or "minecraft:cat"
            or "minecraft:villager_v2" or "minecraft:rabbit" or "minecraft:mooshroom" or "minecraft:fox" or "minecraft:llama" or "minecraft:donkey" or "minecraft:mule")
            defs.Add(new NbtString($"+{identifier}_{(baby ? "baby" : "adult")}"));
        c["definitions"] = defs;
        var pos = Vec(canonical, "Pos", 3);
        c["Pos"] = Floats((float)pos[0], (float)pos[1], (float)pos[2]);
        var motion = Vec(canonical, "Motion", 3);
        c["Motion"] = Floats((float)motion[0], (float)motion[1], (float)motion[2]);
        var rot = Vec(canonical, "Rotation", 2);
        c["Rotation"] = Floats((float)rot[0], (float)rot[1]);
        c.SetLong("UniqueID", uniqueId);
        c.SetByte("OnGround", (sbyte)(canonical.GetBool("OnGround") ? 1 : 0));
        c.SetFloat("FallDistance", (float)canonical.GetDouble("fall_distance", canonical.GetDouble("FallDistance")));
        c.SetByte("Persistent", (sbyte)(canonical.GetBool("PersistenceRequired") ? 1 : 0));
        if (canonical.GetString("CustomName") is string cn)
        {
            c.SetString("CustomName", TextComponents.ToPlain(cn));
            c.SetByte("CustomNameVisible", (sbyte)(canonical.GetBool("CustomNameVisible") ? 1 : 0));
        }
        if (baby) c.SetByte("IsBaby", 1);
        if (canonical["Health"] is { IsNumeric: true } h)
            c["Attributes"] = new NbtList { new NbtCompound { { "Name", new NbtString("minecraft:health") }, { "Base", new NbtFloat((float)h.AsDouble) }, { "Current", new NbtFloat((float)h.AsDouble) }, { "Max", new NbtFloat(Math.Max(20f, (float)h.AsDouble)) }, { "Min", new NbtFloat(0) } } };
        if (id == "minecraft:sheep") { c.SetByte("Color", (sbyte)canonical.GetInt("Color")); c.SetByte("Sheared", (sbyte)(canonical.GetBool("Sheared") ? 1 : 0)); }
        if (canonical.GetBool("Tame") || canonical["Owner"] is NbtIntArray) c.SetByte("IsTamed", 1);
        if (canonical["Owner"] is NbtIntArray { Value.Length: 4 } owner)
        {
            var g = Players.UuidFromInts(owner.Value);
            c.SetLong("OwnerNew", owners?.Invoke(g) ?? BedrockIdForUuid(g));
        }
        if (canonical.GetBool("Sitting")) c.SetByte("Sitting", 1);
        if (canonical["CollarColor"] is { IsNumeric: true } collar) c.SetByte("Color", (sbyte)collar.AsLong);
        switch (id)
        {
            case "minecraft:cat": c.SetInt("Variant", Math.Max(0, Array.IndexOf(BedrockCats, Bare(canonical.GetString("variant") ?? "tabby")))); break;
            // The block an enderman carries, as a Bedrock block.
            case "minecraft:enderman" when canonical.GetCompound("carriedBlockState") is NbtCompound carried
                && BedrockWorldWriter.ToBedrock(World.BlockState.FromJavaNbt(carried)).Block is { IsAir: false } held:
                c["carriedBlock"] = BedrockBlocks.ToPaletteNbt(held);
                break;
            case "minecraft:horse": c.SetInt("Variant", canonical.GetInt("Variant") & 255); c.SetInt("MarkVariant", (canonical.GetInt("Variant") >> 8) & 255); break;
            case "minecraft:llama" or "minecraft:trader_llama" or "minecraft:parrot" or "minecraft:axolotl": c.SetInt("Variant", canonical.GetInt("Variant")); break;
            case "minecraft:rabbit": c.SetInt("Variant", canonical.GetInt("RabbitType")); break;
            case "minecraft:fox": c.SetInt("Variant", canonical.GetString("Type") == "snow" ? 1 : 0); break;
            case "minecraft:mooshroom": c.SetInt("Variant", canonical.GetString("Type") == "brown" ? 1 : 0); break;
            case "minecraft:frog": c.SetInt("Variant", Math.Max(0, Array.IndexOf(Frogs, Bare(canonical.GetString("variant") ?? "temperate")))); break;
            case "minecraft:villager" or "minecraft:zombie_villager" when canonical.GetCompound("VillagerData") is NbtCompound vd:
                {
                    string profession = Bare(vd.GetString("profession") ?? "none");
                    int pi = Math.Max(0, Array.IndexOf(Professions, profession));
                    int ti = Math.Max(0, Array.IndexOf(VillagerTypes, Bare(vd.GetString("type") ?? "plains")));
                    c.SetInt("Variant", pi);
                    c.SetInt("MarkVariant", ti);
                    c.SetString("PreferredProfession", pi == 0 ? "" : profession);
                    c.SetInt("TradeTier", Math.Clamp(vd.GetInt("level", 1) - 1, 0, 4));
                    c.SetInt("TradeExperience", canonical.GetInt("Xp"));
                    // The behaviour groups Bedrock gives a villager of this profession and biome.
                    defs.Add(new NbtString("+" + (pi == 0 ? "unskilled" : profession)));
                    defs.Add(new NbtString($"+{VillagerTypes[ti]}_villager"));
                    defs.Add(new NbtString(profession == "nitwit" ? "+behavior_non_peasant" : "+behavior_peasant"));
                    defs.Add(new NbtString("+basic_schedule"));
                    if (canonical.GetCompound("Offers")?.GetList("Recipes") is NbtList recipes)
                        c["Offers"] = new NbtCompound
                        {
                            { "Recipes", OffersToBedrock(recipes) },
                            { "TierExpRequirements", new NbtList { new NbtCompound { { "0", new NbtInt(0) } }, new NbtCompound { { "1", new NbtInt(10) } }, new NbtCompound { { "2", new NbtInt(70) } }, new NbtCompound { { "3", new NbtInt(150) } }, new NbtCompound { { "4", new NbtInt(250) } } } },
                        };
                    break;
                }
        }
        if (canonical.GetBool("ChestedHorse")) c.SetByte("Chested", 1);
        if (canonical["Strength"] is { IsNumeric: true } strength) c.SetInt("Strength", (int)strength.AsLong);
        if (canonical.GetCompound("equipment") is NbtCompound eq)
        {
            NbtCompound B(string slot) => eq.GetCompound(slot) is NbtCompound it ? Items.ToBedrock(it) : EmptyBedrockItem();
            bool horseLike = id is "minecraft:horse" or "minecraft:llama" or "minecraft:trader_llama" or "minecraft:wolf" or "minecraft:donkey" or "minecraft:mule" or "minecraft:zombie_horse" or "minecraft:skeleton_horse";
            if (horseLike && eq.ContainsKey("body"))
                c["Armor"] = new NbtList { EmptyBedrockItem(), B("body"), EmptyBedrockItem(), EmptyBedrockItem() };
            else if (eq.ContainsKey("head") || eq.ContainsKey("chest") || eq.ContainsKey("legs") || eq.ContainsKey("feet"))
                c["Armor"] = new NbtList { B("head"), B("chest"), B("legs"), B("feet") };
            if (eq.ContainsKey("mainhand")) c["Mainhand"] = new NbtList { B("mainhand") };
            if (eq.ContainsKey("offhand")) c["Offhand"] = new NbtList { B("offhand") };
            if (eq.ContainsKey("saddle")) c.SetByte("Saddled", 1);
        }
        if (id == "minecraft:armor_stand")
            c["Pose"] = new NbtCompound { { "PoseIndex", new NbtInt(ArmorStandPoseIndex(canonical.GetCompound("Pose"))) }, { "LastSignal", new NbtInt(0) } };
        if (id.EndsWith("_boat") || id.EndsWith("_raft"))
        {
            string wood = id.Replace("minecraft:", "").Replace("_chest", "").Replace("_boat", "").Replace("_raft", "");
            c.SetInt("Variant", Math.Max(0, Array.IndexOf(BoatWoods, wood)));
        }
        if (canonical.GetCompound("Item") is NbtCompound item) c["Item"] = Items.ToBedrock(item);
        if (id == "minecraft:item" && !c.ContainsKey("Item")) return null;
        if (canonical.GetList("Items") is NbtList items)
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var it in items.OfType<NbtCompound>()) { var b = Items.ToBedrock(it); b.SetByte("Slot", (sbyte)it.GetInt("Slot")); list.Add(b); }
            c["ChestItems"] = list;
        }
        if (id == "minecraft:painting")
        {
            // Bedrock's own art names (old ones CamelCase, 1.21's lower case), under the field name of the version.
            int[] version = ConversionLog.Current?.Target is { Kind: TargetKind.Bedrock } t ? t.BedrockVersion : [];
            if (Hanging.MissingInBedrock(canonical, version) is string cannot) return Drop(canonical, cannot, itselfAsItem: true);
            Hanging.ToBedrock(c, canonical, version);
        }
        return c;
    }
}
