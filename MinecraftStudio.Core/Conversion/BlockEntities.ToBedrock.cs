// MinecraftStudio.Core/Conversion/BlockEntities.ToBedrock.cs: to bedrock for BlockEntities.
// Part of BlockEntities (main part in BlockEntities.cs).
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

public static partial class BlockEntities
{
    // ---------------------------------------------------------------- canonical -> Bedrock

    /// <summary>Bedrock block entity; <paramref name="javaBlock"/> is the canonical Java block at the position.</summary>
    public static NbtCompound? ToBedrock(NbtCompound canonical, BlockState javaBlock, CanonicalChunk chunk, Func<long>? newActorId = null)
    {
        string original = canonical.GetString("id") ?? "";
        string id = AdaptToBlock(original, javaBlock);
        var target = ConversionLog.Current?.Target;
        if (id.Length == 0) return CarriedItems(canonical).Count > 0 ? Drop(canonical, $"the block became {javaBlock.BaseName.Replace('_', ' ')}") : null;
        if (!ToBedrockId.TryGetValue(id, out var bid)) return Drop(canonical, $"Bedrock has no {ConversionLog.Friendly(original)}");
        if (!KnownIn(id, BedrockContent(target))) return Drop(canonical, $"{target?.Name ?? "Bedrock"} has no {ConversionLog.Friendly(original)}");
        var c = new NbtCompound();
        c.SetString("id", bid);
        c.SetByte("isMovable", 1);
        c.SetInt("x", canonical.GetInt("x")); c.SetInt("y", canonical.GetInt("y")); c.SetInt("z", canonical.GetInt("z"));
        if (canonical.GetList("Items") is NbtList items)
        {
            var list = new NbtList(NbtType.Compound);
            foreach (var it in items.OfType<NbtCompound>())
            {
                var b = Items.ToBedrock(it);
                b.SetByte("Slot", (sbyte)it.GetInt("Slot"));
                list.Add(b);
            }
            if (id == "minecraft:campfire")
            {
                foreach (var b in list.OfType<NbtCompound>()) c[$"Item{b.GetInt("Slot") + 1}"] = b;
                var times = canonical.GetInts("CookingTimes") ?? [];
                for (int i = 0; i < 4; i++) c.SetInt($"ItemTime{i + 1}", i < times.Length ? times[i] : 0);
            }
            else c["Items"] = list;
        }
        if (canonical.GetString("CustomName") is string cn) c.SetString("CustomName", TextComponents.ToFormatted(cn, bedrock: true));
        string jb = javaBlock.BaseName;
        switch (id)
        {
            case "minecraft:chest" or "minecraft:trapped_chest":
                {
                    c.SetByte("Findable", 0);
                    string type = javaBlock.GetProperty("type") ?? "single";
                    if (type != "single")
                    {
                        string f = javaBlock.GetProperty("facing") ?? "north";
                        // LEFT chests have their partner clockwise of the facing, RIGHT ones counter-clockwise.
                        (int dx, int dz) = (type, f) switch
                        {
                            ("left", "north") => (1, 0), ("left", "east") => (0, 1), ("left", "south") => (-1, 0), ("left", "west") => (0, -1),
                            ("right", "north") => (-1, 0), ("right", "east") => (0, -1), ("right", "south") => (1, 0), _ => (0, 1),
                        };
                        c.SetInt("pairx", canonical.GetInt("x") + dx);
                        c.SetInt("pairz", canonical.GetInt("z") + dz);
                        c.SetByte("pairlead", (sbyte)(type == "left" ? 1 : 0));
                    }
                    break;
                }
            case "minecraft:sign" or "minecraft:hanging_sign":
                {
                    NbtCompound Side(NbtCompound? s)
                    {
                        var msgs = s?.GetList("messages");
                        var lines = new List<string>();
                        // Bedrock formatting carries on to the next line: end each formatted line with a reset.
                        for (int i = 0; i < 4; i++)
                        {
                            string line = msgs != null && i < msgs.Count ? TextComponents.ToFormatted(TextComponents.FromJavaTag(msgs[i]), bedrock: true) : "";
                            lines.Add(line.Contains('§') ? line + "§r" : line);
                        }
                        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
                        return new NbtCompound
                        {
                            { "HideGlowOutline", new NbtByte(false) }, { "IgnoreLighting", new NbtByte(s?.GetBool("has_glowing_text") == true) },
                            { "PersistFormatting", new NbtByte(true) }, { "SignTextColor", new NbtInt(DyeArgb(s?.GetString("color") ?? "black")) },
                            { "Text", new NbtString(string.Join("\n", lines)) }, { "TextOwner", new NbtString("") },
                        };
                    }
                    c["FrontText"] = Side(canonical.GetCompound("front_text"));
                    c["BackText"] = Side(canonical.GetCompound("back_text"));
                    c.SetByte("IsWaxed", (sbyte)(canonical.GetBool("is_waxed") ? 1 : 0));
                    break;
                }
            case "minecraft:bed":
                c.SetByte("color", (sbyte)(jb.EndsWith("_bed") ? ColorIndex(jb[..^4]) : 14));
                break;
            case "minecraft:banner":
                {
                    string colorName = jb.Replace("_wall_banner", "").Replace("_banner", "");
                    c.SetInt("Base", 15 - ColorIndex(colorName));
                    var list = new NbtList(NbtType.Compound);
                    foreach (var p in canonical.GetList("patterns")?.OfType<NbtCompound>() ?? [])
                        if (CodeByPattern.TryGetValue(p.GetString("pattern") ?? "", out var code))
                            list.Add(new NbtCompound { { "Color", new NbtInt(15 - ColorIndex(p.GetString("color"))) }, { "Pattern", new NbtString(code) } });
                    c["Patterns"] = list;
                    c.SetInt("Type", 0);
                    break;
                }
            case "minecraft:skull":
                {
                    int type = jb.StartsWith("wither_skeleton") ? 1 : jb.StartsWith("zombie") ? 2 : jb.StartsWith("player") ? 3 : jb.StartsWith("creeper") ? 4
                        : jb.StartsWith("dragon") ? 5 : jb.StartsWith("piglin") ? 6 : 0;
                    c.SetByte("SkullType", (sbyte)type);
                    c.SetFloat("Rotation", int.TryParse(javaBlock.GetProperty("rotation"), out int r) ? r * 22.5f : 0f);
                    c.SetByte("MouthMoving", 0);
                    c.SetInt("MouthTickCount", 0);
                    break;
                }
            case "minecraft:mob_spawner":
                {
                    string ent = canonical.GetCompound("SpawnData")?.GetCompound("entity")?.GetString("id") ?? "minecraft:pig";
                    c.SetString("EntityIdentifier", Entities.BedrockIdentifier(ent));
                    foreach (var k in new[] { "Delay", "MinSpawnDelay", "MaxSpawnDelay", "SpawnCount", "MaxNearbyEntities", "RequiredPlayerRange", "SpawnRange" })
                        if (canonical[k] is { IsNumeric: true } v) c.SetShort(k, (short)v.AsLong);
                    break;
                }
            case "minecraft:jukebox":
                if (canonical.GetCompound("RecordItem") is NbtCompound rec) c["RecordItem"] = Items.ToBedrock(rec);
                break;
            case "minecraft:lectern":
                if (canonical.GetCompound("Book") is NbtCompound book)
                {
                    c["book"] = Items.ToBedrock(book);
                    c.SetByte("hasBook", 1);
                    c.SetInt("page", canonical.GetInt("Page"));
                    c.SetInt("totalPages", Math.Max(1, Items.Components(book)?.GetCompound("minecraft:written_book_content")?.GetList("pages")?.Count ?? 1));
                }
                break;
            case "minecraft:flower_pot":
                if (jb.StartsWith("potted_") && BedrockJavaMap.ToBedrock(JavaBlocks.Canonicalize(BlockState.Get("minecraft:" + jb[7..]))) is BlockState plant)
                    c["PlantBlock"] = BedrockBlocks.ToPaletteNbt(plant);
                break;
            case "minecraft:note_block":
                c.SetByte("note", (sbyte)(int.TryParse(javaBlock.GetProperty("note"), out int n) ? n : 0));
                break;
            case "minecraft:furnace" or "minecraft:blast_furnace" or "minecraft:smoker":
                c.SetShort("BurnTime", (short)canonical.GetInt("BurnTime"));
                c.SetShort("CookTime", (short)canonical.GetInt("CookTime"));
                c.SetShort("BurnDuration", (short)canonical.GetInt("CookTimeTotal", 200));
                c.SetInt("StoredXPInt", 0);
                break;
            case "minecraft:beacon":
                c.SetInt("primary", Effects.Number(canonical.GetString("primary_effect")));
                c.SetInt("secondary", Effects.Number(canonical.GetString("secondary_effect")));
                break;
            case "minecraft:command_block":
                c.SetString("Command", canonical.GetString("Command") ?? "");
                c.SetByte("auto", (sbyte)(canonical.GetBool("auto") ? 1 : 0));
                c.SetByte("powered", (sbyte)(canonical.GetBool("powered") ? 1 : 0));
                c.SetByte("TrackOutput", (sbyte)(canonical.GetBool("TrackOutput", true) ? 1 : 0));
                c.SetInt("Version", 38);
                break;
            case "minecraft:end_gateway":
                if (canonical.GetInts("exit_portal") is { Length: 3 } e)
                    c["ExitPortal"] = new NbtList { new NbtInt(e[0]), new NbtInt(e[1]), new NbtInt(e[2]) };
                c.SetInt("Age", (int)canonical.GetLong("Age"));
                break;
            case "minecraft:decorated_pot":
                if (canonical.GetList("sherds") is NbtList sherds) c["sherds"] = sherds.Clone();
                break;
            case "minecraft:beehive":
                {
                    var occupants = new NbtList(NbtType.Compound);
                    int index = 0;
                    foreach (var b in canonical.GetList("bees")?.OfType<NbtCompound>() ?? [])
                    {
                        if (b.GetCompound("entity_data") is not NbtCompound data) continue;
                        long uid = newActorId?.Invoke() ?? (((long)-2 << 32) | (uint)HashCode.Combine(canonical.GetInt("x"), canonical.GetInt("y"), canonical.GetInt("z"), index++));
                        if (Entities.ToBedrock(data, uid) is not NbtCompound save) continue;
                        occupants.Add(new NbtCompound
                        {
                            { "ActorIdentifier", new NbtString("minecraft:bee<>") }, { "SaveData", save },
                            { "TicksLeftToStay", new NbtInt(Math.Max(0, b.GetInt("min_ticks_in_hive") - b.GetInt("ticks_in_hive"))) },
                        });
                    }
                    c["Occupants"] = occupants;
                    c.SetByte("ShouldSpawnBees", 0);
                    break;
                }
        }
        return c;
    }

    /// <summary>Block entities a Bedrock world needs although Java stores the data in the block (beds, pots, notes...).</summary>
    public static NbtCompound? ImpliedForBedrock(BlockState javaBlock, int x, int y, int z)
    {
        string b = javaBlock.BaseName;
        string? id = b switch
        {
            _ when b.EndsWith("_bed") => "minecraft:bed",
            _ when b.EndsWith("_banner") => "minecraft:banner",
            _ when Substitutes.IsMobHead(b) => "minecraft:skull",
            _ when b.StartsWith("potted_") || b == "flower_pot" => "minecraft:flower_pot",
            "note_block" => "minecraft:note_block",
            _ => null,
        };
        if (id == null) return null;
        return new NbtCompound { { "id", new NbtString(id) }, { "x", new NbtInt(x) }, { "y", new NbtInt(y) }, { "z", new NbtInt(z) } };
    }

    /// <summary>Block entities old Java/Legacy Console worlds need for blocks that keep data in them (beds, pots, skulls, notes, banners).</summary>
    public static NbtCompound? ImpliedForLegacyJava(BlockState javaBlock, int x, int y, int z) => ImpliedForBedrock(javaBlock, x, y, z);
}
