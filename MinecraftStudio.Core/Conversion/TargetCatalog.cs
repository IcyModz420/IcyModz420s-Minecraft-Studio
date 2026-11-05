// MinecraftStudio.Core/Conversion/TargetCatalog.cs: Every version the converter can write.
// Owns: TargetCatalog.
using System.Diagnostics;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>Every version the converter can write.</summary>
public static class TargetCatalog
{
    static ConversionTarget Java(string name, int dv, JavaFormat format, double level = LegacyLevel.Release_1_12) =>
        new() { Kind = TargetKind.Java, Name = "Java " + name, DataVersion = dv, JavaFormat = format, LegacyLevel = level, VersionName = name };

    static ConversionTarget Bedrock(string name, int chunk, int storage, int network, int[] version, int content = 0, BedrockTerrain terrain = BedrockTerrain.States, double level = LegacyLevel.Release_1_12) =>
        new()
        {
            Kind = TargetKind.Bedrock, Name = (name.StartsWith("Pocket") ? "" : "Bedrock ") + name, BedrockChunkVersion = chunk, BedrockStorageVersion = storage,
            BedrockNetworkVersion = network, BedrockVersion = version, VersionName = name, ContentDataVersion = content, BedrockTerrain = terrain, LegacyLevel = level,
        };

    static ConversionTarget Console(LegacyConsole.ConsolePlatform p, string note) =>
        new() { Kind = TargetKind.LegacyConsole, Name = $"{LegacyConsole.ConsolePlatforms.DisplayName(p)} ({note})", Platform = p, VersionName = note };

