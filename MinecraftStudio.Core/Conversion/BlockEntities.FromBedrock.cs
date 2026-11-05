// MinecraftStudio.Core/Conversion/BlockEntities.FromBedrock.cs: from bedrock for BlockEntities.
// Part of BlockEntities (main part in BlockEntities.cs).
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

public static partial class BlockEntities
{
    // ---------------------------------------------------------------- Bedrock -> canonical

    public static NbtCompound? FromBedrock(NbtCompound be)
    {
        string bid = be.GetString("id") ?? "";
        if (!FromBedrockId.TryGetValue(bid, out var id)) return null;
        var c = Base(id, be);
        if (be.GetList("Items") is NbtList items) c["Items"] = ItemsFromBedrock(items);
        if (be.GetString("CustomName") is string cn && cn.Length > 0) c.SetString("CustomName", TextComponents.FromFormatted(cn, bedrock: true));
        switch (id)
        {
            case "minecraft:sign" or "minecraft:hanging_sign":
                {
                    NbtCompound Side(NbtCompound? s, string? oldText)
                    {
                        string text = s?.GetString("Text") ?? oldText ?? "";
                        var lines = text.Split('\n');
                        var msgs = new NbtList(NbtType.String);
                        // Bedrock formatting carries on to the next line; Java lines start plain.
                        string carry = "";
                        for (int i = 0; i < 4; i++)
                        {
                            string line = carry + (i < lines.Length ? lines[i] : "");
                            msgs.Add(new NbtString(TextComponents.FromFormatted(line, bedrock: true)));
                            carry = TextComponents.TrailingCodes(line);
                        }
                        int argb = s?.GetInt("SignTextColor", unchecked((int)0xFF000000)) ?? unchecked((int)0xFF000000);
                        return SignSide(msgs, NearestDye(argb & 0xFFFFFF), s?.GetBool("IgnoreLighting") ?? false);
                    }
                    c["front_text"] = Side(be.GetCompound("FrontText"), be.GetString("Text"));
                    c["back_text"] = Side(be.GetCompound("BackText"), null);
                    c.SetByte("is_waxed", (sbyte)(be.GetBool("IsWaxed") ? 1 : 0));
                    break;
                }
            case "minecraft:banner":
                {
                    var patterns = new NbtList(NbtType.Compound);
                    foreach (var p in be.GetList("Patterns")?.OfType<NbtCompound>() ?? [])
                        if (p.GetString("Pattern") is string code && PatternByCode.TryGetValue(code, out var pattern))
                            patterns.Add(new NbtCompound { { "pattern", new NbtString(pattern) }, { "color", new NbtString(Color(15 - p.GetInt("Color"))) } });
                    c["patterns"] = patterns;
                    break;
                }
            case "minecraft:mob_spawner":
                if (be.GetString("EntityIdentifier") is string ent && ent.Length > 0)
                    c["SpawnData"] = new NbtCompound { { "entity", new NbtCompound { { "id", new NbtString(Entities.JavaIdFromBedrock(ent)) } } } };
                foreach (var k in new[] { "Delay", "MinSpawnDelay", "MaxSpawnDelay", "SpawnCount", "MaxNearbyEntities", "RequiredPlayerRange", "SpawnRange" })
                    if (be[k] is { IsNumeric: true } v) c.SetShort(k, (short)v.AsLong);
                break;
            case "minecraft:jukebox":
                if (be.GetCompound("RecordItem") is NbtCompound rec && Items.FromBedrock(rec) is NbtCompound ri) c["RecordItem"] = ri;
                break;
            case "minecraft:lectern":
                if (be.GetCompound("book") is NbtCompound book && Items.FromBedrock(book) is NbtCompound bk) { c["Book"] = bk; c.SetInt("Page", be.GetInt("page")); }
                break;
            case "minecraft:furnace" or "minecraft:blast_furnace" or "minecraft:smoker":
                c.SetShort("BurnTime", (short)be.GetInt("BurnTime"));
                c.SetShort("CookTime", (short)be.GetInt("CookTime"));
                c.SetShort("CookTimeTotal", (short)Math.Max(1, be.GetInt("BurnDuration", 200)));
                break;
            case "minecraft:beacon":
                // Bedrock numbers a beacon's effects as Java did before 1.20.2.
                if (Effects.Name(be.GetInt("primary")) is string primary) c.SetString("primary_effect", primary);
                if (Effects.Name(be.GetInt("secondary")) is string secondary) c.SetString("secondary_effect", secondary);
                break;
            case "minecraft:command_block":
                c.SetString("Command", be.GetString("Command") ?? "");
                c.SetByte("auto", (sbyte)(be.GetBool("auto") ? 1 : 0));
                c.SetByte("powered", (sbyte)(be.GetBool("powered") ? 1 : 0));
                c.SetByte("TrackOutput", (sbyte)(be.GetBool("TrackOutput", true) ? 1 : 0));
                break;
            case "minecraft:end_gateway":
                if (be.GetList("ExitPortal") is { Count: 3 } exit)
                    c["exit_portal"] = new NbtIntArray([(int)exit[0].AsLong, (int)exit[1].AsLong, (int)exit[2].AsLong]);
                c.SetLong("Age", be.GetLong("Age"));
                break;
            case "minecraft:campfire":
                {
                    var list = new NbtList(NbtType.Compound);
                    for (int i = 1; i <= 4; i++)
                        if (be.GetCompound($"Item{i}") is NbtCompound it && Items.FromBedrock(it) is NbtCompound ci) { ci.SetByte("Slot", (sbyte)(i - 1)); list.Add(ci); }
                    c["Items"] = list;
                    c["CookingTimes"] = new NbtIntArray([be.GetInt("ItemTime1"), be.GetInt("ItemTime2"), be.GetInt("ItemTime3"), be.GetInt("ItemTime4")]);
                    c["CookingTotalTimes"] = new NbtIntArray([600, 600, 600, 600]);
                    break;
                }
            case "minecraft:beehive":
                {
                    var bees = new NbtList(NbtType.Compound);
                    foreach (var o in be.GetList("Occupants")?.OfType<NbtCompound>() ?? [])
                    {
                        var e = o.GetCompound("SaveData") is NbtCompound save ? Entities.FromBedrock(save) : null;
                        e ??= new NbtCompound { { "id", new NbtString("minecraft:bee") } };
                        bees.Add(new NbtCompound { { "entity_data", e }, { "min_ticks_in_hive", new NbtInt(o.GetInt("TicksLeftToStay")) }, { "ticks_in_hive", new NbtInt(0) } });
                    }
                    c["bees"] = bees;
                    break;
                }
            case "minecraft:decorated_pot":
                if (be.GetList("sherds") is NbtList sherds) c["sherds"] = sherds.Clone();
                break;
            case "minecraft:skull" or "minecraft:bed" or "minecraft:flower_pot" or "minecraft:note_block":
                // Their data was folded into the block state (BedrockFixups); keep only what Java stores.
                if (id is "minecraft:flower_pot" or "minecraft:note_block") return null;
                break;
        }
        return c;
    }

