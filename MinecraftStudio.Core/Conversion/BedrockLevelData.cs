// MinecraftStudio.Core/Conversion/BedrockLevelData.cs: Bedrock level.dat for converted worlds (fields as
// written by the game; the game fills in anything newer).
// Owns: BedrockLevelData.
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

/// <summary>Bedrock level.dat for converted worlds (fields as written by the game; the game fills in anything newer).</summary>
public static class BedrockLevelData
{
    // Java game rule -> Bedrock level.dat tag (Bedrock keeps rules as individual lowercase tags).
    static readonly (string Java, string Bedrock, bool IsInt)[] Rules =
    [
        ("commandBlockOutput", "commandblockoutput", false), ("doDaylightCycle", "dodaylightcycle", false), ("doEntityDrops", "doentitydrops", false),
        ("doFireTick", "dofiretick", false), ("doImmediateRespawn", "doimmediaterespawn", false), ("doInsomnia", "doinsomnia", false),
        ("doLimitedCrafting", "dolimitedcrafting", false), ("doMobLoot", "domobloot", false), ("doMobSpawning", "domobspawning", false),
        ("doTileDrops", "dotiledrops", false), ("doWeatherCycle", "doweathercycle", false), ("drowningDamage", "drowningdamage", false),
        ("fallDamage", "falldamage", false), ("fireDamage", "firedamage", false), ("freezeDamage", "freezedamage", false),
        ("keepInventory", "keepinventory", false), ("mobGriefing", "mobgriefing", false), ("naturalRegeneration", "naturalregeneration", false),
        ("sendCommandFeedback", "sendcommandfeedback", false), ("showDeathMessages", "showdeathmessages", false),
        ("randomTickSpeed", "randomtickspeed", true), ("spawnRadius", "spawnradius", true), ("playersSleepingPercentage", "playerssleepingpercentage", true),
        ("maxCommandChainLength", "maxcommandchainlength", true), ("tntExplodes", "tntexplodes", false),
    ];

    public static IEnumerable<(string Java, string Bedrock, bool IsInt)> GameRules => Rules;

