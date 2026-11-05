// MinecraftStudio.Core/World/InventoryTransfer.cs: a player's things into a player of another world — from any edition or
// console into Java, Bedrock or a console save — through the converter's own item forms.
// Owns: TransferResult and InventoryTransfer — WhyNotInto (a world items can be put into, or why not), Copy (every slot of
// the source player read the way the converter reads that edition's items, written in the target world's own form and
// version into the same slot of the target player — hotbar, storage, armour, off-hand, ender chest — by the Players page's
// inventory editor, which lays each record out as its version does; the parts the source has replace the target's, a
// part the source lacks is left as it was; what the target can't hold is said in the notes). The caller saves the target
// player (PlayerStore.Save), after a backup.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.Structures;

namespace MinecraftStudio.Core.World;

/// <summary>How many items went across, and what the target could not keep (each said once, with how often).</summary>
public sealed record TransferResult(int Items, List<string> Notes);

public static class InventoryTransfer
{
    /// <summary>Why items can't be put into this world, or null when they can.</summary>
    public static string? WhyNotInto(SaveWorld target) => target switch
    {
        { OpenedForRescue: true } => SaveWorld.RescueReadOnly,
        // A console save takes them as the converter writes a console player's (numbered or named ids, its own items).
        JavaWorld or BedrockWorld or ConsoleWorld => null,
        // Pocket 0.2 - 0.8 keeps its one player in level.dat, its inventory at slots 9 - 44 behind the hotbar's links.
        PocketLegacyWorld { Level: not null } => null,
        PocketLegacyWorld => "This Pocket Edition 0.1 world keeps no player to put items into. Items can come from any edition or console.",
        // Nothing is written into a 3DS world (a changed 3DS save is deleted by the game); items can come from one.
        ThreeDsWorld => "Items aren't put into 3DS worlds: the game deletes a 3DS world that was changed. They can come from one.",
        _ => "Items can be put into Java, Bedrock, console and Pocket Edition 0.x worlds; they can come from any edition or console.",
    };

    /// <summary>A source item as the converter's canonical item (Java's newest form), or null for one it can't read.</summary>
    public static NbtCompound? Canonical(SaveWorld source, NbtCompound raw, int dataVersion) => source switch
    {
        JavaWorld => Items.FromJava(raw, dataVersion),
        ConsoleWorld => Items.FromJava(raw, 1343),
        FiniteWorld => Items.FromJava(raw, 0),
        BedrockWorld or PocketLegacyWorld or ThreeDsWorld => Items.FromBedrock(raw),
        _ => null,
    };

    /// <summary>
    /// The target player's items replaced by the source player's (the parts the source has), written into
    /// <paramref name="to"/>'s record in the target world's own form; nothing is saved here.
    /// </summary>
    public static TransferResult Copy(SaveWorld source, PlayerRecord from, SaveWorld target, PlayerRecord to, bool inventory = true, bool enderChest = true)
    {
        if (WhyNotInto(target) is string why) throw new NotSupportedException(why);
        var src = new PlayerInventory(source, from);
        var dst = new PlayerInventory(target, to);
        if (target is BedrockWorld && dst.Format == ItemFormat.Numeric)
            throw new NotSupportedException("This Bedrock world keeps items by number (Pocket Edition 0.9 – 0.16): items can be put into Bedrock 1.2.13 and newer worlds.");
        var (t, _) = StructurePlacement.SameFormat(target);
        var notes = new List<string>();
        int moved = 0;
        using (ConversionLog.Start(t, out var log))
        {
            foreach (var part in Enum.GetValues<InventoryPart>())
            {
                if (part == InventoryPart.EnderChest ? !enderChest : !inventory) continue;
                bool sourceHas = part switch { InventoryPart.Offhand => src.HasOffhand, InventoryPart.EnderChest => src.HasEnderChest, _ => true };
                bool targetHas = part switch { InventoryPart.Offhand => dst.HasOffhand, InventoryPart.EnderChest => dst.HasEnderChest, _ => true };
                // A part the source doesn't have (a console save's off-hand) leaves the target's as it was.
                if (!sourceHas) continue;
                for (int i = 0; i < PlayerInventory.Size(part); i++)
                {
                    var item = src.Get(part, i);
                    if (!targetHas)
                    {
                        if (item is { IsEmpty: false }) notes.Add($"{Describe(item)} in the {Part(part)} stayed behind: {PlatformSupport.Describe(target)} has no {Part(part)} here");
                        continue;
                    }
                    if (item == null || item.IsEmpty) { dst.Set(part, i, null); continue; }
                    var raw = item.Raw ?? src.WriteItem(item);
                    var canonical = Canonical(source, raw, src.DataVersion);
                    if (canonical == null || canonical.GetString("id") is not string id || id is "minecraft:air")
                    {
                        notes.Add($"{Describe(item)} ({Part(part)}) could not be read and stayed behind");
                        dst.Set(part, i, null);
                        continue;
                    }
                    var native = target is BedrockWorld ? Items.ToBedrock(canonical)
                        : dst.DataVersion >= 1451 ? Items.ToJava(canonical, dst.DataVersion)
                        : Items.ToLegacyJava(canonical, dst.DataVersion, dst.NumericIds);
                    native.Remove("Slot");
                    // Pocket 0.x items are an id, a count and a data value: names and enchantments have nowhere to go.
                    if (target is PocketLegacyWorld && native.Remove("tag")) notes.Add($"{Describe(item)} ({Part(part)}): its name or enchantments stayed behind (Pocket Edition 0.x items keep none)");
                    dst.Set(part, i, dst.ReadItem(native));
                    moved++;
                }
            }
            notes.AddRange(log.Entries.Where(e => e.Kind != LossKind.World).Select(e => e.ToString()));
        }
        dst.WriteBack();
        return new TransferResult(moved, notes);
    }

    static string Part(InventoryPart p) => p switch
    {
        InventoryPart.Hotbar => "hotbar", InventoryPart.Storage => "inventory", InventoryPart.Armor => "armour slots",
        InventoryPart.Offhand => "off-hand", _ => "ender chest",
    };

    static string Describe(InventoryItem item) => $"{item.Count} × {item.Id.Replace("minecraft:", "")}";
}
