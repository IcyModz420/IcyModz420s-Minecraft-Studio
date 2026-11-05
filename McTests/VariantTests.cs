// McTests/VariantTests.cs: "variants" — old kinds and villager jobs across Java versions.
// Owns: VariantTests — canonical kinds written for releases before 1.11 as the kind with its type number (horses,
// skeletons, zombies, guardians; strays and husks before 1.10 as skeletons and zombies) and read back as the same kinds;
// villager and zombie villager jobs as each release's numbers before 1.14 and back (also the real 1.12.2 Forge zombie
// villager); kinds a release didn't have yet dropped, their riders set down; and that zombie villager's world converted
// to 1.8, 1.10, 1.12, 1.16 and Bedrock with its job checked in each output.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class VariantTests
{
    public static void Run(string[] args)
    {
        Console.WriteLine("== Kinds before 1.11");
        Kinds();
        Console.WriteLine("== Villager jobs before 1.14");
        Jobs();
        Console.WriteLine("== Kinds a release didn't have yet");
        Missing();
        Console.WriteLine("== Equipment and drop chances before 1.9 (one list of five)");
        OldEquipment();
        Console.WriteLine("== Potion effects (numbered before 1.20.2), on mobs and players");
        PotionEffects();
        Console.WriteLine("== Beacons, end gateways, an enderman's block");
        Held();
        Console.WriteLine("== Block entities for 1.12 and older keep their fields");
        LegacyBlockEntities();
        Console.WriteLine("== A real 1.12.2 zombie villager converted");
        Converted();
        Console.WriteLine("== To a console save (the old forms, gear in both lists)");
        ToConsole();
    }

    static NbtCompound Canon(string id, NbtCompound? extra = null)
    {
        var e = new NbtCompound
        {
            ["id"] = new NbtString(id), ["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(1.5), new NbtDouble(64), new NbtDouble(1.5) },
            ["UUID"] = new NbtIntArray([1, 2, 3, 4]), ["Health"] = new NbtFloat(10),
        };
        foreach (var (k, v) in extra ?? []) e[k] = v;
        return e;
    }

    static NbtCompound Job(string profession, int level = 1) => new()
    {
        ["VillagerData"] = new NbtCompound { ["profession"] = new NbtString("minecraft:" + profession), ["level"] = new NbtInt(level), ["type"] = new NbtString("minecraft:taiga") },
    };

    static string Show(NbtCompound? j) => j == null ? "(dropped)" : j.GetString("id") + string.Concat(
        new[] { "Type", "SkeletonType", "ZombieType", "Elder", "IsVillager", "VillagerProfession", "Profession", "Career", "CareerLevel" }
            .Where(j.ContainsKey).Select(k => $" {k}={j[k]!.AsLong}")) + (j.ContainsKey("VillagerData") ? " VillagerData" : "");

    static void Expect(string label, NbtCompound? got, string want) => Program.Check(Show(got) == want, $"{label}: {want} (got {Show(got)})");

    static void Kinds()
    {
        foreach (var (id, want) in new[] { ("minecraft:horse", "EntityHorse Type=0"), ("minecraft:donkey", "EntityHorse Type=1"), ("minecraft:mule", "EntityHorse Type=2"),
            ("minecraft:zombie_horse", "EntityHorse Type=3"), ("minecraft:skeleton_horse", "EntityHorse Type=4"), ("minecraft:elder_guardian", "Guardian Elder=1"),
            ("minecraft:wither_skeleton", "Skeleton SkeletonType=1") })
        {
            Expect($"{id} → 1.8", Entities.ToJava(Canon(id), 0), want);
            var back = Entities.FromJava(Entities.ToJava(Canon(id), 0)!, 0);
            Program.Check(back.GetString("id") == id && !back.ContainsKey("Type") && !back.ContainsKey("SkeletonType") && !back.ContainsKey("Elder"),
                $"{id} → 1.8 → read again: {back.GetString("id")}, no type number left");
        }
        Expect("donkey → 1.12", Entities.ToJava(Canon("minecraft:donkey"), 1139), "minecraft:donkey");
        Expect("stray → 1.10", Entities.ToJava(Canon("minecraft:stray"), 510), "Skeleton SkeletonType=2");
        Expect("stray → 1.9 (no strays: a skeleton)", Entities.ToJava(Canon("minecraft:stray"), 169), "Skeleton");
        Expect("husk → 1.10", Entities.ToJava(Canon("minecraft:husk"), 510), "Zombie ZombieType=6");
        Expect("husk → 1.9 (no husks: a zombie)", Entities.ToJava(Canon("minecraft:husk"), 169), "Zombie");
        Program.Check(Entities.FromJava(Entities.ToJava(Canon("minecraft:stray"), 510)!, 510).GetString("id") == "minecraft:stray"
            && Entities.FromJava(Entities.ToJava(Canon("minecraft:husk"), 510)!, 510).GetString("id") == "minecraft:husk", "stray and husk → 1.10 → read again: the same");
        var plain = Entities.FromJava(new NbtCompound { ["id"] = new NbtString("Zombie"), ["IsVillager"] = new NbtByte(0) }, 0);
        Program.Check(plain.GetString("id") == "minecraft:zombie" && !plain.ContainsKey("IsVillager"), "1.8 Zombie with IsVillager 0: a zombie");
    }

    static void Jobs()
    {
        // Canonical → old numbers.
        Expect("villager weaponsmith (level 3) → 1.12", Entities.ToJava(Canon("minecraft:villager", Job("weaponsmith", 3)), 1139), "minecraft:villager Profession=3 Career=2 CareerLevel=3");
        Expect("villager weaponsmith → 1.13.2", Entities.ToJava(Canon("minecraft:villager", Job("weaponsmith", 3)), 1631), "minecraft:villager Profession=3 Career=2 CareerLevel=3");
        Expect("villager weaponsmith → 1.14 (kept)", Entities.ToJava(Canon("minecraft:villager", Job("weaponsmith", 3)), 1952), "minecraft:villager VillagerData");
        Expect("villager mason → 1.12 (a toolsmith)", Entities.ToJava(Canon("minecraft:villager", Job("mason")), 1139), "minecraft:villager Profession=3 Career=3 CareerLevel=1");
        Expect("villager cartographer → 1.10 (a librarian)", Entities.ToJava(Canon("minecraft:villager", Job("cartographer")), 510), "Villager Profession=1 Career=1 CareerLevel=1");
        Expect("villager cartographer → 1.11", Entities.ToJava(Canon("minecraft:villager", Job("cartographer")), 819), "minecraft:villager Profession=1 Career=2 CareerLevel=1");
        Expect("villager nitwit → 1.10 (a farmer)", Entities.ToJava(Canon("minecraft:villager", Job("nitwit")), 510), "Villager Profession=0 Career=1 CareerLevel=1");
        Expect("villager nitwit → 1.11", Entities.ToJava(Canon("minecraft:villager", Job("nitwit")), 819), "minecraft:villager Profession=5 Career=1 CareerLevel=1");
        Expect("zombie villager librarian → 1.12", Entities.ToJava(Canon("minecraft:zombie_villager", Job("librarian")), 1139), "minecraft:zombie_villager Profession=1");
        Expect("zombie villager librarian → 1.11", Entities.ToJava(Canon("minecraft:zombie_villager", Job("librarian")), 819), "minecraft:zombie_villager Profession=1");
        Expect("zombie villager librarian → 1.10", Entities.ToJava(Canon("minecraft:zombie_villager", Job("librarian")), 510), "Zombie ZombieType=2");
        Expect("zombie villager librarian → 1.9", Entities.ToJava(Canon("minecraft:zombie_villager", Job("librarian")), 169), "Zombie IsVillager=1 VillagerProfession=1");
        Expect("zombie villager librarian → 1.8", Entities.ToJava(Canon("minecraft:zombie_villager", Job("librarian")), 0), "Zombie IsVillager=1");
        // Old numbers → canonical.
        string Read(NbtCompound raw, int dv) => Entities.FromJava(raw, dv) is var c ? $"{c.GetString("id")} {c.GetCompound("VillagerData")?.GetString("profession")} {c.GetCompound("VillagerData")?.GetInt("level")}"
            + string.Concat(new[] { "Profession", "Career", "CareerLevel", "ProfessionName", "ZombieType", "IsVillager", "VillagerProfession" }.Where(c.ContainsKey).Select(k => " +" + k)) : "";
        void Reads(string label, NbtCompound raw, int dv, string want) => Program.Check(Read(raw, dv) == want, $"{label}: {want} (got {Read(raw, dv)})");
        // The real one in the 1.12.2 Forge sample: Profession 3 (a smith, no career) is an armorer, as the game upgrades it.
        Reads("1.12.2 Forge zombie villager (Profession 3, ProfessionName smith)", new NbtCompound
        {
            ["id"] = new NbtString("minecraft:zombie_villager"), ["Profession"] = new NbtInt(3), ["ProfessionName"] = new NbtString("minecraft:smith"), ["ConversionTime"] = new NbtInt(-1),
        }, 1139, "minecraft:zombie_villager minecraft:armorer 1");
        Reads("1.12 villager (Profession 0, Career 3, CareerLevel 2)", new NbtCompound
        {
            ["id"] = new NbtString("minecraft:villager"), ["Profession"] = new NbtInt(0), ["Career"] = new NbtInt(3), ["CareerLevel"] = new NbtInt(2),
        }, 1139, "minecraft:villager minecraft:shepherd 2");
        Reads("1.10 Zombie ZombieType 2", new NbtCompound { ["id"] = new NbtString("Zombie"), ["ZombieType"] = new NbtInt(2) }, 510, "minecraft:zombie_villager minecraft:librarian 1");
        Reads("1.9 Zombie IsVillager, VillagerProfession 4", new NbtCompound { ["id"] = new NbtString("Zombie"), ["IsVillager"] = new NbtByte(1), ["VillagerProfession"] = new NbtInt(4) }, 169,
            "minecraft:zombie_villager minecraft:butcher 1");
        Reads("1.8 Zombie IsVillager", new NbtCompound { ["id"] = new NbtString("Zombie"), ["IsVillager"] = new NbtByte(1) }, 0, "minecraft:zombie_villager minecraft:none 1");
        Reads("1.14 villager (VillagerData kept as it is)", new NbtCompound { ["id"] = new NbtString("minecraft:villager") }.With(Job("cleric", 4)), 1952, "minecraft:villager minecraft:cleric 4");
        // Every 1.14 job → 1.12 → read again: the same job (masons are toolsmiths there, the jobless farmers).
        foreach (var job in new[] { "farmer", "fisherman", "shepherd", "fletcher", "librarian", "cartographer", "cleric", "armorer", "weaponsmith", "toolsmith", "butcher", "leatherworker", "nitwit" })
        {
            var back = Entities.FromJava(Entities.ToJava(Canon("minecraft:villager", Job(job, 2)), 1139)!, 1139);
            Program.Check(back.GetCompound("VillagerData")?.GetString("profession") == "minecraft:" + job && back.GetCompound("VillagerData")?.GetInt("level") == 2,
                $"{job} → 1.12 → read again: {back.GetCompound("VillagerData")?.GetString("profession")}");
        }
    }

    static NbtCompound With(this NbtCompound c, NbtCompound more) { foreach (var (k, v) in more) c[k] = v; return c; }

    static void Missing()
    {
        foreach (var (id, before, from) in new[] { ("minecraft:parrot", 819, 1139), ("minecraft:llama", 510, 819), ("minecraft:polar_bear", 169, 510), ("minecraft:shulker", 0, 169) })
            Program.Check(Entities.ToJava(Canon(id), before) == null && Entities.ToJava(Canon(id), from) != null,
                $"{id}: not written before the release that added it (dv {before}), written from it (dv {from})");
        var llama = Canon("minecraft:llama", new NbtCompound { ["Passengers"] = new NbtList(NbtType.Compound) { Canon("minecraft:villager", Job("farmer")) } });
        var setDown = new List<NbtCompound>();
        Program.Check(Entities.ToJava(llama, 510, setDown) == null && setDown.Count == 1 && setDown[0].GetString("id") == "Villager",
            $"a llama with a villager on it → 1.10 (no llamas): the villager gets off ({setDown.Count} set down)");
    }

    static void OldEquipment()
    {
        static NbtCompound Item(string id) => new() { ["id"] = new NbtString(id), ["Count"] = new NbtByte(1), ["Damage"] = new NbtShort(0) };
        // A 1.8 armour stand: a sword in its hand (picked up, so it always drops: 2.0), boots, a chestplate and a pumpkin.
        var stand = new NbtCompound
        {
            ["id"] = new NbtString("ArmorStand"), ["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(0.5), new NbtDouble(64), new NbtDouble(0.5) },
            ["Equipment"] = new NbtList(NbtType.Compound) { Item("minecraft:diamond_sword"), Item("minecraft:iron_boots"), new NbtCompound(), Item("minecraft:diamond_chestplate"), Item("minecraft:pumpkin") },
            ["DropChances"] = new NbtList(NbtType.Float) { new NbtFloat(2), new NbtFloat(0.085f), new NbtFloat(0.085f), new NbtFloat(0.085f), new NbtFloat(0.085f) },
        };
        var c = Entities.FromJava(stand, 0);
        var eq = c.GetCompound("equipment");
        string Slots(NbtCompound? e) => e == null ? "(none)" : string.Join(" ", e.Select(kv => $"{kv.Key}={(kv.Value as NbtCompound)?.GetString("id")?.Replace("minecraft:", "")}"));
        Program.Check(eq != null && eq.GetCompound("mainhand")?.GetString("id") == "minecraft:diamond_sword" && eq.GetCompound("feet")?.GetString("id") == "minecraft:iron_boots"
            && eq.GetCompound("chest")?.GetString("id") == "minecraft:diamond_chestplate" && eq.GetCompound("head") != null && !eq.ContainsKey("legs")
            && !c.ContainsKey("Equipment") && c.GetCompound("drop_chances")?.GetDouble("mainhand") == 2 && !c.ContainsKey("DropChances"),
            $"1.8 armour stand → canonical: its sword, boots, chestplate and pumpkin, the sword always dropping ({Slots(eq)}; drop {c.GetCompound("drop_chances")?.GetDouble("mainhand")})");
        var j8 = Entities.ToJava(c, 0)!;
        // Written the old way (item ids as numbers there, which 1.8 reads as well as names), then read back to the same gear.
        var list = j8.GetList("Equipment");
        bool filled(int i) => list?[i] is NbtCompound it && it.ContainsKey("id");
        Program.Check(list is { Count: 5 } && filled(0) && filled(1) && !filled(2) && filled(3) && filled(4)
            && j8.GetList("DropChances") is { Count: 5 } dc && dc[0].AsDouble == 2 && !j8.ContainsKey("HandItems") && !j8.ContainsKey("ArmorItems") && !j8.ContainsKey("HandDropChances"),
            $"→ 1.8: five in Equipment (legs empty) and DropChances, nothing in the 1.9 lists ({j8.GetString("id")}: {(list == null ? "no Equipment" : string.Join(", ", list.Select(t => (t as NbtCompound)?["id"]?.ToString() ?? "-")))})");
        var again = Entities.FromJava(j8, 0);
        Program.Check(Slots(again.GetCompound("equipment")) == Slots(eq) && again.GetCompound("drop_chances")?.GetDouble("mainhand") == 2,
            $"→ 1.8 → read again: the same gear ({Slots(again.GetCompound("equipment"))})");
        var j12 = Entities.ToJava(c, 1139)!;
        Program.Check(j12.GetList("HandItems") is { Count: 2 } && j12.GetList("ArmorItems") is { Count: 4 } && j12.GetList("HandDropChances") is { Count: 2 } hd && hd[0].AsDouble == 2
            && !j12.ContainsKey("Equipment") && !j12.ContainsKey("DropChances"), "→ 1.12: HandItems/ArmorItems and HandDropChances (2 in the hand)");
        // An off-hand item has no slot before 1.9 (a conversion puts it in the rescue chests and says so).
        var zombie = Canon("minecraft:zombie", new NbtCompound { ["equipment"] = new NbtCompound { ["offhand"] = new NbtCompound { ["id"] = new NbtString("minecraft:totem_of_undying"), ["count"] = new NbtInt(1) } } });
        var z8 = Entities.ToJava(zombie, 0)!;
        Program.Check(z8.GetList("Equipment") == null && !z8.ContainsKey("HandItems"), "a zombie holding only an off-hand item → 1.8: no Equipment written (the item goes to a rescue chest)");
    }

    static NbtCompound OldEffect(int id, int amplifier, int duration) => new()
    {
        ["Id"] = new NbtByte((sbyte)id), ["Amplifier"] = new NbtByte((sbyte)amplifier), ["Duration"] = new NbtInt(duration),
        ["Ambient"] = new NbtByte(0), ["ShowParticles"] = new NbtByte(1), ["ShowIcon"] = new NbtByte(1),
    };

    static string EffectsOf(NbtCompound? e) =>
        e?.GetList("active_effects") is NbtList named ? "named " + string.Join(",", named.OfType<NbtCompound>().Select(f => $"{f.GetString("id")?.Replace("minecraft:", "")}/{f.GetInt("amplifier")}/{f.GetInt("duration")}"))
        : e?.GetList("ActiveEffects") is NbtList numbered ? "numbered " + string.Join(",", numbered.OfType<NbtCompound>().Select(f => $"{f.GetInt("Id")}/{f.GetInt("Amplifier")}/{f.GetInt("Duration")}"))
        : "none";

    static void PotionEffects()
    {
        // A 1.16 zombie with Speed II for 30 s and Invisibility for 2 min.
        var zombie = new NbtCompound
        {
            ["id"] = new NbtString("minecraft:zombie"), ["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(0.5), new NbtDouble(64), new NbtDouble(0.5) },
            ["ActiveEffects"] = new NbtList(NbtType.Compound) { OldEffect(1, 1, 600), OldEffect(14, 0, 2400) },
        };
        var c = Entities.FromJava(zombie, 2586);
        Program.Check(EffectsOf(c) == "named speed/1/600,invisibility/0/2400" && !c.ContainsKey("ActiveEffects"), $"1.16 effects → canonical by name ({EffectsOf(c)})");
        Program.Check(EffectsOf(Entities.ToJava(c, 3698)) == "named speed/1/600,invisibility/0/2400", $"→ 1.20.4: by name ({EffectsOf(Entities.ToJava(c, 3698))})");
        Program.Check(EffectsOf(Entities.ToJava(c, 3465)) == "numbered 1/1/600,14/0/2400", $"→ 1.20.1: numbered ({EffectsOf(Entities.ToJava(c, 3465))})");
        Program.Check(EffectsOf(Entities.ToJava(c, 0)) == "numbered 1/1/600,14/0/2400", $"→ 1.8: numbered ({EffectsOf(Entities.ToJava(c, 0))})");
        // Effects a version lacks are left out (reported in a conversion).
        NbtCompound With(string effect) => Canon("minecraft:zombie", new NbtCompound
        {
            ["active_effects"] = new NbtList(NbtType.Compound) { new NbtCompound { ["id"] = new NbtString("minecraft:" + effect), ["amplifier"] = new NbtByte(0), ["duration"] = new NbtInt(100) } },
        });
        Program.Check(EffectsOf(Entities.ToJava(With("darkness"), 2586)) == "none" && EffectsOf(Entities.ToJava(With("darkness"), 3105)) == "numbered 33/0/100",
            "darkness: left out for 1.16, number 33 from 1.19");
        Program.Check(EffectsOf(Entities.ToJava(With("glowing"), 0)) == "none" && EffectsOf(Entities.ToJava(With("glowing"), 169)) == "numbered 24/0/100", "glowing: left out for 1.8, number 24 from 1.9");
        Program.Check(EffectsOf(Entities.ToJava(With("infested"), 3698)) == "none" && EffectsOf(Entities.ToJava(With("infested"), 3953)) == "named infested/0/100",
            "infested (no number ever): left out before 1.21, by name from it");
        // A stronger effect hiding a weaker, longer one.
        var layered = Canon("minecraft:zombie", new NbtCompound
        {
            ["active_effects"] = new NbtList(NbtType.Compound)
            {
                new NbtCompound
                {
                    ["id"] = new NbtString("minecraft:speed"), ["amplifier"] = new NbtByte(2), ["duration"] = new NbtInt(200),
                    ["hidden_effect"] = new NbtCompound { ["id"] = new NbtString("minecraft:speed"), ["amplifier"] = new NbtByte(0), ["duration"] = new NbtInt(9000) },
                },
            },
        });
        var old = Entities.ToJava(layered, 3465)?.GetList("ActiveEffects")?.OfType<NbtCompound>().FirstOrDefault();
        Program.Check(old?.GetCompound("HiddenEffect") is NbtCompound hid && hid.GetInt("Id") == 1 && hid.GetInt("Duration") == 9000 && Entities.FromJava(Entities.ToJava(layered, 3465)!, 3465) is var back
            && back.GetList("active_effects")?.OfType<NbtCompound>().First().GetCompound("hidden_effect")?.GetInt("duration") == 9000,
            "a hidden effect underneath → 1.20.1 (HiddenEffect, numbered) → read again: still underneath");
        // A player's effects.
        var t20 = TargetCatalog.All.First(t => t.Kind == TargetKind.Java && t.Name.EndsWith("1.20.3 – 1.20.4", StringComparison.Ordinal));
        var t201 = TargetCatalog.All.First(t => t.Kind == TargetKind.Java && t.Name.EndsWith("1.20 – 1.20.1", StringComparison.Ordinal));
        var player = Players.FromJava("Player", new NbtCompound { ["ActiveEffects"] = new NbtList(NbtType.Compound) { OldEffect(16, 0, 3600), OldEffect(32, 1, 48000) } }, 2586);
        Program.Check(EffectsOf(player.Data) == "named night_vision/0/3600,hero_of_the_village/1/48000", $"a 1.16 player's effects → canonical ({EffectsOf(player.Data)})");
        Program.Check(EffectsOf(Players.ToJava(player, 3698, t20)) == "named night_vision/0/3600,hero_of_the_village/1/48000"
            && EffectsOf(Players.ToJava(player, 3463, t201)) == "numbered 16/0/3600,32/1/48000", "→ the player's file for 1.20.4 (named) and 1.20.1 (numbered)");
        // The recipe book, the enchanting seed and where they last died come along where the version has them.
        var book = new NbtCompound { ["recipes"] = new NbtList(NbtType.String) { new NbtString("minecraft:crafting_table") }, ["isGuiOpen"] = new NbtByte(1) };
        var death = new NbtCompound { ["dimension"] = new NbtString("minecraft:overworld"), ["pos"] = new NbtIntArray([10, 64, -5]) };
        var full = Players.FromJava("Player", new NbtCompound { ["recipeBook"] = book, ["XpSeed"] = new NbtInt(42), ["LastDeathLocation"] = death }, 3105);
        var t12 = TargetCatalog.All.First(t => t.Kind == TargetKind.Java && t.Name.EndsWith("1.12 – 1.12.2", StringComparison.Ordinal));
        var t8 = TargetCatalog.All.First(t => t.Kind == TargetKind.Java && t.Name.EndsWith("1.8 – 1.8.9", StringComparison.Ordinal));
        var p20 = Players.ToJava(full, 3698, t20);
        var p12 = Players.ToJava(full, 1139, t12);
        var p8 = Players.ToJava(full, 0, t8);
        Program.Check(p20.GetCompound("recipeBook")?.ContentEquals(book) == true && p20.GetInt("XpSeed") == 42 && p20.GetCompound("LastDeathLocation")?.ContentEquals(death) == true
            && p12.GetCompound("recipeBook") != null && !p12.ContainsKey("LastDeathLocation") && p8.GetCompound("recipeBook") == null && p8.GetInt("XpSeed") == 42,
            "recipe book, enchanting seed and last death: all for 1.20.4; book and seed for 1.12 (no recovery compass yet); the seed for 1.8 (no recipe book)");
    }

    static void Held()
    {
        // A 1.16 beacon: Speed and Regeneration.
        var beacon = new NbtCompound { ["id"] = new NbtString("minecraft:beacon"), ["x"] = new NbtInt(0), ["y"] = new NbtInt(64), ["z"] = new NbtInt(0), ["Primary"] = new NbtInt(1), ["Secondary"] = new NbtInt(10), ["Levels"] = new NbtInt(4) };
        var c = BlockEntities.FromJava(beacon, 2586);
        Program.Check(c.GetString("primary_effect") == "minecraft:speed" && c.GetString("secondary_effect") == "minecraft:regeneration" && !c.ContainsKey("Primary"),
            $"1.16 beacon → canonical: {c.GetString("primary_effect")}, {c.GetString("secondary_effect")}");
        var block = BlockState.Get("minecraft:beacon");
        var j20 = BlockEntities.ToJava(c, 3698, block);
        var j201 = BlockEntities.ToJava(c, 3465, block);
        var j12 = BlockEntities.ToJava(c, 1139, block);
        Program.Check(j20?.GetString("primary_effect") == "minecraft:speed" && !j20.ContainsKey("Primary") && j201?.GetInt("Primary") == 1 && j201.GetInt("Secondary") == 10
            && !j201.ContainsKey("primary_effect") && j12?.GetInt("Primary") == 1 && j12.GetInt("Secondary") == 10,
            "→ 1.20.4 by name, → 1.20.1 and 1.12 as Primary 1 / Secondary 10");
        var fromBedrock = BlockEntities.FromBedrock(new NbtCompound { ["id"] = new NbtString("Beacon"), ["x"] = new NbtInt(0), ["y"] = new NbtInt(64), ["z"] = new NbtInt(0), ["primary"] = new NbtInt(3), ["secondary"] = new NbtInt(0) });
        Program.Check(fromBedrock?.GetString("primary_effect") == "minecraft:haste" && fromBedrock.GetString("secondary_effect") == null, $"Bedrock beacon (primary 3) → canonical haste ({fromBedrock?.GetString("primary_effect")})");
        // An end gateway's exit.
        var gateway = new NbtCompound
        {
            ["id"] = new NbtString("minecraft:end_gateway"), ["x"] = new NbtInt(100), ["y"] = new NbtInt(70), ["z"] = new NbtInt(0), ["Age"] = new NbtLong(500), ["ExactTeleport"] = new NbtByte(1),
            ["ExitPortal"] = new NbtCompound { ["X"] = new NbtInt(1000), ["Y"] = new NbtInt(60), ["Z"] = new NbtInt(-20) },
        };
        var g = BlockEntities.FromJava(gateway, 2586);
        var gBlock = BlockState.Get("minecraft:end_gateway");
        var g21 = BlockEntities.ToJava(g, 3953, gBlock);
        var g20 = BlockEntities.ToJava(g, 3698, gBlock);
        var g12 = BlockEntities.ToJava(g, 1139, gBlock);
        Program.Check(g.GetInts("exit_portal") is [1000, 60, -20] && !g.ContainsKey("ExitPortal") && g21?.GetInts("exit_portal") is [1000, 60, -20] && !g21.ContainsKey("ExitPortal")
            && g20?.GetCompound("ExitPortal")?.GetInt("X") == 1000 && g12?.GetCompound("ExitPortal")?.GetInt("Z") == -20 && g12.GetLongOrNull("Age") == 500 && g12.GetBool("ExactTeleport"),
            "a 1.16 gateway's exit → canonical exit_portal → 1.21 the same, 1.20.4 and 1.12 as ExitPortal (with its age and exact teleport)");
        // A 1.12 enderman carrying a grass block (numbers there).
        var enderman = new NbtCompound { ["id"] = new NbtString("minecraft:enderman"), ["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(0.5), new NbtDouble(64), new NbtDouble(0.5) }, ["carried"] = new NbtShort(2), ["carriedData"] = new NbtShort(0) };
        var e = Entities.FromJava(enderman, 1139);
        var held = e.GetCompound("carriedBlockState")?.GetString("Name");
        var e8 = Entities.ToJava(e, 0);
        var e20 = Entities.ToJava(e, 3698);
        Program.Check(held == "minecraft:grass_block" && !e.ContainsKey("carried") && e8?.GetInt("carried") == 2 && e8.GetInt("carriedData") == 0 && !e8.ContainsKey("carriedBlockState")
            && e20?.GetCompound("carriedBlockState")?.GetString("Name") == "minecraft:grass_block" && !e20.ContainsKey("carried"),
            $"a 1.12 enderman's block (2:0) → canonical {held} → 1.8 as 2:0, 1.20.4 by name");
        var onBedrock = Entities.ToBedrock(e, 1);
        var endermanBack = onBedrock == null ? null : Entities.FromBedrock(onBedrock);
        Program.Check(onBedrock?.GetCompound("carriedBlock")?.GetString("name") is string bedrockName && bedrockName.Contains("grass")
            && endermanBack?.GetCompound("carriedBlockState")?.GetString("Name") == "minecraft:grass_block",
            $"→ Bedrock (carriedBlock {onBedrock?.GetCompound("carriedBlock")?.GetString("name")}) → read again: {endermanBack?.GetCompound("carriedBlockState")?.GetString("Name")}");
    }

    static void LegacyBlockEntities()
    {
        NbtCompound At(string id, NbtCompound fields)
        {
            var c = new NbtCompound { ["id"] = new NbtString(id), ["x"] = new NbtInt(5), ["y"] = new NbtInt(64), ["z"] = new NbtInt(5) };
            foreach (var (k, v) in fields) c[k] = v;
            return c;
        }
        var command = At("minecraft:command_block", new NbtCompound { ["Command"] = new NbtString("say hello"), ["auto"] = new NbtByte(1), ["TrackOutput"] = new NbtByte(1), ["SuccessCount"] = new NbtInt(3) });
        var cmd12 = BlockEntities.ToJava(command, 1139, BlockState.Get("minecraft:command_block"));
        var cmd8 = BlockEntities.ToJava(command, 0, BlockState.Get("minecraft:command_block"));
        Program.Check(cmd12?.GetString("Command") == "say hello" && cmd12.GetBool("auto") && cmd12.GetInt("SuccessCount") == 3 && cmd8?.GetString("Command") == "say hello",
            $"a command block → 1.12 and 1.8 keeps its command ({cmd12?.GetString("Command")} / {cmd8?.GetString("Command")})");
        var structure = At("minecraft:structure_block", new NbtCompound
        {
            ["name"] = new NbtString("house"), ["mode"] = new NbtString("SAVE"), ["posX"] = new NbtInt(1), ["posY"] = new NbtInt(0), ["posZ"] = new NbtInt(1),
            ["sizeX"] = new NbtInt(7), ["sizeY"] = new NbtInt(5), ["sizeZ"] = new NbtInt(9), ["rotation"] = new NbtString("NONE"), ["mirror"] = new NbtString("NONE"),
        });
        var st12 = BlockEntities.ToJava(structure, 1139, BlockState.Get("minecraft:structure_block"));
        Program.Check(st12?.GetString("name") == "house" && st12.GetString("mode") == "SAVE" && st12.GetInt("sizeZ") == 9, $"a structure block → 1.12 keeps its name, mode and box ({st12?.GetString("name")})");
        var loot = At("minecraft:chest", new NbtCompound { ["LootTable"] = new NbtString("minecraft:chests/simple_dungeon"), ["LootTableSeed"] = new NbtLong(1234), ["Lock"] = new NbtString("key") });
        var l12 = BlockEntities.ToJava(loot, 1139, BlockState.Get("minecraft:chest[facing=north,type=single,waterlogged=false]"));
        var l8 = BlockEntities.ToJava(loot, 0, BlockState.Get("minecraft:chest[facing=north,type=single,waterlogged=false]"));
        Program.Check(l12?.GetString("LootTable") == "minecraft:chests/simple_dungeon" && l12.GetLongOrNull("LootTableSeed") == 1234 && l12.GetString("Lock") == "key"
            && l8 != null && !l8.ContainsKey("LootTable") && l8.GetString("Lock") == "key",
            "an unopened loot chest → 1.12 keeps its loot table and lock; → 1.8 (no loot tables) keeps the lock (the report says it is empty there)");
        // An item in today's form must not be copied over a field the old version reads (the jukebox keeps its own handling).
        var jukebox = At("minecraft:jukebox", new NbtCompound { ["RecordItem"] = new NbtCompound { ["id"] = new NbtString("minecraft:music_disc_cat"), ["count"] = new NbtInt(1) } });
        var j12 = BlockEntities.ToJava(jukebox, 1139, BlockState.Get("minecraft:jukebox[has_record=true]"));
        Program.Check(j12 != null && (j12.GetCompound("RecordItem") is not NbtCompound ri || ri.ContainsKey("Count")) && !(j12.GetCompound("RecordItem")?.ContainsKey("count") ?? false),
            $"a jukebox → 1.12: its disc in the old item form ({j12?.GetCompound("RecordItem")?["id"]}, Record {j12?.GetInt("Record")})");
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    static void ToConsole()
    {
        string source = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
        if (!Directory.Exists(source)) { Console.WriteLine("   (not found: skipped)"); return; }
        string temp = Path.Combine(TestPaths.Output, "variants-console", DateTime.Now.ToString("HHmmss"));
        string copy = Path.Combine(temp, "source");
        CopyDir(source, copy);
        ChunkPos at;
        using (var w = WorldLoader.Open(copy))
        {
            var info = WorldInfoReader.Read(w);
            at = new ChunkPos(info.SpawnX >> 4, info.SpawnZ >> 4);
            var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
            var nbt = dim.LoadChunkNbt(at) ?? throw new InvalidDataException("no spawn chunk");
            var level = nbt.GetCompound("Level") ?? nbt;
            if (level.GetList("Entities") is not NbtList stored) level["Entities"] = stored = new NbtList(NbtType.Compound);
            NbtList Pos(double dx) => new(NbtType.Double) { new NbtDouble(at.X * 16 + 4 + dx), new NbtDouble(70), new NbtDouble(at.Z * 16 + 4) };
            NbtCompound Item(string id) => new() { ["id"] = new NbtString(id), ["Count"] = new NbtByte(1) };
            stored.Add(new NbtCompound
            {
                ["id"] = new NbtString("minecraft:zombie_villager"), ["Pos"] = Pos(0), ["UUID"] = new NbtIntArray([140, 141, 142, 143]), ["Health"] = new NbtFloat(20),
                ["VillagerData"] = new NbtCompound { ["profession"] = new NbtString("minecraft:armorer"), ["level"] = new NbtInt(1), ["type"] = new NbtString("minecraft:plains") },
            });
            stored.Add(new NbtCompound
            {
                ["id"] = new NbtString("minecraft:zombie"), ["Pos"] = Pos(2), ["UUID"] = new NbtIntArray([150, 151, 152, 153]), ["Health"] = new NbtFloat(20), ["PersistenceRequired"] = new NbtByte(1),
                ["HandItems"] = new NbtList(NbtType.Compound) { Item("minecraft:iron_sword"), new NbtCompound() },
                ["ArmorItems"] = new NbtList(NbtType.Compound) { new NbtCompound(), new NbtCompound(), new NbtCompound(), Item("minecraft:iron_helmet") },
                ["HandDropChances"] = new NbtList(NbtType.Float) { new NbtFloat(2), new NbtFloat(0.085f) },
            });
            dim.SaveChunkNbt(at, nbt);
        }
        var target = TargetCatalog.All.First(t => t.Kind == TargetKind.LegacyConsole && t.Platform == MinecraftStudio.Core.LegacyConsole.ConsolePlatform.PS4);
        string outDir = Path.Combine(temp, "ps4");
        try
        {
            ConversionReport report;
            using (var w = WorldLoader.Open(copy))
                report = WorldConverter.Run(w, target, outDir, new ConversionOptions { Dimensions = ["minecraft:overworld"], ConsoleWorldChunks = 16 }, new Progress<ConversionProgress>(_ => { }));
            using var o = WorldLoader.Open(report.OpenPath ?? outDir);
            var dim = o.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
            // The console world is centred on the spawn's chunk.
            var outAt = new ChunkPos(0, 0);
            var raw = dim.LoadChunkNbt(outAt);
            var list = ((raw?.GetCompound("Level") ?? raw)?.GetList("Entities")?.OfType<NbtCompound>() ?? []).ToList();
            var villager = list.FirstOrDefault(e => e.GetString("id") == "Zombie" && e.GetBool("IsVillager"));
            var armed = list.FirstOrDefault(e => e.GetString("id") == "Zombie" && !e.GetBool("IsVillager") && (e.ContainsKey("Equipment") || e.ContainsKey("HandItems")));
            Program.Check(villager != null && villager.GetInt("VillagerProfession") == 3, $"PS4: the zombie villager stored the old way, IsVillager and VillagerProfession 3 ({(villager == null ? "not found" : Show(villager))})");
            Program.Check(armed?.GetList("Equipment") is { Count: 5 } && armed.GetList("HandItems") is { Count: 2 } && armed.GetList("DropChances") is { Count: 5 } dc && dc[0].AsDouble == 2
                && armed.GetList("HandDropChances") is { Count: 2 },
                "PS4: the armed zombie's gear and drop chances in both forms (Equipment/DropChances and HandItems/HandDropChances)");
            var canonical = new CanonicalReader(o, dim).Get(outAt)?.Entities ?? [];
            var cv = canonical.FirstOrDefault(e => e.GetString("id") == "minecraft:zombie_villager");
            var cz = canonical.FirstOrDefault(e => e.GetString("id") == "minecraft:zombie" && e.GetCompound("equipment") != null);
            Program.Check(cv?.GetCompound("VillagerData")?.GetString("profession") == "minecraft:armorer"
                && cz?.GetCompound("equipment")?.GetCompound("mainhand")?.GetString("id") == "minecraft:iron_sword" && cz.GetCompound("equipment")?.GetCompound("head") != null
                && cz.GetCompound("drop_chances")?.GetDouble("mainhand") == 2,
                "PS4 → read again: still an armorer zombie villager, still a zombie with its sword and helmet (always dropping the sword)");
        }
        catch (Exception ex) { Program.Check(false, $"PS4: threw {ex.GetType().Name}: {ex.Message}"); }
        try { Directory.Delete(temp, true); } catch (IOException) { }
    }

    static void Converted()
    {
        string source = Path.Combine(TestPaths.Data, "amulet", "java", "forge", "1_12_2_twilight_forest");
        if (!Directory.Exists(source)) { Console.WriteLine("   (not found: skipped)"); return; }
        string temp = Path.Combine(TestPaths.Output, "variants", DateTime.Now.ToString("HHmmss"));
        foreach (var (name, kind, want) in new[]
        {
            ("1.16.2 – 1.16.5", TargetKind.Java, "minecraft:zombie_villager VillagerData"), ("1.12 – 1.12.2", TargetKind.Java, "minecraft:zombie_villager Profession=3"),
            ("1.10 – 1.10.2", TargetKind.Java, "Zombie ZombieType=4"), ("1.8 – 1.8.9", TargetKind.Java, "Zombie IsVillager=1"), ("1.21.120 – 26.x", TargetKind.Bedrock, ""),
        })
        {
            var target = TargetCatalog.All.First(t => t.Kind == kind && t.Name.EndsWith(name, StringComparison.Ordinal));
            string outDir = Path.Combine(temp, string.Concat(target.Name.Where(char.IsLetterOrDigit)));
            try
            {
                ConversionReport report;
                using (var w = WorldLoader.Open(source))
                    report = WorldConverter.Run(w, target, outDir, new ConversionOptions { Dimensions = ["minecraft:overworld"], Area = (-3, 9, 1, 13) }, new Progress<ConversionProgress>(_ => { }));
                using var o = WorldLoader.Open(report.OpenPath ?? outDir);
                var dim = o.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
                var at = new ChunkPos(-1, 11);
                var canonical = new CanonicalReader(o, dim).Get(at)?.Entities.FirstOrDefault(e => e.GetString("id") == "minecraft:zombie_villager");
                string job = canonical?.GetCompound("VillagerData")?.GetString("profession") ?? "(none)";
                // Java 1.8 keeps no job for zombie villagers (cured, they get one at random).
                string wantJob = name == "1.8 – 1.8.9" ? "minecraft:none" : "minecraft:armorer";
                Program.Check(canonical != null && job == wantJob, $"{target.Name}: the zombie villager reads back as one, job {job} (want {wantJob})");
                if (kind == TargetKind.Java)
                {
                    var raw = dim.LoadChunkNbt(at);
                    var list = ((raw?.GetCompound("Level") ?? raw)?.GetList("Entities")?.OfType<NbtCompound>() ?? []).ToList();
                    // 1.17+ keeps entities in their own files; the 1.16 target still in the chunk.
                    var stored = list.FirstOrDefault(e => (e.GetString("id") ?? "").Contains("ombie", StringComparison.Ordinal) && (e.ContainsKey("VillagerData") || e.ContainsKey("Profession") || e.ContainsKey("ZombieType") || e.ContainsKey("IsVillager")));
                    Program.Check(Show(stored) == want, $"{target.Name}: stored as {want} (got {Show(stored)})");
                }
            }
            catch (Exception ex) { Program.Check(false, $"{target.Name}: threw {ex.GetType().Name}: {ex.Message}"); }
        }
        try { Directory.Delete(temp, true); } catch (IOException) { }
    }
}