    public static IReadOnlyList<ConversionTarget> All { get; } =
    [
        Console(LegacyConsole.ConsolePlatform.Xbox360, "Legacy Console, TU17+ save"),
        Console(LegacyConsole.ConsolePlatform.PS3, "Legacy Console, TU17+ save"),
        Console(LegacyConsole.ConsolePlatform.WiiU, "Legacy Console"),
        Console(LegacyConsole.ConsolePlatform.PSVita, "Legacy Console"),
        Console(LegacyConsole.ConsolePlatform.PS4, "Legacy Console"),
        Console(LegacyConsole.ConsolePlatform.XboxOne, "Legacy Console"),
        Console(LegacyConsole.ConsolePlatform.Switch, "Legacy Console"),
        Bedrock("1.21.120 – 26.x", 42, 10, 859, [1, 21, 120, 0, 0]),
        Bedrock("1.21.40 – 1.21.11x", 41, 10, 748, [1, 21, 40, 0, 0], 3955),
        Bedrock("1.21.0 – 1.21.30", 40, 10, 685, [1, 21, 0, 0, 0], 3953),
        Bedrock("1.20.0 – 1.20.8x", 40, 10, 589, [1, 20, 0, 0, 0], 3463),
        Bedrock("1.19.40 – 1.19.8x", 40, 10, 557, [1, 19, 40, 0, 0], 3105),
        Bedrock("1.18.30 – 1.19.30", 40, 9, 503, [1, 18, 30, 0, 0], 2860),
        Bedrock("1.18.0 – 1.18.1x", 39, 8, 475, [1, 18, 0, 0, 0], 2860),
        Bedrock("1.17.0 – 1.17.4x", 22, 8, 440, [1, 17, 0, 0, 0], 2724),
        Bedrock("1.16.0 – 1.16.20x", 19, 8, 407, [1, 16, 0, 0, 0], 2566),
        Bedrock("1.13 – 1.15", 15, 8, 388, [1, 13, 0, 0, 0], 1952),
        Bedrock("1.4 – 1.12", 8, 8, 261, [1, 4, 0, 0, 0], 1519, BedrockTerrain.NameVal),
        Bedrock("1.2.13 – 1.3", 8, 6, 223, [1, 2, 13, 0, 0], 0, BedrockTerrain.NameValSingle, LegacyLevel.Release_1_12),
        Bedrock("1.1 – 1.2.10", 4, 5, 113, [1, 1, 0, 0, 0], 0, BedrockTerrain.Numeric, LegacyLevel.Release_1_12),
        Bedrock("Pocket Edition 0.9 – 0.16", 2, 3, 0, [0, 9, 5, 0, 0], 0, BedrockTerrain.LegacyTerrain, LegacyLevel.Release_1_8),
        new() { Kind = TargetKind.PocketLegacy, Name = "Pocket Edition 0.2 – 0.8 · Pi Edition", VersionName = "Pocket Edition 0.2 – 0.8 · Pi Edition", LegacyLevel = LegacyLevel.Release_1_7 },
        new() { Kind = TargetKind.Indev, Name = "Indev (.mclevel)", VersionName = "Indev", LegacyLevel = LegacyLevel.Indev },
        new() { Kind = TargetKind.Classic, Name = "Classic 0.0.13a – 0.30 · ClassiCube", VersionName = "Classic 0.0.13a – 0.30 · ClassiCube", LegacyLevel = LegacyLevel.Classic },
        Java("26.1", 4786, JavaFormat.AnvilContainers),
        Java("1.21.11", 4671, JavaFormat.AnvilContainers),
        Java("1.21.9 – 1.21.10", 4554, JavaFormat.AnvilContainers),
        Java("1.21.6 – 1.21.8", 4435, JavaFormat.AnvilContainers),
        Java("1.21.5", 4325, JavaFormat.AnvilContainers),
        Java("1.21.4", 4189, JavaFormat.AnvilContainers),
        Java("1.21.2 – 1.21.3", 4080, JavaFormat.AnvilContainers),
        Java("1.21 – 1.21.1", 3953, JavaFormat.AnvilContainers),
        Java("1.20.5 – 1.20.6", 3837, JavaFormat.AnvilContainers),
        Java("1.20.3 – 1.20.4", 3698, JavaFormat.AnvilContainers),
        Java("1.20.2", 3578, JavaFormat.AnvilContainers),
        Java("1.20 – 1.20.1", 3463, JavaFormat.AnvilContainers),
        Java("1.19.4", 3337, JavaFormat.AnvilContainers),
        Java("1.19.3", 3218, JavaFormat.AnvilContainers),
        Java("1.19 – 1.19.2", 3105, JavaFormat.AnvilContainers),
        Java("1.18 – 1.18.2", 2860, JavaFormat.AnvilContainers),
        Java("1.17 – 1.17.1", 2724, JavaFormat.AnvilPalette),
        Java("1.16.2 – 1.16.5", 2578, JavaFormat.AnvilPalette),
        Java("1.16 – 1.16.1", 2566, JavaFormat.AnvilPalette),
        Java("1.15 – 1.15.2", 2225, JavaFormat.AnvilPalette),
        Java("1.14 – 1.14.4", 1952, JavaFormat.AnvilPalette),
        Java("1.13.2", 1631, JavaFormat.AnvilPalette),
        Java("1.13 – 1.13.1", 1519, JavaFormat.AnvilPalette),
        Java("1.12 – 1.12.2", 1139, JavaFormat.AnvilNumeric),
        Java("1.11 – 1.11.2", 819, JavaFormat.AnvilNumeric, 1.11),
        Java("1.10 – 1.10.2", 510, JavaFormat.AnvilNumeric, 1.10),
        Java("1.9 – 1.9.4", 169, JavaFormat.AnvilNumeric, 1.09),
        Java("1.8 – 1.8.9", 0, JavaFormat.AnvilNumeric, LegacyLevel.Release_1_8),
        Java("1.7.2 – 1.7.10", 0, JavaFormat.AnvilNumeric, LegacyLevel.Release_1_7),
        Java("1.6 – 1.6.4", 0, JavaFormat.AnvilNumeric, 1.06),
        Java("1.5 – 1.5.2", 0, JavaFormat.AnvilNumeric, 1.05),
        Java("1.4.2 – 1.4.7", 0, JavaFormat.AnvilNumeric, 1.04),
        Java("1.3 – 1.3.2", 0, JavaFormat.AnvilNumeric, 1.03),
        Java("1.2 – 1.2.5", 0, JavaFormat.AnvilNumeric, LegacyLevel.Release_1_2),
        Java("1.1", 0, JavaFormat.McRegion, 1.01),
        Java("1.0", 0, JavaFormat.McRegion, LegacyLevel.Release_1_0),
        Java("Beta 1.8 – 1.8.1", 0, JavaFormat.McRegion, LegacyLevel.Beta_1_8),
        Java("Beta 1.7 – 1.7.3", 0, JavaFormat.McRegion, LegacyLevel.Beta_1_7),
        Java("Beta 1.6 – 1.6.6", 0, JavaFormat.McRegion, 0.56),
        Java("Beta 1.5", 0, JavaFormat.McRegion, 0.55),
        Java("Beta 1.3 – 1.4", 0, JavaFormat.McRegion, LegacyLevel.Beta_1_3),
        Java("Beta 1.2", 0, JavaFormat.Alpha, LegacyLevel.Beta_1_2),
        Java("Alpha 1.2 – Beta 1.1", 0, JavaFormat.Alpha, LegacyLevel.Alpha_1_2),
        Java("Infdev / Alpha 1.0 – 1.1", 0, JavaFormat.Alpha, LegacyLevel.Alpha),
    ];

