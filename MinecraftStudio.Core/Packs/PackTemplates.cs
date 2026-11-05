// MinecraftStudio.Core/Packs/PackTemplates.cs: a new, empty pack set up right for a version — to start making things.
// Owns: PackTemplates and DataPackFormat. A Java data pack: pack.mcmeta with the version's data pack format (pack_format
// before 1.21.9, min_format/max_format from it), the namespace's load and tick functions wired through the minecraft
// load/tick tags, in the folder names of the version (functions before 1.21, function from it). A Bedrock add-on: a
// behavior pack and a resource pack with their own UUIDs, the behavior pack depending on the resource pack, an example
// function. Formats from the Minecraft Wiki's data pack table. Nothing is written over: the folders must be new.
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.Packs;

/// <summary>A Java data pack format and the releases that use it (minor versions from 1.21.9).</summary>
public sealed record DataPackFormat(string Versions, int Major, int Minor = 0)
{
    /// <summary>From 1.21.9 (format 88) pack.mcmeta names its formats with min_format/max_format.</summary>
    public bool MinMax => Major >= 88;
    /// <summary>From 1.21 (format 48) the folders have singular names (function, recipe, loot_table …).</summary>
    public bool Singular => Major >= 48;
    public string Text => MinMax ? $"{Major}.{Minor}" : Major.ToString();
}

public static class PackTemplates
{
    /// <summary>Every release's data pack format, oldest first (data packs came in 1.13).</summary>
    public static readonly IReadOnlyList<DataPackFormat> DataPackFormats =
    [
        new("1.13 – 1.14.4", 4), new("1.15 – 1.16.1", 5), new("1.16.2 – 1.16.5", 6), new("1.17 – 1.17.1", 7), new("1.18 – 1.18.1", 8),
        new("1.18.2", 9), new("1.19 – 1.19.3", 10), new("1.19.4", 12), new("1.20 – 1.20.1", 15), new("1.20.2", 18), new("1.20.3 – 1.20.4", 26),
        new("1.20.5 – 1.20.6", 41), new("1.21 – 1.21.1", 48), new("1.21.2 – 1.21.3", 57), new("1.21.4", 61), new("1.21.5", 71), new("1.21.6", 80),
        new("1.21.7 – 1.21.8", 81), new("1.21.9 – 1.21.10", 88, 0), new("1.21.11", 94, 1), new("26.1 – 26.1.2", 101, 1), new("26.2", 107, 1), new("26.3", 121, 0),
    ];

    static readonly Regex Namespace = new("^[a-z0-9_.-]{1,64}$", RegexOptions.CultureInvariant);

    /// <summary>Whether a namespace is one the game takes (lower case letters, digits, _ - .).</summary>
    public static bool IsNamespace(string ns) => Namespace.IsMatch(ns) && ns != "minecraft";

    /// <summary>A namespace made from a name ("My Cool Pack" → my_cool_pack).</summary>
    public static string NamespaceFor(string name)
    {
        var sb = new StringBuilder();
        foreach (char ch in name.ToLowerInvariant())
            sb.Append(ch is >= 'a' and <= 'z' or >= '0' and <= '9' ? ch : sb.Length > 0 && sb[^1] != '_' ? '_' : "");
        string ns = sb.ToString().Trim('_');
        if (ns.Length == 0 || ns == "minecraft") ns = "my_pack";
        return ns.Length > 64 ? ns[..64] : ns;
    }

