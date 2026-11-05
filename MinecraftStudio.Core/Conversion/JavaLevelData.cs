// MinecraftStudio.Core/Conversion/JavaLevelData.cs: level.dat for Java targets.
// Owns: JavaLevelData.
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// level.dat for Java targets. For 1.20.4 and newer the file is written in 1.20.4's layout (DataVersion 3700) so the
/// game's own upgrader produces exactly the layout of the target (spawn compound, typed game rules, the 26.1 folder
/// split...); older targets get their native layout.
/// </summary>
public static class JavaLevelData
{
    public const int UpgradeBase = 3700; // 1.20.4

    public static int LevelDataVersion(ConversionTarget t) => t.DataVersion >= UpgradeBase ? UpgradeBase : t.DataVersion;

    static readonly (int Dv, string Name)[] Names =
    [
        (100, "15w32a"), (169, "1.9"), (510, "1.10"), (819, "1.11"), (1139, "1.12"), (1343, "1.12.2"), (1519, "1.13"), (1631, "1.13.2"), (1952, "1.14"),
        (1976, "1.14.4"), (2225, "1.15"), (2230, "1.15.2"), (2566, "1.16"), (2586, "1.16.5"), (2724, "1.17"), (2730, "1.17.1"), (2860, "1.18"),
        (2975, "1.18.2"), (3105, "1.19"), (3120, "1.19.2"), (3218, "1.19.3"), (3337, "1.19.4"), (3463, "1.20"), (3465, "1.20.1"), (3578, "1.20.2"),
        (3698, "1.20.3"), (3700, "1.20.4"),
    ];

    static string VersionName(int dv) => Names.LastOrDefault(n => n.Dv <= dv).Name ?? "1.12.2";

    public static NbtCompound Build(WorldInfo info, ConversionTarget t)
    {
        int dv = LevelDataVersion(t);
        double level = t.JavaFormat >= JavaFormat.AnvilPalette ? 99 : t.LegacyLevel;
        bool anvil = t.JavaFormat >= JavaFormat.AnvilNumeric;
        var d = new NbtCompound();
        if (t.JavaFormat != JavaFormat.Alpha)
        {
            d.SetString("LevelName", info.Name);
            d.SetInt("version", anvil ? 19133 : 19132);
        }
        if (dv < 2566) d.SetLong("RandomSeed", info.Seed);
        d.SetInt("SpawnX", info.SpawnX);
        d.SetInt("SpawnY", info.SpawnY);
        d.SetInt("SpawnZ", info.SpawnZ);
        d.SetLong("Time", info.Time);
        d.SetLong("LastPlayed", new DateTimeOffset(info.LastPlayed).ToUnixTimeMilliseconds());
        d.SetLong("SizeOnDisk", 0);
        if (level >= LegacyLevel.Beta_1_2 + 0.03 || anvil)
        {
            d.SetByte("raining", (sbyte)(info.Raining ? 1 : 0));
            d.SetInt("rainTime", info.RainTime);
            d.SetByte("thundering", (sbyte)(info.Thundering ? 1 : 0));
            d.SetInt("thunderTime", info.ThunderTime);
        }
        if (level >= LegacyLevel.Beta_1_8 || anvil)
        {
            d.SetInt("GameType", Math.Clamp(info.GameType, 0, level >= 1.08 ? 3 : 1));
            if (dv < 2566) d.SetByte("MapFeatures", 1);
        }
        if (level >= LegacyLevel.Release_1_0 || anvil) d.SetByte("hardcore", (sbyte)(info.Hardcore ? 1 : 0));
        if ((level >= 1.01 || anvil) && dv < 2566)
        {
            d.SetString("generatorName", info.Generator switch { "flat" => "flat", "amplified" when dv >= 1 => "amplified", "large_biomes" => "largeBiomes", _ => "default" });
            d.SetInt("generatorVersion", 1);
            if (info.Generator == "flat") d.SetString("generatorOptions", "");
        }
        if (level >= 1.03 || anvil)
        {
            d.SetByte("allowCommands", (sbyte)(info.AllowCommands ? 1 : 0));
            d.SetByte("initialized", 1);
        }
        if (level >= 1.04 || anvil)
        {
            d.SetLong("DayTime", info.DayTime);
            var rules = new NbtCompound();
            foreach (var (k, v) in info.GameRules) rules.SetString(k, v);
            d["GameRules"] = rules;
        }
        if (level >= 1.08 || anvil)
        {
            d.SetByte("Difficulty", (sbyte)Math.Clamp(info.Difficulty, 0, 3));
            d.SetByte("DifficultyLocked", 0);
            d.SetDouble("BorderCenterX", 0); d.SetDouble("BorderCenterZ", 0); d.SetDouble("BorderSize", 59999968);
            d.SetDouble("BorderSafeZone", 5); d.SetDouble("BorderWarningBlocks", 5); d.SetDouble("BorderWarningTime", 15);
            d.SetDouble("BorderSizeLerpTarget", 59999968); d.SetLong("BorderSizeLerpTime", 0); d.SetDouble("BorderDamagePerBlock", 0.2);
            d.SetInt("clearWeatherTime", 0);
        }
        if (dv >= 100)
        {
            d.SetInt("DataVersion", dv);
            var version = new NbtCompound { { "Id", new NbtInt(dv) }, { "Name", new NbtString(VersionName(dv)) }, { "Snapshot", new NbtByte(false) } };
            if (dv >= 2825) version.SetString("Series", "main");
            d["Version"] = version;
        }
        if (dv >= 1451)
        {
            var enabled = new NbtList(NbtType.String) { new NbtString("vanilla") };
            d["DataPacks"] = new NbtCompound { { "Enabled", enabled }, { "Disabled", new NbtList(NbtType.String) } };
            d["ServerBrands"] = new NbtList(NbtType.String) { new NbtString("vanilla") };
            d.SetByte("WasModded", 0);
        }
        if (dv >= 1952)
        {
            d.SetInt("WanderingTraderSpawnDelay", 24000);
            d.SetInt("WanderingTraderSpawnChance", 25);
        }
        if (dv >= 2566)
        {
            d.SetFloat("SpawnAngle", 0);
            d["WorldGenSettings"] = WorldGenSettings(info, dv);
        }
        if (dv >= 3205) d["enabled_features"] = new NbtList(NbtType.String) { new NbtString("minecraft:vanilla") };
        return d;
    }

