// MinecraftStudio.Core/World/PlayerInventory.Reading.cs: reading for PlayerInventory.
// Part of PlayerInventory (main part in PlayerInventory.cs).
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

public partial class PlayerInventory
{
    // ------------------------------------------------------------------ reading

    void Read()
    {
        if (_layout == Layout.Pocket)
        {
            // The links (0 - 8: id 255, Damage the inventory slot they show) and anything past slot 44 stay as they are.
            foreach (var raw in _player.GetList("Inventory")?.OfType<NbtCompound>() ?? [])
            {
                int slot = raw.GetInt("Slot");
                if (raw.GetInt("id") == 0 && raw.GetInt("Count") == 0 && raw["Name"] == null) _pocketEmpties = true;
                if (slot is >= 9 and <= 17) Put(InventoryPart.Hotbar, slot - 9, raw);
                else if (slot is >= 18 and <= 44) Put(InventoryPart.Storage, slot - 18, raw);
                else _otherSlots.Add(raw);
            }
            var pocketArmor = _player.GetList("Armor")?.OfType<NbtCompound>().ToList() ?? [];
            for (int i = 0; i < pocketArmor.Count && i < 4; i++) Put(InventoryPart.Armor, i, pocketArmor[i]);
            if (_player.GetList("Offhand")?.OfType<NbtCompound>().FirstOrDefault() is NbtCompound pocketOff) Put(InventoryPart.Offhand, 0, pocketOff);
            foreach (var raw in _player.GetList("EnderChestInventory")?.OfType<NbtCompound>() ?? [])
                if (raw.GetInt("Slot") is int s and >= 0 and < 27) Put(InventoryPart.EnderChest, s, raw);
            return;
        }
        if (_layout == Layout.Bedrock)
        {
            foreach (var raw in _player.GetList("Inventory")?.OfType<NbtCompound>() ?? [])
            {
                int slot = raw.GetInt("Slot");
                if (slot is >= 0 and <= 8) Put(InventoryPart.Hotbar, slot, raw);
                else if (slot is >= 9 and <= 35) Put(InventoryPart.Storage, slot - 9, raw);
            }
            var armor = _player.GetList("Armor")?.OfType<NbtCompound>().ToList() ?? [];
            for (int i = 0; i < armor.Count && i < 4; i++) Put(InventoryPart.Armor, i, armor[i]);
            if (_player.GetList("Offhand")?.OfType<NbtCompound>().FirstOrDefault() is NbtCompound off) Put(InventoryPart.Offhand, 0, off);
            foreach (var raw in _player.GetList("EnderChestInventory")?.OfType<NbtCompound>() ?? [])
                if (raw.GetInt("Slot") is int s and >= 0 and < 27) Put(InventoryPart.EnderChest, s, raw);
            return;
        }
        foreach (var raw in _player.GetList("Inventory")?.OfType<NbtCompound>() ?? [])
        {
            int slot = raw.GetInt("Slot");
            if (slot is >= 0 and <= 8) Put(InventoryPart.Hotbar, slot, raw);
            else if (slot is >= 9 and <= 35) Put(InventoryPart.Storage, slot - 9, raw);
            else if (slot is >= 100 and <= 103 && _layout == Layout.Java) Put(InventoryPart.Armor, 103 - slot, raw);
            else if (slot == -106 && _layout == Layout.Java) Put(InventoryPart.Offhand, 0, raw);
            else _otherSlots.Add(raw);
        }
        if (_layout == Layout.JavaEquipment && _player.GetCompound("equipment") is NbtCompound eq)
        {
            for (int i = 0; i < 4; i++)
                if (eq.GetCompound(EquipmentKeys[i]) is NbtCompound a) Put(InventoryPart.Armor, i, a);
            if (eq.GetCompound("offhand") is NbtCompound off) Put(InventoryPart.Offhand, 0, off);
        }
        foreach (var raw in _player.GetList("EnderItems")?.OfType<NbtCompound>() ?? [])
            if (raw.GetInt("Slot") is int s and >= 0 and < 27) Put(InventoryPart.EnderChest, s, raw);
    }

    static readonly string[] EquipmentKeys = ["head", "chest", "legs", "feet"];

