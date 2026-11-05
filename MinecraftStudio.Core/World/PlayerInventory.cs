// MinecraftStudio.Core/World/PlayerInventory.cs: A player's items in an edition-neutral form for editing:
// hotbar, storage, armour, off-hand and ender chest, read from the player record in its own format (every Java
// version from Alpha to 26.x, Bedrock, Pocket Edition 0.x, Legacy Console) and written back in the same format.
// Owns: PlayerInventory.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

/// <summary>
/// A player's items in an edition-neutral form for editing: hotbar, storage, armour, off-hand and ender chest, read
/// from the player record in its own format (every Java version from Alpha to 26.x, Bedrock, Pocket Edition 0.x,
/// Legacy Console) and written back in the same format. Only the fields the editor changes are touched; everything
/// else on an item (custom data, book pages, potion contents …) is kept, as are slots the editor does not know.
/// </summary>
public sealed partial class PlayerInventory
{
    enum Layout { Java, JavaEquipment, Bedrock, Pocket }

    readonly NbtCompound _player;
    readonly Layout _layout;
    readonly int _dv;
    readonly Dictionary<(InventoryPart, int), InventoryItem> _items = [];
    readonly List<NbtCompound> _otherSlots = [];
    // Pocket: the record lists its empty slots too (the 3DS does), so they are written back.
    bool _pocketEmpties;

    public PlayerInventory(SaveWorld world, PlayerRecord player)
    {
        _player = player.Data;
        int worldDv = world is JavaWorld j ? j.DataVersion ?? 0 : 0;
        _dv = (int)(_player.GetLongOrNull("DataVersion") ?? worldDv);
        var existing = (_player.GetList("Inventory")?.OfType<NbtCompound>() ?? [])
            .Concat(_player.GetList("EnderItems")?.OfType<NbtCompound>() ?? []).Concat(_player.GetList("EnderChestInventory")?.OfType<NbtCompound>() ?? []).ToList();
        if (world is PocketLegacyWorld or ThreeDsWorld)
        {
            // Pocket Edition 0.x and the 3DS: links in 0 - 8, the inventory in 9 - 44, armour in its own list (see Read).
            _layout = Layout.Pocket;
            Format = existing.Any(i => i["Name"] is NbtString) ? ItemFormat.Bedrock : ItemFormat.Numeric;
        }
        else if (world is BedrockWorld)
        {
            _layout = Layout.Bedrock;
            // Pocket Edition 0.9–0.16 (and Bedrock before 1.2.13?) kept numeric ids.
            Format = existing.Any(i => i["Name"] is NbtString) || !existing.Any(i => i["id"] is { IsNumeric: true }) ? ItemFormat.Bedrock : ItemFormat.Numeric;
        }
        else
        {
            _layout = _player.ContainsKey("equipment") || _dv >= 4325 ? Layout.JavaEquipment : Layout.Java;
            Format = existing.Any(i => i["id"] is { IsNumeric: true }) ? ItemFormat.Numeric
                : _dv >= 3819 ? ItemFormat.Components
                : _dv >= 1451 ? ItemFormat.Flattened
                : existing.Any(i => i["id"] is NbtString) || _dv >= 100 ? ItemFormat.Named
                : world is ConsoleWorld c && c.Archive.Version >= 11 ? ItemFormat.Named
                : ItemFormat.Numeric;
        }
        // Off-hand from Java 1.9 (and every Bedrock); ender chests from Java 1.3 and Bedrock 0.12.
        HasOffhand = _layout != Layout.Java || _dv >= 169 || _player.GetList("Inventory")?.OfType<NbtCompound>().Any(i => i.GetInt("Slot") == -106) == true;
        if (world is ConsoleWorld) HasOffhand = false;
        else if (world is PocketLegacyWorld or ThreeDsWorld) HasOffhand = _player.ContainsKey("Offhand");
        HasEnderChest = _player.ContainsKey("EnderItems") || _player.ContainsKey("EnderChestInventory") || _layout != Layout.Bedrock && _dv > 0;
        Read();
    }

    public ItemFormat Format { get; }
    public bool HasOffhand { get; }
    public bool HasEnderChest { get; }
    /// <summary>Whether the format stores item ids as numbers.</summary>
    public bool NumericIds => Format == ItemFormat.Numeric;
    /// <summary>Bedrock keeps a data value apart from durability.</summary>
    public bool SeparateData => Format == ItemFormat.Bedrock;
    public int DataVersion => _dv;

    public static int Size(InventoryPart part) => part switch
    {
        InventoryPart.Hotbar => 9,
        InventoryPart.Storage => 27,
        InventoryPart.Armor => 4,
        InventoryPart.Offhand => 1,
        _ => 27,
    };

    /// <summary>The parts this player has.</summary>
    public IEnumerable<InventoryPart> Parts
    {
        get
        {
            yield return InventoryPart.Hotbar;
            yield return InventoryPart.Storage;
            yield return InventoryPart.Armor;
            if (HasOffhand) yield return InventoryPart.Offhand;
            if (HasEnderChest) yield return InventoryPart.EnderChest;
        }
    }

    /// <summary>Names of the armour slots, top to bottom.</summary>
    public static readonly string[] ArmorSlots = ["Helmet", "Chestplate", "Leggings", "Boots"];

    public InventoryItem? Get(InventoryPart part, int index) => _items.TryGetValue((part, index), out var item) ? item : null;

    public void Set(InventoryPart part, int index, InventoryItem? item)
    {
        if (item == null || item.IsEmpty) _items.Remove((part, index));
        else _items[(part, index)] = item;
        IsModified = true;
    }

    public bool IsModified { get; private set; }
}
