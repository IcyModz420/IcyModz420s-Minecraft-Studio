// MinecraftStudio.Core/Conversion/Entities.FromJava.cs: from java for Entities.
// Part of Entities (main part in Entities.cs).
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public static partial class Entities
{
    // ---------------------------------------------------------------- Java -> canonical

    public static NbtCompound FromJava(NbtCompound e, int dv)
    {
        // Before 1.9 a rider kept what it rode inside it ("Riding"); canonical, the one at the bottom keeps its riders.
        if (e["Riding"] is NbtCompound) return FromJava(RidersOnTop(e), dv);
        var c = (NbtCompound)e.Clone();
        string id = e.GetString("id") ?? "minecraft:pig";
        // Pre-1.11 variants stored as flags.
        if (id == "Skeleton" && e.GetInt("SkeletonType") is int st and > 0) id = st == 1 ? "wither_skeleton" : "stray";
        else if (id == "Zombie" && e.GetInt("ZombieType") is int zt and > 0) id = zt == 6 ? "husk" : "zombie_villager";
        else if (id == "Zombie" && e.GetBool("IsVillager")) id = "zombie_villager";
        else if (id == "EntityHorse") id = e.GetInt("Type") switch { 1 => "donkey", 2 => "mule", 3 => "zombie_horse", 4 => "skeleton_horse", _ => "horse" };
        else if (id == "Guardian" && e.GetBool("Elder")) id = "elder_guardian";
        id = CanonicalId(id);
        if (id is "minecraft:boat" or "minecraft:chest_boat")
        {
            string wood = e.GetString("Type") ?? "oak";
            id = $"minecraft:{wood}{(id.Contains("chest") ? "_chest" : "")}_{(wood == "bamboo" ? "raft" : "boat")}";
            c.Remove("Type");
        }
        c.SetString("id", id);
        // The type numbers of the kinds that split in 1.11 are in the kind now; a villager's old job numbers in VillagerData.
        switch (e.GetString("id"))
        {
            case "Skeleton": c.Remove("SkeletonType"); break;
            case "Zombie": c.Remove("ZombieType"); c.Remove("IsVillager"); c.Remove("VillagerProfession"); break;
            case "EntityHorse": c.Remove("Type"); break;
            case "Guardian": c.Remove("Elder"); break;
        }
        VillagerJobFromJava(c, e, id);
        // Paintings, frames and leash knots: art, facing and block in their canonical fields.
        Hanging.FromJava(c, e, dv);
        if (e.GetCompound("Item") is NbtCompound item && Items.FromJava(item, dv) is NbtCompound ci) c["Item"] = ci;
        if (e.GetList("Items") is NbtList items)
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var it in items.OfType<NbtCompound>())
                if (Items.FromJava(it, dv) is NbtCompound x) { x.SetByte("Slot", (sbyte)it.GetInt("Slot")); list.Add(x); }
            c["Items"] = list;
        }
        if (e["CustomName"] is NbtTag name) c["CustomName"] = new NbtString(TextComponents.FromJavaTag(name));
        NormalizeEquipment(c, dv);
        // Trades, mob inventories, thrown tridents and riders hold items and entities in the version's own format.
        if (e.GetCompound("Offers")?.GetList("Recipes") is NbtList offers)
        {
            var recipes = new NbtList(NbtType.Compound);
            foreach (var r in offers.OfType<NbtCompound>())
            {
                var copy = (NbtCompound)r.Clone();
                foreach (var key in new[] { "buy", "buyB", "sell" })
                {
                    copy.Remove(key);
                    if (r.GetCompound(key) is NbtCompound it && Items.FromJava(it, dv) is NbtCompound offerItem) copy[key] = offerItem;
                }
                if (copy.ContainsKey("buy") && copy.ContainsKey("sell")) recipes.Add(copy);
            }
            c["Offers"] = new NbtCompound { { "Recipes", recipes } };
        }
        if (e.GetList("Inventory") is NbtList inventory)
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var it in inventory.OfType<NbtCompound>())
                if (Items.FromJava(it, dv) is NbtCompound x) { if (it.ContainsKey("Slot")) x.SetByte("Slot", (sbyte)it.GetInt("Slot")); list.Add(x); }
            c["Inventory"] = list;
        }
        if ((e.GetCompound("Trident") ?? e.GetCompound("item")) is NbtCompound projectile && Items.FromJava(projectile, dv) is NbtCompound pi)
        {
            c.Remove("Trident");
            c["item"] = pi;
        }
        if (e.GetList("Passengers") is NbtList riders)
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var r in riders.OfType<NbtCompound>()) list.Add(FromJava(r, dv));
            c["Passengers"] = list;
        }
        // 1.12–1.15 kept the owner as a UUID string.
        if (e.GetString("OwnerUUID") is string ownerText && Guid.TryParse(ownerText, out var owner))
        {
            c["Owner"] = new NbtIntArray(Players.UuidToInts(owner));
            c.Remove("OwnerUUID");
        }
        // 1.14–1.18 numbered cat types; 1.19+ names them.
        if (id == "minecraft:cat" && e["CatType"] is { IsNumeric: true } catType)
        {
            c.SetString("variant", "minecraft:" + JavaCatTypes[Math.Clamp((int)catType.AsLong, 0, JavaCatTypes.Length - 1)]);
            c.Remove("CatType");
        }
        if (e.ContainsKey("HealF")) { c.SetFloat("Health", (float)e.GetDouble("HealF")); c.Remove("HealF"); }
        else if (e["Health"] is NbtShort hs) c.SetFloat("Health", hs.Value);
        if (!e.ContainsKey("UUID") && e.GetLongOrNull("UUIDMost") is long most && e.GetLongOrNull("UUIDLeast") is long least)
        {
            c["UUID"] = new NbtIntArray([(int)(most >> 32), (int)most, (int)(least >> 32), (int)least]);
            c.Remove("UUIDMost"); c.Remove("UUIDLeast");
        }
        // Potion effects by name (numbered before 1.20.2); the block an enderman carries under its newest name.
        Effects.FromJava(c, e);
        if (id == "minecraft:enderman") CarriedFromJava(c, e, dv);
        // Drop chances, fall distance, beds, phantom/turtle/vex fields under their 1.21.5 names.
        EntityFields.FromJava(c);
        return c;
    }
}
