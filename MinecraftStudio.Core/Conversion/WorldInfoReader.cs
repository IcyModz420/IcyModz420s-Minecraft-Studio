// MinecraftStudio.Core/Conversion/WorldInfoReader.cs: World settings and players of any source world in
// edition-neutral form.
// Owns: WorldInfoReader.
using System.Diagnostics;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>World settings and players of any source world in edition-neutral form.</summary>
public static class WorldInfoReader
{
    public static WorldInfo Read(SaveWorld w)
    {
        var info = new WorldInfo { Name = w.Name };
        try
        {
            switch (w)
            {
                case JavaWorld j: ReadJava(j, info); break;
                case BedrockWorld b: ReadBedrock(b.LevelRoot, info); ReadBedrockPlayers(b, info); break;
                case ConsoleWorld c: if (c.Level?.RootCompound.GetCompound("Data") is NbtCompound cd) ReadJavaData(cd, info, 1343); ReadConsolePlayers(c, info); break;
                case PocketLegacyWorld p: ReadPocket(p, info); break;
                case ThreeDsWorld t:
                    ReadBedrock(t.Level.RootCompound, info);
                    // Its player numbers the inventory as Pocket Edition did (hotbar links first).
                    foreach (var p in t.Players) info.Players.Add(Players.FromBedrock(p.Name, p.Data, pocketSlots: true));
                    break;
                case FiniteWorld f: ReadFinite(f, info); break;
                case HeightmapWorld h: h.Describe(info); break;
            }
        }
        catch { }
        return info;
    }

    static void ReadJava(JavaWorld w, WorldInfo info)
    {
        int dv = w.DataVersion ?? 0;
        if (w.Data is NbtCompound d) ReadJavaData(d, info, dv);
        if (w.Seed is long seed) info.Seed = seed;
        // The single-player host is level.dat's Player (read with the level data); playerdata holds everyone else and a
        // copy of the host, which is skipped so the host is not converted twice.
        foreach (var p in w.Players)
        {
            if (p.Source.EndsWith("level.dat", StringComparison.OrdinalIgnoreCase)) continue;
            var player = Players.FromJava(p.Name, p.Data, p.Data.GetInt("DataVersion", dv));
            if (info.Players.Any(x => x.Uuid == player.Uuid)) continue;
            info.Players.Add(player);
        }
    }

    static void ReadJavaData(NbtCompound d, WorldInfo info, int dv)
    {
        if (d.GetString("LevelName") is string name && name.Length > 0) info.Name = name;
        if (d.GetLongOrNull("RandomSeed") is long seed) info.Seed = seed;
        if (d.GetCompound("WorldGenSettings")?.GetLongOrNull("seed") is long s2) info.Seed = s2;
        if (d.GetLongOrNull("SpawnX") is long sx) { info.SpawnX = (int)sx; info.SpawnY = d.GetInt("SpawnY", 64); info.SpawnZ = d.GetInt("SpawnZ"); }
        else if (d.GetCompound("spawn")?.GetInts("pos") is { Length: 3 } sp) { info.SpawnX = sp[0]; info.SpawnY = sp[1]; info.SpawnZ = sp[2]; }
        info.GameType = d.GetInt("GameType");
        if (d.GetLongOrNull("Difficulty") is long diff) info.Difficulty = (int)diff;
        else if (d.GetCompound("difficulty_settings")?.GetString("difficulty") is string dn)
            info.Difficulty = dn switch { "peaceful" => 0, "easy" => 1, "hard" => 3, _ => 2 };
        info.Hardcore = d.GetBool("hardcore") || d.GetCompound("difficulty_settings")?.GetBool("hardcore") == true;
        info.AllowCommands = d.GetBool("allowCommands");
        info.Time = d.GetLong("Time");
        info.DayTime = d.GetLong("DayTime", info.Time % 24000);
        info.Raining = d.GetBool("raining");
        info.Thundering = d.GetBool("thundering");
        info.RainTime = d.GetInt("rainTime", 12000);
        info.ThunderTime = d.GetInt("thunderTime", 12000);
        if (d.GetLongOrNull("LastPlayed") is long lp && lp > 0) info.LastPlayed = DateTimeOffset.FromUnixTimeMilliseconds(lp).UtcDateTime;
        foreach (var (k, v) in d.GetCompound("GameRules") ?? []) info.GameRules[k] = v is NbtString s ? s.Value : v.ToString();
        string? gen = d.GetString("generatorName") ?? d.GetCompound("WorldGenSettings")?.GetCompound("dimensions")?.GetCompound("minecraft:overworld")?.GetCompound("generator")?.GetString("type");
        info.Generator = gen switch { "flat" or "minecraft:flat" => "flat", "amplified" => "amplified", "largeBiomes" => "large_biomes", _ => "default" };
        if (d.GetCompound("Player") is NbtCompound player && info.Players.Count == 0) info.Players.Add(Players.FromJava("Player", player, dv));
    }

