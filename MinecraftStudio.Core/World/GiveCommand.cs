// MinecraftStudio.Core/World/GiveCommand.cs: the /give command that makes an item, in the world's own edition and version.
// Owns: GiveCommand — from an item as the world saves it (PlayerInventory.WriteItem): Java 1.20.5+ "id[components] count"
// (components as SNBT), 1.13–1.20.4 "id{tag} count", 1.8–1.12 "id count damage {tag}", numbered ids before 1.8; Bedrock
// "name count data" (Bedrock's /give takes no enchantments or names — said in the note). Commands longer than the chat
// box takes (256 characters) are said to need a command block or a function.
using System.Text;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

/// <summary>A /give command and what to know about it (null when there is nothing to say).</summary>
public sealed record GiveResult(string Command, string? Note);

public static class GiveCommand
{
    /// <summary>The most a command typed in chat may be.</summary>
    public const int ChatLimit = 256;

    /// <summary>The command, or null for formats whose game has no /give (consoles, old Pocket Edition).</summary>
    public static GiveResult? For(NbtCompound item, ItemFormat format, bool bedrock, string target = "@p")
    {
        var sb = new StringBuilder("/give ").Append(target).Append(' ');
        string? note = null;
        switch (format)
        {
            case ItemFormat.Components:
                {
                    string id = item.GetString("id") ?? "minecraft:air";
                    sb.Append(id);
                    if (item.GetCompound("components") is { Count: > 0 } components)
                    {
                        sb.Append('[');
                        bool first = true;
                        foreach (var (key, value) in components)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            // A component the item has taken away is written !name.
                            if (key.StartsWith('!')) { sb.Append(key); continue; }
                            sb.Append(key).Append('=').Append(Snbt.Write(value, SnbtStyle.Compact));
                        }
                        sb.Append(']');
                    }
                    int count = item.GetInt("count", 1);
                    if (count != 1) sb.Append(' ').Append(count);
                    break;
                }
            case ItemFormat.Flattened:
                {
                    sb.Append(item.GetString("id") ?? "minecraft:air");
                    if (item.GetCompound("tag") is { Count: > 0 } tag) sb.Append(Snbt.Write(tag, SnbtStyle.Compact));
                    int count = item.GetInt("Count", 1);
                    if (count != 1) sb.Append(' ').Append(count);
                    break;
                }
            case ItemFormat.Named or ItemFormat.Numeric when !bedrock:
                {
                    // 1.8–1.12: /give <player> <item> [amount] [data] [dataTag]; before 1.8 the number works too.
                    string id = item["id"] is NbtString s ? s.Value : item["id"] is { IsNumeric: true } n ? n.AsLong.ToString() : "minecraft:air";
                    sb.Append(id).Append(' ').Append(item.GetInt("Count", 1)).Append(' ').Append(item.GetInt("Damage"));
                    if (item.GetCompound("tag") is { Count: > 0 } tag) sb.Append(' ').Append(Snbt.Write(tag, SnbtStyle.Compact));
                    break;
                }
            case ItemFormat.Bedrock:
                {
                    // /give <player> <item> [amount] [data]: no enchantments, names or other data in Bedrock's command.
                    sb.Append(item.GetString("Name") ?? "minecraft:air").Append(' ').Append(Math.Max(1, item.GetInt("Count", 1))).Append(' ').Append(item.GetInt("Damage"));
                    if (item.GetCompound("tag") is { Count: > 0 }) note = "Bedrock's /give cannot add enchantments, names or other item data: the command gives the plain item.";
                    break;
                }
            default:
                return null;
        }
        string command = sb.ToString();
        if (command.Length > ChatLimit)
            note = (note == null ? "" : note + " ") + $"It is {command.Length:N0} characters: more than chat takes ({ChatLimit}), so paste it into a command block or a function.";
        return new GiveResult(command, note);
    }
}
