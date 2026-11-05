// MinecraftStudio.Core/Conversion/Players.cs: Players across editions.
// Owns: Players.
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Players across editions. Canonical form = Java-style player compound: Pos/Motion (doubles, feet position),
/// Rotation, Health (float), foodLevel, foodSaturationLevel, XpLevel, XpP, XpTotal, SelectedItemSlot, playerGameType,
/// Inventory (canonical items; slots 0–8 hotbar, 9–35 storage, 100–103 armour feet→head, -106 off-hand), EnderItems,
/// abilities, and Dimension as a namespaced id.
/// </summary>
public static class Players
{
    const double BedrockEyeHeight = 1.62001;

    static NbtList Doubles(params double[] v) { var l = new NbtList(NbtType.Double); foreach (var d in v) l.Add(new NbtDouble(d)); return l; }
    static NbtList Floats(params float[] v) { var l = new NbtList(NbtType.Float); foreach (var d in v) l.Add(new NbtFloat(d)); return l; }

    static double[] Vec(NbtCompound e, string key, int n)
    {
        var r = new double[n];
        if (e.GetList(key) is NbtList l) for (int i = 0; i < n && i < l.Count; i++) r[i] = l[i].AsDouble;
        return r;
    }

    static string DimensionFromInt(long d) => d switch { -1 => "minecraft:the_nether", 1 => "minecraft:the_end", _ => "minecraft:overworld" };
    static string DimensionFromBedrock(long d) => d switch { 1 => "minecraft:the_nether", 2 => "minecraft:the_end", _ => "minecraft:overworld" };

    static NbtCompound Abilities(NbtCompound? a, int gameType)
    {
        var c = new NbtCompound();
        c.SetByte("invulnerable", (sbyte)(a?.GetBool("invulnerable") ?? gameType is 1 or 3 ? 1 : 0));
        c.SetByte("mayfly", (sbyte)(a?.GetBool("mayfly") ?? gameType is 1 or 3 ? 1 : 0));
        c.SetByte("instabuild", (sbyte)(a?.GetBool("instabuild") ?? gameType == 1 ? 1 : 0));
        c.SetByte("flying", (sbyte)(a?.GetBool("flying") == true ? 1 : 0));
        c.SetByte("mayBuild", (sbyte)(a?.GetBool("mayBuild", gameType != 2) ?? gameType != 2 ? 1 : 0));
        c.SetFloat("flySpeed", (float)(a?.GetDouble("flySpeed", 0.05) ?? 0.05));
        c.SetFloat("walkSpeed", (float)(a?.GetDouble("walkSpeed", 0.1) ?? 0.1));
        return c;
    }

    // ---------------------------------------------------------------- sources -> canonical

