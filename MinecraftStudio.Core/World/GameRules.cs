// MinecraftStudio.Core/World/GameRules.cs: a world's game rules — keep inventory, the day and night cycle, mob griefing,
// tick speed and the rest — as each edition keeps them in level.dat.
// Owns: GameRule, GameRules. Java and Legacy Console keep them in Data.GameRules (values as text, "true" / "3"; newer
// versions may use other names or typed values: whatever the file holds is shown and kept in its own form); Bedrock, 3DS
// and Pocket Edition keep each rule as a level.dat key of its own (lower-case names, bytes and ints). Only the rules the
// world already has are shown and changed, each written back in the type it was stored with, so the game reads them
// as before; the caller saves level.dat (WorldFlags.Save, after a backup).
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

/// <summary>One game rule as the world keeps it; <see cref="Value"/> is "true" / "false" or a whole number.</summary>
public sealed class GameRule
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required bool IsBool { get; init; }
    public required string Original { get; init; }
    public string Value { get; set; } = "";
    /// <summary>The most-used rules come first on the page.</summary>
    public bool Common { get; init; }
    public bool Changed => Value != Original;
    public bool On => Value == "true";
}

public static class GameRules
{
    // Rules Bedrock-based editions keep as level.dat keys of their own (the others there are not game rules).
    static readonly HashSet<string> BedrockKeys = new(StringComparer.Ordinal)
    {
        "commandblockoutput", "commandblocksenabled", "dodaylightcycle", "doentitydrops", "dofiretick", "doimmediaterespawn", "doinsomnia",
        "dolimitedcrafting", "domobloot", "domobspawning", "dotiledrops", "doweathercycle", "drowningdamage", "falldamage", "firedamage",
        "freezedamage", "functioncommandlimit", "keepinventory", "locatorbar", "maxcommandchainlength", "mobgriefing", "naturalregeneration",
        "playerssleepingpercentage", "projectilescanbreakblocks", "pvp", "randomtickspeed", "recipesunlock", "respawnblocksexplode",
        "sendcommandfeedback", "showbordereffect", "showcoordinates", "showdaysplayed", "showdeathmessages", "showrecipemessages", "showtags",
        "spawnradius", "tntexplodes", "tntexplosiondropdecay",
    };