    static void ReadBedrock(NbtCompound d, WorldInfo info)
    {
        if (d.GetString("LevelName") is string name && name.Length > 0) info.Name = name;
        info.Seed = d.GetLong("RandomSeed");
        info.SpawnX = d.GetInt("SpawnX");
        int sy = d.GetInt("SpawnY", 64);
        info.SpawnY = sy == 32767 ? 64 : sy;
        info.SpawnZ = d.GetInt("SpawnZ");
        int gt = d.GetInt("GameType");
        info.GameType = gt switch { 6 => 3, 5 => 0, var g and >= 0 and <= 2 => g, _ => 0 };
        info.Difficulty = d.GetInt("Difficulty", 2);
        info.Hardcore = d.GetBool("IsHardcore");
        info.AllowCommands = d.GetBool("cheatsEnabled") || d.GetBool("commandsEnabled");
        info.DayTime = d.GetLong("Time");
        info.Time = d.GetLong("currentTick", info.DayTime);
        info.Raining = d.GetDouble("rainLevel") > 0;
        info.Thundering = d.GetDouble("lightningLevel") > 0;
        info.RainTime = d.GetInt("rainTime", 12000);
        info.ThunderTime = d.GetInt("lightningTime", 12000);
        if (d.GetLongOrNull("LastPlayed") is long lp && lp > 0) info.LastPlayed = DateTimeOffset.FromUnixTimeSeconds(lp).UtcDateTime;
        info.Generator = d.GetInt("Generator", 1) switch { 0 => "old", 2 => "flat", 5 => "void", _ => "default" };
        foreach (var (java, bedrock, isInt) in BedrockLevelData.GameRules)
        {
            if (d[bedrock] is not { IsNumeric: true } v) continue;
            info.GameRules[java] = isInt ? (java == "randomTickSpeed" ? (v.AsLong * 3).ToString() : v.AsLong.ToString()) : (v.AsLong != 0 ? "true" : "false");
        }
    }

    static void ReadBedrockPlayers(BedrockWorld w, WorldInfo info)
    {
        foreach (var p in w.Players.OrderBy(p => p.Name == "Local player" ? 0 : 1))
        {
            if (p.Data.GetString("MsaId") != null && !p.Data.ContainsKey("Pos")) continue; // player_<id> index records
            info.Players.Add(Players.FromBedrock(p.Name, p.Data));
        }
    }

    static void ReadConsolePlayers(ConsoleWorld w, WorldInfo info)
    {
        foreach (var p in w.Players)
        {
            var player = Players.FromJava(p.Name, p.Data, 1343);
            player.SourceFile = p.Source;
            info.Players.Add(player);
        }
    }

    static void ReadPocket(PocketLegacyWorld w, WorldInfo info)
    {
        if (w.RawLevel is PocketRawLevel raw)
        {
            info.Name = raw.Name.Length > 0 ? raw.Name : info.Name;
            info.Seed = raw.Seed;
            info.SpawnX = raw.SpawnX; info.SpawnY = raw.SpawnY; info.SpawnZ = raw.SpawnZ;
        }
        else if (w.Level?.RootCompound is NbtCompound d)
        {
            ReadBedrock(d, info);
            if (d.GetCompound("Player") is NbtCompound player) info.Players.Add(Players.FromBedrock("Player", player, pocketSlots: true));
        }
        info.Generator = "old";
    }

    static void ReadFinite(FiniteWorld f, WorldInfo info)
    {
        info.Generator = "flat";
        if (f.Classic is ClassicLevel c)
        {
            info.SpawnX = c.SpawnX; info.SpawnY = c.SpawnY; info.SpawnZ = c.SpawnZ;
            info.GameType = 1;
        }
        else if (f.Indev is IndevLevel l)
        {
            var (x, y, z) = l.Spawn;
            info.SpawnX = x; info.SpawnY = y; info.SpawnZ = z;
            // The level's player (its inventory) travels with the world like any other edition's.
            if (f.Players.FirstOrDefault() is PlayerRecord p) info.Players.Add(Players.FromJava(p.Name, p.Data, 0));
        }
    }
}