    static NbtCompound WorldGenSettings(WorldInfo info, int dv)
    {
        bool seeds = dv < 2844;
        long seed = info.Seed;
        NbtCompound Gen(string settings, NbtCompound biomeSource)
        {
            var g = new NbtCompound { { "type", new NbtString("minecraft:noise") } };
            if (seeds) g.SetLong("seed", seed);
            g.SetString("settings", settings);
            if (seeds) biomeSource.SetLong("seed", seed);
            g["biome_source"] = biomeSource;
            return g;
        }
        NbtCompound overworldGen;
        if (info.Generator == "flat")
        {
            var layers = new NbtList(NbtType.Compound)
            {
                new NbtCompound { { "block", new NbtString("minecraft:bedrock") }, { "height", new NbtInt(1) } },
                new NbtCompound { { "block", new NbtString("minecraft:dirt") }, { "height", new NbtInt(2) } },
                new NbtCompound { { "block", new NbtString("minecraft:grass_block") }, { "height", new NbtInt(1) } },
            };
            var settings = new NbtCompound { { "biome", new NbtString("minecraft:plains") }, { "features", new NbtByte(false) }, { "lakes", new NbtByte(false) }, { "layers", layers } };
            if (dv < 3105) settings["structures"] = new NbtCompound { { "structures", new NbtCompound() } };
            else settings["structure_overrides"] = new NbtList(NbtType.String);
            overworldGen = new NbtCompound { { "type", new NbtString("minecraft:flat") }, { "settings", settings } };
        }
        else
        {
            var source = seeds
                ? new NbtCompound { { "type", new NbtString("minecraft:vanilla_layered") }, { "large_biomes", new NbtByte(info.Generator == "large_biomes") } }
                : new NbtCompound { { "type", new NbtString("minecraft:multi_noise") }, { "preset", new NbtString("minecraft:overworld") } };
            overworldGen = Gen(info.Generator switch { "amplified" => "minecraft:amplified", "large_biomes" when !seeds => "minecraft:large_biomes", _ => "minecraft:overworld" }, source);
        }
        var dims = new NbtCompound
        {
            { "minecraft:overworld", new NbtCompound { { "type", new NbtString("minecraft:overworld") }, { "generator", overworldGen } } },
            { "minecraft:the_nether", new NbtCompound { { "type", new NbtString("minecraft:the_nether") }, { "generator", Gen("minecraft:nether", new NbtCompound { { "type", new NbtString("minecraft:multi_noise") }, { "preset", new NbtString("minecraft:nether") } }) } } },
            { "minecraft:the_end", new NbtCompound { { "type", new NbtString("minecraft:the_end") }, { "generator", Gen("minecraft:end", new NbtCompound { { "type", new NbtString("minecraft:the_end") } }) } } },
        };
        return new NbtCompound
        {
            { "bonus_chest", new NbtByte(false) }, { "generate_features", new NbtByte(true) }, { "seed", new NbtLong(seed) }, { "dimensions", dims },
        };
    }

    /// <summary>Host player into level.dat (all versions in the written layout), other players into playerdata/ (1.7.6+) or players/.</summary>
    public static void WritePlayers(string folder, WorldInfo info, ConversionTarget t, NbtCompound levelData)
    {
        if (info.Players.Count == 0) return;
        int dv = LevelDataVersion(t);
        bool host = true;
        foreach (var p in info.Players)
        {
            var nbt = Players.ToJava(p, dv, t);
            if (host)
            {
                levelData["Player"] = nbt;
                host = false;
                // Rewrite level.dat with the player included.
                var file = new NbtFile { Format = NbtFormat.Java, Compression = IO.CompressionKind.Gzip };
                file.Roots.Add(new NamedTag("", new NbtCompound { { "Data", levelData } }));
                file.Save(Path.Combine(folder, "level.dat"));
                continue;
            }
            bool uuids = dv >= 100 || t.LegacyLevel >= 1.0706;
            string dir = Path.Combine(folder, uuids ? "playerdata" : "players");
            Directory.CreateDirectory(dir);
            var f = new NbtFile { Format = NbtFormat.Java, Compression = IO.CompressionKind.Gzip };
            f.Roots.Add(new NamedTag("", nbt));
            f.Save(Path.Combine(dir, (uuids ? p.Uuid.ToString() : p.Name) + ".dat"));
        }
    }
}