    // Plain names, by the rule's name without "minecraft:", underscores or capitals.
    static readonly Dictionary<string, (string Label, bool Common)> Names = new(StringComparer.Ordinal)
    {
        ["keepinventory"] = ("Keep inventory after death", true),
        ["dodaylightcycle"] = ("Day and night cycle", true),
        ["advancetime"] = ("Day and night cycle", true),
        ["doweathercycle"] = ("Weather changes", true),
        ["advanceweather"] = ("Weather changes", true),
        ["domobspawning"] = ("Mobs spawn", true),
        ["spawnmobs"] = ("Mobs spawn", true),
        ["mobgriefing"] = ("Mobs change blocks (creepers, endermen …)", true),
        ["dofiretick"] = ("Fire spreads and burns out", true),
        ["naturalregeneration"] = ("Health comes back from food", true),
        ["doimmediaterespawn"] = ("Respawn at once (no death screen)", true),
        ["immediaterespawn"] = ("Respawn at once (no death screen)", true),
        ["playerssleepingpercentage"] = ("Players who must sleep to skip the night (%)", true),
        ["randomtickspeed"] = ("Random tick speed (how fast crops and plants grow)", true),
        ["showcoordinates"] = ("Show coordinates", true),
        ["showdeathmessages"] = ("Death messages in chat", true),
        ["pvp"] = ("Players can hurt each other", true),
        ["doinsomnia"] = ("Phantoms come to players who do not sleep", true),
        ["spawnphantoms"] = ("Phantoms come to players who do not sleep", true),
        ["tntexplodes"] = ("TNT explodes", true),
        ["commandblockoutput"] = ("Command blocks report in chat", false),
        ["sendcommandfeedback"] = ("Commands report in chat", false),
        ["commandblocksenabled"] = ("Command blocks work", false),
        ["domobloot"] = ("Mobs drop loot", false),
        ["dotiledrops"] = ("Broken blocks drop items", false),
        ["doentitydrops"] = ("Minecarts, boats and frames drop items", false),
        ["falldamage"] = ("Fall damage", false),
        ["firedamage"] = ("Fire damage", false),
        ["drowningdamage"] = ("Drowning damage", false),
        ["freezedamage"] = ("Freezing damage", false),
        ["spawnradius"] = ("Spawn radius (blocks)", false),
        ["respawnradius"] = ("Spawn radius (blocks)", false),
        ["announceadvancements"] = ("Advancements are announced", false),
        ["disableraids"] = ("No raids", false),
        ["dopatrolspawning"] = ("Pillager patrols", false),
        ["dotraderspawning"] = ("Wandering traders", false),
        ["dowardenspawning"] = ("Wardens appear", false),
        ["forgivedeadplayers"] = ("Angry mobs forgive dead players", false),
        ["universalanger"] = ("Angry mobs attack every player", false),
        ["maxentitycramming"] = ("Mobs in one block before they get hurt (cramming)", false),
        ["spectatorsgeneratechunks"] = ("Spectators load new terrain", false),
        ["dolimitedcrafting"] = ("Only unlocked recipes can be crafted", false),
        ["reduceddebuginfo"] = ("Less on the debug screen", false),
        ["logadmincommands"] = ("Log operators' commands", false),
        ["maxcommandchainlength"] = ("Most commands one chain runs", false),
        ["functioncommandlimit"] = ("Most commands one function runs", false),
        ["commandmodificationblocklimit"] = ("Most blocks one command changes", false),
        ["snowaccumulationheight"] = ("Snow layers from snowfall", false),
        ["watersourceconversion"] = ("New water sources form", false),
        ["lavasourceconversion"] = ("New lava sources form", false),
        ["blockexplosiondropdecay"] = ("Blocks blown up by blocks drop fewer items", false),
        ["mobexplosiondropdecay"] = ("Blocks blown up by mobs drop fewer items", false),
        ["tntexplosiondropdecay"] = ("Blocks blown up by TNT drop fewer items", false),
        ["globalsoundevents"] = ("Boss and portal sounds heard everywhere", false),
        ["dovinesspread"] = ("Vines spread", false),
        ["enderpearlsvanishondeath"] = ("Ender pearls vanish with a dead thrower", false),
        ["projectilescanbreakblocks"] = ("Projectiles break blocks", false),
        ["spawnchunkradius"] = ("Spawn chunks kept loaded (radius)", false),
        ["showtags"] = ("Item tags shown", false),
        ["showbordereffect"] = ("World border effect shown", false),
        ["showrecipemessages"] = ("Recipe unlock messages", false),
        ["showdaysplayed"] = ("Days played shown", false),
        ["recipesunlock"] = ("Recipes unlock as you play", false),
        ["respawnblocksexplode"] = ("Beds and anchors explode in the wrong dimension", false),
        ["locatorbar"] = ("Locator bar", false),
        ["disableelytramovementcheck"] = ("No elytra speed check (servers)", false),
        ["disableplayermovementcheck"] = ("No player speed check (servers)", false),
        ["allowfiretickawayfromplayer"] = ("Fire spreads away from players", false),
        ["tntexplosions"] = ("TNT explodes", true),
    };