    /// <summary>
    /// The target for a world's own edition and version, so a rescued world can be written back for the game it came
    /// from: Java by the data version of its level.dat (or of its chunks when level.dat is lost), Bedrock by the version
    /// that last opened it (or its chunks' format), consoles by platform. A 3DS world gets the newest Bedrock.
    /// </summary>
    public static ConversionTarget ClosestTo(SaveWorld world)
    {
        var bedrock = All.Where(t => t.Kind == TargetKind.Bedrock).ToList();
        switch (world)
        {
            case JavaWorld j:
                return ForJava(j.DataVersion is int dv && dv > 0 ? dv : ChunkDataVersion(j), j.Storage, j.Data?.GetCompound("Version")?.GetBool("Snapshot") == true);
            case BedrockWorld b:
                if (b.LevelRoot.GetList("lastOpenedWithVersion") is { Count: >= 3 } list && list.All(t => t.IsNumeric))
                {
                    var version = list.Select(t => (int)t.AsLong).ToArray();
                    if (version[0] > 0 && bedrock.FirstOrDefault(t => CompareVersions(t.BedrockVersion, version) <= 0) is ConversionTarget opened) return opened;
                }
                // Without the version: the format of the chunks. Of the versions writing that format the oldest is
                // chosen, so the world does not claim to need a newer game than it does.
                int chunkVersion = BedrockChunkVersion(b);
                return chunkVersion > 0
                    ? bedrock.LastOrDefault(t => t.BedrockChunkVersion == chunkVersion) ?? bedrock.FirstOrDefault(t => t.BedrockChunkVersion <= chunkVersion) ?? bedrock[0]
                    : bedrock[0];
            case ConsoleWorld c:
                return All.FirstOrDefault(t => t.Kind == TargetKind.LegacyConsole && t.Platform == c.Platform) ?? All.First(t => t.Kind == TargetKind.LegacyConsole);
            case PocketLegacyWorld:
                return All.First(t => t.Kind == TargetKind.PocketLegacy);
            case FiniteWorld f:
                return All.First(t => t.Kind == (f.Classic != null ? TargetKind.Classic : TargetKind.Indev));
            default:
                return bedrock[0];
        }
    }

    /// <summary>
    /// The Java target for a data version: a release belongs to the newest target not newer than it, a snapshot or
    /// pre-release to the release after it (the one before cannot open it). 0 (before 1.9) is chosen by how the chunks
    /// are stored.
    /// </summary>
    public static ConversionTarget ForJava(int dataVersion, JavaStorage storage, bool snapshot = false)
    {
        var java = All.Where(t => t.Kind == TargetKind.Java).ToList();
        if (dataVersion > 0)
        {
            if (snapshot && java.LastOrDefault(t => t.DataVersion >= dataVersion) is ConversionTarget next) return next;
            return java.FirstOrDefault(t => t.DataVersion > 0 && t.DataVersion <= dataVersion) ?? java.Last(t => t.DataVersion > 0);
        }
        return storage switch
        {
            JavaStorage.Alpha => java.First(t => t.JavaFormat == JavaFormat.Alpha),
            JavaStorage.McRegion => java.First(t => t.JavaFormat == JavaFormat.McRegion),
            _ => java.First(t => t.DataVersion == 0 && t.JavaFormat == JavaFormat.AnvilNumeric),
        };
    }

    static int CompareVersions(int[] a, int[] b)
    {
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    /// <summary>The newest DataVersion of a Java world's chunks (for a world whose level.dat is lost).</summary>
    static int ChunkDataVersion(JavaWorld world)
    {
        int best = world.Dimensions.OfType<JavaRegionDimension>().SelectMany(d => d.Recoveries.Values).Select(r => r.DataVersion).DefaultIfEmpty(0).Max();
        if (best > 0) return best;
        foreach (var d in world.Dimensions)
            foreach (var pos in d.ChunkPositions.Take(32))
            {
                try { if (d.LoadChunkNbt(pos)?.GetLongOrNull("DataVersion") is long dv && dv > 0) return (int)dv; }
                catch (Exception e) when (e is IOException or InvalidDataException or IO.SaveFormatException or EndOfStreamException) { }
            }
        return 0;
    }

    /// <summary>The newest chunk format in a Bedrock database (its chunks' version records), 0 when there are none.</summary>
    static int BedrockChunkVersion(BedrockWorld world)
    {
        int best = 0;
        foreach (var key in world.Db.Keys())
        {
            if (key.Length is not (9 or 13) || key[^1] is not (44 or 118)) continue;
            try { if (world.Db.Get(key) is { Length: > 0 } v) best = Math.Max(best, v[0]); }
            catch (Exception e) when (e is IOException or InvalidDataException or IO.SaveFormatException) { }
        }
        return best;
    }
}
