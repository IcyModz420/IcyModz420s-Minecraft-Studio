// McTests/EntityFieldTests.cs: "fields" — the entity fields Java 1.21.5 renamed, read either way and written in the target's.
// Owns: EntityFieldTests — a zombie (drop chances, fall distance, bed), a phantom (size, anchor), a turtle (home, egg) and a
// vex (lifetime, bounds) in 1.21.4 form become the 1.21.5 names, are written unchanged for 1.21.5+, and come back for
// 1.21.4 exactly as they were; 1.21.5 form reads the same; a rider is converted with its mount; Bedrock fall distance.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;

static class EntityFieldTests
{
    public static void Run(string[] args)
    {
        static NbtList Floats(params float[] v) { var l = new NbtList(NbtType.Float); foreach (var f in v) l.Add(new NbtFloat(f)); return l; }
        static NbtList Pos() => new(NbtType.Double) { new NbtDouble(1.5), new NbtDouble(64), new NbtDouble(2.5) };
        var zombie = new NbtCompound
        {
            { "id", new NbtString("minecraft:zombie") }, { "Pos", Pos() }, { "HandDropChances", Floats(0.085f, 2.0f) }, { "ArmorDropChances", Floats(0.085f, 0.085f, 0.085f, 1.0f) },
            { "FallDistance", new NbtFloat(3.5f) }, { "SleepingX", new NbtInt(10) }, { "SleepingY", new NbtInt(65) }, { "SleepingZ", new NbtInt(-4) },
        };
        var phantom = new NbtCompound { { "id", new NbtString("minecraft:phantom") }, { "Pos", Pos() }, { "Size", new NbtInt(3) }, { "AX", new NbtInt(5) }, { "AY", new NbtInt(90) }, { "AZ", new NbtInt(6) } };
        var turtle = new NbtCompound { { "id", new NbtString("minecraft:turtle") }, { "Pos", Pos() }, { "HomePosX", new NbtInt(100) }, { "HomePosY", new NbtInt(62) }, { "HomePosZ", new NbtInt(200) }, { "HasEgg", new NbtByte(1) } };
        var vex = new NbtCompound { { "id", new NbtString("minecraft:vex") }, { "Pos", Pos() }, { "LifeTicks", new NbtInt(400) }, { "BoundX", new NbtInt(1) }, { "BoundY", new NbtInt(2) }, { "BoundZ", new NbtInt(3) } };
        zombie["Passengers"] = new NbtList(NbtType.Compound) { phantom.Clone() };

        var cz = Entities.FromJava(zombie, 4189);
        var drops = cz.GetCompound("drop_chances");
        Program.Check(drops != null && drops.Count == 2 && Math.Abs(drops.GetDouble("offhand") - 2.0) < 1e-6 && Math.Abs(drops.GetDouble("head") - 1.0) < 1e-6
            && !cz.ContainsKey("HandDropChances") && !cz.ContainsKey("ArmorDropChances"), $"1.21.4 drop chances become drop_chances with only the unusual ones ({drops})");
        Program.Check(cz["fall_distance"] is NbtDouble { Value: 3.5 } && !cz.ContainsKey("FallDistance"), "FallDistance (float) becomes fall_distance (double)");
        Program.Check(cz["sleeping_pos"] is NbtIntArray { Value: [10, 65, -4] } && !cz.ContainsKey("SleepingX"), "SleepingX/Y/Z become sleeping_pos");
        var rider = cz.GetList("Passengers")?.OfType<NbtCompound>().FirstOrDefault();
        Program.Check(rider?["size"] is NbtInt { Value: 3 } && rider["anchor_pos"] is NbtIntArray { Value: [5, 90, 6] }, "a rider (a phantom on the zombie) is converted too: size and anchor_pos");
        var ct = Entities.FromJava(turtle, 4189);
        Program.Check(ct["home_pos"] is NbtIntArray { Value: [100, 62, 200] } && ct["has_egg"] is NbtByte { Value: 1 } && !ct.ContainsKey("HasEgg"), "turtle: home_pos and has_egg");
        var cv = Entities.FromJava(vex, 4189);
        Program.Check(cv["life_ticks"] is NbtInt { Value: 400 } && cv["bound_pos"] is NbtIntArray { Value: [1, 2, 3] }, "vex: life_ticks and bound_pos");

        // Written for 1.21.5+: the new names as they are; for 1.21.4: exactly the old fields back.
        var modern = Entities.ToJava(cz, 4786)!;
        Program.Check(modern.ContainsKey("drop_chances") && modern["fall_distance"] is NbtDouble && modern.ContainsKey("sleeping_pos") && !modern.ContainsKey("FallDistance"),
            "for 26.1 the 1.21.5 names are written");
        foreach (var (label, original) in new[] { ("zombie", zombie), ("phantom", phantom), ("turtle", turtle), ("vex", vex) })
        {
            var back = Entities.ToJava(Entities.FromJava(original, 4189), 4189)!;
            bool same = true;
            foreach (var key in original.Keys.Where(k => k is not ("Passengers" or "id" or "Pos")))
                if (back[key] is not NbtTag t || !Same(t, original[key]!)) { same = false; Program.Info($"   {label}: {key} came back as {(back[key]?.ToString() ?? "nothing")} (was {original[key]})"); }
            Program.Check(same && !back.ContainsKey("drop_chances") && !back.ContainsKey("fall_distance"), $"{label}: 1.21.4 → canonical → 1.21.4 gives the same fields back");
        }
        // Already in 1.21.5 form: read as it is.
        var newForm = new NbtCompound { { "id", new NbtString("minecraft:skeleton") }, { "Pos", Pos() }, { "drop_chances", new NbtCompound { { "mainhand", new NbtFloat(0.5f) } } }, { "fall_distance", new NbtDouble(1.25) } };
        var cn = Entities.FromJava(newForm, 4325);
        Program.Check(cn.GetCompound("drop_chances")?.GetDouble("mainhand") is > 0.49 and < 0.51 && cn["fall_distance"] is NbtDouble { Value: 1.25 }, "1.21.5 form reads as it is");
        var old = Entities.ToJava(cn, 3953)!;
        Program.Check(old.GetList("HandDropChances") is { Count: 2 } h && Math.Abs(h[0].AsDouble - 0.5) < 1e-6 && Math.Abs(h[1].AsDouble - 0.085) < 1e-6 && old.GetList("ArmorDropChances")?.Count == 4 && old["FallDistance"] is NbtFloat,
            "for 1.21 the chances go back into the two lists (the usual chance where none was given) and FallDistance is a float");
        // Bedrock keeps FallDistance; canonical has fall_distance.
        var be = Entities.FromBedrock(new NbtCompound { { "identifier", new NbtString("minecraft:zombie") }, { "FallDistance", new NbtFloat(2f) }, { "Pos", new NbtList(NbtType.Float) { new NbtFloat(0), new NbtFloat(64), new NbtFloat(0) } } })!;
        var toBe = Entities.ToBedrock(be, 1)!;
        Program.Check(be["fall_distance"] is NbtDouble { Value: 2 } && toBe["FallDistance"] is NbtFloat { Value: 2f }, "Bedrock's FallDistance is fall_distance in canonical, and FallDistance again for Bedrock");
    }

    static bool Same(NbtTag a, NbtTag b) => a.ContentEquals(b) || a.IsNumeric && b.IsNumeric && Math.Abs(a.AsDouble - b.AsDouble) < 1e-6;
}
