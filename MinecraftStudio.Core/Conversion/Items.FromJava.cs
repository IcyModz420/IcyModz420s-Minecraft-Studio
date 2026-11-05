// MinecraftStudio.Core/Conversion/Items.FromJava.cs: from java for Items.
// Part of Items (main part in Items.cs).
using System.Collections.Concurrent;
using System.Text.Json;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public static partial class Items
{
    // ---------------------------------------------------------------- Java (all eras) -> canonical

    /// <summary>Canonical item from a Java item stack of the given data version (0 for pre-1.9 without one).</summary>
    public static NbtCompound? FromJava(NbtCompound item, int dataVersion)
    {
        var idTag = item["id"];
        if (idTag == null) return null;
        int damage = item.GetInt("Damage");
        int count = item.ContainsKey("count") ? item.GetInt("count", 1) : item.GetInt("Count", 1);
        string id;
        var tag = item.GetCompound("tag");
        bool legacy = idTag.IsNumeric || dataVersion is > 0 and < 1451 || (dataVersion == 0 && item.ContainsKey("Damage"));
        if (idTag.IsNumeric || legacy)
        {
            string? name = idTag.IsNumeric
                ? JavaLegacyData.ItemLookup((int)idTag.AsLong, damage)
                : LegacyItemByName(((NbtString)idTag).Value, damage);
            if (name == null) return UnknownNumbered(idTag, ((NbtString?)(idTag as NbtString))?.Value, damage, count);
            id = CanonicalName(name);
        }
        else id = CanonicalName(((NbtString)idTag).Value);
        if (id is "minecraft:air") return null;

        var comp = item.GetCompound("components")?.Clone() as NbtCompound ?? new NbtCompound();
        NormalizeComponents(comp);
        if (tag != null) TagToComponents(tag, comp, legacy, dataVersion);
        // Pre-1.13 durability lived in Damage; for items with a data meaning the flattened name already used it.
        if (legacy && damage > 0 && IsDamageable(id)) comp.SetInt("minecraft:damage", damage);
        return Restore(comp, count) ?? Make(id, count, comp);
    }

    /// <summary>1.8–1.12 item names (string id + damage) through the numeric table.</summary>
    static string? LegacyItemByName(string name, int damage)
    {
        name = Ns(name);
        // legacy.json is keyed by number; find the number of this 1.12 name via the block/item tables.
        var numeric = LegacyItemNumbers.Value.GetValueOrDefault(name, -1);
        if (numeric >= 0) return JavaLegacyData.ItemLookup(numeric, damage);
        return name;
    }

    static readonly Lazy<Dictionary<string, int>> LegacyItemNumbers = new(() =>
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, id) in JavaBlockData.LegacyItemIds) d.TryAdd(name, id);
        return d;
    });

    static bool IsDamageable(string id) =>
        id.EndsWith("_sword") || id.EndsWith("_pickaxe") || id.EndsWith("_axe") || id.EndsWith("_shovel") || id.EndsWith("_hoe")
        || id.EndsWith("_helmet") || id.EndsWith("_chestplate") || id.EndsWith("_leggings") || id.EndsWith("_boots")
        || id is "minecraft:bow" or "minecraft:crossbow" or "minecraft:fishing_rod" or "minecraft:shears" or "minecraft:flint_and_steel"
            or "minecraft:shield" or "minecraft:elytra" or "minecraft:trident" or "minecraft:carrot_on_a_stick" or "minecraft:warped_fungus_on_a_stick"
            or "minecraft:mace" or "minecraft:brush" or "minecraft:turtle_helmet" or "minecraft:wolf_armor";

    static void TagToComponents(NbtCompound tag, NbtCompound comp, bool legacy, int dv)
    {
        var rest = (NbtCompound)tag.Clone();
        if (rest.Remove("Damage") && tag.GetInt("Damage") > 0) comp.SetInt("minecraft:damage", tag.GetInt("Damage"));
        if (tag.GetCompound("display") is NbtCompound display)
        {
            if (display["Name"] is NbtTag name) comp["minecraft:custom_name"] = new NbtString(legacy || dv < 1451 ? TextComponents.FromPlain(name is NbtString s ? s.Value : name.ToString()) : TextComponents.FromJavaTag(name));
            if (display.GetList("Lore") is NbtList lore)
            {
                var l = new NbtList(NbtType.String);
                foreach (var line in lore) l.Add(new NbtString(dv < 1803 ? TextComponents.FromPlain(line is NbtString ls ? ls.Value : line.ToString()) : TextComponents.FromJavaTag(line)));
                comp["minecraft:lore"] = l;
            }
            if (display.GetLongOrNull("color") is long color) comp["minecraft:dyed_color"] = new NbtInt((int)color);
            rest.Remove("display");
        }
        foreach (var (key, component) in new[] { ("Enchantments", "minecraft:enchantments"), ("ench", "minecraft:enchantments"), ("StoredEnchantments", "minecraft:stored_enchantments") })
        {
            if (tag.GetList(key) is not NbtList list) continue;
            var levels = new NbtCompound();
            foreach (var e in list.OfType<NbtCompound>())
            {
                string? name = e["id"] switch
                {
                    NbtString s => Ns(s.Value),
                    { IsNumeric: true } n => JavaLegacyEnchantments.TryGetValue((int)n.AsLong, out var en) ? "minecraft:" + en : null,
                    _ => null,
                };
                if (name != null) levels.SetInt(name, e.GetInt("lvl", 1));
            }
            if (levels.Count > 0) comp[component] = levels;
            rest.Remove(key);
        }
        if (tag.GetString("Potion") is string potion) { comp["minecraft:potion_contents"] = new NbtCompound { { "potion", new NbtString(Ns(potion)) } }; rest.Remove("Potion"); }
        if (tag.GetBool("Unbreakable")) { comp["minecraft:unbreakable"] = new NbtCompound(); rest.Remove("Unbreakable"); }
        if (tag.GetLongOrNull("RepairCost") is long rc) { if (rc > 0) comp.SetInt("minecraft:repair_cost", (int)rc); rest.Remove("RepairCost"); }
        if (tag.GetCompound("BlockEntityTag") is NbtCompound bet)
        {
            var other = (NbtCompound)bet.Clone();
            if (bet.GetList("Items") is NbtList inner)
            {
                var container = new NbtList(NbtType.Compound);
                foreach (var it in inner.OfType<NbtCompound>())
                    if (FromJava(it, dv) is NbtCompound ci)
                        container.Add(new NbtCompound { { "slot", new NbtInt(it.GetInt("Slot")) }, { "item", ci } });
                comp["minecraft:container"] = container;
                other.Remove("Items");
            }
            if (bet.GetList("Patterns") is NbtList patterns)
            {
                comp["minecraft:banner_patterns"] = BannerPatternsFromTag(patterns, legacy || dv < 1451);
                other.Remove("Patterns");
            }
            if (bet["Base"] is { IsNumeric: true } baseColor)
            {
                comp.SetString("minecraft:base_color", StateEdit.Colors[(legacy || dv < 1451 ? 15 - (int)baseColor.AsLong : (int)baseColor.AsLong) & 15]);
                other.Remove("Base");
            }
            if (bet.GetList("sherds") is NbtList sherds) { comp["minecraft:pot_decorations"] = sherds.Clone(); other.Remove("sherds"); }
            other.Remove("id");
            if (other.Count > 0) comp["minecraft:block_entity_data"] = other;
            rest.Remove("BlockEntityTag");
        }
        if (tag.GetCompound("SkullOwner") is NbtCompound owner) { comp["minecraft:profile"] = ProfileFromSkullOwner(owner); rest.Remove("SkullOwner"); }
        else if (tag.GetString("SkullOwner") is string ownerName && ownerName.Length > 0) { comp["minecraft:profile"] = new NbtCompound { { "name", new NbtString(ownerName) } }; rest.Remove("SkullOwner"); }
        if (tag.GetCompound("Trim") is NbtCompound trim) { comp["minecraft:trim"] = new NbtCompound { { "material", new NbtString(trim.GetString("material") ?? "") }, { "pattern", new NbtString(trim.GetString("pattern") ?? "") } }; rest.Remove("Trim"); }
        if (tag["CustomModelData"] is { IsNumeric: true } model) { comp["minecraft:custom_model_data"] = ModelData((float)model.AsDouble); rest.Remove("CustomModelData"); }
        if (tag.GetCompound("Fireworks") is NbtCompound fw)
        {
            var explosions = new NbtList(NbtType.Compound);
            foreach (var e in fw.GetList("Explosions")?.OfType<NbtCompound>() ?? []) explosions.Add(ExplosionFromTag(e));
            comp["minecraft:fireworks"] = new NbtCompound { { "flight_duration", new NbtByte((sbyte)fw.GetInt("Flight", 1)) }, { "explosions", explosions } };
            rest.Remove("Fireworks");
        }
        if (tag.GetCompound("Explosion") is NbtCompound explosion) { comp["minecraft:firework_explosion"] = ExplosionFromTag(explosion); rest.Remove("Explosion"); }
        if (tag.GetCompound("EntityTag") is NbtCompound entityTag) { comp["minecraft:entity_data"] = entityTag.Clone(); rest.Remove("EntityTag"); }
        if (tag.GetString("instrument") is string instrument) { comp.SetString("minecraft:instrument", instrument); rest.Remove("instrument"); }
        if (tag["CustomPotionColor"] is { IsNumeric: true } potionColor)
        {
            var pc = comp.GetCompound("minecraft:potion_contents") ?? new NbtCompound();
            pc.SetInt("custom_color", (int)potionColor.AsLong);
            comp["minecraft:potion_contents"] = pc;
            rest.Remove("CustomPotionColor");
        }
        if (tag.GetList("pages") is NbtList pages)
        {
            bool written = tag.ContainsKey("author") || tag.ContainsKey("title");
            var p = new NbtList(NbtType.Compound);
            foreach (var page in pages) p.Add(new NbtCompound { { "raw", new NbtString(written ? TextComponents.FromJavaTag(page) : page is NbtString ps ? ps.Value : page.ToString()) } });
            if (written)
            {
                var book = new NbtCompound { { "pages", p }, { "author", new NbtString(tag.GetString("author") ?? "") } };
                book["title"] = new NbtCompound { { "raw", new NbtString(tag.GetString("title") ?? "") } };
                comp["minecraft:written_book_content"] = book;
            }
            else comp["minecraft:writable_book_content"] = new NbtCompound { { "pages", p } };
            rest.Remove("pages"); rest.Remove("author"); rest.Remove("title"); rest.Remove("generation"); rest.Remove("resolved");
        }
        if (tag.GetLongOrNull("map") is long map) { comp.SetInt("minecraft:map_id", (int)map); rest.Remove("map"); }
        if (rest.Count > 0) comp["minecraft:custom_data"] = rest;
    }

    static NbtCompound ProfileFromSkullOwner(NbtCompound owner)
    {
        var p = new NbtCompound();
        if (owner.GetString("Name") is string n && n.Length > 0) p.SetString("name", n);
        if (owner["Id"] is NbtIntArray { Value.Length: 4 } ints) p["id"] = ints.Clone();
        else if (owner.GetString("Id") is string idText && Guid.TryParse(idText, out var g)) p["id"] = new NbtIntArray(Players.UuidToInts(g));
        var props = new NbtList(NbtType.Compound);
        foreach (var t in owner.GetCompound("Properties")?.GetList("textures")?.OfType<NbtCompound>() ?? [])
        {
            var e = new NbtCompound { { "name", new NbtString("textures") }, { "value", new NbtString(t.GetString("Value") ?? "") } };
            if (t.GetString("Signature") is string sig) e.SetString("signature", sig);
            props.Add(e);
        }
        if (props.Count > 0) p["properties"] = props;
        return p;
    }

    static NbtCompound SkullOwnerFromProfile(NbtCompound profile, int dv)
    {
        var o = new NbtCompound();
        if (profile.GetString("name") is string n) o.SetString("Name", n);
        if (profile["id"] is NbtIntArray { Value.Length: 4 } ints)
        {
            if (dv >= 2514) o["Id"] = ints.Clone();
            else o.SetString("Id", Players.UuidFromInts(ints.Value).ToString());
        }
        var textures = new NbtList(NbtType.Compound);
        foreach (var p in profile.GetList("properties")?.OfType<NbtCompound>() ?? [])
            if (p.GetString("name") == "textures")
            {
                var t = new NbtCompound { { "Value", new NbtString(p.GetString("value") ?? "") } };
                if (p.GetString("signature") is string sig) t.SetString("Signature", sig);
                textures.Add(t);
            }
        if (textures.Count > 0) o["Properties"] = new NbtCompound { { "textures", textures } };
        return o;
    }

    static readonly string[] FireworkShapes = ["small_ball", "large_ball", "star", "creeper", "burst"];

    static NbtCompound ExplosionFromTag(NbtCompound e) => new()
    {
        { "shape", new NbtString(FireworkShapes[Math.Clamp(e.GetInt("Type"), 0, 4)]) },
        { "colors", new NbtIntArray(e.GetInts("Colors") ?? []) }, { "fade_colors", new NbtIntArray(e.GetInts("FadeColors") ?? []) },
        { "has_trail", new NbtByte(e.GetBool("Trail")) }, { "has_twinkle", new NbtByte(e.GetBool("Flicker")) },
    };

    static NbtCompound ExplosionToTag(NbtCompound e) => new()
    {
        { "Type", new NbtByte((sbyte)Math.Max(0, Array.IndexOf(FireworkShapes, (e.GetString("shape") ?? "small_ball").Replace("minecraft:", "")))) },
        { "Colors", new NbtIntArray(e.GetInts("colors") ?? []) }, { "FadeColors", new NbtIntArray(e.GetInts("fade_colors") ?? []) },
        { "Trail", new NbtByte(e.GetBool("has_trail")) }, { "Flicker", new NbtByte(e.GetBool("has_twinkle")) },
    };

    static NbtList BannerPatternsFromTag(NbtList patterns, bool legacyColors)
    {
        var list = new NbtList(NbtType.Compound);
        foreach (var p in patterns.OfType<NbtCompound>())
        {
            if (BlockEntities.PatternForCode(p.GetString("Pattern") ?? "") is not string pattern) continue;
            int color = p.GetInt("Color");
            list.Add(new NbtCompound { { "pattern", new NbtString(pattern) }, { "color", new NbtString(StateEdit.Colors[(legacyColors ? 15 - color : color) & 15]) } });
        }
        return list;
    }

    static NbtList BannerPatternsToTag(NbtList patterns, bool legacyColors)
    {
        var list = new NbtList(NbtType.Compound);
        foreach (var p in patterns.OfType<NbtCompound>())
        {
            if (BlockEntities.CodeForPattern(p["pattern"] is NbtString ps ? ps.Value : "") is not string code) continue;
            int color = Math.Max(0, Array.IndexOf(StateEdit.Colors, p.GetString("color") ?? "white"));
            list.Add(new NbtCompound { { "Pattern", new NbtString(code) }, { "Color", new NbtInt(legacyColors ? 15 - color : color) } });
        }
        return list;
    }

    static NbtCompound ModelData(float value) => new() { { "floats", new NbtList(NbtType.Float) { new NbtFloat(value) } } };

    /// <summary>Components a 1.20.5+ item may hold that the newest version stores differently (canonical = newest layout).</summary>
    static readonly string[] TooltipFlagComponents =
    [
        "minecraft:trim", "minecraft:unbreakable", "minecraft:can_place_on", "minecraft:can_break", "minecraft:attribute_modifiers",
        "minecraft:jukebox_playable", "minecraft:dyed_color", "minecraft:enchantments", "minecraft:stored_enchantments",
    ];

    /// <summary>
    /// Brings components of any 1.20.5+ version into the newest layout: enchantment levels unwrapped, dyed colour as a
    /// number, model data as a compound, show_in_tooltip flags folded into tooltip_display, text as canonical JSON.
    /// </summary>
    static void NormalizeComponents(NbtCompound comp)
    {
        if (comp.Count == 0) return;
        var hidden = new List<string>();
        foreach (var key in TooltipFlagComponents)
        {
            if (comp.GetCompound(key) is not NbtCompound c) continue;
            if (c.ContainsKey("show_in_tooltip"))
            {
                if (!c.GetBool("show_in_tooltip", true)) hidden.Add(key);
                c.Remove("show_in_tooltip");
            }
            switch (key)
            {
                case "minecraft:enchantments" or "minecraft:stored_enchantments" when c.GetCompound("levels") is NbtCompound levels:
                    comp[key] = levels.Clone();
                    break;
                case "minecraft:dyed_color" when c["rgb"] is { IsNumeric: true } rgb:
                    comp.SetInt(key, (int)rgb.AsLong);
                    break;
                case "minecraft:attribute_modifiers" when c.GetList("modifiers") is NbtList modifiers:
                    comp[key] = modifiers.Clone();
                    break;
                case "minecraft:can_place_on" or "minecraft:can_break" when c["predicates"] is NbtTag predicates:
                    comp[key] = predicates.Clone();
                    break;
            }
        }
        if (comp["minecraft:custom_model_data"] is { IsNumeric: true } cmd) comp["minecraft:custom_model_data"] = ModelData((float)cmd.AsDouble);
        bool hideAll = comp.Remove("minecraft:hide_tooltip");
        if (comp.Remove("minecraft:hide_additional_tooltip")) hidden.AddRange(["minecraft:potion_contents", "minecraft:written_book_content", "minecraft:fireworks", "minecraft:firework_explosion", "minecraft:banner_patterns", "minecraft:container", "minecraft:bundle_contents", "minecraft:map_id", "minecraft:instrument"]);
        if (hideAll || hidden.Count > 0)
        {
            var td = comp.GetCompound("minecraft:tooltip_display") ?? new NbtCompound();
            if (hideAll) td.SetByte("hide_tooltip", 1);
            var list = td.GetList("hidden_components") ?? new NbtList(NbtType.String);
            foreach (var h in hidden.Distinct()) list.Add(new NbtString(h));
            if (list.Count > 0) td["hidden_components"] = list;
            comp["minecraft:tooltip_display"] = td;
        }
        // From 25w02a a name or lore line can be plain text (a string that is not JSON): FromJavaTag tells them apart.
        foreach (var key in new[] { "minecraft:custom_name", "minecraft:item_name" })
            if (comp[key] is NbtTag t) comp.SetString(key, TextComponents.FromJavaTag(t));
        if (comp.GetList("minecraft:lore") is NbtList lore)
        {
            var l = new NbtList(NbtType.String);
            foreach (var line in lore) l.Add(new NbtString(TextComponents.FromJavaTag(line)));
            comp["minecraft:lore"] = l;
        }
        if (comp.GetCompound("minecraft:written_book_content") is NbtCompound book)
            foreach (var page in book.GetList("pages")?.OfType<NbtCompound>() ?? [])
                foreach (var k in new[] { "raw", "filtered" })
                    if (page[k] is NbtTag raw && raw is not NbtString) page.SetString(k, TextComponents.FromJavaTag(raw));
        foreach (var inner in comp.GetList("minecraft:container")?.OfType<NbtCompound>() ?? [])
            if (inner.GetCompound("item")?.GetCompound("components") is NbtCompound ic) NormalizeComponents(ic);
    }

    /// <summary>Components in the layout of a 1.20.5+ target (the reverse of <see cref="NormalizeComponents"/>).</summary>
    static void ComponentsForVersion(NbtCompound comp, int dv)
    {
        if (dv >= 4325) return;
        var td = comp.GetCompound("minecraft:tooltip_display");
        comp.Remove("minecraft:tooltip_display");
        var hidden = td?.GetList("hidden_components")?.OfType<NbtString>().Select(h => h.Value).ToHashSet() ?? [];
        if (td?.GetBool("hide_tooltip") == true) comp["minecraft:hide_tooltip"] = new NbtCompound();
        foreach (var key in new[] { "minecraft:enchantments", "minecraft:stored_enchantments" })
            if (comp.GetCompound(key) is NbtCompound en && !en.ContainsKey("levels"))
            {
                var wrapped = new NbtCompound { { "levels", en } };
                if (hidden.Contains(key)) wrapped.SetByte("show_in_tooltip", 0);
                comp[key] = wrapped;
            }
        if (comp["minecraft:dyed_color"] is { IsNumeric: true } rgb)
        {
            var d = new NbtCompound { { "rgb", new NbtInt((int)rgb.AsLong) } };
            if (hidden.Contains("minecraft:dyed_color")) d.SetByte("show_in_tooltip", 0);
            comp["minecraft:dyed_color"] = d;
        }
        if (comp["minecraft:attribute_modifiers"] is NbtList modifiers) comp["minecraft:attribute_modifiers"] = new NbtCompound { { "modifiers", modifiers } };
        foreach (var key in new[] { "minecraft:can_place_on", "minecraft:can_break" })
            if (comp[key] is NbtTag t && (t is not NbtCompound c || !c.ContainsKey("predicates"))) comp[key] = new NbtCompound { { "predicates", t is NbtList ? t : new NbtList(NbtType.Compound) { t } } };
        foreach (var key in new[] { "minecraft:trim", "minecraft:unbreakable", "minecraft:jukebox_playable" })
            if (hidden.Contains(key) && comp.GetCompound(key) is NbtCompound c) c.SetByte("show_in_tooltip", 0);
        if (hidden.Count > 0 && hidden.Overlaps(["minecraft:potion_contents", "minecraft:written_book_content", "minecraft:fireworks", "minecraft:banner_patterns", "minecraft:container"]))
            comp["minecraft:hide_additional_tooltip"] = new NbtCompound();
        if (dv < 4189 && comp.GetCompound("minecraft:custom_model_data") is NbtCompound cmd)
        {
            if (cmd.GetList("floats") is { Count: > 0 } floats && floats[0].IsNumeric) comp.SetInt("minecraft:custom_model_data", (int)floats[0].AsDouble);
            else comp.Remove("minecraft:custom_model_data");
        }
        if (dv < 4298 && comp.GetCompound("minecraft:written_book_content") is NbtCompound book)
            foreach (var page in book.GetList("pages")?.OfType<NbtCompound>() ?? [])
                foreach (var k in new[] { "raw", "filtered" })
                    if (page[k] is NbtTag raw && raw is not NbtString) page.SetString(k, TextComponents.FromJavaTag(raw));
    }
}
