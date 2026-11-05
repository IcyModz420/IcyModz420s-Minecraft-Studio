// McTests/GiveTests.cs: "give" — the /give command for an item, in each edition's and version's own syntax.
// Owns: GiveTests — Java 1.20.5+ components ("id[name=SNBT,…] count", count 1 left out, every component's SNBT reading
// back as itself), 1.13–1.20.4 "id{tag} count" (the tag reading back), 1.8–1.12 "id count damage {tag}", Bedrock
// "name count data" with its note about what Bedrock's /give can't add, the chat-length note; and every item of the
// players in the Java 1.16.5, 1.12.2 and Bedrock sample worlds gives a command naming that item.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

static class GiveTests
{
    public static void Run(string[] args)
    {
        // Java 1.20.5+: components.
        var sword = new NbtCompound
        {
            { "id", new NbtString("minecraft:diamond_sword") }, { "count", new NbtInt(1) },
            { "components", new NbtCompound
                {
                    { "minecraft:enchantments", new NbtCompound { { "levels", new NbtCompound { { "minecraft:sharpness", new NbtInt(5) } } } } },
                    { "minecraft:custom_name", new NbtString("{\"text\":\"Blade\",\"color\":\"gold\"}") },
                    { "minecraft:damage", new NbtInt(12) },
                }
            },
        };
        var c = GiveCommand.For(sword, ItemFormat.Components, bedrock: false)!;
        string inner = c.Command[(c.Command.IndexOf('[') + 1)..c.Command.LastIndexOf(']')];
        bool parsed = Components(inner, sword.GetCompound("components")!);
        Program.Check(c.Command.StartsWith("/give @p minecraft:diamond_sword[minecraft:enchantments=") && c.Command.EndsWith("]") && parsed && c.Note == null,
            $"1.20.5+: id[components] (count 1 left out), each component's SNBT reads back as itself: {c.Command}");
        sword.SetInt("count", 3);
        Program.Check(GiveCommand.For(sword, ItemFormat.Components, false)!.Command.EndsWith("] 3"), "1.20.5+: a count other than 1 is written after the components");

        // Java 1.13–1.20.4: id{tag} count.
        var tag = new NbtCompound { { "Damage", new NbtInt(3) }, { "Enchantments", new NbtList(NbtType.Compound) { new NbtCompound { { "id", new NbtString("minecraft:sharpness") }, { "lvl", new NbtShort(5) } } } } };
        var flat = new NbtCompound { { "id", new NbtString("minecraft:diamond_sword") }, { "Count", new NbtByte(2) }, { "tag", tag } };
        var f = GiveCommand.For(flat, ItemFormat.Flattened, false)!;
        string snbt = f.Command["/give @p minecraft:diamond_sword".Length..f.Command.LastIndexOf(' ')];
        Program.Check(f.Command.StartsWith("/give @p minecraft:diamond_sword{") && f.Command.EndsWith(" 2") && Snbt.Parse(snbt).ContentEquals(tag), $"1.13–1.20.4: id{{tag}} count, the tag reading back: {f.Command}");

        // Java 1.8–1.12: id count damage {tag}.
        var legacyTag = new NbtCompound { { "ench", new NbtList(NbtType.Compound) { new NbtCompound { { "id", new NbtShort(16) }, { "lvl", new NbtShort(5) } } } } };
        var named = new NbtCompound { { "id", new NbtString("minecraft:diamond_sword") }, { "Count", new NbtByte(1) }, { "Damage", new NbtShort(7) }, { "tag", legacyTag } };
        var n = GiveCommand.For(named, ItemFormat.Named, false)!;
        Program.Check(n.Command.StartsWith("/give @p minecraft:diamond_sword 1 7 {") && Snbt.Parse(n.Command["/give @p minecraft:diamond_sword 1 7 ".Length..]).ContentEquals(legacyTag),
            $"1.8–1.12: id count damage {{tag}}: {n.Command}");

        // Bedrock: name count data, and the note when the item carries more.
        var bed = new NbtCompound { { "Name", new NbtString("minecraft:wool") }, { "Count", new NbtByte(16) }, { "Damage", new NbtShort(14) }, { "tag", new NbtCompound { { "display", new NbtCompound { { "Name", new NbtString("Red") } } } } } };
        var b = GiveCommand.For(bed, ItemFormat.Bedrock, bedrock: true)!;
        Program.Check(b.Command == "/give @p minecraft:wool 16 14" && b.Note != null, $"Bedrock: name count data ({b.Command}), and it says what Bedrock's /give can't add");

        // Very long commands are said to need a command block.
        var book = new NbtCompound { { "id", new NbtString("minecraft:written_book") }, { "count", new NbtInt(1) }, { "components", new NbtCompound { { "minecraft:lore", new NbtList(NbtType.String) { new NbtString(new string('x', 400)) } } } } };
        Program.Check(GiveCommand.For(book, ItemFormat.Components, false)!.Note?.Contains("command block") == true, "a command longer than chat takes says to use a command block or a function");

        // Every item of the sample worlds' players gives a command naming it (Java and Bedrock: consoles have no commands).
        var worlds = Directory.EnumerateFiles(Path.Combine(TestPaths.Data, "amulet"), "level.dat", SearchOption.AllDirectories).Select(f => Path.GetDirectoryName(f)!).ToList();
        int all = 0, allGood = 0, snbtItems = 0, snbtSame = 0;
        foreach (var path in worlds)
        {
            string label = Path.GetRelativePath(TestPaths.Data, path);
            try
            {
                using var w = WorldLoader.Open(path);
                if (w is not (JavaWorld or BedrockWorld)) continue;
                int items = 0, good = 0;
                foreach (var p in w.Players)
                {
                    var inv = new PlayerInventory(w, p);
                    foreach (var part in inv.Parts)
                        for (int i = 0; i < PlayerInventory.Size(part); i++)
                            if (inv.Get(part, i) is InventoryItem item && !item.IsEmpty)
                            {
                                items++;
                                var nbt = inv.WriteItem(item);
                                var g = GiveCommand.For(nbt, inv.Format, w is BedrockWorld);
                                string id = nbt.GetString("id") ?? nbt.GetString("Name") ?? nbt["id"]?.ToString() ?? "?";
                                if (g != null && g.Command.StartsWith("/give @p " + id, StringComparison.Ordinal)) good++;
                                else Program.Info($"   {label}: {id} gave {g?.Command ?? "nothing"}");
                                // "Edit the item as SNBT": the text read back unchanged is the same item.
                                snbtItems++;
                                var again = inv.WriteItem(inv.ReadItem((NbtCompound)Snbt.Parse(Snbt.Write(nbt, SnbtStyle.Pretty))));
                                if (again.ContentEquals(nbt)) snbtSame++; else Program.Info($"   {label}: {Snbt.Write(nbt, SnbtStyle.Compact)} came back as {Snbt.Write(again, SnbtStyle.Compact)}");
                            }
                }
                if (items > 0) Program.Info($"{label}: {items} items in the players' inventories");
                all += items; allGood += good;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { }
        }
        Program.Check(all > 0 && allGood == all, $"every item the sample worlds' players have gives a /give command naming it ({allGood} of {all})");
        Program.Check(snbtItems > 0 && snbtSame == snbtItems, $"every one, written as SNBT and read back unchanged, is the same item (Edit the item as SNBT) ({snbtSame} of {snbtItems})");
    }

    /// <summary>"a=SNBT,b=SNBT" split at the top level, each value parsed and compared with the component it came from.</summary>
    static bool Components(string inner, NbtCompound expected)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        char? quote = null;
        for (int i = 0; i < inner.Length; i++)
        {
            char ch = inner[i];
            if (quote != null) { if (ch == '\\') i++; else if (ch == quote) quote = null; continue; }
            if (ch is '"' or '\'') quote = ch;
            else if (ch is '{' or '[') depth++;
            else if (ch is '}' or ']') depth--;
            else if (ch == ',' && depth == 0) { parts.Add(inner[start..i]); start = i + 1; }
        }
        parts.Add(inner[start..]);
        if (parts.Count != expected.Count) return false;
        foreach (var part in parts)
        {
            int eq = part.IndexOf('=');
            if (eq < 0 || expected[part[..eq]] is not NbtTag want) return false;
            if (!Snbt.Parse(part[(eq + 1)..]).ContentEquals(want)) return false;
        }
        return true;
    }
}