    static string NearestDye(int rgb)
    {
        // Bedrock stores sign colours as ARGB; pick the dye whose text colour is closest.
        (string, int)[] dyes =
        [
            ("white", 0xF0F0F0), ("orange", 0xF9801D), ("magenta", 0xC74EBD), ("light_blue", 0x3AB3DA), ("yellow", 0xFED83D), ("lime", 0x80C71F),
            ("pink", 0xF38BAA), ("gray", 0x474F52), ("light_gray", 0x9D9D97), ("cyan", 0x169C9C), ("purple", 0x8932B8), ("blue", 0x3C44AA),
            ("brown", 0x835432), ("green", 0x5E7C16), ("red", 0xB02E26), ("black", 0x000000),
        ];
        string best = "black";
        long bestD = long.MaxValue;
        foreach (var (name, c) in dyes)
        {
            long dr = ((rgb >> 16) & 255) - ((c >> 16) & 255), dg = ((rgb >> 8) & 255) - ((c >> 8) & 255), db = (rgb & 255) - (c & 255);
            long d = dr * dr + dg * dg + db * db;
            if (d < bestD) { bestD = d; best = name; }
        }
        return best;
    }

    static int DyeArgb(string color) => color switch
    {
        "white" => unchecked((int)0xFFF0F0F0), "orange" => unchecked((int)0xFFF9801D), "magenta" => unchecked((int)0xFFC74EBD),
        "light_blue" => unchecked((int)0xFF3AB3DA), "yellow" => unchecked((int)0xFFFED83D), "lime" => unchecked((int)0xFF80C71F),
        "pink" => unchecked((int)0xFFF38BAA), "gray" => unchecked((int)0xFF474F52), "light_gray" => unchecked((int)0xFF9D9D97),
        "cyan" => unchecked((int)0xFF169C9C), "purple" => unchecked((int)0xFF8932B8), "blue" => unchecked((int)0xFF3C44AA),
        "brown" => unchecked((int)0xFF835432), "green" => unchecked((int)0xFF5E7C16), "red" => unchecked((int)0xFFB02E26), _ => unchecked((int)0xFF000000),
    };
}