    public static CanonicalPlayer FromJava(string name, NbtCompound p, int dv)
    {
        var c = new NbtCompound();
        var pos = Vec(p, "Pos", 3);
        c["Pos"] = Doubles(pos);
        c["Motion"] = Doubles(Vec(p, "Motion", 3));
        var rot = Vec(p, "Rotation", 2);
        c["Rotation"] = Floats((float)rot[0], (float)rot[1]);
        c.SetFloat("Health", p["Health"] is NbtShort hs ? hs.Value : p.ContainsKey("HealF") ? (float)p.GetDouble("HealF") : (float)p.GetDouble("Health", 20));
        c.SetInt("foodLevel", p.GetInt("foodLevel", 20));
        c.SetFloat("foodSaturationLevel", (float)p.GetDouble("foodSaturationLevel", 5));
        c.SetInt("XpLevel", p.GetInt("XpLevel"));
        c.SetFloat("XpP", (float)p.GetDouble("XpP"));
        c.SetInt("XpTotal", p.GetInt("XpTotal"));
        c.SetInt("Score", p.GetInt("Score"));
        c.SetInt("SelectedItemSlot", p.GetInt("SelectedItemSlot"));
        c.SetInt("playerGameType", p.GetInt("playerGameType"));
        c.SetShort("Air", (short)p.GetInt("Air", 300));
        c.SetShort("Fire", (short)p.GetInt("Fire", -20));
        c.SetFloat("FallDistance", (float)p.GetDouble("FallDistance"));
        c.SetByte("OnGround", (sbyte)(p.GetBool("OnGround", true) ? 1 : 0));
        c["abilities"] = Abilities(p.GetCompound("abilities"), p.GetInt("playerGameType"));
        c["Inventory"] = ItemList(p.GetList("Inventory"), it => Items.FromJava(it, dv));
        // 1.21.5+: armour and off-hand moved to "equipment".
        if (p.GetCompound("equipment") is NbtCompound eq)
        {
            var inv = (NbtList)c["Inventory"]!;
            foreach (var (slot, key) in new[] { (100, "feet"), (101, "legs"), (102, "chest"), (103, "head"), (-106, "offhand") })
                if (eq.GetCompound(key) is NbtCompound it && Items.FromJava(it, dv) is NbtCompound ci) { ci.SetByte("Slot", (sbyte)slot); inv.Add(ci); }
        }
        c["EnderItems"] = ItemList(p.GetList("EnderItems"), it => Items.FromJava(it, dv));
        string dim = p["Dimension"] switch
        {
            NbtString s => s.Value,
            { IsNumeric: true } n => DimensionFromInt(n.AsLong),
            _ => "minecraft:overworld",
        };
        c.SetString("Dimension", dim);
        if (p.GetLongOrNull("SpawnX") is long sx)
        {
            c.SetInt("SpawnX", (int)sx); c.SetInt("SpawnY", p.GetInt("SpawnY")); c.SetInt("SpawnZ", p.GetInt("SpawnZ"));
            c.SetString("SpawnDimension", p.GetString("SpawnDimension") ?? "minecraft:overworld");
            c.SetByte("SpawnForced", (sbyte)(p.GetBool("SpawnForced") ? 1 : 0));
            c.SetFloat("SpawnAngle", (float)p.GetDouble("SpawnAngle"));
        }
        else if (p.GetCompound("respawn") is NbtCompound respawn && respawn.GetInts("pos") is { Length: 3 } rp)
        {
            // 1.21.5+: the respawn point is one compound.
            c.SetInt("SpawnX", rp[0]); c.SetInt("SpawnY", rp[1]); c.SetInt("SpawnZ", rp[2]);
            c.SetString("SpawnDimension", respawn.GetString("dimension") ?? "minecraft:overworld");
            c.SetByte("SpawnForced", (sbyte)(respawn.GetBool("forced") ? 1 : 0));
            c.SetFloat("SpawnAngle", (float)respawn.GetDouble("angle", respawn.GetDouble("yaw")));
        }
        // What they ride: kept only in their own file, and put back under them when they join. Since 1.9 the bottom one
        // (RootVehicle, with the seat they're in: Attach); before, what they sit on, holding what it rides ("Riding").
        if (p.GetCompound("RootVehicle") is NbtCompound rv && rv.GetCompound("Entity") is NbtCompound mount)
        {
            var root = new NbtCompound { ["Entity"] = Entities.FromJava(mount, dv) };
            if (rv["Attach"] is NbtIntArray { Value.Length: 4 } attach) root["Attach"] = new NbtIntArray((int[])attach.Value.Clone());
            else if (rv.GetLongOrNull("AttachMost") is long am && rv.GetLongOrNull("AttachLeast") is long al)
                root["Attach"] = new NbtIntArray([(int)(am >> 32), (int)am, (int)(al >> 32), (int)al]);
            c["RootVehicle"] = root;
        }
        else if (p.GetCompound("Riding") is NbtCompound seat)
        {
            var root = new NbtCompound { ["Entity"] = Entities.FromJava(seat, dv) };
            if (seat["UUID"] is NbtIntArray { Value.Length: 4 } su) root["Attach"] = new NbtIntArray((int[])su.Value.Clone());
            else if (seat.GetLongOrNull("UUIDMost") is long sm && seat.GetLongOrNull("UUIDLeast") is long sl)
                root["Attach"] = new NbtIntArray([(int)(sm >> 32), (int)sm, (int)(sl >> 32), (int)sl]);
            c["RootVehicle"] = root;
        }
        // The parrots on their shoulders (1.12+) and their thrown ender pearls (1.21.2+, each with its dimension) live
        // only here too.
        foreach (var key in Shoulders)
            if (p.GetCompound(key) is NbtCompound bird && bird.GetString("id") != null) c[key] = Entities.FromJava(bird, dv);
        if (p.GetList("ender_pearls") is { Count: > 0 } pearls)
        {
            var thrown = new NbtList(NbtType.Compound);
            foreach (var pearl in pearls.OfType<NbtCompound>()) thrown.Add(Entities.FromJava(pearl, dv));
            c["ender_pearls"] = thrown;
        }
        // Potion effects by name (numbered before 1.20.2).
        Effects.FromJava(c, p);
        // What else the game keeps about them, in the same form since it came (Java targets get it back).
        foreach (var (key, _) in KeptAsIs)
            if (p[key] is NbtTag kept) c[key] = kept.Clone();
        var player = new CanonicalPlayer { Name = name, Data = c, Dimension = dim };
        if (p["UUID"] is NbtIntArray { Value.Length: 4 } u) player.Uuid = UuidFromInts(u.Value);
        else if (p.GetLongOrNull("UUIDMost") is long most && p.GetLongOrNull("UUIDLeast") is long least)
            player.Uuid = UuidFromInts([(int)(most >> 32), (int)most, (int)(least >> 32), (int)least]);
        else if (Guid.TryParse(name, out var g)) player.Uuid = g;
        return player;
    }

