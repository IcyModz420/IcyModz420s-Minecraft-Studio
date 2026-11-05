// MinecraftStudio.Core/Conversion/BlockEntities.ToJava.cs: to java for BlockEntities.
// Part of BlockEntities (main part in BlockEntities.cs).
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

public static partial class BlockEntities
{
    // ---------------------------------------------------------------- canonical -> Java target

    /// <summary>Block entity for a Java target; <paramref name="block"/> is the (target) block at its position.</summary>
    public static NbtCompound? ToJava(NbtCompound canonical, int dv, BlockState block)
    {
        string id = canonical.GetString("id") ?? "";
        // Block entities whose block was replaced by a stand-in follow the stand-in.
        string original = id;
        id = AdaptToBlock(id, block);
        if (id.Length == 0) return CarriedItems(canonical).Count > 0 ? Drop(canonical, $"the block became {block.BaseName.Replace('_', ' ')} in {Version(dv)}") : null;
        if (dv >= 1451 && id is "minecraft:note_block" or "minecraft:flower_pot") return null;
        var c = new NbtCompound();
        if (dv < 1451) return ToLegacyJava(canonical, id, dv, block);
        if (!KnownIn(id, dv)) return Drop(canonical, $"{Version(dv)} has no {ConversionLog.Friendly(original)}");
        c.SetString("id", id);
        c.SetInt("x", canonical.GetInt("x")); c.SetInt("y", canonical.GetInt("y")); c.SetInt("z", canonical.GetInt("z"));
        foreach (var (k, v) in canonical)
        {
            if (k is "id" or "x" or "y" or "z") continue;
            c[k] = v.Clone();
        }
        if (canonical.GetList("Items") is NbtList items)
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var it in items.OfType<NbtCompound>())
            {
                var j = Items.ToJava(it, dv);
                j.SetByte("Slot", (sbyte)it.GetInt("Slot"));
                list.Add(j);
            }
            c["Items"] = list;
        }
        if (canonical.GetString("CustomName") is string cn) c["CustomName"] = TextComponents.ToJavaTag(cn, dv);
        foreach (var key in new[] { "RecordItem", "Book" })
            if (canonical.GetCompound(key) is NbtCompound it) c[key] = Items.ToJava(it, dv);
        switch (id)
        {
            case "minecraft:beacon":
                Effects.BeaconToJava(c, canonical, dv);
                break;
            case "minecraft:sign" or "minecraft:hanging_sign":
                if (dv < 3463)
                {
                    var front = canonical.GetCompound("front_text");
                    var msgs = front?.GetList("messages");
                    for (int i = 0; i < 4; i++) c[$"Text{i + 1}"] = new NbtString(msgs != null && i < msgs.Count ? TextComponents.FromJavaTag(msgs[i]) : "\"\"");
                    c.SetString("Color", front?.GetString("color") ?? "black");
                    if (dv >= 2681) c.SetByte("GlowingText", (sbyte)(front?.GetBool("has_glowing_text") == true ? 1 : 0));
                    c.Remove("front_text"); c.Remove("back_text"); c.Remove("is_waxed");
                }
                else
                {
                    foreach (var side in new[] { "front_text", "back_text" })
                        if (canonical.GetCompound(side) is NbtCompound s)
                        {
                            var copy = (NbtCompound)s.Clone();
                            // Plain and formatted lines mixed (one formatted line, three empty ones) make a wrapped list.
                            copy["messages"] = TextComponents.JavaList((s.GetList("messages") ?? []).Select(line => TextComponents.ToJavaTag(TextComponents.FromJavaTag(line), dv)));
                            c[side] = copy;
                        }
                }
                break;
            case "minecraft:banner":
                if (dv < 3819)
                {
                    var list = new NbtList(NbtType.Compound);
                    foreach (var p in canonical.GetList("patterns")?.OfType<NbtCompound>() ?? [])
                        if (CodeByPattern.TryGetValue(p.GetString("pattern") ?? "", out var code))
                            list.Add(new NbtCompound { { "Pattern", new NbtString(code) }, { "Color", new NbtInt(ColorIndex(p.GetString("color"))) } });
                    c.Remove("patterns");
                    c["Patterns"] = list;
                }
                break;
            case "minecraft:mob_spawner":
                if (dv < 2831 && canonical.GetCompound("SpawnData")?.GetCompound("entity")?.GetString("id") is string ent)
                    c["SpawnData"] = new NbtCompound { { "id", new NbtString(ent) } };
                break;
            case "minecraft:skull":
                if (dv < 3819 && canonical.GetCompound("profile") is NbtCompound profile)
                {
                    var owner = new NbtCompound();
                    if (profile.GetString("name") is string n) owner.SetString("Name", n);
                    if (profile["id"] is NbtIntArray uuid) owner["Id"] = uuid.Clone();
                    c.Remove("profile");
                    c["SkullOwner"] = owner;
                }
                break;
            case "minecraft:end_gateway":
                if (dv < 3819 && canonical.GetInts("exit_portal") is { Length: 3 } e)
                {
                    c.Remove("exit_portal");
                    c["ExitPortal"] = new NbtCompound { { "X", new NbtInt(e[0]) }, { "Y", new NbtInt(e[1]) }, { "Z", new NbtInt(e[2]) } };
                }
                break;
            case "minecraft:beehive":
                {
                    var bees = new NbtList(NbtType.Compound);
                    foreach (var b in canonical.GetList("bees")?.OfType<NbtCompound>() ?? [])
                    {
                        if (b.GetCompound("entity_data") is not NbtCompound data || Entities.ToJava(data, dv) is not NbtCompound bee) continue;
                        bees.Add(dv >= 3837
                            ? new NbtCompound { { "entity_data", bee }, { "min_ticks_in_hive", new NbtInt(b.GetInt("min_ticks_in_hive")) }, { "ticks_in_hive", new NbtInt(b.GetInt("ticks_in_hive")) } }
                            : new NbtCompound { { "EntityData", bee }, { "MinOccupationTicks", new NbtInt(b.GetInt("min_ticks_in_hive")) }, { "TicksInHive", new NbtInt(b.GetInt("ticks_in_hive")) } });
                    }
                    c.Remove("bees");
                    c[dv >= 3837 ? "bees" : "Bees"] = bees;
                    if (dv < 3837 && canonical.GetInts("flower_pos") is { Length: 3 } fp)
                    {
                        c.Remove("flower_pos");
                        c["FlowerPos"] = new NbtCompound { { "X", new NbtInt(fp[0]) }, { "Y", new NbtInt(fp[1]) }, { "Z", new NbtInt(fp[2]) } };
                    }
                    break;
                }
        }
        return c;
    }

    /// <summary>The block entity id that fits the block actually written (stand-ins like barrel → chest).</summary>
    static string AdaptToBlock(string id, BlockState block)
    {
        string b = block.BaseName;
        string? wanted = b switch
        {
            "chest" => "minecraft:chest", "trapped_chest" => "minecraft:trapped_chest", "barrel" => "minecraft:barrel", "furnace" => "minecraft:furnace",
            "blast_furnace" => "minecraft:blast_furnace", "smoker" => "minecraft:smoker", "hopper" => "minecraft:hopper", "dispenser" => "minecraft:dispenser",
            "dropper" => "minecraft:dropper", "ender_chest" => "minecraft:ender_chest", "spawner" => "minecraft:mob_spawner", "jukebox" => "minecraft:jukebox",
            "lectern" => "minecraft:lectern", "beacon" => "minecraft:beacon", "brewing_stand" => "minecraft:brewing_stand", "bookshelf" => "",
            "air" => "",
            _ when b.EndsWith("shulker_box") => "minecraft:shulker_box",
            _ when b.EndsWith("_hanging_sign") => "minecraft:hanging_sign",
            _ when b.EndsWith("_sign") => "minecraft:sign",
            _ when b.EndsWith("_banner") => "minecraft:banner",
            _ when b.EndsWith("_bed") => "minecraft:bed",
            _ when Substitutes.IsMobHead(b) => "minecraft:skull",
            _ => null,
        };
        if (wanted == null) return id;
        // Containers keep their items when one container type stands in for another.
        return wanted;
    }

    /// <summary>Pre-1.13 Java (and Legacy Console) block entity.</summary>
    public static NbtCompound? ToLegacyJava(NbtCompound canonical, string id, int dv, BlockState block)
    {
        var c = new NbtCompound();
        bool namespaced = dv >= 819; // 1.11 (16w32a)
        string oldId = ToOldId.GetValueOrDefault(id, "");
        if ((oldId.Length == 0 && !namespaced) || !KnownIn(id, dv >= 819 ? dv : 0))
            return Drop(canonical, $"{Version(dv)} has no {ConversionLog.Friendly(id)}");
        string writeId = namespaced ? id switch
        {
            "minecraft:trapped_chest" => "minecraft:chest",
            "minecraft:note_block" => "minecraft:noteblock",
            _ => id,
        } : oldId;
        c.SetString("id", writeId);
        c.SetInt("x", canonical.GetInt("x")); c.SetInt("y", canonical.GetInt("y")); c.SetInt("z", canonical.GetInt("z"));
        if (canonical.GetList("Items") is NbtList items)
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var it in items.OfType<NbtCompound>())
            {
                var j = Items.ToLegacyJava(it, dv, numericId: dv < 100);
                j.SetByte("Slot", (sbyte)it.GetInt("Slot"));
                list.Add(j);
            }
            c["Items"] = list;
        }
        if (canonical.GetString("CustomName") is string cn) c.SetString("CustomName", TextComponents.ToFormatted(cn, bedrock: false));
        switch (id)
        {
            case "minecraft:beacon":
                Effects.BeaconToJava(c, canonical, dv);
                break;
            case "minecraft:end_gateway":
                if (canonical.GetInts("exit_portal") is { Length: 3 } exit)
                    c["ExitPortal"] = new NbtCompound { { "X", new NbtInt(exit[0]) }, { "Y", new NbtInt(exit[1]) }, { "Z", new NbtInt(exit[2]) } };
                break;
            case "minecraft:sign":
                {
                    // JSON text since Java 1.8; Legacy Console and older Java store § formatted text.
                    var target = ConversionLog.Current?.Target;
                    bool json = dv >= 100 || target is { Kind: TargetKind.Java } && target.LegacyLevel >= LegacyLevel.Release_1_8;
                    var msgs = canonical.GetCompound("front_text")?.GetList("messages");
                    for (int i = 0; i < 4; i++)
                    {
                        string text = msgs != null && i < msgs.Count ? TextComponents.FromJavaTag(msgs[i]) : "\"\"";
                        c.SetString($"Text{i + 1}", json ? text : TextComponents.ToFormatted(text, bedrock: false));
                    }
                    if (canonical.GetCompound("back_text")?.GetList("messages")?.Any(m => TextComponents.ToPlain(TextComponents.FromJavaTag(m)).Length > 0) == true)
                        ConversionLog.Note(LossKind.Block, "Sign back text", "removed: signs before Java 1.20 have one side", lost: true, where: $"{canonical.GetInt("x")}, {canonical.GetInt("y")}, {canonical.GetInt("z")}");
                    break;
                }
            case "minecraft:banner":
                {
                    int baseColor = block.BaseName.EndsWith("_banner") ? ColorIndex(block.BaseName.Replace("_wall_banner", "").Replace("_banner", "")) : 0;
                    c.SetInt("Base", 15 - baseColor);
                    var list = new NbtList(NbtType.Compound);
                    foreach (var p in canonical.GetList("patterns")?.OfType<NbtCompound>() ?? [])
                        if (CodeByPattern.TryGetValue(p.GetString("pattern") ?? "", out var code))
                            list.Add(new NbtCompound { { "Pattern", new NbtString(code) }, { "Color", new NbtInt(15 - ColorIndex(p.GetString("color"))) } });
                    c["Patterns"] = list;
                    break;
                }
            case "minecraft:bed":
                c.SetInt("color", block.BaseName.EndsWith("_bed") ? ColorIndex(block.BaseName[..^4]) : 14);
                break;
            case "minecraft:skull":
                {
                    string b = block.BaseName;
                    int type = b.StartsWith("wither_skeleton") ? 1 : b.StartsWith("zombie") ? 2 : b.StartsWith("player") ? 3 : b.StartsWith("creeper") ? 4 : b.StartsWith("dragon") ? 5 : 0;
                    c.SetByte("SkullType", (sbyte)type);
                    c.SetByte("Rot", (sbyte)(int.TryParse(block.GetProperty("rotation"), out int r) ? r : 0));
                    if (canonical.GetCompound("profile")?.GetString("name") is string owner) c["Owner"] = new NbtCompound { { "Name", new NbtString(owner) } };
                    break;
                }
            case "minecraft:mob_spawner":
                {
                    string ent = canonical.GetCompound("SpawnData")?.GetCompound("entity")?.GetString("id") ?? "minecraft:pig";
                    string legacyEntity = Entities.LegacyJavaId(ent, namespaced);
                    if (dv >= 100) c["SpawnData"] = new NbtCompound { { "id", new NbtString(legacyEntity) } };
                    else c.SetString("EntityId", legacyEntity);
                    foreach (var k in new[] { "Delay", "MinSpawnDelay", "MaxSpawnDelay", "SpawnCount", "MaxNearbyEntities", "RequiredPlayerRange", "SpawnRange" })
                        if (canonical[k] is { IsNumeric: true } v) c.SetShort(k, (short)v.AsLong);
                    break;
                }
            case "minecraft:jukebox":
                if (canonical.GetCompound("RecordItem") is NbtCompound rec)
                {
                    var item = Items.ToLegacyJava(rec, dv, numericId: dv < 100);
                    c["RecordItem"] = item;
                    c.SetInt("Record", Items.LegacyItemNumber(Items.Id(rec)).Id);
                }
                break;
            case "minecraft:flower_pot":
                if (block.BaseName.StartsWith("potted_"))
                {
                    var (pid, pdata) = Items.LegacyItemNumber("minecraft:" + block.BaseName[7..]);
                    if (dv >= 100) c.SetString("Item", JavaBlockData.LegacyItemNames.GetValueOrDefault(pid) ?? "minecraft:air"); else c.SetInt("Item", pid);
                    c.SetInt("Data", pdata);
                }
                break;
            case "minecraft:note_block":
                c.SetByte("note", (sbyte)(int.TryParse(block.GetProperty("note"), out int n) ? n : 0));
                break;
            case "minecraft:furnace":
                foreach (var k in new[] { "BurnTime", "CookTime", "CookTimeTotal" })
                    if (canonical[k] is { IsNumeric: true } v) c.SetShort(k, (short)v.AsLong);
                break;
        }
        // The fields that kept their name and meaning since (command blocks, structure blocks, locks, loot tables, brewing,
        // hoppers, gateways, comparators), as they are; a version leaves alone what it doesn't use.
        foreach (var key in LegacyKept)
            if (!c.ContainsKey(key) && canonical[key] is NbtTag v && !(key is "LootTable" or "LootTableSeed" && dv < 169)) c[key] = v.Clone();
        if (dv < 169 && canonical.GetString("LootTable") != null)
            ConversionLog.Note(LossKind.Container, "unopened loot " + ConversionLog.Friendly(id), $"empty: {Version(dv)} has no loot tables (its loot is made when it is first opened)",
                lost: true, where: $"{canonical.GetInt("x")}, {canonical.GetInt("y")}, {canonical.GetInt("z")}");
        return c;
    }

    // What block entities of 1.12 and older keep under the same names they still have (and only them: an item in today's
    // form, say, would read as nothing there).
    static readonly string[] LegacyKept =
    [
        "Lock", "LootTable", "LootTableSeed", "TransferCooldown", "BrewTime", "Fuel", "OutputSignal", "Age", "ExactTeleport",
        "Command", "SuccessCount", "LastOutput", "TrackOutput", "powered", "auto", "conditionMet", "UpdateLastExecution", "LastExecution",
        "name", "author", "metadata", "posX", "posY", "posZ", "sizeX", "sizeY", "sizeZ", "rotation", "mirror", "mode", "ignoreEntities",
        "showboundingbox", "showair", "integrity", "seed",
    ];
}
