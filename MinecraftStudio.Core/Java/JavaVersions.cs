// MinecraftStudio.Core/Java/JavaVersions.cs: Maps data versions to game versions (from the embedded
// dataversions.csv).
// Owns: JavaVersions.
using System.Reflection;

namespace MinecraftStudio.Core.Java;

/// <summary>Maps data versions to game versions (from the embedded dataversions.csv).</summary>
public static class JavaVersions
{
    static readonly Lazy<SortedDictionary<int, (string Name, string Type)>> Table = new(Load);

    static SortedDictionary<int, (string, string)> Load()
    {
        var map = new SortedDictionary<int, (string, string)>();
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MinecraftStudio.Data.dataversions.csv");
        if (s != null)
        {
            using var reader = new StreamReader(s);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var parts = line.Split(',');
                if (parts.Length < 2 || !int.TryParse(parts[0], out int dv)) continue;
                string type = parts.Length > 2 ? parts[2].Trim() : "";
                // Prefer releases when several versions share a data version.
                if (map.TryGetValue(dv, out var existing) && existing.Item2 == "release" && type != "release") continue;
                map[dv] = (parts[1].Trim(), type);
            }
        }
        if (map.Count == 0)
        {
            foreach (var (dv, name) in Fallback) map[dv] = (name, "release");
        }
        return map;
    }

    static readonly (int, string)[] Fallback =
    [
        (169, "1.9"), (184, "1.9.4"), (510, "1.10"), (512, "1.10.2"), (819, "1.11"), (922, "1.11.2"),
        (1139, "1.12"), (1343, "1.12.2"), (1519, "1.13"), (1631, "1.13.2"), (1952, "1.14"), (1976, "1.14.4"),
        (2225, "1.15"), (2230, "1.15.2"), (2566, "1.16"), (2586, "1.16.5"), (2724, "1.17"), (2730, "1.17.1"),
        (2860, "1.18"), (2865, "1.18.1"), (2975, "1.18.2"), (3105, "1.19"), (3120, "1.19.2"), (3218, "1.19.3"),
        (3337, "1.19.4"), (3463, "1.20"), (3465, "1.20.1"), (3578, "1.20.2"), (3700, "1.20.4"), (3839, "1.20.6"),
        (3953, "1.21"), (3955, "1.21.1"), (4082, "1.21.3"), (4189, "1.21.4"), (4325, "1.21.5"), (4435, "1.21.6"),
        (4438, "1.21.7"), (4440, "1.21.8"),
    ];

    /// <summary>Exact version name for a data version, or the closest older one with a "+".</summary>
    public static string? NameOf(int dataVersion)
    {
        var t = Table.Value;
        if (t.TryGetValue(dataVersion, out var e)) return e.Name;
        string? best = null;
        foreach (var kv in t)
        {
            if (kv.Key > dataVersion) break;
            best = kv.Value.Name;
        }
        return best != null ? best + "+" : null;
    }

    public static int? DataVersionOf(string versionName)
    {
        foreach (var kv in Table.Value) if (string.Equals(kv.Value.Name, versionName, StringComparison.OrdinalIgnoreCase)) return kv.Key;
        return null;
    }

    public static IEnumerable<(int DataVersion, string Name, string Type)> All => Table.Value.Select(kv => (kv.Key, kv.Value.Name, kv.Value.Type));

    public static string Describe(int? dataVersion, JavaStorage storage, int? storageVersion)
    {
        if (dataVersion is int dv) return NameOf(dv) ?? $"data version {dv}";
        return storage switch
        {
            JavaStorage.Alpha => "Infdev / Alpha / Beta 1.0-1.2",
            JavaStorage.McRegion => "Beta 1.3 - 1.1",
            JavaStorage.Anvil => "1.2 - 1.8",
            _ => storageVersion == 19133 ? "1.2 - 1.8" : storageVersion == 19132 ? "Beta 1.3 - 1.1" : "unknown version",
        };
    }
}
