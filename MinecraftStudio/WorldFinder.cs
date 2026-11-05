// MinecraftStudio/WorldFinder.cs: A world found on this PC, for the start page and explorer.
// Owns: FoundWorld, WorldFinder.
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio;

/// <summary>A world found on this PC, for the start page and explorer.</summary>
sealed record FoundWorld(string Path, string Name, WorldKind Kind, string Group, DateTime LastWrite, string? Detail);

/// <summary>The names of the places on this PC where worlds are found (they say which edition, and that it is the PC one).</summary>
static class WorldGroups
{
    public const string Java = "Java Edition (PC)";
    public const string JavaTLauncher = "Java Edition (PC · TLauncher)";
    public const string Bedrock = "Bedrock Edition (Windows PC)";
    public const string BedrockShared = "Bedrock Edition (Windows PC · shared)";
    public const string BedrockPreview = "Bedrock Preview (Windows PC)";
    public const string BedrockOldStore = "Bedrock Edition (Windows PC · older Store version)";
    public const string BedrockPreviewOldStore = "Bedrock Preview (Windows PC · older Store version)";
    public const string Education = "Education Edition (PC)";
    public static string JavaInstance(string name) => $"Java Edition (PC · {name})";
    public static string BedrockInstall(string name) => $"Bedrock Edition (Windows PC · {name})";
}

/// <summary>Finds worlds in the usual places for every launcher/edition on Windows.</summary>
static class WorldFinder
{
    public static IEnumerable<(string Group, string Folder)> Locations()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return (WorldGroups.Java, Path.Combine(appData, ".minecraft", "saves"));
        yield return (WorldGroups.JavaTLauncher, Path.Combine(appData, ".tlauncher", "legacy", "Minecraft", "game", "saves"));
        foreach (var root in new[] { Path.Combine(appData, "PrismLauncher", "instances"), Path.Combine(appData, ".minecraft", "instances"),
                                     Path.Combine(home, "curseforge", "minecraft", "Instances"), Path.Combine(appData, "ModrinthApp", "profiles"),
                                     Path.Combine(appData, "com.modrinth.theseus", "profiles"), Path.Combine(appData, "gdlauncher_next", "instances"),
                                     Path.Combine(appData, "MultiMC", "instances"), Path.Combine(appData, "ATLauncher", "instances") })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var inst in SafeDirs(root))
            {
                string saves = Path.Combine(inst, "saves");
                if (!Directory.Exists(saves)) saves = Path.Combine(inst, ".minecraft", "saves");
                if (Directory.Exists(saves)) yield return (WorldGroups.JavaInstance(Path.GetFileName(inst)), saves);
            }
        }
        string gdk = Path.Combine(appData, "Minecraft Bedrock", "Users");
        if (Directory.Exists(gdk))
            foreach (var u in SafeDirs(gdk))
            {
                string w = Path.Combine(u, "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(w)) yield return (Path.GetFileName(u) == "Shared" ? WorldGroups.BedrockShared : WorldGroups.Bedrock, w);
            }
        string preview = Path.Combine(appData, "Minecraft Bedrock Preview", "Users");
        if (Directory.Exists(preview))
            foreach (var u in SafeDirs(preview))
            {
                string w = Path.Combine(u, "games", "com.mojang", "minecraftWorlds");
                if (Directory.Exists(w)) yield return (WorldGroups.BedrockPreview, w);
            }
        yield return (WorldGroups.BedrockOldStore, Path.Combine(local, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftWorlds"));
        yield return (WorldGroups.BedrockPreviewOldStore, Path.Combine(local, "Packages", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftWorlds"));
        yield return (WorldGroups.Education, Path.Combine(local, "Packages", "Microsoft.MinecraftEducationEdition_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftWorlds"));
        yield return (WorldGroups.Education, Path.Combine(appData, "Minecraft Education Edition", "games", "com.mojang", "minecraftWorlds"));
        string launcher = Path.Combine(appData, ".minecraft_bedrock", "installations");
        if (Directory.Exists(launcher))
            foreach (var inst in SafeDirs(launcher))
            {
                string w = Path.Combine(inst, "packageData", "minecraftWorlds");
                if (Directory.Exists(w)) yield return (WorldGroups.BedrockInstall(Path.GetFileName(inst)), w);
            }
    }

    static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.EnumerateDirectories(path).ToList(); }
        catch { return []; }
    }

    public static List<FoundWorld> FindAll(IEnumerable<string> extraFolders)
    {
        var list = new List<FoundWorld>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var locations = Locations().ToList();
        foreach (var extra in extraFolders) locations.Add(("Added folders", extra));
        foreach (var (group, folder) in locations)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var dir in SafeDirs(folder))
            {
                if (!seen.Add(dir)) continue;
                var w = Describe(dir, group);
                if (w != null) list.Add(w);
            }
        }
        return list;
    }

    public static FoundWorld? Describe(string dir, string group)
    {
        try
        {
            var kind = WorldLoader.Detect(dir);
            if (kind == WorldKind.Unknown) return null;
            string name = Path.GetFileName(dir);
            string? detail = null;
            string levelDat = Path.Combine(dir, "level.dat");
            if (File.Exists(levelDat))
            {
                try
                {
                    var f = NbtFile.Load(levelDat);
                    var root = f.RootCompound;
                    var data = root.GetCompound("Data") ?? root;
                    name = data.GetString("LevelName") ?? name;
                    detail = data.GetCompound("Version")?.GetString("Name") is string v ? "Java " + v
                        : root.GetList("lastOpenedWithVersion") is { Count: >= 3 } lv ? "Bedrock " + string.Join(".", lv.Take(4).Select(t => t.AsLong))
                        : null;
                }
                catch { }
            }
            string txt = Path.Combine(dir, "levelname.txt");
            if (File.Exists(txt) && kind == WorldKind.Bedrock)
            {
                try { var t = File.ReadAllText(txt).Trim(); if (t.Length > 0) name = t; } catch { }
            }
            return new FoundWorld(dir, name, kind, group, Directory.GetLastWriteTime(dir), detail);
        }
        catch { return null; }
    }
}
