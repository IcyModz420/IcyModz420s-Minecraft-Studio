// MinecraftStudio.Core/World/InventoryItem.cs: The places a player keeps items.
// Owns: InventoryPart, ItemFormat, Enchantment, InventoryItem.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

/// <summary>The places a player keeps items.</summary>
public enum InventoryPart { Hotbar, Storage, Armor, Offhand, EnderChest }

/// <summary>How a player record stores items.</summary>
public enum ItemFormat
{
    /// <summary>Java before 1.8, Pocket Edition 0.x, old Bedrock and old console saves: numeric ids, Damage on the item.</summary>
    Numeric,
    /// <summary>Java 1.8–1.12 and Legacy Console TU69+: string ids, Damage on the item, enchantments by number.</summary>
    Named,
    /// <summary>Java 1.13–1.20.4: Damage, enchantments by name and a JSON name in the item's tag.</summary>
    Flattened,
    /// <summary>Java 1.20.5+: item components.</summary>
    Components,
    /// <summary>Bedrock: Name, the data value in Damage, durability in tag.Damage, Bedrock enchantment numbers.</summary>
    Bedrock,
}

public readonly record struct Enchantment(string Name, int Level);

/// <summary>One item as the inventory editor shows it; <see cref="Raw"/> keeps everything else the item had.</summary>
public sealed class InventoryItem
{
    /// <summary>The item id (minecraft:diamond_sword), or its number for numeric formats ("276").</summary>
    public string Id { get; set; } = "";
    public int Count { get; set; } = 1;
    /// <summary>Durability used (tools, weapons, armour). Before Java 1.13 this is also the data value.</summary>
    public int Damage { get; set; }
    /// <summary>Bedrock only: the data value kept apart from durability (old colours, potion types …).</summary>
    public int Data { get; set; }
    public string? CustomName { get; set; }
    public List<Enchantment> Enchantments { get; } = [];
    /// <summary>The enchantments are stored ones (an enchanted book), not active ones.</summary>
    public bool StoredEnchantments { get; set; }
    /// <summary>The item as it was read (null for a new one).</summary>
    public NbtCompound? Raw { get; set; }

    public bool IsEmpty => Id.Length == 0 || Id is "minecraft:air" or "air" or "0" || Count <= 0;

    public InventoryItem Clone()
    {
        var c = new InventoryItem { Id = Id, Count = Count, Damage = Damage, Data = Data, CustomName = CustomName, StoredEnchantments = StoredEnchantments, Raw = (NbtCompound?)Raw?.Clone() };
        c.Enchantments.AddRange(Enchantments);
        return c;
    }
}