    /// <summary>
    /// A new Java data pack in <paramref name="folder"/> (which must not exist yet, or be empty): pack.mcmeta, and the
    /// namespace's load function (says the pack is on) and tick function (empty, runs 20 times a second) wired through
    /// minecraft's load and tick tags. Returns the files written, relative to the folder.
    /// </summary>
    public static List<string> JavaDataPack(string folder, string name, string description, string ns, DataPackFormat format, bool examples = false)
    {
        if (!IsNamespace(ns)) throw new ArgumentException("A namespace is lower case letters, digits, _ - and . only (and not \"minecraft\").");
        Fresh(folder);
        string functions = format.Singular ? "function" : "functions";
        var files = new List<string>();
        void Write(string relative, string text)
        {
            string path = Path.Combine([folder, .. relative.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SafeFile.WriteAllBytes(path, Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n")));
            files.Add(relative);
        }
        Write("pack.mcmeta", Mcmeta(description, format));
        Write($"data/minecraft/tags/{functions}/load.json", Json(new { values = new[] { $"{ns}:load" } }));
        Write($"data/minecraft/tags/{functions}/tick.json", Json(new { values = new[] { $"{ns}:tick" } }));
        // A text component written {"text":…} is JSON before 1.21.5 and SNBT from it: the same line works in both.
        Write($"data/{ns}/{functions}/load.mcfunction",
            $"# Runs once when the world loads (and after /reload).\ntellraw @a {{\"text\":\"{Escape(name)} is on\",\"color\":\"green\"}}\n");
        Write($"data/{ns}/{functions}/tick.mcfunction", "# Runs every game tick (20 times a second). Put commands here.\n");
        if (examples) Examples(Write, name, ns, format);
        return files;
    }

    /// <summary>
    /// A recipe, a loot table and an advancement to start from, each written the way the version reads it: a recipe's result
    /// item → id at 1.20.5 (format 41) and its ingredients as plain names at 1.21.2 (57); an advancement icon's item → id at
    /// 1.20.5; the folders' singular names at 1.21 (48).
    /// </summary>
    static void Examples(Action<string, string> write, string name, string ns, DataPackFormat format)
    {
        string recipes = format.Singular ? "recipe" : "recipes", loot = format.Singular ? "loot_table" : "loot_tables", advancements = format.Singular ? "advancement" : "advancements";
        bool ids = format.Major >= 41, plainIngredients = format.Major >= 57;
        object Ingredient(string item) => plainIngredients ? item : new { item };
        object Result(string item, int count) => ids ? new { id = item, count } : new { item, count };
        write($"data/{ns}/{recipes}/coarse_dirt.json", Json(new
        {
            type = "minecraft:crafting_shapeless",
            ingredients = new[] { Ingredient("minecraft:dirt"), Ingredient("minecraft:gravel") },
            result = Result("minecraft:coarse_dirt", 2),
        }));
        write($"data/{ns}/{loot}/treasure_chest.json", Json(new
        {
            type = "minecraft:chest",
            pools = new[] { new { rolls = 1, entries = new object[] { new { type = "minecraft:item", name = "minecraft:diamond" }, new { type = "minecraft:item", name = "minecraft:emerald" } } } },
        }));
        object icon = ids ? new { id = "minecraft:diamond" } : new { item = "minecraft:diamond" };
        write($"data/{ns}/{advancements}/welcome.json", Json(new
        {
            parent = "minecraft:story/root",
            display = new { icon, title = $"Welcome to {name}", description = $"The {name} data pack is on", frame = "task", show_toast = true, announce_to_chat = false },
            criteria = new { on = new { trigger = "minecraft:tick" } },
        }));
    }

    /// <summary>pack.mcmeta for the format: pack_format, or min_format and max_format from 1.21.9.</summary>
    public static string Mcmeta(string description, DataPackFormat format)
    {
        object pack = format.MinMax
            ? new Dictionary<string, object> { ["description"] = description, ["min_format"] = new[] { format.Major, format.Minor }, ["max_format"] = new[] { format.Major, format.Minor } }
            : new Dictionary<string, object> { ["description"] = description, ["pack_format"] = format.Major };
        return Json(new Dictionary<string, object> { ["pack"] = pack });
    }

    /// <summary>
    /// A new Bedrock add-on in <paramref name="folder"/>: "<name> BP" (behavior pack, with an example function) and
    /// "<name> RP" (resource pack), each with its own UUIDs; the behavior pack depends on the resource pack, so a world
    /// that turns one on turns on both. Returns the two pack folders.
    /// </summary>
    public static (string Behavior, string Resource) BedrockAddOn(string folder, string name, string description, int[] minEngine)
    {
        if (minEngine.Length != 3) throw new ArgumentException("The minimum game version is three numbers, like 1.21.0.");
        string safe = SafeName.For(name, "Add-on");
        string bp = Path.Combine(folder, safe + " BP"), rp = Path.Combine(folder, safe + " RP");
        Fresh(bp);
        Fresh(rp);
        Guid bpId = Guid.NewGuid(), rpId = Guid.NewGuid();
        object Manifest(Guid id, string type, Guid? dependsOn) => new Dictionary<string, object?>
        {
            ["format_version"] = 2,
            ["header"] = new Dictionary<string, object>
            {
                ["name"] = name, ["description"] = description, ["uuid"] = id.ToString(), ["version"] = new[] { 1, 0, 0 }, ["min_engine_version"] = minEngine,
            },
            ["modules"] = new[] { new Dictionary<string, object> { ["type"] = type, ["uuid"] = Guid.NewGuid().ToString(), ["version"] = new[] { 1, 0, 0 } } },
            ["dependencies"] = dependsOn is Guid d ? new[] { new Dictionary<string, object> { ["uuid"] = d.ToString(), ["version"] = new[] { 1, 0, 0 } } } : null,
        }.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value);
        void Write(string pack, string relative, string text)
        {
            string path = Path.Combine([pack, .. relative.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SafeFile.WriteAllBytes(path, Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n")));
        }
        Write(rp, "manifest.json", Json(Manifest(rpId, "resources", null)));
        Write(bp, "manifest.json", Json(Manifest(bpId, "data", rpId)));
        Write(bp, "functions/hello.mcfunction", $"# Run it in the game with /function hello\nsay {name} is here\n");
        return (bp, rp);
    }

    /// <summary>The folder must not exist yet, or be empty: a template never writes over anything.</summary>
    static void Fresh(string folder)
    {
        if (File.Exists(folder)) throw new IOException($"{Path.GetFileName(folder)} is a file; choose another name.");
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new IOException($"{folder} is not empty; choose a new folder.");
        Directory.CreateDirectory(folder);
    }

    // Readable JSON (an apostrophe stays an apostrophe): these files are read by the game, never shown as a web page.
    static string Json(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    }) + "\n";

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
