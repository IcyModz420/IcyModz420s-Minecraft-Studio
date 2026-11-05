// MinecraftStudio.Core/Packs/GameFolders.cs: Where the games on this PC keep their packs.
// Owns: GameFolders.
namespace MinecraftStudio.Core.Packs;

/// <summary>Where the games on this PC keep their packs.</summary>
public static class GameFolders
{
    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Java game folders (the folder with saves, resourcepacks and options.txt): the official launcher's .minecraft,
    /// TLauncher's, and every instance of Prism, MultiMC, CurseForge, Modrinth, GDLauncher and ATLauncher.
    /// </summary>
    public static List<(string Label, string Folder)> Java()
    {
        var list = new List<(string, string)>();
        void Add(string label, string folder)
        {
            if (Directory.Exists(folder) && (File.Exists(Path.Combine(folder, "options.txt")) || Directory.Exists(Path.Combine(folder, "saves")) ||
                Directory.Exists(Path.Combine(folder, "resourcepacks")) || Directory.Exists(Path.Combine(folder, "versions"))))
                list.Add((label, folder));
        }
        Add("Minecraft Launcher", Path.Combine(AppData, ".minecraft"));
        Add("TLauncher", Path.Combine(AppData, ".tlauncher", "legacy", "Minecraft", "game"));
        foreach (var (launcher, root) in new[]
        {
            ("Prism", Path.Combine(AppData, "PrismLauncher", "instances")), ("MultiMC", Path.Combine(AppData, "MultiMC", "instances")),
            ("CurseForge", Path.Combine(Home, "curseforge", "minecraft", "Instances")), ("Modrinth", Path.Combine(AppData, "ModrinthApp", "profiles")),
            ("Modrinth", Path.Combine(AppData, "com.modrinth.theseus", "profiles")), ("GDLauncher", Path.Combine(AppData, "gdlauncher_next", "instances")),
            ("ATLauncher", Path.Combine(AppData, "ATLauncher", "instances")), ("Launcher instance", Path.Combine(AppData, ".minecraft", "instances")),
        })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var inst in SafeDirs(root))
            {
                string name = Path.GetFileName(inst);
                string game = Directory.Exists(Path.Combine(inst, ".minecraft")) ? Path.Combine(inst, ".minecraft")
                    : Directory.Exists(Path.Combine(inst, "minecraft")) ? Path.Combine(inst, "minecraft") : inst;
                Add($"{launcher}: {name}", game);
            }
        }
        return list;
    }

    /// <summary>
    /// Bedrock's com.mojang folders that hold installed packs (resource_packs, skin_packs): the current game's shared
    /// folder, the Preview's, and those of older Microsoft Store versions.
    /// </summary>
    public static List<(string Label, string Folder)> Bedrock()
    {
        var list = new List<(string, string)>();
        void Add(string label, string folder) { if (Directory.Exists(folder)) list.Add((label, folder)); }
        Add("Minecraft (Bedrock)", Path.Combine(AppData, "Minecraft Bedrock", "Users", "Shared", "games", "com.mojang"));
        Add("Minecraft Preview", Path.Combine(AppData, "Minecraft Bedrock Preview", "Users", "Shared", "games", "com.mojang"));
        Add("Minecraft (older Store version)", Path.Combine(Local, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang"));
        Add("Minecraft Preview (older Store version)", Path.Combine(Local, "Packages", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", "LocalState", "games", "com.mojang"));
        Add("Education Edition", Path.Combine(AppData, "Minecraft Education Edition", "games", "com.mojang"));
        return list;
    }

    /// <summary>The installed Bedrock game's data folder (built-in packs and skins), if found.</summary>
    public static string? BedrockGameData()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            string data = Path.Combine(drive.RootDirectory.FullName, "XboxGames", "Minecraft for Windows", "Content", "data");
            if (Directory.Exists(Path.Combine(data, "resource_packs"))) return data;
        }
        return null;
    }

    /// <summary>
    /// Installed Java releases that have their game jar (for vanilla textures and default skins), newest first. Alpha
    /// ("a1.2.6") and beta ("b1.7.3") versions count; snapshots and modded versions do not.
    /// </summary>
    public static List<(string Version, string Jar)> JavaJars()
    {
        var list = new List<(string Version, string Jar, int[] Key)>();
        string versions = Path.Combine(AppData, ".minecraft", "versions");
        foreach (var dir in SafeDirs(versions))
        {
            string v = Path.GetFileName(dir), jar = Path.Combine(dir, v + ".jar");
            if (VersionKey(v) is not { } key || !File.Exists(jar) || new FileInfo(jar).Length < 100_000) continue;
            list.Add((v, jar, key));
        }
        list.Sort((a, b) =>
        {
            for (int i = 0; i < Math.Max(a.Key.Length, b.Key.Length); i++)
            {
                int x = i < a.Key.Length ? a.Key[i] : 0, y = i < b.Key.Length ? b.Key[i] : 0;
                if (x != y) return y.CompareTo(x);
            }
            return 0;
        });
        return list.Select(l => (l.Version, l.Jar)).ToList();
    }

    /// <summary>Sort key of a release name: alpha before beta before full releases, then the numbers.</summary>
    static int[]? VersionKey(string v)
    {
        int era = 2;
        if (v.Length > 1 && (v[0] == 'a' || v[0] == 'b') && char.IsDigit(v[1])) { era = v[0] == 'a' ? 0 : 1; v = v[1..]; }
        var parts = v.Split('.');
        if (parts.Length < 2) return null;
        var key = new List<int> { era };
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out int n)) return null;
            key.Add(n);
        }
        return key.ToArray();
    }

    static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.Exists(path) ? Directory.EnumerateDirectories(path).ToList() : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
}
