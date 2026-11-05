// MinecraftStudio.Core/World/WorldFlags.cs: The settings that show whether a world has been cheated in, as each
// edition keeps them in level.dat: cheats (commands) allowed, played in Creative, experiments used,
// achievements switched off, the default game mode, hardcore and difficulty.
// Owns: WorldFlags.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

/// <summary>
/// The settings that show whether a world has been cheated in, as each edition keeps them in level.dat:
/// cheats (commands) allowed, played in Creative, experiments used, achievements switched off, the default game mode,
/// hardcore and difficulty. Bedrock (and the 3DS edition) switch achievements off for good once any of the marks is
/// set; Legacy Console keeps a "has been in Creative" mark; Java keeps none (only whether cheats are allowed now) and
/// never locks advancements. A flag is null when the world's version does not have it; only flags the file already
/// has are written back, so nothing new is added to older formats.
/// </summary>
public sealed class WorldFlags
{
    public const int Survival = 0, Creative = 1, Adventure = 2, Spectator = 3;

    readonly NbtCompound? _root;

    WorldFlags(SaveWorld world, NbtCompound? root)
    {
        World = world;
        _root = root;
        if (root == null) return;
        bool bedrockLike = world is BedrockWorld or ThreeDsWorld or PocketLegacyWorld;
        Cheats = Bool(bedrockLike ? "commandsEnabled" : "allowCommands");
        PlayedInCreative = Bool(bedrockLike ? "hasBeenLoadedInCreative" : "hasBeenInCreative");
        AchievementsDisabled = Bool("achievementsDisabled");
        if (root.GetCompound("experiments") is NbtCompound ex)
        {
            ExperimentsUsed = ex.GetBool("experiments_ever_used") || ex.GetBool("saved_with_toggled_experiments");
            ExperimentsOn = ex.Where(kv => kv.Key is not ("experiments_ever_used" or "saved_with_toggled_experiments") && kv.Value.IsNumeric && kv.Value.AsLong != 0)
                .Select(kv => kv.Key).ToList();
        }
        if (root.GetLongOrNull("GameType") is long g) GameMode = BedrockToMode((int)g, bedrockLike);
        ForceGameMode = Bool("ForceGameType");
        Hardcore = bedrockLike ? null : Bool("hardcore");
        if (root.GetLongOrNull("Difficulty") is long d) Difficulty = (int)d;
        AddOns = world is BedrockWorld b && HasAddOns(b.Folder);

        bool? Bool(string name) => root.GetLongOrNull(name) is long v ? v != 0 : null;
    }

    public SaveWorld World { get; }

    /// <summary>Cheats (commands) are allowed: Java and console allowCommands, Bedrock commandsEnabled.</summary>
    public bool? Cheats { get; set; }
    /// <summary>The world has been played in Creative: Bedrock hasBeenLoadedInCreative, console hasBeenInCreative.</summary>
    public bool? PlayedInCreative { get; set; }
    /// <summary>Older Bedrock and 3DS versions: achievements switched off for the world.</summary>
    public bool? AchievementsDisabled { get; set; }
    /// <summary>Bedrock 1.16+: experimental gameplay has been used.</summary>
    public bool? ExperimentsUsed { get; set; }
    /// <summary>Experiments still switched on (they mark the world again when the game loads it).</summary>
    public IReadOnlyList<string> ExperimentsOn { get; } = [];
    /// <summary>The world's default game mode (0 Survival, 1 Creative, 2 Adventure, 3 Spectator).</summary>
    public int? GameMode { get; set; }
    /// <summary>Bedrock: every player is kept in the world's game mode.</summary>
    public bool? ForceGameMode { get; set; }
    public bool? Hardcore { get; set; }
    /// <summary>0 Peaceful … 3 Hard.</summary>
    public int? Difficulty { get; set; }
    /// <summary>Bedrock: behaviour packs are applied (achievements stay off while they are).</summary>
    public bool AddOns { get; }

    /// <summary>Whether this edition locks achievements once a world is marked (Bedrock, 3DS, Legacy Console).</summary>
    public bool LocksAchievements => World is BedrockWorld or ThreeDsWorld or ConsoleWorld;

    public static WorldFlags Read(SaveWorld world) => new(world, RootOf(world));

    /// <summary>The compound in level.dat that holds the settings, or null when the world has none.</summary>
    static NbtCompound? RootOf(SaveWorld world) => world switch
    {
        JavaWorld j => j.Data,
        BedrockWorld b => b.LevelRoot,
        ThreeDsWorld t => t.Level.RootCompound,
        PocketLegacyWorld p => p.Level?.RootCompound,
        ConsoleWorld c => c.Level?.RootCompound.GetCompound("Data"),
        _ => null,
    };

    /// <summary>Why achievements are off in this world (empty when they are on, or the edition has none).</summary>
    public IReadOnlyList<string> AchievementBlockers()
    {
        var why = new List<string>();
        if (!LocksAchievements) return why;
        if (PlayedInCreative == true) why.Add("the world has been played in Creative");
        if (GameMode == Creative) why.Add("its game mode is Creative");
        if (Cheats == true) why.Add(World is ConsoleWorld ? "host privileges / commands are allowed" : "cheats are on");
        if (AchievementsDisabled == true) why.Add("achievements were switched off for it");
        if (ExperimentsUsed == true) why.Add("experiments have been used");
        if (ExperimentsOn.Count > 0) why.Add($"experiments are still on ({string.Join(", ", ExperimentsOn)})");
        if (AddOns) why.Add("add-ons (behaviour packs) are applied");
        return why;
    }