    void Put(InventoryPart part, int index, NbtCompound raw)
    {
        var item = ReadItem(raw);
        if (!item.IsEmpty) _items[(part, index)] = item;
    }

    /// <summary>An item as this record's format stores it, read for editing (its NBT kept as <see cref="InventoryItem.Raw"/>).</summary>
    public InventoryItem ReadItem(NbtCompound raw)
    {
        var item = new InventoryItem { Raw = raw };
        var tag = raw.GetCompound("tag");
        switch (Format)
        {
            case ItemFormat.Bedrock:
                item.Id = raw.GetString("Name") ?? "";
                item.Count = raw.GetInt("Count");
                item.Data = raw.GetInt("Damage");
                item.Damage = tag?.GetInt("Damage") ?? 0;
                item.CustomName = tag?.GetCompound("display")?.GetString("Name");
                ReadNumbered(tag?.GetList("ench"), item, bedrock: true);
                break;
            case ItemFormat.Numeric:
                item.Id = raw["id"] is { IsNumeric: true } n ? n.AsLong.ToString() : raw.GetString("id") ?? "";
                item.Count = raw.GetInt("Count");
                item.Damage = raw.GetInt("Damage");
                item.CustomName = tag?.GetCompound("display")?.GetString("Name");
                ReadNumbered(tag?.GetList("StoredEnchantments") ?? tag?.GetList("ench"), item, bedrock: _layout == Layout.Bedrock);
                item.StoredEnchantments = tag?.ContainsKey("StoredEnchantments") == true;
                break;
            case ItemFormat.Named:
                item.Id = raw.GetString("id") ?? (raw["id"] is { IsNumeric: true } m ? m.AsLong.ToString() : "");
                item.Count = raw.GetInt("Count");
                item.Damage = raw.GetInt("Damage");
                item.CustomName = tag?.GetCompound("display")?.GetString("Name");
                ReadNumbered(tag?.GetList("StoredEnchantments") ?? tag?.GetList("ench"), item, bedrock: false);
                item.StoredEnchantments = tag?.ContainsKey("StoredEnchantments") == true;
                break;
            case ItemFormat.Flattened:
                item.Id = raw.GetString("id") ?? "";
                item.Count = raw.GetInt("Count");
                item.Damage = tag?.GetInt("Damage") ?? 0;
                if (tag?.GetCompound("display")?["Name"] is NbtTag fname) item.CustomName = TextComponents.ToPlain(TextComponents.FromJavaTag(fname));
                foreach (var e in (tag?.GetList("StoredEnchantments") ?? tag?.GetList("Enchantments"))?.OfType<NbtCompound>() ?? [])
                    if (e.GetString("id") is string id) item.Enchantments.Add(new(Bare(id), e.GetInt("lvl", 1)));
                item.StoredEnchantments = tag?.ContainsKey("StoredEnchantments") == true;
                break;
            case ItemFormat.Components:
                item.Id = raw.GetString("id") ?? "";
                item.Count = raw.GetInt("count", 1);
                var comp = raw.GetCompound("components");
                item.Damage = comp?.GetInt("minecraft:damage") ?? 0;
                if (comp?["minecraft:custom_name"] is NbtTag cname) item.CustomName = TextComponents.ToPlain(TextComponents.FromJavaTag(cname));
                var en = comp?.GetCompound("minecraft:stored_enchantments") ?? comp?.GetCompound("minecraft:enchantments");
                item.StoredEnchantments = comp?.ContainsKey("minecraft:stored_enchantments") == true;
                foreach (var (id, lvl) in en?.GetCompound("levels") ?? en ?? [])
                    if (lvl.IsNumeric) item.Enchantments.Add(new(Bare(id), (int)lvl.AsLong));
                break;
        }
        return item;
    }

    static void ReadNumbered(NbtList? list, InventoryItem item, bool bedrock)
    {
        foreach (var e in list?.OfType<NbtCompound>() ?? [])
        {
            int id = e.GetInt("id");
            string name = (bedrock ? Items.BedrockEnchantmentName(id) : Items.JavaLegacyEnchantmentName(id)) ?? "#" + id;
            item.Enchantments.Add(new(name, e.GetInt("lvl", 1)));
        }
    }

    static string Bare(string id) => id.StartsWith("minecraft:", StringComparison.Ordinal) ? id[10..] : id;
}
