// MinecraftStudio.Core/Conversion/BlockEntities.FromJava.cs: from java for BlockEntities.
// Part of BlockEntities (main part in BlockEntities.cs).
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

public static partial class BlockEntities
{
    // ---------------------------------------------------------------- Java -> canonical

    public static NbtCompound FromJava(NbtCompound be, int dv)
    {
        string id = CanonicalId(be);
        var c = (NbtCompound)be.Clone();
        c.SetString("id", id);
        c.Remove("keepPacked");
        if (be.GetList("Items") is NbtList items) c["Items"] = ItemsFromJava(items, dv);
        foreach (var key in new[] { "CustomName" })
            if (be[key] is NbtTag t) c[key] = new NbtString(dv is > 0 and < 1451 ? TextComponents.FromPlain(t is NbtString s ? s.Value : t.ToString()) : TextComponents.FromJavaTag(t));
        switch (id)
        {
            case "minecraft:sign" or "minecraft:hanging_sign":
                if (!be.ContainsKey("front_text"))
                {
                    var msgs = new NbtList(NbtType.String);
                    for (int i = 1; i <= 4; i++)
                    {
                        var t = be[$"Text{i}"];
                        // JSON since 1.8; FromJavaTag detects plain text from older versions and Legacy Console.
                        string json = t == null ? "\"\"" : TextComponents.FromJavaTag(t);
                        msgs.Add(new NbtString(json));
                        c.Remove($"Text{i}");
                    }
                    c["front_text"] = SignSide(msgs, be.GetString("Color") ?? "black", be.GetBool("GlowingText"));
                    c["back_text"] = SignSide(EmptyMessages(), "black", false);
                    c.Remove("Color"); c.Remove("GlowingText");
                    c.SetByte("is_waxed", 0);
                }
                else
                {
                    foreach (var side in new[] { "front_text", "back_text" })
                        if (be.GetCompound(side) is NbtCompound s && s.GetList("messages") is NbtList m)
                        {
                            var copy = (NbtCompound)s.Clone();
                            var msgs = new NbtList(NbtType.String);
                            foreach (var line in m) msgs.Add(new NbtString(TextComponents.FromJavaTag(line)));
                            copy["messages"] = msgs;
                            c[side] = copy;
                        }
                }
                break;
            case "minecraft:banner":
                {
                    var patterns = new NbtList(NbtType.Compound);
                    bool legacyColors = dv is > 0 and < 1451 || be.ContainsKey("Base");
                    foreach (var p in (be.GetList("patterns") ?? be.GetList("Patterns"))?.OfType<NbtCompound>() ?? [])
                    {
                        string? pattern = p.GetString("pattern") ?? (p.GetString("Pattern") is string code ? PatternByCode.GetValueOrDefault(code) : null);
                        if (pattern == null) continue;
                        string color = p["color"] is NbtString cs ? cs.Value : Color(legacyColors ? 15 - p.GetInt("Color") : p.GetInt("Color"));
                        patterns.Add(new NbtCompound { { "pattern", new NbtString(pattern) }, { "color", new NbtString(color) } });
                    }
                    c.Remove("Patterns"); c.Remove("Base");
                    c["patterns"] = patterns;
                    break;
                }
            case "minecraft:mob_spawner":
                {
                    string? entity = be.GetCompound("SpawnData")?.GetCompound("entity")?.GetString("id") ?? be.GetCompound("SpawnData")?.GetString("id") ?? be.GetString("EntityId");
                    if (entity != null)
                    {
                        c["SpawnData"] = new NbtCompound { { "entity", new NbtCompound { { "id", new NbtString(Entities.CanonicalId(entity)) } } } };
                        c.Remove("EntityId");
                        c.Remove("SpawnPotentials");
                    }
                    break;
                }
            case "minecraft:jukebox":
                if (be.GetCompound("RecordItem") is NbtCompound rec && Items.FromJava(rec, dv) is NbtCompound ri) c["RecordItem"] = ri;
                else if (be.GetInt("Record") > 0 && Items.FromJava(new NbtCompound { { "id", new NbtShort((short)be.GetInt("Record")) }, { "Count", new NbtByte(1) } }, dv) is NbtCompound r2) c["RecordItem"] = r2;
                c.Remove("Record");
                break;
            case "minecraft:lectern":
                if (be.GetCompound("Book") is NbtCompound book && Items.FromJava(book, dv) is NbtCompound bk) c["Book"] = bk;
                break;
            case "minecraft:beehive":
                {
                    var bees = new NbtList(NbtType.Compound);
                    foreach (var b in (be.GetList("bees") ?? be.GetList("Bees"))?.OfType<NbtCompound>() ?? [])
                    {
                        if ((b.GetCompound("entity_data") ?? b.GetCompound("EntityData")) is not NbtCompound data) continue;
                        var e = (NbtCompound)data.Clone();
                        if (e.GetString("id") == null) e.SetString("id", "minecraft:bee");
                        bees.Add(new NbtCompound
                        {
                            { "entity_data", Entities.FromJava(e, dv) },
                            { "min_ticks_in_hive", new NbtInt(b.GetInt("min_ticks_in_hive", b.GetInt("MinOccupationTicks"))) },
                            { "ticks_in_hive", new NbtInt(b.GetInt("ticks_in_hive", b.GetInt("TicksInHive"))) },
                        });
                    }
                    c.Remove("Bees");
                    c["bees"] = bees;
                    if (be.GetCompound("FlowerPos") is NbtCompound fp)
                    {
                        c.Remove("FlowerPos");
                        c["flower_pos"] = new NbtIntArray([fp.GetInt("X"), fp.GetInt("Y"), fp.GetInt("Z")]);
                    }
                    break;
                }
            case "minecraft:beacon":
                // Its effects by name (numbered before 1.20.2).
                Effects.BeaconFromJava(c, be);
                break;
            case "minecraft:end_gateway":
                // Where it sends you: exit_portal since 1.20.5, ExitPortal {X, Y, Z} before.
                if (be.GetCompound("ExitPortal") is NbtCompound exit)
                {
                    c.Remove("ExitPortal");
                    c["exit_portal"] = new NbtIntArray([exit.GetInt("X"), exit.GetInt("Y"), exit.GetInt("Z")]);
                }
                break;
            case "minecraft:skull":
                if (be.GetCompound("SkullOwner") is NbtCompound owner && !be.ContainsKey("profile"))
                {
                    var profile = new NbtCompound();
                    if (owner.GetString("Name") is string n) profile.SetString("name", n);
                    if (owner["Id"] is NbtIntArray uuid) profile["id"] = uuid.Clone();
                    c["profile"] = profile;
                    c.Remove("SkullOwner");
                }
                else if (be.GetString("ExtraType") is string extra && extra.Length > 0)
                    c["profile"] = new NbtCompound { { "name", new NbtString(extra) } };
                break;
        }
        return c;
    }

    static NbtCompound SignSide(NbtList messages, string color, bool glowing) => new()
    {
        { "messages", messages }, { "color", new NbtString(color) }, { "has_glowing_text", new NbtByte(glowing) },
    };

    static NbtList EmptyMessages()
    {
        var l = new NbtList(NbtType.String);
        for (int i = 0; i < 4; i++) l.Add(new NbtString("\"\""));
        return l;
    }
}
