// MinecraftStudio.Core/Conversion/EntityFields.cs: entity fields Java 1.21.5 renamed or merged — read either way, written
// in the target's.
// Owns: EntityFields. Canonical uses the 1.21.5 names (as it does for equipment): drop_chances (HandDropChances,
// ArmorDropChances and body_armor_drop_chance before), fall_distance as a double (FallDistance float), sleeping_pos
// (SleepingX/Y/Z), the phantom's size and anchor_pos (Size, AX/AY/AZ), the turtle's home_pos and has_egg (HomePosX/Y/Z,
// HasEgg), the vex's life_ticks and bound_pos (LifeTicks, BoundX/Y/Z). Targets before 1.21.5 get the old names back.
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public static class EntityFields
{
    /// <summary>The Java data version (1.21.5) that renamed them.</summary>
    public const int Since = 4325;

    /// <summary>The chance a mob drops what it wears or holds, when nothing says otherwise.</summary>
    const float DefaultChance = 0.085f;

    static readonly string[] Hands = ["mainhand", "offhand"];
    static readonly string[] Armor = ["feet", "legs", "chest", "head"];

    /// <summary>Old names → canonical (1.21.5) on <paramref name="c"/>, the copy being made canonical.</summary>
    public static void FromJava(NbtCompound c)
    {
        string id = c.GetString("id") ?? "";
        // Before 1.9 one list of five: what it holds, then feet to head.
        if (c.GetList("DropChances") is NbtList old)
        {
            var map = c.GetCompound("drop_chances") ?? new NbtCompound();
            for (int i = 0; i < Math.Min(old.Count, Entities.OldEquipment.Length); i++)
                if (old[i].IsNumeric && (float)old[i].AsDouble is float f && f != DefaultChance) map.SetFloat(Entities.OldEquipment[i], f);
            c.Remove("DropChances");
            if (map.Count > 0) c["drop_chances"] = map;
        }
        // Drop chances: two lists and a float become one map of the chances that are not the usual one.
        if (c.GetList("HandDropChances") is not null || c.GetList("ArmorDropChances") is not null || c.ContainsKey("body_armor_drop_chance"))
        {
            var map = c.GetCompound("drop_chances") ?? new NbtCompound();
            void Take(NbtList? list, string[] slots)
            {
                if (list == null) return;
                for (int i = 0; i < Math.Min(list.Count, slots.Length); i++)
                    if (list[i].IsNumeric && (float)list[i].AsDouble is float f && f != DefaultChance) map.SetFloat(slots[i], f);
            }
            Take(c.GetList("HandDropChances"), Hands);
            Take(c.GetList("ArmorDropChances"), Armor);
            if (c["body_armor_drop_chance"] is { IsNumeric: true } body && (float)body.AsDouble != DefaultChance) map.SetFloat("body", (float)body.AsDouble);
            c.Remove("HandDropChances"); c.Remove("ArmorDropChances"); c.Remove("body_armor_drop_chance");
            if (map.Count > 0) c["drop_chances"] = map;
        }
        if (c["FallDistance"] is { IsNumeric: true } fall)
        {
            if (!c.ContainsKey("fall_distance")) c.SetDouble("fall_distance", fall.AsDouble);
            c.Remove("FallDistance");
        }
        Merge(c, "SleepingX", "SleepingY", "SleepingZ", "sleeping_pos");
        if (id == "minecraft:phantom")
        {
            Rename(c, "Size", "size");
            Merge(c, "AX", "AY", "AZ", "anchor_pos");
        }
        if (id == "minecraft:turtle")
        {
            Merge(c, "HomePosX", "HomePosY", "HomePosZ", "home_pos");
            Rename(c, "HasEgg", "has_egg");
        }
        if (id == "minecraft:vex")
        {
            Rename(c, "LifeTicks", "life_ticks");
            Merge(c, "BoundX", "BoundY", "BoundZ", "bound_pos");
        }
    }

    /// <summary>Canonical → the names of Java data version <paramref name="dv"/>, on <paramref name="c"/> (the output copy).</summary>
    public static void ToJava(NbtCompound c, int dv)
    {
        if (dv >= Since)
        {
            // Canonical already has the new names; a leftover old one (from an older canonical) is renamed too.
            FromJava(c);
            return;
        }
        string id = c.GetString("id") ?? "";
        if (c.GetCompound("drop_chances") is NbtCompound map)
        {
            float Get(string slot) => map[slot] is { IsNumeric: true } v ? (float)v.AsDouble : DefaultChance;
            var hands = new NbtList(NbtType.Float);
            foreach (var s in Hands) hands.Add(new NbtFloat(Get(s)));
            var armor = new NbtList(NbtType.Float);
            foreach (var s in Armor) armor.Add(new NbtFloat(Get(s)));
            // A console save gets both forms (see Entities.EquipmentToJava).
            bool console = ConversionLog.Current?.Target.Kind == TargetKind.LegacyConsole;
            if (dv < Entities.HandsAndArmor)
            {
                // Before 1.9 one list of five: what it holds, then feet to head.
                var all = new NbtList(NbtType.Float);
                foreach (var s in Entities.OldEquipment) all.Add(new NbtFloat(Get(s)));
                c["DropChances"] = all;
            }
            if (dv >= Entities.HandsAndArmor || console)
            {
                c["HandDropChances"] = hands;
                c["ArmorDropChances"] = armor;
            }
            if (map.ContainsKey("body") && dv >= 3837) c.SetFloat("body_armor_drop_chance", Get("body"));
            c.Remove("drop_chances");
        }
        if (c["fall_distance"] is { IsNumeric: true } fall)
        {
            c.SetFloat("FallDistance", (float)fall.AsDouble);
            c.Remove("fall_distance");
        }
        Split(c, "sleeping_pos", "SleepingX", "SleepingY", "SleepingZ");
        if (id == "minecraft:phantom")
        {
            Rename(c, "size", "Size");
            Split(c, "anchor_pos", "AX", "AY", "AZ");
        }
        if (id == "minecraft:turtle")
        {
            Split(c, "home_pos", "HomePosX", "HomePosY", "HomePosZ");
            Rename(c, "has_egg", "HasEgg");
        }
        if (id == "minecraft:vex")
        {
            Rename(c, "life_ticks", "LifeTicks");
            Split(c, "bound_pos", "BoundX", "BoundY", "BoundZ");
        }
    }

    static void Rename(NbtCompound c, string from, string to)
    {
        if (c[from] is not NbtTag t) return;
        if (!c.ContainsKey(to)) c[to] = t;
        c.Remove(from);
    }

    static void Merge(NbtCompound c, string x, string y, string z, string to)
    {
        if (c[x] is { IsNumeric: true } a && c[y] is { IsNumeric: true } b && c[z] is { IsNumeric: true } d)
        {
            if (!c.ContainsKey(to)) c[to] = new NbtIntArray([(int)a.AsLong, (int)b.AsLong, (int)d.AsLong]);
            c.Remove(x); c.Remove(y); c.Remove(z);
        }
    }

    static void Split(NbtCompound c, string from, string x, string y, string z)
    {
        if (c[from] is not NbtIntArray { Value.Length: 3 } p) return;
        c.SetInt(x, p.Value[0]); c.SetInt(y, p.Value[1]); c.SetInt(z, p.Value[2]);
        c.Remove(from);
    }
}