    /// <summary>
    /// Clears every mark the world keeps (cheats, Creative, experiments used, achievements off) and sets a Creative
    /// default game mode to Survival. Experiments still switched on and add-ons are left alone: switching them off
    /// can break what they added, so the game is the place to turn them off.
    /// </summary>
    public void ClearMarks()
    {
        if (Cheats != null) Cheats = false;
        if (PlayedInCreative != null) PlayedInCreative = false;
        if (AchievementsDisabled != null) AchievementsDisabled = false;
        if (ExperimentsUsed != null) ExperimentsUsed = false;
        if (GameMode == Creative) GameMode = Survival;
    }

    /// <summary>Writes the flags into the world's level.dat in memory (only fields the file has); the caller saves it.</summary>
    public void Apply()
    {
        if (_root == null) return;
        bool bedrockLike = World is BedrockWorld or ThreeDsWorld or PocketLegacyWorld;
        Set(bedrockLike ? "commandsEnabled" : "allowCommands", Cheats);
        Set(bedrockLike ? "hasBeenLoadedInCreative" : "hasBeenInCreative", PlayedInCreative);
        Set("achievementsDisabled", AchievementsDisabled);
        Set("ForceGameType", ForceGameMode);
        if (!bedrockLike) Set("hardcore", Hardcore);
        if (ExperimentsUsed is bool used && _root.GetCompound("experiments") is NbtCompound ex)
        {
            if (ex.ContainsKey("experiments_ever_used")) ex.SetByte("experiments_ever_used", (sbyte)(used ? 1 : 0));
            if (ex.ContainsKey("saved_with_toggled_experiments")) ex.SetByte("saved_with_toggled_experiments", (sbyte)(used ? 1 : 0));
        }
        if (GameMode is int mode && _root.ContainsKey("GameType")) SetNumber("GameType", ModeToBedrock(mode, bedrockLike));
        if (Difficulty is int d && _root.ContainsKey("Difficulty")) SetNumber("Difficulty", d);

        void Set(string name, bool? value)
        {
            if (value is bool v && _root.ContainsKey(name)) SetNumber(name, v ? 1 : 0);
        }
    }

    /// <summary>Writes a number keeping the tag's own type (byte, int …).</summary>
    void SetNumber(string name, int value)
    {
        switch (_root![name])
        {
            case NbtByte: _root.SetByte(name, (sbyte)value); break;
            case NbtShort: _root[name] = new NbtShort((short)value); break;
            case NbtLong: _root.SetLong(name, value); break;
            default: _root.SetInt(name, value); break;
        }
    }

    /// <summary>Saves level.dat (the caller backs the world up first).</summary>
    public void Save()
    {
        if (World.OpenedForRescue) throw new InvalidOperationException(SaveWorld.RescueReadOnly);
        Apply();
        switch (World)
        {
            case JavaWorld j: j.SaveLevelDat(); break;
            case BedrockWorld b: b.SaveLevelDat(); break;
            case ThreeDsWorld t: t.SaveLevelDat(); break;
            case PocketLegacyWorld p: p.SaveLevel(); break;
            case ConsoleWorld c: c.LevelModified = true; c.Save(); break;
            default: throw new NotSupportedException("This world has no settings to save.");
        }
    }

    /// <summary>Bedrock writes Spectator as 6 (and 5 means "the world's default" for players); the rest share Java's numbers.</summary>
    static int BedrockToMode(int value, bool bedrock) => bedrock && value == 6 ? Spectator : value;
    static int ModeToBedrock(int mode, bool bedrock) => bedrock && mode == Spectator ? 6 : mode;

    static bool HasAddOns(string folder)
    {
        string list = Path.Combine(folder, "world_behavior_packs.json");
        try { return File.Exists(list) && File.ReadAllText(list).Contains("pack_id", StringComparison.OrdinalIgnoreCase); }
        catch (IOException) { return false; }
    }

    /// <summary>A player's own game mode (null when the player record has none).</summary>
    public static int? PlayerGameMode(SaveWorld world, NbtCompound player) =>
        (world is BedrockWorld or ThreeDsWorld or PocketLegacyWorld ? player.GetLongOrNull("PlayerGameMode") : player.GetLongOrNull("playerGameType")) is long v
            ? world is BedrockWorld && v == 6 ? Spectator : (int)v : null;

    /// <summary>Sets a player's game mode (only when the record has the field).</summary>
    public static bool SetPlayerGameMode(SaveWorld world, NbtCompound player, int mode)
    {
        string name = world is BedrockWorld or ThreeDsWorld or PocketLegacyWorld ? "PlayerGameMode" : "playerGameType";
        if (!player.ContainsKey(name)) return false;
        int value = world is BedrockWorld && mode == Spectator ? 6 : mode;
        if (player[name] is NbtByte) player.SetByte(name, (sbyte)value); else player.SetInt(name, value);
        return true;
    }
}
