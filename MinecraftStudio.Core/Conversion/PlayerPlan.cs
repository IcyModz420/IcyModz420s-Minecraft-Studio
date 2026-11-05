// MinecraftStudio.Core/Conversion/PlayerPlan.cs: Decides where every player's things go before anything is
// written. The host becomes the target's own player where the target allows it.
// Owns: PlayerPlan.
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Decides where every player's things go before anything is written. The host becomes the target's own player where
/// the target allows it. Players the target cannot link to an account, and slots the version lacks (off-hand before
/// 1.9, ender chest before 1.3, armour and ender chest on Pocket Edition 0.x), go into rescue chests at the spawn.
/// Nothing a player carries is dropped without being reported.
/// </summary>
static class PlayerPlan
{
    public static void Prepare(SaveWorld source, ConversionTarget target, WorldInfo info, ConversionOptions options, ConversionReport report)
    {
        if (info.Players.Count == 0) return;
        if (!options.ConvertPlayers)
        {
            ConversionLog.Note(LossKind.Player, "Players", "not converted: turned off in the options", info.Players.Count, lost: true, where: "");
            // What they rode, the parrots on their shoulders and their thrown pearls are part of the world: they stay.
            for (int i = 0; i < info.Players.Count; i++) LetGo(info.Players[i], Who(i, info.Players[i]), info, target, options, kept: false);
            return;
        }
        bool hostNative = target.Kind switch
        {
            TargetKind.Java or TargetKind.Bedrock or TargetKind.PocketLegacy => true,
            TargetKind.LegacyConsole => ConsoleWorldWriter.HostPlayerFile(info, options) != null,
            _ => false,
        };
        // Java keeps every player by UUID; from other editions the UUIDs are made up, so only the host gets their data back.
        bool othersNative = target.Kind == TargetKind.Java && source is JavaWorld;
        report.HostWritten = hostNative;
        for (int i = 0; i < info.Players.Count; i++)
        {
            var p = info.Players[i];
            string who = Who(i, p);
            string label = i == 0 ? "Your items" : $"Items of player {ShortName(p)}";
            bool native = i == 0 ? hostNative : othersNative;
            // What lives only in their file stays with them where the target keeps it for them; the rest goes into the world.
            LetGo(p, who, info, target, options, kept: native);
            if (!native)
            {
                var items = Take(p.Data, "Inventory").Concat(Take(p.Data, "EnderItems")).ToList();
                string why = i == 0
                    ? target.Kind == TargetKind.LegacyConsole
                        ? "a console only loads a player saved under its owner's id; choose one of your own saves as the template to keep your player"
                        : $"{target.Name} has no player data"
                    : target.Kind == TargetKind.Bedrock || target.Kind == TargetKind.LegacyConsole
                        ? $"{target.Name} links players to accounts the converter cannot know"
                        : "their UUID only exists in the source edition";
                if (items.Count == 0) { ConversionLog.Note(LossKind.Player, who, why + "; they carried no items", where: ""); continue; }
                bool rescued = ConversionLog.Rescue(label, items);
                ConversionLog.Note(LossKind.Player, who, why + (rescued ? $"; their {items.Count} item stacks are in a rescue chest at the spawn" : $"; their {items.Count} item stacks were lost"),
                    items.Count, lost: !rescued, where: "");
                continue;
            }
            if (!HasOffhand(target) && TakeSlot(p.Data, -106) is NbtCompound off)
            {
                if (FreeSlot(p.Data) is int free)
                {
                    off.SetByte("Slot", (sbyte)free);
                    ((NbtList)p.Data["Inventory"]!).Add(off);
                    ConversionLog.Note(LossKind.Player, who, $"off-hand item moved into inventory slot {free}: {target.Name} has no off-hand", where: "");
                }
                else Rescue(who, label, [off], $"{target.Name} has no off-hand and the inventory is full");
            }
            if (!HasArmorSlots(target))
            {
                var armor = new List<NbtCompound>();
                foreach (int slot in new[] { 100, 101, 102, 103 })
                    if (TakeSlot(p.Data, slot) is NbtCompound a) armor.Add(a);
                if (armor.Count > 0) Rescue(who, label, armor, $"{target.Name} keeps no armour on the player");
            }
            if (!HasEnderChest(target) && Take(p.Data, "EnderItems") is { Count: > 0 } ender)
                Rescue(who, label + " (ender chest)", ender, $"{target.Name} has no ender chest");
        }
    }

    static void Rescue(string who, string label, List<NbtCompound> items, string why)
    {
        bool rescued = ConversionLog.Rescue(label, items);
        ConversionLog.Note(LossKind.Player, who, why + (rescued ? $"; {items.Count} item stack(s) are in a rescue chest at the spawn" : $"; {items.Count} item stack(s) were lost"),
            items.Count, lost: !rescued, where: "");
    }

    static string Who(int index, CanonicalPlayer p) => index == 0 ? "Your player" : "Player " + ShortName(p);

