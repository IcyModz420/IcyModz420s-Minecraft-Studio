// MinecraftStudio.Core/World/PlayerInventory.Writing.cs: writing for PlayerInventory.
// Part of PlayerInventory (main part in PlayerInventory.cs).
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

public partial class PlayerInventory
{
    // ------------------------------------------------------------------ writing

    /// <summary>Writes every slot back into the player record, in its own format.</summary>
    public void WriteBack()
    {
        if (_layout == Layout.Bedrock) WriteBedrock();
        else if (_layout == Layout.Pocket) WritePocket();
        else WriteJava();
        IsModified = false;
    }

    void WriteJava()
    {
        var inventory = new NbtList(NbtType.Compound);
        foreach (var raw in _otherSlots) inventory.Add(raw);
        for (int i = 0; i < 9; i++) AddSlotted(inventory, InventoryPart.Hotbar, i, i);
        for (int i = 0; i < 27; i++) AddSlotted(inventory, InventoryPart.Storage, i, 9 + i);
        if (_layout == Layout.Java)
        {
            for (int i = 0; i < 4; i++) AddSlotted(inventory, InventoryPart.Armor, i, 103 - i);
            if (HasOffhand) AddSlotted(inventory, InventoryPart.Offhand, 0, -106);
        }
        else
        {
            var eq = _player.GetCompound("equipment") ?? new NbtCompound();
            for (int i = 0; i < 4; i++)
            {
                if (Get(InventoryPart.Armor, i) is InventoryItem a) eq[EquipmentKeys[i]] = WriteItem(a);
                else eq.Remove(EquipmentKeys[i]);
            }
            if (Get(InventoryPart.Offhand, 0) is InventoryItem o) eq["offhand"] = WriteItem(o);
            else eq.Remove("offhand");
            if (eq.Count > 0) _player["equipment"] = eq;
            else _player.Remove("equipment");
        }
        _player["Inventory"] = inventory;
        if (HasEnderChest)
        {
            var ender = new NbtList(NbtType.Compound);
            for (int i = 0; i < 27; i++) AddSlotted(ender, InventoryPart.EnderChest, i, i);
            _player["EnderItems"] = ender;
        }
    }

    // Pocket 0.x and the 3DS: the links first as they were, the 36 slots at 9 - 44 (empty ones written only when the record
    // had them), the armour list of four.
    void WritePocket()
    {
        var inventory = new NbtList(NbtType.Compound);
        foreach (var raw in _otherSlots.Where(r => r.GetInt("Slot") < 9)) inventory.Add(raw);
        for (int s = 0; s < 36; s++)
        {
            var item = s < 9 ? Get(InventoryPart.Hotbar, s) : Get(InventoryPart.Storage, s - 9);
            if (item == null && !_pocketEmpties) continue;
            var c = item != null ? WriteItem(item) : EmptyBedrock();
            c.SetByte("Slot", (sbyte)(s + 9));
            inventory.Add(c);
        }
        foreach (var raw in _otherSlots.Where(r => r.GetInt("Slot") >= 9)) inventory.Add(raw);
        _player["Inventory"] = inventory;
        if (_player.ContainsKey("Armor") || Enumerable.Range(0, 4).Any(i => Get(InventoryPart.Armor, i) != null))
        {
            var armor = new NbtList(NbtType.Compound);
            for (int i = 0; i < 4; i++) armor.Add(Get(InventoryPart.Armor, i) is InventoryItem a ? WriteItem(a) : EmptyBedrock());
            _player["Armor"] = armor;
        }
        if (HasOffhand) _player["Offhand"] = new NbtList(NbtType.Compound) { Get(InventoryPart.Offhand, 0) is InventoryItem o ? WriteItem(o) : EmptyBedrock() };
        if (HasEnderChest && _player.ContainsKey("EnderChestInventory"))
        {
            var ender = new NbtList(NbtType.Compound);
            for (int s = 0; s < 27; s++)
            {
                var c = Get(InventoryPart.EnderChest, s) is InventoryItem e ? WriteItem(e) : EmptyBedrock();
                c.SetByte("Slot", (sbyte)s);
                ender.Add(c);
            }
            _player["EnderChestInventory"] = ender;
        }
    }

    void AddSlotted(NbtList list, InventoryPart part, int index, int slot)
    {
        if (Get(part, index) is not InventoryItem item) return;
        var c = WriteItem(item);
        c.SetByte("Slot", (sbyte)slot);
        list.Add(c);
    }