    // Pocket's inventory: the links (0 - 8) left out, slots 9 - 44 as 0 - 35.
    static NbtList PocketInventory(NbtList? source)
    {
        var list = new NbtList(NbtType.Compound);
        foreach (var it in source?.OfType<NbtCompound>() ?? [])
        {
            int slot = it.GetInt("Slot");
            if (slot is < 9 or > 44 || Items.FromBedrock(it) is not NbtCompound c) continue;
            c.SetByte("Slot", (sbyte)(slot - 9));
            list.Add(c);
        }
        return list;
    }

    static NbtList ItemList(NbtList? source, Func<NbtCompound, NbtCompound?> convert)
    {
        var list = new NbtList(NbtType.Compound);
        foreach (var it in source?.OfType<NbtCompound>() ?? [])
        {
            if (convert(it) is not NbtCompound c) continue;
            c.SetByte("Slot", (sbyte)it.GetInt("Slot"));
            list.Add(c);
        }
        return list;
    }

    /// <summary>
    /// A Bedrock player as canonical. <paramref name="pocketSlots"/>: Pocket Edition 0.x and the 3DS number the inventory
    /// their own way — slots 0 - 8 are the hotbar's links to inventory slots (id 255, Damage the slot), the 36 inventory
    /// slots are 9 - 44 (the first nine the hotbar, as the links point at first) — read into Java's 0 - 35.
    /// </summary>
    public static CanonicalPlayer FromBedrock(string name, NbtCompound p, bool pocketSlots = false)
    {
        var c = new NbtCompound();
        var pos = Vec(p, "Pos", 3);
        c["Pos"] = Doubles(pos[0], pos[1] - BedrockEyeHeight, pos[2]);
        c["Motion"] = Doubles(Vec(p, "Motion", 3));
        var rot = Vec(p, "Rotation", 2);
        c["Rotation"] = Floats((float)rot[0], (float)rot[1]);
        float health = 20, hunger = 20, saturation = 5;
        foreach (var a in p.GetList("Attributes")?.OfType<NbtCompound>() ?? [])
            switch (a.GetString("Name"))
            {
                case "minecraft:health": health = (float)a.GetDouble("Current", 20); break;
                case "minecraft:player.hunger": hunger = (float)a.GetDouble("Current", 20); break;
                case "minecraft:player.saturation": saturation = (float)a.GetDouble("Current", 5); break;
            }
        c.SetFloat("Health", health);
        c.SetInt("foodLevel", (int)hunger);
        c.SetFloat("foodSaturationLevel", saturation);
        c.SetInt("XpLevel", p.GetInt("PlayerLevel"));
        c.SetFloat("XpP", (float)p.GetDouble("PlayerLevelProgress"));
        c.SetInt("SelectedItemSlot", p.GetInt("SelectedInventorySlot"));
        int mode = p.GetInt("PlayerGameMode");
        c.SetInt("playerGameType", mode is 0 or 1 or 2 ? mode : mode == 6 ? 3 : 0);
        c.SetShort("Air", (short)p.GetInt("Air", 300));
        c.SetFloat("FallDistance", (float)p.GetDouble("FallDistance"));
        c.SetByte("OnGround", (sbyte)(p.GetBool("OnGround", true) ? 1 : 0));
        c["abilities"] = Abilities(p.GetCompound("abilities"), mode);
        var inv = pocketSlots ? PocketInventory(p.GetList("Inventory")) : ItemList(p.GetList("Inventory"), Items.FromBedrock);
        // Bedrock armour list is head, chest, legs, feet.
        var armor = p.GetList("Armor")?.OfType<NbtCompound>().ToList() ?? [];
        for (int i = 0; i < armor.Count && i < 4; i++)
            if (Items.FromBedrock(armor[i]) is NbtCompound a) { a.SetByte("Slot", (sbyte)(103 - i)); inv.Add(a); }
        if (p.GetList("Offhand")?.OfType<NbtCompound>().FirstOrDefault() is NbtCompound off && Items.FromBedrock(off) is NbtCompound o)
        {
            o.SetByte("Slot", -106);
            inv.Add(o);
        }
        c["Inventory"] = inv;
        c["EnderItems"] = ItemList(p.GetList("EnderChestInventory"), Items.FromBedrock);
        string dim = DimensionFromBedrock(p.GetLong("DimensionId"));
        c.SetString("Dimension", dim);
        if (p.GetInt("SpawnY", int.MinValue) is int sy and > -1000 && sy != 32767)
        {
            c.SetInt("SpawnX", p.GetInt("SpawnX")); c.SetInt("SpawnY", sy); c.SetInt("SpawnZ", p.GetInt("SpawnZ"));
            c.SetString("SpawnDimension", DimensionFromBedrock(p.GetLong("SpawnDimension")));
        }
        // Pets point at their owner's UniqueID: the same id gives the same UUID on Java, so they stay with this player.
        var player = new CanonicalPlayer { Name = name, Data = c, Dimension = dim };
        if (p.GetLongOrNull("UniqueID") is long unique) player.Uuid = Entities.UuidForBedrockId(unique);
        return player;
    }

