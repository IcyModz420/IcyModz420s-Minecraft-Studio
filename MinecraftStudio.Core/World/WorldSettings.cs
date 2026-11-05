// MinecraftStudio.Core/World/WorldSettings.cs: the world's name, spawn point, time of day, weather and (Java) world
// border, as each edition keeps them in level.dat.
// Owns: Weather, WorldSettings — read from the fields the world has (what it lacks stays null and is not offered), and
// written back only where changed, each in the type it was stored with. The time of day keeps the days already played;
// weather sets the flags and gives the new weather ten minutes before the game changes it. Bedrock's spawn height 32767
// means "on the surface". The caller saves level.dat (WorldFlags.Save, after a backup).
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

public enum Weather { Clear, Rain, Thunder }

public sealed class WorldSettings
{
    /// <summary>Bedrock's spawn height for "on the surface".</summary>
    public const int Surface = 32767;
    /// <summary>Ticks in a Minecraft day; 0 is sunrise, 6000 noon, 12000 sunset, 18000 midnight.</summary>
    public const int Day = 24000;
    const int TenMinutes = 12000;

    readonly NbtCompound? _root;
    readonly bool _bedrock;
    readonly string? _timeKey;
    readonly string? _nameFile;

    WorldSettings(SaveWorld world)
    {
        (_root, _bedrock) = world switch
        {
            JavaWorld j => (j.Data, false),
            ConsoleWorld c => (c.Level?.RootCompound.GetCompound("Data"), false),
            BedrockWorld b => (b.LevelRoot, true),
            ThreeDsWorld t => (t.Level.RootCompound, true),
            PocketLegacyWorld p => (p.Level?.RootCompound, true),
            _ => (null, false),
        };
        if (_root == null) return;
        // The name the game's world list shows: level.dat's LevelName (and Bedrock's levelname.txt beside it). A console
        // save's list shows the save's own title, so the name inside it is not offered.
        if (world is not ConsoleWorld && _root["LevelName"] is NbtString levelName)
        {
            Name = OriginalName = levelName.Value;
            if (world is BedrockWorld bw && File.Exists(Path.Combine(bw.Folder, "levelname.txt"))) _nameFile = Path.Combine(bw.Folder, "levelname.txt");
        }
        _timeKey = _bedrock ? (_root.ContainsKey("Time") ? "Time" : null) : (_root.ContainsKey("DayTime") ? "DayTime" : null);
        SpawnX = OriginalSpawnX = Int("SpawnX");
        SpawnY = OriginalSpawnY = Int("SpawnY");
        SpawnZ = OriginalSpawnZ = Int("SpawnZ");
        if (_timeKey != null) TimeOfDay = OriginalTimeOfDay = (int)(((_root.GetLong(_timeKey) % Day) + Day) % Day);
        bool rain = _bedrock ? _root.GetDouble("rainLevel") > 0 : _root.GetBool("raining");
        bool thunder = _bedrock ? _root.GetDouble("lightningLevel") > 0 : _root.GetBool("thundering");
        if (_root.ContainsKey(_bedrock ? "rainLevel" : "raining")) Weather = OriginalWeather = thunder && rain ? World.Weather.Thunder : rain ? World.Weather.Rain : World.Weather.Clear;
        if (!_bedrock && _root.ContainsKey("BorderSize"))
        {
            BorderSize = OriginalBorderSize = _root.GetDouble("BorderSize");
            BorderCenterX = OriginalBorderCenterX = _root.GetDouble("BorderCenterX");
            BorderCenterZ = OriginalBorderCenterZ = _root.GetDouble("BorderCenterZ");
        }
    }

    int? Int(string key) => _root?[key] is { IsNumeric: true } t ? (int)t.AsLong : null;

    public static WorldSettings Read(SaveWorld world) => new(world);

    public bool IsBedrock => _bedrock;
    /// <summary>The world's name in the game's list, or null when the world keeps none this program changes.</summary>
    public string? Name { get; set; }
    public int? SpawnX { get; set; }
    public int? SpawnY { get; set; }
    public int? SpawnZ { get; set; }
    /// <summary>Ticks into the current day (0 sunrise … 23999), or null when the world keeps no time.</summary>
    public int? TimeOfDay { get; set; }
    public Weather? Weather { get; set; }
    /// <summary>Java: the world border's width in blocks and its centre.</summary>
    public double? BorderSize { get; set; }
    public double? BorderCenterX { get; set; }
    public double? BorderCenterZ { get; set; }

    string? OriginalName { get; }
    int? OriginalSpawnX { get; }
    int? OriginalSpawnY { get; }
    int? OriginalSpawnZ { get; }
    int? OriginalTimeOfDay { get; }
    Weather? OriginalWeather { get; }
    double? OriginalBorderSize { get; }
    double? OriginalBorderCenterX { get; }
    double? OriginalBorderCenterZ { get; }