    void WriteBedrock()
    {
        var inventory = new NbtList(NbtType.Compound);
        for (int s = 0; s < 36; s++)
        {
            var item = s < 9 ? Get(InventoryPart.Hotbar, s) : Get(InventoryPart.Storage, s - 9);
            var c = item != null ? WriteItem(item) : EmptyBedrock();
            c.SetByte("Slot", (sbyte)s);
            inventory.Add(c);
        }
        _player["Inventory"] = inventory;
        var armor = new NbtList(NbtType.Compound);
        for (int i = 0; i < 4; i++) armor.Add(Get(InventoryPart.Armor, i) is InventoryItem a ? WriteItem(a) : EmptyBedrock());
        _player["Armor"] = armor;
        _player["Offhand"] = new NbtList(NbtType.Compound) { Get(InventoryPart.Offhand, 0) is InventoryItem o ? WriteItem(o) : EmptyBedrock() };
        if (HasEnderChest)
        {
            var ender = new NbtList(NbtType.Compound);
            for (int s = 0; s < 27; s++)
            {
                var c = Get(InventoryPart.EnderChest, s) is InventoryItem e ? WriteItem(e) : EmptyBedrock();
                c.SetByte("Slot", (sbyte)s);
                ender.Add(c);
            }
            _player["EnderChestInventory"] = ender;
        }
    }

    NbtCompound EmptyBedrock() => Format == ItemFormat.Numeric
        ? new NbtCompound { { "id", new NbtShort(0) }, { "Count", new NbtByte(0) }, { "Damage", new NbtShort(0) } }
        : new NbtCompound { { "Count", new NbtByte(0) }, { "Damage", new NbtShort(0) }, { "Name", new NbtString("") }, { "WasPickedUp", new NbtByte(false) } };

    /// <summary>
    /// The item in this record's format. An edited item is its original with only the changed fields rewritten, so
    /// everything else stays exactly as the game wrote it (a name with colours, a Damage of 0 …); a new item, or one
    /// whose id changed, is written from scratch.
    /// </summary>
    public NbtCompound WriteItem(InventoryItem item)
    {
        var before = item.Raw != null ? ReadItem(item.Raw) : null;
        bool fresh = before == null || before.Id != item.Id;
        var c = fresh ? new NbtCompound() : (NbtCompound)item.Raw!.Clone();
        c.Remove("Slot");
        bool count = fresh || before!.Count != item.Count;
        bool damage = fresh || before!.Damage != item.Damage;
        bool data = fresh || before!.Data != item.Data;
        bool name = fresh || before!.CustomName != item.CustomName;
        bool ench = fresh || before!.StoredEnchantments != item.StoredEnchantments || !before.Enchantments.SequenceEqual(item.Enchantments);
        switch (Format)
        {
            case ItemFormat.Bedrock:
                if (fresh) { c.SetString("Name", item.Id); c.SetByte("WasPickedUp", 0); }
                if (count) c.SetByte("Count", (sbyte)Math.Clamp(item.Count, 1, 127));
                if (data) c["Damage"] = new NbtShort((short)Math.Clamp(item.Data, 0, short.MaxValue));
                if (damage || name || ench)
                {
                    var tag = c.GetCompound("tag") ?? new NbtCompound();
                    if (damage) SetOrRemoveInt(tag, "Damage", item.Damage);
                    if (name) SetPlainName(tag, item.CustomName);
                    if (ench) SetNumbered(tag, "ench", item.Enchantments, bedrock: true);
                    Attach(c, "tag", tag);
                }
                break;
            case ItemFormat.Numeric:
            case ItemFormat.Named:
                if (fresh)
                {
                    if (Format == ItemFormat.Numeric) c["id"] = new NbtShort((short)(int.TryParse(item.Id, out int num) ? num : 0));
                    else c.SetString("id", item.Id);
                }
                if (count) c.SetByte("Count", (sbyte)Math.Clamp(item.Count, 1, 127));
                if (damage) c["Damage"] = new NbtShort((short)Math.Clamp(item.Damage, short.MinValue, short.MaxValue));
                if (name || ench)
                {
                    var tag = c.GetCompound("tag") ?? new NbtCompound();
                    if (name) SetPlainName(tag, item.CustomName);
                    if (ench) SetNumbered(tag, item.StoredEnchantments ? "StoredEnchantments" : "ench", item.Enchantments, bedrock: _layout == Layout.Bedrock);
                    Attach(c, "tag", tag);
                }
                break;
            case ItemFormat.Flattened:
                if (fresh) c.SetString("id", item.Id);
                if (count) c.SetByte("Count", (sbyte)Math.Clamp(item.Count, 1, 127));
                if (damage || name || ench)
                {
                    var tag = c.GetCompound("tag") ?? new NbtCompound();
                    if (damage) SetOrRemoveInt(tag, "Damage", item.Damage);
                    if (name)
                    {
                        var display = tag.GetCompound("display") ?? new NbtCompound();
                        if (string.IsNullOrEmpty(item.CustomName)) display.Remove("Name");
                        else display.SetString("Name", TextComponents.FromPlain(item.CustomName));
                        Attach(tag, "display", display);
                    }
                    if (ench)
                    {
                        string key = item.StoredEnchantments ? "StoredEnchantments" : "Enchantments";
                        tag.Remove(item.StoredEnchantments ? "Enchantments" : "StoredEnchantments");
                        if (item.Enchantments.Count == 0) tag.Remove(key);
                        else
                        {
                            var list = new NbtList(NbtType.Compound);
                            foreach (var e in item.Enchantments)
                                list.Add(new NbtCompound { { "id", new NbtString("minecraft:" + (e.Name == "sweeping_edge" ? "sweeping" : e.Name)) }, { "lvl", new NbtShort((short)Math.Clamp(e.Level, 1, short.MaxValue)) } });
                            tag[key] = list;
                        }
                    }
                    Attach(c, "tag", tag);
                }
                break;
            case ItemFormat.Components:
                if (fresh) c.SetString("id", item.Id);
                if (count) { c.SetInt("count", Math.Clamp(item.Count, 1, 99)); c.Remove("Count"); }
                if (damage || name || ench)
                {
                    var comp = c.GetCompound("components") ?? new NbtCompound();
                    if (damage) SetOrRemoveInt(comp, "minecraft:damage", item.Damage);
                    if (name)
                    {
                        if (string.IsNullOrEmpty(item.CustomName)) comp.Remove("minecraft:custom_name");
                        else comp["minecraft:custom_name"] = TextComponents.ToJavaTag(TextComponents.FromPlain(item.CustomName), _dv);
                    }
                    if (ench)
                    {
                        string key = item.StoredEnchantments ? "minecraft:stored_enchantments" : "minecraft:enchantments";
                        comp.Remove(item.StoredEnchantments ? "minecraft:enchantments" : "minecraft:stored_enchantments");
                        if (item.Enchantments.Count == 0) comp.Remove(key);
                        else
                        {
                            var levels = new NbtCompound();
                            foreach (var e in item.Enchantments)
                                levels.SetInt("minecraft:" + (e.Name == "sweeping" ? "sweeping_edge" : e.Name), Math.Clamp(e.Level, 1, 255));
                            // 1.20.5–1.21.4 wrap the levels; 1.21.5 dropped the wrapper.
                            comp[key] = _dv >= 4325 ? levels : new NbtCompound { { "levels", levels } };
                        }
                    }
                    Attach(c, "components", comp);
                }
                break;
        }
        return c;
    }