    // ---------------------------------------------------------------- canonical -> targets

    /// <summary>Java player compound in the layout of data version <paramref name="dv"/> (0 = pre-1.9 legacy).</summary>
    public static NbtCompound ToJava(CanonicalPlayer player, int dv, ConversionTarget t, bool? numericItemIds = null)
    {
        var p = player.Data;
        var c = new NbtCompound();
        c["Pos"] = Doubles(Vec(p, "Pos", 3));
        c["Motion"] = Doubles(Vec(p, "Motion", 3));
        var rot = Vec(p, "Rotation", 2);
        c["Rotation"] = Floats((float)rot[0], (float)rot[1]);
        c.SetByte("OnGround", (sbyte)(p.GetBool("OnGround", true) ? 1 : 0));
        c.SetFloat("FallDistance", (float)p.GetDouble("FallDistance"));
        c.SetShort("Fire", (short)p.GetInt("Fire", -20));
        c.SetShort("Air", (short)p.GetInt("Air", 300));
        float health = (float)p.GetDouble("Health", 20);
        bool legacy = dv < 1451;
        if (dv >= 169) c.SetFloat("Health", health);
        else c.SetShort("Health", (short)Math.Round(health));
        c.SetShort("AttackTime", 0); c.SetShort("HurtTime", 0); c.SetShort("DeathTime", 0);
        if (t.LegacyLevel >= LegacyLevel.Beta_1_8 || dv >= 100)
        {
            c.SetInt("foodLevel", p.GetInt("foodLevel", 20));
            c.SetFloat("foodSaturationLevel", (float)p.GetDouble("foodSaturationLevel", 5));
            c.SetFloat("foodExhaustionLevel", 0);
            c.SetInt("foodTickTimer", 0);
            c.SetInt("XpLevel", p.GetInt("XpLevel"));
            c.SetFloat("XpP", (float)p.GetDouble("XpP"));
            c.SetInt("XpTotal", p.GetInt("XpTotal"));
            c.SetInt("playerGameType", p.GetInt("playerGameType"));
        }
        c.SetInt("Score", p.GetInt("Score"));
        if (dv >= 100 || t.LegacyLevel >= 1.03) c["abilities"] = p.GetCompound("abilities")?.Clone() ?? Abilities(null, p.GetInt("playerGameType"));
        if (dv >= 100) c.SetInt("SelectedItemSlot", p.GetInt("SelectedItemSlot"));
        bool numericIds = numericItemIds ?? (dv < 100 && t.LegacyLevel < LegacyLevel.Release_1_8);
        NbtList Convert(NbtList? items, bool allowOffhand)
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var it in items?.OfType<NbtCompound>() ?? [])
            {
                int slot = it.GetInt("Slot");
                if (slot == -106 && !allowOffhand)
                {
                    ConversionLog.Note(LossKind.Player, player.Name, "off-hand item not kept: the version has no off-hand", lost: true);
                    continue;
                }
                var j = legacy ? Items.ToLegacyJava(it, dv, numericIds) : Items.ToJava(it, dv);
                j.SetByte("Slot", (sbyte)slot);
                list.Add(j);
            }
            return list;
        }
        c["Inventory"] = Convert(p.GetList("Inventory"), dv >= 169);
        if (dv >= 100 || t.LegacyLevel >= 1.03) c["EnderItems"] = Convert(p.GetList("EnderItems"), false);
        string dim = p.GetString("Dimension") ?? "minecraft:overworld";
        if (dv >= 2566) c.SetString("Dimension", dim);
        else if (t.LegacyLevel >= LegacyLevel.Alpha_1_2 || dv >= 100) c.SetInt("Dimension", dim switch { "minecraft:the_nether" => -1, "minecraft:the_end" => 1, _ => 0 });
        if (p.GetLongOrNull("SpawnX") is long sx)
        {
            string spawnDim = p.GetString("SpawnDimension") ?? "minecraft:overworld";
            if (dv >= 2566 || spawnDim == "minecraft:overworld")
            {
                c.SetInt("SpawnX", (int)sx); c.SetInt("SpawnY", p.GetInt("SpawnY")); c.SetInt("SpawnZ", p.GetInt("SpawnZ"));
                if (dv >= 2566)
                {
                    c.SetString("SpawnDimension", spawnDim);
                    c.SetByte("SpawnForced", (sbyte)(p.GetBool("SpawnForced") ? 1 : 0));
                    c.SetFloat("SpawnAngle", (float)p.GetDouble("SpawnAngle"));
                }
            }
            else ConversionLog.Note(LossKind.Player, player.Name, "respawn point outside the overworld not kept: versions before 1.16 respawn only there");
        }
        if (dv >= 2515) c["UUID"] = new NbtIntArray(UuidToInts(player.Uuid));
        else if (dv >= 100 || t.LegacyLevel >= 1.0706)
        {
            var ints = UuidToInts(player.Uuid);
            c.SetLong("UUIDMost", ((long)ints[0] << 32) | (uint)ints[1]);
            c.SetLong("UUIDLeast", ((long)ints[2] << 32) | (uint)ints[3]);
        }
        // What they ride, in the version's own form (PlayerPlan puts it into the world instead where it can't be kept here).
        if (dv >= MountsKept && p.GetCompound("RootVehicle") is NbtCompound rv && rv.GetCompound("Entity") is NbtCompound mount && Entities.ToJava(mount, dv) is NbtCompound vehicle)
        {
            var root = new NbtCompound();
            if (rv["Attach"] is NbtIntArray { Value.Length: 4 } attach)
            {
                var a = attach.Value;
                if (dv >= 2515) root["Attach"] = new NbtIntArray((int[])a.Clone());
                else
                {
                    root.SetLong("AttachMost", ((long)a[0] << 32) | (uint)a[1]);
                    root.SetLong("AttachLeast", ((long)a[2] << 32) | (uint)a[3]);
                }
            }
            root["Entity"] = vehicle;
            c["RootVehicle"] = root;
        }
        // Parrots on the shoulders and thrown pearls, where the version keeps them with the player (PlayerPlan puts them in
        // the world otherwise).
        if (dv >= ShouldersKept)
            foreach (var key in Shoulders)
                if (p.GetCompound(key) is NbtCompound bird && Entities.ToJava(bird, dv) is NbtCompound j) c[key] = j;
        if (dv >= PearlsKept && p.GetList("ender_pearls") is { Count: > 0 } pearls)
        {
            var thrown = new NbtList(NbtType.Compound);
            foreach (var pearl in pearls.OfType<NbtCompound>()) if (Entities.ToJava(pearl, dv) is NbtCompound j) thrown.Add(j);
            c["ender_pearls"] = thrown;
        }
        if (p.GetList("active_effects") is { Count: > 0 } effects)
        {
            c["active_effects"] = effects.Clone();
            Effects.ToJava(c, dv, player.Name);
        }
        foreach (var (key, since) in KeptAsIs)
            if (dv >= since && p[key] is NbtTag kept && !c.ContainsKey(key)) c[key] = kept.Clone();
        if (dv >= 100) c.SetInt("DataVersion", dv);
        return c;
    }

    /// <summary>The first Java data version (1.9) that keeps what a player rides in their own file (RootVehicle).</summary>
    public const int MountsKept = 169;
    /// <summary>The first Java data version (1.12) with parrots on a player's shoulders (ShoulderEntityLeft/Right).</summary>
    public const int ShouldersKept = 1139;
    /// <summary>The first Java data version (1.21.2) keeping a player's thrown ender pearls with them (ender_pearls).</summary>
    public const int PearlsKept = 4080;
    /// <summary>
    /// Player fields kept as they are, with the first data version having them: the recipe book, the end credits seen,
    /// where they last died (the recovery compass), the warden's warnings, the game mode before the last switch, the
    /// enchanting seed, where they entered the Nether. (Attributes are not: their names and layout changed twice.)
    /// </summary>
    static readonly (string Key, int Since)[] KeptAsIs =
    [
        ("recipeBook", 1139), ("seenCredits", 169), ("LastDeathLocation", 3105), ("warden_spawn_tracker", 3105),
        ("previousPlayerGameType", 2225), ("XpSeed", 0), ("enteredNetherPosition", 1952),
    ];

    /// <summary>The player's shoulders, where a parrot sits.</summary>
    public static readonly string[] Shoulders = ["ShoulderEntityLeft", "ShoulderEntityRight"];

    /// <summary>Bedrock player compound (~local_player / player_server_*).</summary>
    public static NbtCompound ToBedrock(CanonicalPlayer player, long uniqueId)
    {
        var p = player.Data;
        var c = new NbtCompound();
        c.SetString("identifier", "minecraft:player");
        var pos = Vec(p, "Pos", 3);
        c["Pos"] = Floats((float)pos[0], (float)(pos[1] + BedrockEyeHeight), (float)pos[2]);
        var motion = Vec(p, "Motion", 3);
        c["Motion"] = Floats((float)motion[0], (float)motion[1], (float)motion[2]);
        var rot = Vec(p, "Rotation", 2);
        c["Rotation"] = Floats((float)rot[0], (float)rot[1]);
        c.SetLong("UniqueID", uniqueId);
        c.SetByte("OnGround", (sbyte)(p.GetBool("OnGround", true) ? 1 : 0));
        c.SetFloat("FallDistance", (float)p.GetDouble("FallDistance"));
        c.SetShort("Air", (short)p.GetInt("Air", 300));
        c.SetInt("PlayerLevel", p.GetInt("XpLevel"));
        c.SetFloat("PlayerLevelProgress", (float)p.GetDouble("XpP"));
        c.SetInt("SelectedInventorySlot", p.GetInt("SelectedItemSlot"));
        c.SetInt("SelectedContainerId", 0);
        int mode = p.GetInt("playerGameType");
        c.SetInt("PlayerGameMode", mode == 3 ? 6 : mode);
        c.SetInt("DimensionId", (p.GetString("Dimension") ?? player.Dimension) switch { "minecraft:the_nether" => 1, "minecraft:the_end" => 2, _ => 0 });
        float health = (float)p.GetDouble("Health", 20);
        NbtCompound Attr(string name, float current, float max, float min = 0) => new()
        {
            { "Base", new NbtFloat(max) }, { "Current", new NbtFloat(current) }, { "DefaultMax", new NbtFloat(max) }, { "DefaultMin", new NbtFloat(min) },
            { "Max", new NbtFloat(max) }, { "Min", new NbtFloat(min) }, { "Name", new NbtString(name) },
        };
        c["Attributes"] = new NbtList
        {
            Attr("minecraft:health", health, 20), Attr("minecraft:player.hunger", p.GetInt("foodLevel", 20), 20),
            Attr("minecraft:player.saturation", (float)p.GetDouble("foodSaturationLevel", 5), 20), Attr("minecraft:player.exhaustion", 0, 5),
            Attr("minecraft:player.level", p.GetInt("XpLevel"), 24791), Attr("minecraft:player.experience", (float)p.GetDouble("XpP"), 1),
            Attr("minecraft:movement", 0.1f, 3.4028235E+38f),
        };
        var abilities = p.GetCompound("abilities");
        var ab = new NbtCompound
        {
            { "attackmobs", new NbtByte(true) }, { "attackplayers", new NbtByte(true) }, { "build", new NbtByte(abilities?.GetBool("mayBuild", true) ?? true) },
            { "doorsandswitches", new NbtByte(true) }, { "flySpeed", new NbtFloat((float)(abilities?.GetDouble("flySpeed", 0.05) ?? 0.05)) },
            { "flying", new NbtByte(abilities?.GetBool("flying") ?? false) }, { "instabuild", new NbtByte(abilities?.GetBool("instabuild") ?? mode == 1) },
            { "invulnerable", new NbtByte(abilities?.GetBool("invulnerable") ?? mode == 1) }, { "lightning", new NbtByte(false) },
            { "mayfly", new NbtByte(abilities?.GetBool("mayfly") ?? mode == 1) }, { "mine", new NbtByte(true) }, { "op", new NbtByte(false) },
            { "opencontainers", new NbtByte(true) }, { "teleport", new NbtByte(false) }, { "walkSpeed", new NbtFloat((float)(abilities?.GetDouble("walkSpeed", 0.1) ?? 0.1)) },
        };
        c["abilities"] = ab;
        var inventory = new NbtList(NbtType.Compound);
        var armor = new NbtCompound[4];
        NbtCompound? offhand = null;
        foreach (var it in p.GetList("Inventory")?.OfType<NbtCompound>() ?? [])
        {
            int slot = it.GetInt("Slot");
            var b = Items.ToBedrock(it);
            if (slot is >= 0 and <= 35) { b.SetByte("Slot", (sbyte)slot); inventory.Add(b); }
            else if (slot is >= 100 and <= 103) armor[103 - slot] = b;
            else if (slot == -106) offhand = b;
        }
        c["Inventory"] = inventory;
        var armorList = new NbtList(NbtType.Compound);
        foreach (var a in armor) armorList.Add(a ?? EmptyItem());
        c["Armor"] = armorList;
        c["Offhand"] = new NbtList(NbtType.Compound) { offhand ?? EmptyItem() };
        var ender = new NbtList(NbtType.Compound);
        foreach (var it in p.GetList("EnderItems")?.OfType<NbtCompound>() ?? [])
        {
            var b = Items.ToBedrock(it);
            b.SetByte("Slot", (sbyte)it.GetInt("Slot"));
            ender.Add(b);
        }
        c["EnderChestInventory"] = ender;
        if (p.GetLongOrNull("SpawnX") is long sx)
        {
            c.SetInt("SpawnX", (int)sx); c.SetInt("SpawnY", p.GetInt("SpawnY")); c.SetInt("SpawnZ", p.GetInt("SpawnZ"));
            c.SetInt("SpawnDimension", (p.GetString("SpawnDimension") ?? "minecraft:overworld") switch { "minecraft:the_nether" => 1, "minecraft:the_end" => 2, _ => 0 });
        }
        return c;
    }

    static NbtCompound EmptyItem() => new() { { "Count", new NbtByte(0) }, { "Damage", new NbtShort(0) }, { "Name", new NbtString("") }, { "WasPickedUp", new NbtByte(false) } };

    public static Guid UuidFromInts(int[] v)
    {
        var bytes = new byte[16];
        for (int i = 0; i < 4; i++)
        {
            bytes[i * 4] = (byte)(v[i] >> 24); bytes[i * 4 + 1] = (byte)(v[i] >> 16); bytes[i * 4 + 2] = (byte)(v[i] >> 8); bytes[i * 4 + 3] = (byte)v[i];
        }
        return new Guid(bytes, bigEndian: true);
    }

    public static int[] UuidToInts(Guid g)
    {
        var b = g.ToByteArray(bigEndian: true);
        var v = new int[4];
        for (int i = 0; i < 4; i++) v[i] = (b[i * 4] << 24) | (b[i * 4 + 1] << 16) | (b[i * 4 + 2] << 8) | b[i * 4 + 3];
        return v;
    }
}