    /// <summary>
    /// What lives only in a Java player's file (what they ride, the parrots on their shoulders, their thrown ender pearls)
    /// stays there where the target keeps it for this player (<paramref name="kept"/>: written as themselves); the rest is
    /// put into the world where it is: a horse keeps its saddle, armour and chest, a parrot stands beside its owner, a pearl
    /// in a stasis chamber stays in it.
    /// </summary>
    static void LetGo(CanonicalPlayer p, string who, WorldInfo info, ConversionTarget target, ConversionOptions options, bool kept)
    {
        var d = p.Data;
        int dv = kept && target.Kind == TargetKind.Java ? JavaLevelData.LevelDataVersion(target) : -1;
        if (d.GetCompound("RootVehicle") is NbtCompound rv && !(dv >= Players.MountsKept && Seated(d)))
        {
            d.Remove("RootVehicle");
            if (rv.GetCompound("Entity") is NbtCompound mount)
                Place(mount, p.Dimension, $"{who} was riding it", $"it stands in the world where they were ({target.Name} can't seat them on it again)", info, options);
        }
        foreach (var key in Players.Shoulders)
            if (d.GetCompound(key) is NbtCompound bird && dv < Players.ShouldersKept)
            {
                d.Remove(key);
                // Beside its owner, a little above their feet.
                if (d.GetList("Pos") is { Count: 3 } at)
                    bird["Pos"] = new NbtList(NbtType.Double) { new NbtDouble(at[0].AsDouble), new NbtDouble(at[1].AsDouble + 1), new NbtDouble(at[2].AsDouble) };
                Place(bird, p.Dimension, $"{who} had it on their shoulder", $"it is in the world beside them ({target.Name} keeps no parrot on a player)", info, options);
            }
        if (d.GetList("ender_pearls") is { Count: > 0 } pearls && dv < Players.PearlsKept)
        {
            d.Remove("ender_pearls");
            foreach (var pearl in pearls.OfType<NbtCompound>())
            {
                string dimension = pearl.GetString("ender_pearl_dimension") ?? p.Dimension;
                pearl.Remove("ender_pearl_dimension");
                Place(pearl, dimension, $"{who} had thrown it", "it is in the world where it was, and lands as before", info, options);
            }
        }
    }

    // Into the world where it is (WorldInfo.LooseEntities, added to its chunk as that is read), said in the report.
    static void Place(NbtCompound e, string dimension, string whose, string outcome, WorldInfo info, ConversionOptions options)
    {
        string what = ConversionLog.Friendly(e.GetString("id") ?? "entity");
        if (!options.ConvertEntities)
        {
            ConversionLog.Note(LossKind.Entity, what, $"{whose}; not converted: entities are turned off in the options", lost: true, where: "");
            return;
        }
        info.LooseEntities.Add((dimension, e));
        ConversionLog.Note(LossKind.Entity, what, $"{whose}: {outcome}", where: "");
    }

    // The game seats them again only on the mount itself or one of its riders (Attach); otherwise it throws the mount away.
    static bool Seated(NbtCompound player)
    {
        if (player.GetCompound("RootVehicle") is not { } rv || rv["Attach"] is not NbtIntArray { Value.Length: 4 } attach || rv.GetCompound("Entity") is not { } mount) return false;
        bool Has(NbtCompound e) => e["UUID"] is NbtIntArray { Value.Length: 4 } u && u.Value.AsSpan().SequenceEqual(attach.Value)
            || e.GetList("Passengers")?.OfType<NbtCompound>().Any(Has) == true;
        return Has(mount);
    }

    static string ShortName(CanonicalPlayer p)
    {
        string n = p.Name.Replace("player_server_", "").Replace("players/", "").Replace("players_", "");
        return n.Length > 12 ? n[..8] : n;
    }

    static bool HasOffhand(ConversionTarget t) => t.Kind switch
    {
        TargetKind.Java => t.DataVersion >= 169,
        TargetKind.Bedrock => t.BedrockTerrain != BedrockTerrain.LegacyTerrain,
        _ => false,
    };

    static bool HasEnderChest(ConversionTarget t) => t.Kind switch
    {
        TargetKind.Java => t.DataVersion >= 100 || t.LegacyLevel >= 1.03,
        TargetKind.Bedrock or TargetKind.LegacyConsole => t.BedrockTerrain != BedrockTerrain.LegacyTerrain,
        _ => false,
    };

    static bool HasArmorSlots(ConversionTarget t) => t.Kind != TargetKind.PocketLegacy;

    /// <summary>Removes and returns every item of a list.</summary>
    static List<NbtCompound> Take(NbtCompound player, string key)
    {
        var items = player.GetList(key)?.OfType<NbtCompound>().Where(i => !ConversionLog.IsEmptyItem(i)).ToList() ?? [];
        if (items.Count > 0) player[key] = new NbtList(NbtType.Compound);
        return items;
    }

    static NbtCompound? TakeSlot(NbtCompound player, int slot)
    {
        if (player.GetList("Inventory") is not NbtList inv) return null;
        for (int i = 0; i < inv.Count; i++)
            if (inv[i] is NbtCompound it && it.GetInt("Slot") == slot && !ConversionLog.IsEmptyItem(it))
            {
                inv.RemoveAt(i);
                return it;
            }
        return null;
    }

    static int? FreeSlot(NbtCompound player)
    {
        var used = player.GetList("Inventory")?.OfType<NbtCompound>().Select(i => i.GetInt("Slot")).ToHashSet() ?? [];
        for (int s = 0; s < 36; s++) if (!used.Contains(s)) return s;
        return null;
    }
}
