// MinecraftStudio.Core/Conversion/Items.ToJava.cs: to java for Items.
// Part of Items (main part in Items.cs).
using System.Collections.Concurrent;
using System.Text.Json;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public static partial class Items
{
    // ---------------------------------------------------------------- canonical -> Java target

    /// <summary>Item stack for a Java target data version (Slot added by the caller).</summary>
    public static NbtCompound ToJava(NbtCompound canonical, int dataVersion)
    {
        if (dataVersion >= 1451) canonical = Fit(canonical, JavaRules(dataVersion));
        string id = Id(canonical);
        int count = Count(canonical);
        var comp = Components(canonical);
        if (dataVersion >= 3819)
        {
            var c = new NbtCompound();
            c.SetString("id", JavaName(id, dataVersion));
            c.SetInt("count", count);
            if (comp is { Count: > 0 })
            {
                var copy = (NbtCompound)comp.Clone();
                FixTextComponents(copy, dataVersion);
                if (dataVersion < 4325 && copy.GetCompound("minecraft:enchantments") is NbtCompound en && !en.ContainsKey("levels"))
                    copy["minecraft:enchantments"] = new NbtCompound { { "levels", en } };
                if (dataVersion < 4325 && copy.GetCompound("minecraft:stored_enchantments") is NbtCompound sen && !sen.ContainsKey("levels"))
                    copy["minecraft:stored_enchantments"] = new NbtCompound { { "levels", sen } };
                if (copy.GetList("minecraft:container") is NbtList cont)
                    foreach (var e in cont.OfType<NbtCompound>())
                        if (e.GetCompound("item") is NbtCompound inner) e["item"] = ToJava(inner, dataVersion);
                if (copy.GetList("minecraft:bundle_contents") is NbtList bundle)
                    for (int i = 0; i < bundle.Count; i++)
                        if (bundle[i] is NbtCompound inner) bundle[i] = ToJava(inner, dataVersion);
                ComponentsForVersion(copy, dataVersion);
                c["components"] = copy;
            }
            return c;
        }
        if (dataVersion >= 1451)
        {
            var c = new NbtCompound();
            c.SetString("id", JavaName(id, dataVersion));
            c.SetByte("Count", (sbyte)Math.Clamp(count, 1, 127));
            var tag = ComponentsToTag(comp, dataVersion, legacy: false);
            ReportUnmapped(comp, TagHandled, "Java " + JavaBlockData.For(dataVersion).Name);
            if (tag.Count > 0) c["tag"] = tag;
            return c;
        }
        return ToLegacyJava(canonical, dataVersion);
    }

    static void FixTextComponents(NbtCompound comp, int dv)
    {
        foreach (var key in new[] { "minecraft:custom_name", "minecraft:item_name" })
            if (comp[key] is NbtString s) comp[key] = TextComponents.ToJavaTag(s.Value, dv);
        if (comp.GetList("minecraft:lore") is NbtList lore && dv >= 4298)
            comp["minecraft:lore"] = TextComponents.JavaList(lore.Select(line => line is NbtString ls ? TextComponents.ToJavaTag(ls.Value, dv) : line));
    }

    static NbtCompound ComponentsToTag(NbtCompound? comp, int dv, bool legacy)
    {
        var tag = new NbtCompound();
        if (comp == null) return tag;
        if (comp.GetCompound("minecraft:custom_data") is NbtCompound custom)
            foreach (var (k, v) in custom) tag[k] = v.Clone();
        if (comp.GetLongOrNull("minecraft:damage") is long dmg && !legacy) tag.SetInt("Damage", (int)dmg);
        var display = new NbtCompound();
        if (comp["minecraft:custom_name"] is NbtTag name)
            display["Name"] = legacy ? TextComponents.ToPlainTag(TextComponents.FromJavaTag(name)) : new NbtString(TextComponents.FromJavaTag(name));
        if (comp.GetList("minecraft:lore") is NbtList lore)
        {
            var l = new NbtList(NbtType.String);
            foreach (var line in lore) l.Add(legacy || dv < 1803 ? TextComponents.ToPlainTag(TextComponents.FromJavaTag(line)) : new NbtString(TextComponents.FromJavaTag(line)));
            display["Lore"] = l;
        }
        if (comp.GetLongOrNull("minecraft:dyed_color") is long color) display.SetInt("color", (int)color);
        else if (comp.GetCompound("minecraft:dyed_color")?.GetLongOrNull("rgb") is long rgb) display.SetInt("color", (int)rgb);
        if (display.Count > 0) tag["display"] = display;
        foreach (var (component, key) in new[] { ("minecraft:enchantments", legacy ? "ench" : "Enchantments"), ("minecraft:stored_enchantments", "StoredEnchantments") })
        {
            if (comp.GetCompound(component) is not NbtCompound en) continue;
            var levels = en.GetCompound("levels") ?? en;
            var list = new NbtList(NbtType.Compound);
            foreach (var (ench, lvl) in levels)
            {
                if (!lvl.IsNumeric) continue;
                var e = new NbtCompound();
                if (legacy)
                {
                    string baseName = ench.StartsWith("minecraft:") ? ench[10..] : ench;
                    var hit = JavaLegacyEnchantments.FirstOrDefault(kv => kv.Value == baseName);
                    if (hit.Value == null)
                    {
                        ConversionLog.Note(LossKind.Item, "enchantment " + ench, "removed: that version has no such enchantment", lost: true);
                        continue;
                    }
                    e.SetShort("id", (short)hit.Key);
                }
                else e.SetString("id", ench);
                e.SetShort("lvl", (short)lvl.AsLong);
                list.Add(e);
            }
            if (list.Count > 0) tag[key] = list;
        }
        if (comp.GetCompound("minecraft:potion_contents")?.GetString("potion") is string potion) tag.SetString("Potion", potion);
        if (comp.GetCompound("minecraft:potion_contents")?["custom_color"] is { IsNumeric: true } potionColor) tag.SetInt("CustomPotionColor", (int)potionColor.AsLong);
        if (comp.ContainsKey("minecraft:unbreakable")) tag.SetByte("Unbreakable", 1);
        if (comp.GetCompound("minecraft:profile") is NbtCompound profile) tag["SkullOwner"] = SkullOwnerFromProfile(profile, dv);
        else if (comp.GetString("minecraft:profile") is string profileName) tag["SkullOwner"] = new NbtCompound { { "Name", new NbtString(profileName) } };
        if (comp.GetCompound("minecraft:trim") is NbtCompound trim)
        {
            if (dv >= 3337) tag["Trim"] = new NbtCompound { { "material", new NbtString(trim.GetString("material") ?? "") }, { "pattern", new NbtString(trim.GetString("pattern") ?? "") } };
            else ConversionLog.Note(LossKind.Item, "armour trim", "removed: that version has no armour trims", lost: true);
        }
        if (comp.GetCompound("minecraft:custom_model_data")?.GetList("floats") is { Count: > 0 } floats && floats[0].IsNumeric && dv >= 1952)
            tag.SetInt("CustomModelData", (int)floats[0].AsDouble);
        else if (comp["minecraft:custom_model_data"] is { IsNumeric: true } cmdValue && dv >= 1952) tag.SetInt("CustomModelData", (int)cmdValue.AsLong);
        if (comp.GetCompound("minecraft:fireworks") is NbtCompound fireworks)
        {
            var explosions = new NbtList(NbtType.Compound);
            foreach (var e in fireworks.GetList("explosions")?.OfType<NbtCompound>() ?? []) explosions.Add(ExplosionToTag(e));
            tag["Fireworks"] = new NbtCompound { { "Flight", new NbtByte((sbyte)fireworks.GetInt("flight_duration", 1)) }, { "Explosions", explosions } };
        }
        if (comp.GetCompound("minecraft:firework_explosion") is NbtCompound explosion) tag["Explosion"] = ExplosionToTag(explosion);
        if (comp.GetCompound("minecraft:entity_data") is NbtCompound entityData) tag["EntityTag"] = entityData.Clone();
        if (comp.GetCompound("minecraft:bucket_entity_data") is NbtCompound bucket) foreach (var (k, v) in bucket) tag[k] = v.Clone();
        if (comp["minecraft:instrument"] is NbtString instrument && dv >= 3105) tag.SetString("instrument", instrument.Value);
        if (comp.GetCompound("minecraft:block_entity_data") is NbtCompound bed)
        {
            var be = tag.GetCompound("BlockEntityTag") ?? new NbtCompound();
            foreach (var (k, v) in bed) if (k != "id") be[k] = v.Clone();
            tag["BlockEntityTag"] = be;
        }
        if (comp.GetList("minecraft:banner_patterns") is NbtList bannerPatterns)
        {
            var be = tag.GetCompound("BlockEntityTag") ?? new NbtCompound();
            be["Patterns"] = BannerPatternsToTag(bannerPatterns, legacy || dv < 1451);
            if (comp.GetString("minecraft:base_color") is string baseColor)
            {
                int bc = Math.Max(0, Array.IndexOf(StateEdit.Colors, baseColor));
                be.SetInt("Base", legacy || dv < 1451 ? 15 - bc : bc);
            }
            tag["BlockEntityTag"] = be;
        }
        if (comp.GetList("minecraft:pot_decorations") is NbtList sherds && dv >= 3463)
        {
            var be = tag.GetCompound("BlockEntityTag") ?? new NbtCompound();
            be["sherds"] = sherds.Clone();
            tag["BlockEntityTag"] = be;
        }
        if (comp.GetCompound("minecraft:tooltip_display") is NbtCompound tooltip && dv >= 1451)
        {
            var hiddenSet = tooltip.GetList("hidden_components")?.OfType<NbtString>().Select(h => h.Value).ToHashSet() ?? [];
            int flags = (hiddenSet.Contains("minecraft:enchantments") ? 1 : 0) | (hiddenSet.Contains("minecraft:attribute_modifiers") ? 2 : 0)
                | (hiddenSet.Contains("minecraft:unbreakable") ? 4 : 0) | (hiddenSet.Contains("minecraft:can_break") ? 8 : 0) | (hiddenSet.Contains("minecraft:can_place_on") ? 16 : 0)
                | (hiddenSet.Overlaps(["minecraft:potion_contents", "minecraft:written_book_content", "minecraft:fireworks"]) ? 32 : 0)
                | (hiddenSet.Contains("minecraft:dyed_color") ? 64 : 0) | (hiddenSet.Contains("minecraft:trim") ? 128 : 0);
            if (tooltip.GetBool("hide_tooltip")) flags = 255;
            if (flags != 0) tag.SetInt("HideFlags", flags);
        }
        if (comp.GetLongOrNull("minecraft:repair_cost") is long rc) tag.SetInt("RepairCost", (int)rc);
        if (comp.GetLongOrNull("minecraft:map_id") is long map) tag.SetInt("map", (int)map);
        if (comp.GetList("minecraft:container") is NbtList cont)
        {
            var items = new NbtList(NbtType.Compound);
            foreach (var e in cont.OfType<NbtCompound>())
                if (e.GetCompound("item") is NbtCompound inner)
                {
                    var it = legacy ? ToLegacyJava(inner, dv) : ToJava(inner, dv);
                    it.SetByte("Slot", (sbyte)e.GetInt("slot"));
                    items.Add(it);
                }
            tag["BlockEntityTag"] = new NbtCompound { { "Items", items } };
        }
        foreach (var bookKey in new[] { "minecraft:written_book_content", "minecraft:writable_book_content" })
        {
            if (comp.GetCompound(bookKey) is not NbtCompound book) continue;
            var pages = new NbtList(NbtType.String);
            foreach (var p in book.GetList("pages")?.OfType<NbtCompound>() ?? [])
            {
                string raw = p.GetString("raw") ?? "";
                pages.Add(new NbtString(bookKey.Contains("written") && legacy ? TextComponents.ToPlain(raw) : raw));
            }
            tag["pages"] = pages;
            if (bookKey.Contains("written"))
            {
                tag.SetString("author", book.GetString("author") ?? "");
                tag.SetString("title", book.GetCompound("title")?.GetString("raw") ?? "");
            }
        }
        return tag;
    }

    /// <summary>Pre-flattening item stack (numeric id up to 1.7, string id 1.8–1.12) with Damage carrying the data value.</summary>
    public static NbtCompound ToLegacyJava(NbtCompound canonical, int dataVersion, bool numericId = false)
    {
        canonical = Fit(canonical, LegacyRules(numericId || dataVersion is > 0 and < 100));
        string id = Id(canonical);
        var (num, data) = LegacyItemNumber(id);
        var comp = Components(canonical);
        ReportUnmapped(comp, TagHandled, ConversionLog.Current?.Target.Name ?? "Java 1.12");
        if (comp?.GetLongOrNull("minecraft:damage") is long dmg) data = (int)dmg;
        var c = new NbtCompound();
        // Modded items kept for string-id versions keep their own id (numbered versions got a placeholder in Fit).
        bool modded = !id.StartsWith("minecraft:", StringComparison.Ordinal);
        if (numericId || dataVersion is > 0 and < 100) c.SetShort("id", (short)num);
        else c.SetString("id", modded ? id : Legacy112Name(num) ?? id);
        c.SetByte("Count", (sbyte)Math.Clamp(Count(canonical), 1, 127));
        c.SetShort("Damage", (short)data);
        var tag = ComponentsToTag(comp, dataVersion, legacy: true);
        if (tag.Count > 0) c["tag"] = tag;
        return c;
    }

    static readonly ConcurrentDictionary<string, (int Id, int Data, bool Exact)> LegacyNumberCache = new(StringComparer.Ordinal);

    /// <summary>Numeric id and data of a canonical item for pre-1.13 targets (blocks via the block table).</summary>
    public static (int Id, int Data) LegacyItemNumber(string canonical)
    {
        var (id, data, _) = LegacyLookup(canonical);
        return (id, data);
    }

    /// <summary>The 1.12 number of an item; Exact is false when it is the number of a stand-in block or paper.</summary>
    static (int Id, int Data, bool Exact) LegacyLookup(string canonical) => LegacyNumberCache.GetOrAdd(canonical, id =>
    {
        foreach (var name in new[] { id, Renamed(id, 1631) })
            if (JavaLegacyData.ItemReverse(name) is (int num, int dmg)) return (num, dmg, true);
        foreach (var (oldName, newName, _) in Renames)
            if (newName == id && JavaLegacyData.ItemReverse(oldName) is (int n2, int d2)) return (n2, d2, true);
        if (JavaBlockData.Latest.Blocks.ContainsKey(id))
        {
            var (bid, bdata) = JavaBlocks.ToLegacy(World.BlockState.Get(id));
            bool exact = JavaLegacyData.ItemLookup(bid, bdata) is string back && CanonicalName(back) == id;
            return (bid, bdata, exact);
        }
        return (339, 0, false); // paper
    });

    /// <summary>Numeric id and data for a Pocket Edition 0.x / pre-1.8 target, logging items the version lacks.</summary>
    public static (int Id, int Data) LegacyItemNumberFitted(NbtCompound canonical)
    {
        var fitted = Fit(canonical, LegacyRules(numeric: true));
        return LegacyItemNumber(Id(fitted));
    }

    static string? Legacy112Name(int numericId) => JavaBlockData.LegacyItemNames.GetValueOrDefault(numericId);
}