    public static NbtCompound Build(WorldInfo info, ConversionTarget t)
    {
        var d = new NbtCompound();
        var version = t.BedrockVersion.Length == 5 ? t.BedrockVersion : [1, 21, 120, 0, 0];
        d.SetString("LevelName", info.Name);
        d.SetLong("RandomSeed", info.Seed);
        d.SetInt("GameType", info.GameType switch { 3 => 6, var g => g });
        d.SetInt("Difficulty", Math.Clamp(info.Difficulty, 0, 3));
        d.SetInt("Generator", info.Generator switch { "flat" => 2, "old" => 0, "void" => 5, _ => 1 });
        d.SetInt("SpawnX", info.SpawnX);
        d.SetInt("SpawnY", info.SpawnY);
        d.SetInt("SpawnZ", info.SpawnZ);
        d.SetLong("Time", info.DayTime);
        d.SetLong("currentTick", info.Time);
        d.SetLong("LastPlayed", new DateTimeOffset(info.LastPlayed).ToUnixTimeSeconds());
        d.SetInt("StorageVersion", t.BedrockStorageVersion);
        d.SetInt("NetworkVersion", t.BedrockNetworkVersion);
        d["lastOpenedWithVersion"] = IntList(version);
        d["MinimumCompatibleClientVersion"] = IntList(version);
        d.SetString("InventoryVersion", $"{version[0]}.{version[1]}.{version[2]}");
        d.SetString("baseGameVersion", "*");
        d.SetInt("WorldVersion", 1);
        d.SetLong("worldStartCount", 4294967295L);
        d.SetInt("Platform", 2);
        d.SetInt("PlatformBroadcastIntent", 3);
        d.SetInt("XBLBroadcastIntent", 3);
        d.SetByte("MultiplayerGame", 1);
        d.SetByte("MultiplayerGameIntent", 1);
        d.SetByte("LANBroadcast", 1);
        d.SetByte("LANBroadcastIntent", 1);
        d.SetByte("IsHardcore", (sbyte)(info.Hardcore ? 1 : 0));
        d.SetByte("cheatsEnabled", (sbyte)(info.AllowCommands ? 1 : 0));
        d.SetByte("commandsEnabled", (sbyte)(info.AllowCommands ? 1 : 0));
        d.SetByte("commandblocksenabled", 1);
        d.SetByte("hasBeenLoadedInCreative", (sbyte)(info.GameType == 1 || info.AllowCommands ? 1 : 0));
        d.SetByte("spawnMobs", 1);
        d.SetByte("texturePacksRequired", 0);
        d.SetByte("educationFeaturesEnabled", 0);
        d.SetInt("eduOffer", 0);
        d.SetByte("immutableWorld", 0);
        d.SetByte("bonusChestEnabled", 0);
        d.SetByte("bonusChestSpawned", 0);
        d.SetByte("startWithMapEnabled", 0);
        d.SetByte("showcoordinates", 0);
        d.SetInt("NetherScale", 8);
        d.SetInt("serverChunkTickRange", 4);
        d.SetInt("permissionsLevel", 0);
        d.SetInt("playerPermissionsLevel", 1);
        d.SetInt("LimitedWorldOriginX", info.SpawnX);
        d.SetInt("LimitedWorldOriginY", 32767);
        d.SetInt("LimitedWorldOriginZ", info.SpawnZ);
        d.SetFloat("rainLevel", info.Raining ? 1 : 0);
        d.SetInt("rainTime", info.RainTime);
        d.SetFloat("lightningLevel", info.Thundering ? 1 : 0);
        d.SetInt("lightningTime", info.ThunderTime);
        // Game rules: defaults first, then the converted world's values.
        foreach (var (java, bedrock, isInt) in Rules)
        {
            string? v = info.GameRules.GetValueOrDefault(java);
            if (isInt)
            {
                int def = java switch { "randomTickSpeed" => 1, "spawnRadius" => 5, "playersSleepingPercentage" => 100, "maxCommandChainLength" => 65535, _ => 0 };
                d.SetInt(bedrock, int.TryParse(v, out int iv) ? (java == "randomTickSpeed" ? Math.Max(0, iv / 3) : iv) : def);
            }
            else
            {
                bool def = java is not ("keepInventory" or "doImmediateRespawn" or "doLimitedCrafting");
                d.SetByte(bedrock, (sbyte)((v == null ? def : v.Equals("true", StringComparison.OrdinalIgnoreCase)) ? 1 : 0));
            }
        }
        d["experiments"] = new NbtCompound { { "experiments_ever_used", new NbtByte(false) }, { "saved_with_toggled_experiments", new NbtByte(false) } };
        d["abilities"] = new NbtCompound
        {
            { "attackmobs", new NbtByte(false) }, { "attackplayers", new NbtByte(false) }, { "build", new NbtByte(true) }, { "doorsandswitches", new NbtByte(false) },
            { "flySpeed", new NbtFloat(0.05f) }, { "flying", new NbtByte(false) }, { "instabuild", new NbtByte(false) }, { "invulnerable", new NbtByte(false) },
            { "lightning", new NbtByte(false) }, { "mayfly", new NbtByte(false) }, { "mine", new NbtByte(true) }, { "op", new NbtByte(false) },
            { "opencontainers", new NbtByte(false) }, { "teleport", new NbtByte(false) }, { "verticalFlySpeed", new NbtFloat(1.0f) }, { "walkSpeed", new NbtFloat(0.1f) },
        };
        if (info.Generator == "flat")
            d.SetString("FlatWorldLayers", "{\"biome_id\":1,\"block_layers\":[{\"block_name\":\"minecraft:bedrock\",\"count\":1},{\"block_name\":\"minecraft:dirt\",\"count\":2},{\"block_name\":\"minecraft:grass_block\",\"count\":1}],\"encoding_version\":6,\"preset_id\":\"ClassicFlat\",\"world_version\":\"version.post_1_18\"}\n");
        return d;
    }

    static NbtList IntList(int[] v)
    {
        var l = new NbtList(NbtType.Int);
        foreach (var i in v) l.Add(new NbtInt(i));
        return l;
    }
}