    static void Attach(NbtCompound parent, string key, NbtCompound child)
    {
        if (child.Count > 0) parent[key] = child;
        else parent.Remove(key);
    }

    static void SetOrRemoveInt(NbtCompound c, string key, int value)
    {
        if (value > 0) c.SetInt(key, value);
        else c.Remove(key);
    }

    static void SetPlainName(NbtCompound tag, string? name)
    {
        var display = tag.GetCompound("display") ?? new NbtCompound();
        if (string.IsNullOrEmpty(name)) display.Remove("Name");
        else display.SetString("Name", name);
        Attach(tag, "display", display);
    }

    static void SetNumbered(NbtCompound tag, string key, List<Enchantment> enchantments, bool bedrock)
    {
        tag.Remove(key == "ench" ? "StoredEnchantments" : "ench");
        var list = new NbtList(NbtType.Compound);
        foreach (var e in enchantments)
        {
            int? id = e.Name.StartsWith('#') && int.TryParse(e.Name[1..], out int raw) ? raw : bedrock ? Items.BedrockEnchantmentNumber(e.Name) : Items.JavaLegacyEnchantmentNumber(e.Name);
            if (id is not int n) continue; // the version has no such enchantment
            list.Add(new NbtCompound { { "id", new NbtShort((short)n) }, { "lvl", new NbtShort((short)Math.Clamp(e.Level, 1, short.MaxValue)) } });
        }
        if (list.Count > 0) tag[key] = list;
        else tag.Remove(key);
    }

    /// <summary>Item ids to offer for this format (the editor also takes any other id typed in).</summary>
    public IReadOnlyList<string> KnownIds() => Format switch
    {
        ItemFormat.Bedrock => Items.AllBedrockItems,
        ItemFormat.Numeric => JavaBlockData.LegacyItemIds.OrderBy(kv => kv.Value).Select(kv => kv.Value.ToString()).Distinct().ToList(),
        ItemFormat.Named => JavaBlockData.LegacyItemIds.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
        _ => Items.AllJavaItems,
    };

    /// <summary>The name for a numeric id (for display), or null.</summary>
    public static string? NameOfNumber(int id) => LegacyNames.Value.TryGetValue(id, out var n) ? n : null;
    static readonly Lazy<Dictionary<int, string>> LegacyNames = new(() =>
    {
        var d = new Dictionary<int, string>();
        foreach (var (name, id) in JavaBlockData.LegacyItemIds) d.TryAdd(id, name);
        return d;
    });

    /// <summary>Whether this version knows an enchantment (numeric formats only have the old ones).</summary>
    public bool KnowsEnchantment(string name) => Format switch
    {
        ItemFormat.Bedrock => Items.BedrockEnchantmentNumber(name) != null,
        ItemFormat.Numeric when _layout == Layout.Bedrock => Items.BedrockEnchantmentNumber(name) != null,
        ItemFormat.Numeric or ItemFormat.Named => Items.JavaLegacyEnchantmentNumber(name) != null,
        _ => true,
    };
}