    static string Plain(string key)
    {
        string k = key.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase) ? key[10..] : key;
        return k.Replace("_", "").ToLowerInvariant();
    }

    // A rule without a plain name of its own: its name in words (doFireTick → "do fire tick").
    static string Words(string key)
    {
        string k = key.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase) ? key[10..] : key;
        var sb = new System.Text.StringBuilder();
        foreach (char ch in k)
        {
            if (ch == '_') { sb.Append(' '); continue; }
            if (char.IsUpper(ch) && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
            sb.Append(char.ToLowerInvariant(ch));
        }
        string s = sb.ToString();
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : key;
    }

    /// <summary>Where the rules live: the GameRules compound (Java, console) or level.dat's own root (Bedrock-based).</summary>
    static (NbtCompound? Holder, bool OwnKeys) Holder(SaveWorld world)
    {
        switch (world)
        {
            case JavaWorld j: return (j.Data?.GetCompound("GameRules") ?? j.Data?.GetCompound("game_rules") ?? j.Data?.GetCompound("gamerules"), false);
            case ConsoleWorld c: return (c.Level?.RootCompound.GetCompound("Data")?.GetCompound("GameRules"), false);
            case BedrockWorld b: return (b.LevelRoot, true);
            case ThreeDsWorld t: return (t.Level.RootCompound, true);
            case PocketLegacyWorld p: return (p.Level?.RootCompound, true);
            default: return (null, false);
        }
    }

    /// <summary>The rules the world has, the most-used first.</summary>
    public static List<GameRule> Read(SaveWorld world)
    {
        var (holder, own) = Holder(world);
        var list = new List<GameRule>();
        if (holder == null) return list;
        foreach (var (key, tag) in holder)
        {
            if (own && !BedrockKeys.Contains(key)) continue;
            string? value = tag switch
            {
                NbtString s when s.Value is "true" or "false" => s.Value,
                NbtString s when long.TryParse(s.Value, out _) => s.Value.Trim(),
                // Bedrock's byte rules are on/off (bar a few numbers); a typed Java rule is on/off when it is 0 or 1.
                NbtByte b => (own ? !IntRule(key) : b.Value is 0 or 1) ? (b.Value != 0 ? "true" : "false") : b.Value.ToString(),
                NbtInt or NbtShort or NbtLong => tag.AsLong.ToString(),
                _ => null,
            };
            if (value == null) continue;
            bool isBool = value is "true" or "false";
            var (label, common) = Names.TryGetValue(Plain(key), out var n) ? n : (Words(key), false);
            list.Add(new GameRule { Key = key, Label = label, IsBool = isBool, Original = value, Value = value, Common = common });
        }
        return list.OrderBy(r => r.Common ? 0 : 1).ThenBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // Bedrock keeps these rules as numbers (the rest of its byte rules are on/off).
    static bool IntRule(string key) => key is "functioncommandlimit" or "maxcommandchainlength" or "playerssleepingpercentage" or "randomtickspeed" or "spawnradius";

    /// <summary>
    /// Writes the changed rules into level.dat in memory, each in the type it had (text stays text, a byte stays a byte);
    /// numbers outside what the tag can hold are refused. Returns what was changed; the caller saves level.dat.
    /// </summary>
    public static List<string> Apply(SaveWorld world, IEnumerable<GameRule> rules)
    {
        var (holder, _) = Holder(world);
        var done = new List<string>();
        if (holder == null) return done;
        foreach (var r in rules.Where(r => r.Changed))
        {
            if (holder[r.Key] is not NbtTag old) continue;
            NbtTag? replacement = old switch
            {
                NbtString => new NbtString(r.Value),
                NbtByte when r.IsBool => new NbtByte(r.Value == "true"),
                NbtByte when sbyte.TryParse(r.Value, out var b) => new NbtByte(b),
                NbtShort when short.TryParse(r.Value, out var s) => new NbtShort(s),
                NbtInt when int.TryParse(r.Value, out var i) => new NbtInt(i),
                NbtLong when long.TryParse(r.Value, out var l) => new NbtLong(l),
                _ => null,
            };
            if (replacement == null) throw new ArgumentException($"{r.Label}: {r.Value} is not a value this world can keep for it.");
            holder[r.Key] = replacement;
            done.Add($"{r.Label}: {(r.IsBool ? (r.On ? "on" : "off") : r.Value)}");
        }
        return done;
    }
}
