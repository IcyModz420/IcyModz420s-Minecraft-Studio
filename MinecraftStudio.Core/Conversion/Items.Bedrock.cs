// MinecraftStudio.Core/Conversion/Items.Bedrock.cs: bedrock for Items.
// Part of Items (main part in Items.cs).
using System.Collections.Concurrent;
using System.Text.Json;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public static partial class Items
{
    // ---------------------------------------------------------------- Bedrock

    /// <summary>Canonical item from a Bedrock item compound {Name, Count, Damage, tag, Block}.</summary>
    public static NbtCompound? FromBedrock(NbtCompound item)
    {
        string name = item.GetString("Name") ?? item.GetString("id") ?? "";
        if (name.Length == 0 && item["id"] is { IsNumeric: true } nid)
        {
            int num = (int)nid.AsLong;
            if (num == 0) return null;
            // Numbers from 256 up are items (numbered like Java's); below are blocks, and blocks above 255 are negative.
            if (num >= 256) return FromJava(item, 0);
            name = BedrockBlocks.LegacyName(num < 0 ? 255 - num : num) ?? "";
            // Pocket Edition 0.x and early Bedrock numbered their items like Java did: tools, food and the rest
            // come from the Java table (an unknown number becomes a named placeholder instead of vanishing).
            if (name.Length == 0) return FromJava(item, 0);
        }
        if (name.Length == 0 || name == "minecraft:air") return null;
        int damage = item.GetInt("Damage");
        int count = item.GetInt("Count", 1);
        var g = Geyser.Value;
        if (g.LegacyBedrockComplex.TryGetValue((name, damage), out var upgraded)) { name = upgraded; damage = 0; }
        else if (g.LegacyBedrockNames.TryGetValue(name, out var simple)) name = simple;
        string? java = g.BedrockToJava.GetValueOrDefault((name, damage)) ?? g.BedrockAnyData.GetValueOrDefault(name);
        if (java == null && item.GetCompound("Block") is NbtCompound block)
            java = CanonicalReader.BedrockToJava(BedrockBlocks.FromPaletteNbt(block)).Name;
        java ??= JavaBlockData.Latest.Items.Contains(name) ? name : null;
        if (java == null)
        {
            // Numbered items the Bedrock tables do not know: the classic (Java) numbering, else a named placeholder.
            if (item["id"] is { IsNumeric: true }) return FromJava(item, 0);
            // Add-on items keep their id: a Bedrock target with the world's packs keeps them, other targets report them.
            if (!name.StartsWith("minecraft:", StringComparison.Ordinal) && name.Contains(':')) java = name;
            else
            {
                ConversionLog.Note(LossKind.Item, name, "unknown Bedrock item: became a placeholder (paper named after it)", count);
                return Placeholder(Make(name, count, null), "Unknown Bedrock item");
            }
        }
        java = CanonicalName(java);
        var comp = new NbtCompound();
        var tag = item.GetCompound("tag");
        if (tag != null)
        {
            if (tag.GetLongOrNull("Damage") is long d && d > 0) comp.SetInt("minecraft:damage", (int)d);
            if (tag.GetCompound("display") is NbtCompound display)
            {
                if (display.GetString("Name") is string n) comp["minecraft:custom_name"] = new NbtString(TextComponents.FromFormatted(n, bedrock: true));
                if (display.GetList("Lore") is NbtList lore)
                {
                    var l = new NbtList(NbtType.String);
                    foreach (var line in lore) l.Add(new NbtString(TextComponents.FromFormatted(line is NbtString s ? s.Value : "", bedrock: true)));
                    comp["minecraft:lore"] = l;
                }
            }
            if (tag.GetCompound("Fireworks") is NbtCompound fw)
            {
                var explosions = new NbtList(NbtType.Compound);
                foreach (var e in fw.GetList("Explosions")?.OfType<NbtCompound>() ?? [])
                    explosions.Add(new NbtCompound
                    {
                        { "shape", new NbtString(FireworkShapes[Math.Clamp(e.GetInt("FireworkType"), 0, 4)]) },
                        { "colors", new NbtIntArray([.. ((e["FireworkColor"] as NbtByteArray)?.Value ?? []).Select(b => DyeRgb(b))]) },
                        { "fade_colors", new NbtIntArray([.. ((e["FireworkFade"] as NbtByteArray)?.Value ?? []).Select(b => DyeRgb(b))]) },
                        { "has_trail", new NbtByte(e.GetBool("FireworkTrail")) }, { "has_twinkle", new NbtByte(e.GetBool("FireworkFlicker")) },
                    });
                comp["minecraft:fireworks"] = new NbtCompound { { "flight_duration", new NbtByte((sbyte)fw.GetInt("Flight", 1)) }, { "explosions", explosions } };
            }
            if (tag.GetList("Patterns") is NbtList patterns) comp["minecraft:banner_patterns"] = BannerPatternsFromTag(patterns, legacyColors: true);
            if (tag.GetCompound("Trim") is NbtCompound trim)
                comp["minecraft:trim"] = new NbtCompound
                {
                    { "material", new NbtString("minecraft:" + (trim.GetString("Material") ?? "")) },
                    { "pattern", new NbtString("minecraft:" + (trim.GetString("Pattern") ?? "")) },
                };
            if (tag.GetList("ench") is NbtList ench)
            {
                var levels = new NbtCompound();
                foreach (var e in ench.OfType<NbtCompound>())
                {
                    int eid = e.GetInt("id");
                    if (eid >= 0 && eid < BedrockEnchantments.Length) levels.SetInt("minecraft:" + BedrockEnchantments[eid], e.GetInt("lvl", 1));
                }
                if (levels.Count > 0) comp[java == "minecraft:enchanted_book" ? "minecraft:stored_enchantments" : "minecraft:enchantments"] = levels;
            }
            if (tag.GetLongOrNull("RepairCost") is long rc && rc > 0) comp.SetInt("minecraft:repair_cost", (int)rc);
            if (tag.GetLongOrNull("customColor") is long cc) comp.SetInt("minecraft:dyed_color", (int)(cc & 0xFFFFFF));
            if (tag.GetBool("Unbreakable")) comp["minecraft:unbreakable"] = new NbtCompound();
            if (tag.GetList("Items") is NbtList inner && java.EndsWith("shulker_box"))
            {
                var container = new NbtList(NbtType.Compound);
                foreach (var it in inner.OfType<NbtCompound>())
                    if (FromBedrock(it) is NbtCompound ci) container.Add(new NbtCompound { { "slot", new NbtInt(it.GetInt("Slot")) }, { "item", ci } });
                comp["minecraft:container"] = container;
            }
            if (tag.GetList("pages") is NbtList pages)
            {
                var p = new NbtList(NbtType.Compound);
                foreach (var page in pages.OfType<NbtCompound>()) p.Add(new NbtCompound { { "raw", new NbtString(java == "minecraft:written_book" ? TextComponents.FromPlain(page.GetString("text") ?? "") : page.GetString("text") ?? "") } });
                if (java == "minecraft:written_book")
                {
                    var book = new NbtCompound { { "pages", p }, { "author", new NbtString(tag.GetString("author") ?? "") } };
                    book["title"] = new NbtCompound { { "raw", new NbtString(tag.GetString("title") ?? "") } };
                    comp["minecraft:written_book_content"] = book;
                }
                else comp["minecraft:writable_book_content"] = new NbtCompound { { "pages", p } };
            }
            if (tag.GetLongOrNull("map_uuid") is long mapId) comp.SetInt("minecraft:map_id", ConversionLog.Current?.MapIdForBedrock(mapId) ?? (int)mapId);
        }
        if (java is "minecraft:potion" or "minecraft:splash_potion" or "minecraft:lingering_potion" or "minecraft:tipped_arrow" && damage > 0)
        {
            int idx = java == "minecraft:tipped_arrow" ? damage - 1 : damage;
            if (idx >= 0 && idx < BedrockPotions.Length) comp["minecraft:potion_contents"] = new NbtCompound { { "potion", new NbtString("minecraft:" + BedrockPotions[idx]) } };
        }
        return Make(java, count, comp);
    }

    /// <summary>Firework colours of the 16 dyes (Java stores RGB, Bedrock the dye index in Bedrock dye order: black first).</summary>
    static readonly int[] FireworkDyeRgb =
    [
        0x1E1B1B, 0xB3312C, 0x3B511A, 0x51301A, 0x253192, 0x7B2FBE, 0x287697, 0xABABAB,
        0x434343, 0xD88198, 0x41CD34, 0xDECF2A, 0x6689D3, 0xC354CD, 0xEB8844, 0xF0F0F0,
    ];

    static int DyeRgb(byte index) => FireworkDyeRgb[index & 15];

    static byte DyeIndexForRgb(int rgb)
    {
        int best = 0;
        long bestD = long.MaxValue;
        for (int i = 0; i < 16; i++)
        {
            int c = FireworkDyeRgb[i];
            long dr = ((rgb >> 16) & 255) - ((c >> 16) & 255), dg = ((rgb >> 8) & 255) - ((c >> 8) & 255), db = (rgb & 255) - (c & 255);
            long d = dr * dr + dg * dg + db * db;
            if (d < bestD) { bestD = d; best = i; }
        }
        return (byte)best;
    }

    /// <summary>
    /// Older Bedrock targets: the item name (and data value) used before the item flattening, which every later version
    /// still upgrades on load; the "Block" compound is dropped so the game derives the block from name and data.
    /// </summary>
    public static void DowngradeBedrock(NbtCompound item)
    {
        if (item.GetString("Name") is not string name) return;
        if (Geyser.Value.CurrentToLegacy.TryGetValue(name, out var old))
        {
            item.SetString("Name", old.Name);
            if (old.Data >= 0) item.SetShort("Damage", (short)old.Data);
        }
        item.Remove("Block");
    }

    /// <summary>
    /// Bedrock before 1.2.13 and Pocket Edition 0.9–0.16 keep items by number ({id, Count, Damage}), with durability in
    /// Damage. A block item keeps its block's number (blocks past 255 count down from -1, as the reader expects); other
    /// items are numbered like Java 1.12's, as those versions did. An item they did not have becomes paper named after
    /// it. Call after <see cref="DowngradeBedrock"/> (the names are then the ones the reverse tables know).
    /// </summary>
    public static void NumberBedrock(NbtCompound item)
    {
        if (item.GetString("Name") is not string name) return;
        int data = item.GetInt("Damage");
        item.Remove("Name");
        var tag = item.GetCompound("tag");
        // Durability goes back to the item's own Damage.
        if (tag?.GetLongOrNull("Damage") is long used && used > 0 && data == 0)
        {
            data = (int)used;
            tag.Remove("Damage");
            if (tag.Count == 0) item.Remove("tag");
        }
        item["Damage"] = new NbtShort((short)Math.Clamp(data, short.MinValue, short.MaxValue));
        if (name.Length == 0 || name == "minecraft:air") { item["id"] = new NbtShort(0); return; }
        if (BedrockBlocks.LegacyNumber(name) is int block)
        {
            item["id"] = new NbtShort((short)(block <= 255 ? block : 255 - block));
            return;
        }
        var g = Geyser.Value;
        string current = g.LegacyBedrockComplex.TryGetValue((name, data), out var complex) ? complex : g.LegacyBedrockNames.TryGetValue(name, out var simple) ? simple : name;
        string? java = g.BedrockToJava.TryGetValue((current, 0), out var j) ? j : g.BedrockAnyData.TryGetValue(current, out var any) ? any : null;
        if (java != null && LegacyLookup(java) is (int id, int legacyData, true) && id >= 256)
        {
            item["id"] = new NbtShort((short)id);
            if (legacyData > 0 && complex != null) item["Damage"] = new NbtShort((short)legacyData);
            return;
        }
        // Not in those versions: paper named after it, so it can still be told apart.
        item["id"] = new NbtShort(339);
        item["Damage"] = new NbtShort(0);
        tag = item.GetCompound("tag") ?? new NbtCompound();
        var display = tag.GetCompound("display") ?? new NbtCompound();
        if (display.GetString("Name") == null) display.SetString("Name", ConversionLog.Friendly(name));
        tag["display"] = display;
        item["tag"] = tag;
        ConversionLog.Note(LossKind.Item, name, "became paper named after it: that version has no such item", item.GetInt("Count", 1));
    }

    /// <summary>Bedrock item compound for a canonical item.</summary>
    public static NbtCompound ToBedrock(NbtCompound canonical)
    {
        canonical = Fit(canonical, BedrockRules(ConversionLog.Current?.Target));
        string id = Id(canonical);
        var g = Geyser.Value;
        ReportUnmapped(Components(canonical), BedrockHandled, ConversionLog.Current?.Target.Name ?? "Bedrock");
        var comp = Components(canonical);
        string name;
        int damage = 0;
        bool isBlock = false;
        if (g.JavaToBedrock.TryGetValue(id, out var hit)) (name, damage, isBlock) = hit;
        else name = id;
        if (comp?.GetCompound("minecraft:potion_contents")?.GetString("potion") is string potion)
        {
            int idx = Array.IndexOf(BedrockPotions, potion.Replace("minecraft:", ""));
            if (idx >= 0) damage = id == "minecraft:tipped_arrow" ? idx + 1 : idx;
        }
        var c = new NbtCompound();
        c.SetByte("Count", (sbyte)Math.Clamp(Count(canonical), 1, 127));
        c.SetShort("Damage", (short)damage);
        c.SetString("Name", name);
        c.SetByte("WasPickedUp", 0);
        if (isBlock && JavaBlockData.Latest.Blocks.ContainsKey(id) && BedrockJavaMap.ToBedrock(JavaBlocks.Canonicalize(World.BlockState.Get(id))) is World.BlockState bs)
            c["Block"] = BedrockBlocks.ToPaletteNbt(bs);
        var tag = new NbtCompound();
        if (comp != null)
        {
            if (comp.GetLongOrNull("minecraft:damage") is long dmg) tag.SetInt("Damage", (int)dmg);
            var display = new NbtCompound();
            if (comp["minecraft:custom_name"] is NbtTag n) display.SetString("Name", TextComponents.ToFormatted(TextComponents.FromJavaTag(n), bedrock: true));
            if (comp.GetList("minecraft:lore") is NbtList lore)
            {
                var l = new NbtList(NbtType.String);
                foreach (var line in lore) l.Add(new NbtString(TextComponents.ToFormatted(TextComponents.FromJavaTag(line), bedrock: true)));
                display["Lore"] = l;
            }
            if (display.Count > 0) tag["display"] = display;
            var ench = new NbtList(NbtType.Compound);
            foreach (var key in new[] { "minecraft:enchantments", "minecraft:stored_enchantments" })
            {
                if (comp.GetCompound(key) is not NbtCompound en) continue;
                foreach (var (e, lvl) in en.GetCompound("levels") ?? en)
                {
                    int eid = Array.IndexOf(BedrockEnchantments, e.Replace("minecraft:", ""));
                    if (eid < 0)
                    {
                        ConversionLog.Note(LossKind.Item, "enchantment " + e, "removed: Bedrock has no such enchantment", lost: true);
                        continue;
                    }
                    if (!lvl.IsNumeric) continue;
                    ench.Add(new NbtCompound { { "id", new NbtShort((short)eid) }, { "lvl", new NbtShort((short)lvl.AsLong) } });
                }
            }
            if (ench.Count > 0) tag["ench"] = ench;
            if (comp.GetLongOrNull("minecraft:repair_cost") is long rc) tag.SetInt("RepairCost", (int)rc);
            if (comp.GetLongOrNull("minecraft:dyed_color") is long color) tag.SetInt("customColor", unchecked((int)(0xFF000000u | (uint)color)));
            else if (comp.GetCompound("minecraft:dyed_color")?.GetLongOrNull("rgb") is long rgb) tag.SetInt("customColor", unchecked((int)(0xFF000000u | (uint)rgb)));
            if (comp.GetCompound("minecraft:fireworks") is NbtCompound fireworks)
            {
                var explosions = new NbtList(NbtType.Compound);
                foreach (var e in fireworks.GetList("explosions")?.OfType<NbtCompound>() ?? [])
                    explosions.Add(new NbtCompound
                    {
                        { "FireworkType", new NbtByte((sbyte)Math.Max(0, Array.IndexOf(FireworkShapes, (e.GetString("shape") ?? "small_ball").Replace("minecraft:", "")))) },
                        { "FireworkColor", new NbtByteArray([.. (e.GetInts("colors") ?? []).Select(DyeIndexForRgb)]) },
                        { "FireworkFade", new NbtByteArray([.. (e.GetInts("fade_colors") ?? []).Select(DyeIndexForRgb)]) },
                        { "FireworkFlicker", new NbtByte(e.GetBool("has_twinkle")) }, { "FireworkTrail", new NbtByte(e.GetBool("has_trail")) },
                    });
                tag["Fireworks"] = new NbtCompound { { "Flight", new NbtByte((sbyte)fireworks.GetInt("flight_duration", 1)) }, { "Explosions", explosions } };
            }
            if (comp.GetList("minecraft:banner_patterns") is NbtList bannerPatterns) tag["Patterns"] = BannerPatternsToTag(bannerPatterns, legacyColors: true);
            if (comp.GetCompound("minecraft:trim") is NbtCompound trim)
                tag["Trim"] = new NbtCompound
                {
                    { "Material", new NbtString((trim.GetString("material") ?? "").Replace("minecraft:", "")) },
                    { "Pattern", new NbtString((trim.GetString("pattern") ?? "").Replace("minecraft:", "")) },
                };
            if (comp.GetString("minecraft:base_color") is string baseColor && id == "minecraft:shield")
                tag.SetInt("Base", 15 - Math.Max(0, Array.IndexOf(StateEdit.Colors, baseColor)));
            if (comp.ContainsKey("minecraft:unbreakable")) tag.SetByte("Unbreakable", 1);
            if (comp.GetList("minecraft:container") is NbtList cont)
            {
                var items = new NbtList(NbtType.Compound);
                foreach (var e in cont.OfType<NbtCompound>())
                    if (e.GetCompound("item") is NbtCompound inner)
                    {
                        var it = ToBedrock(inner);
                        it.SetByte("Slot", (sbyte)e.GetInt("slot"));
                        items.Add(it);
                    }
                tag["Items"] = items;
            }
            foreach (var bookKey in new[] { "minecraft:written_book_content", "minecraft:writable_book_content" })
            {
                if (comp.GetCompound(bookKey) is not NbtCompound book) continue;
                var pages = new NbtList(NbtType.Compound);
                foreach (var p in book.GetList("pages")?.OfType<NbtCompound>() ?? [])
                    pages.Add(new NbtCompound { { "photoname", new NbtString("") }, { "text", new NbtString(TextComponents.ToPlain(p.GetString("raw") ?? "")) } });
                tag["pages"] = pages;
                if (bookKey.Contains("written"))
                {
                    tag.SetString("author", book.GetString("author") ?? "");
                    tag.SetString("title", book.GetCompound("title")?.GetString("raw") ?? "");
                }
            }
            if (comp.GetLongOrNull("minecraft:map_id") is long map) tag.SetLong("map_uuid", map);
        }
        if (tag.Count > 0) c["tag"] = tag;
        return c;
    }
}