    /// <summary>
    /// Writes what changed into level.dat in memory (only fields it has, each in its own type) — and Bedrock's levelname.txt
    /// beside it, on disk; returns what changed.
    /// </summary>
    public List<string> Apply()
    {
        var done = new List<string>();
        if (_root == null) return done;
        if (Name is string name && name.Trim().Length > 0 && name.Trim() != OriginalName)
        {
            name = name.Trim();
            _root.SetString("LevelName", name);
            if (_nameFile != null) IO.SafeFile.WriteAllBytes(_nameFile, System.Text.Encoding.UTF8.GetBytes(name));
            done.Add($"name \"{name}\"");
        }
        if (SpawnX != OriginalSpawnX || SpawnY != OriginalSpawnY || SpawnZ != OriginalSpawnZ)
        {
            Number("SpawnX", SpawnX); Number("SpawnY", SpawnY); Number("SpawnZ", SpawnZ);
            done.Add($"world spawn {SpawnX}, {(SpawnY == Surface && _bedrock ? "surface" : SpawnY?.ToString())}, {SpawnZ}");
        }
        if (_timeKey != null && TimeOfDay is int tod && tod != OriginalTimeOfDay)
        {
            // The days already played stay; only the time within the day changes.
            long now = _root.GetLong(_timeKey);
            long days = (long)Math.Floor(now / (double)Day);
            Long(_timeKey, days * Day + Math.Clamp(tod, 0, Day - 1));
            done.Add($"time of day {Clock(tod)}");
        }
        if (Weather is World.Weather w && w != OriginalWeather)
        {
            bool rain = w != World.Weather.Clear, thunder = w == World.Weather.Thunder;
            if (_bedrock)
            {
                Float("rainLevel", rain ? 1 : 0); Float("lightningLevel", thunder ? 1 : 0);
                Number("rainTime", TenMinutes); Number("lightningTime", TenMinutes);
            }
            else
            {
                Number("raining", rain ? 1 : 0); Number("thundering", thunder ? 1 : 0);
                Number("rainTime", TenMinutes); Number("thunderTime", TenMinutes);
                if (_root.ContainsKey("clearWeatherTime")) Number("clearWeatherTime", rain ? 0 : TenMinutes);
            }
            done.Add("weather " + w.ToString().ToLowerInvariant());
        }
        if (BorderSize != OriginalBorderSize || BorderCenterX != OriginalBorderCenterX || BorderCenterZ != OriginalBorderCenterZ)
        {
            Double("BorderSize", BorderSize); Double("BorderCenterX", BorderCenterX); Double("BorderCenterZ", BorderCenterZ);
            // A border that was shrinking keeps the new size.
            if (_root.ContainsKey("BorderSizeLerpTarget") && BorderSize is double size) Double("BorderSizeLerpTarget", size);
            if (_root.ContainsKey("BorderSizeLerpTime")) Long("BorderSizeLerpTime", 0);
            done.Add($"world border {BorderSize:0} wide at {BorderCenterX:0}, {BorderCenterZ:0}");
        }
        return done;
    }

    /// <summary>A time of day as a clock reads it (0 = 6:00, 6000 = noon).</summary>
    public static string Clock(int ticks)
    {
        int minutes = (int)((ticks / (double)Day * 24 * 60 + 6 * 60) % (24 * 60));
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    // Writes keep the tag's own type; a field the file does not have is not added.
    void Number(string key, int? value)
    {
        if (value is not int v || _root![key] is not NbtTag old) return;
        _root[key] = old switch
        {
            NbtByte => new NbtByte((sbyte)Math.Clamp(v, sbyte.MinValue, sbyte.MaxValue)),
            NbtShort => new NbtShort((short)Math.Clamp(v, short.MinValue, short.MaxValue)),
            NbtLong => new NbtLong(v),
            NbtFloat => new NbtFloat(v),
            NbtDouble => new NbtDouble(v),
            _ => new NbtInt(v),
        };
    }

    void Long(string key, long value)
    {
        if (_root![key] is not NbtTag old) return;
        _root[key] = old is NbtInt ? new NbtInt((int)Math.Clamp(value, int.MinValue, int.MaxValue)) : new NbtLong(value);
    }

    void Float(string key, float value)
    {
        if (_root![key] is not NbtTag old) return;
        _root[key] = old is NbtDouble ? new NbtDouble(value) : new NbtFloat(value);
    }

    void Double(string key, double? value)
    {
        if (value is not double v || _root![key] is not NbtTag old) return;
        _root[key] = old is NbtFloat ? new NbtFloat((float)v) : new NbtDouble(v);
    }
}
