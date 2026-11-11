// MinecraftStudio.Core/World/PlatformSupport.cs: which games, PCs, phones and consoles every feature works on — one
// table that the app's menus, tiles and its "What works where" window read, and that FEATURES.md is written from, so
// what the program says matches what it does.
// Owns: Platform (Java, Bedrock on every device, Pocket 0.x, the seven Legacy consoles, 3DS, Classic/Indev), Support,
// PlatformCell, Feature (one row: its group, name, what it does, a cell per platform) and PlatformSupport — the rows,
// Of (a world's platform), For (may a feature be used on a world, and why not), WorksOn (the sentence) and Markdown.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;

namespace MinecraftStudio.Core.World;

/// <summary>Where a world comes from: an edition, and for the Legacy Console Edition, its console.</summary>
public enum Platform { Java, Bedrock, Pocket, Xbox360, XboxOne, PS3, PS4, PSVita, WiiU, Switch, ThreeDS, Classic }

/// <summary>How a feature works on a platform.</summary>
public enum Support
{
    /// <summary>Works fully.</summary>
    Yes,
    /// <summary>Works, with a limit the note says.</summary>
    Limited,
    /// <summary>Shows it, but can't change or save it there.</summary>
    Look,
    /// <summary>Not there (the note says why).</summary>
    No,
}

public sealed record PlatformCell(Support Support, string? Note = null);

/// <summary>One feature: its menu group, name, what it does, and how it works on each platform.</summary>
public sealed record Feature(string Id, string Group, string Name, string What, IReadOnlyDictionary<Platform, PlatformCell> Cells, bool PcOnly = false)
{
    public PlatformCell this[Platform p] => Cells.TryGetValue(p, out var c) ? c : new PlatformCell(Support.No);
}

public static class PlatformSupport
{
    /// <summary>Every platform in table order with its short name (a column) and what it covers.</summary>
    public static readonly IReadOnlyList<(Platform Platform, string Short, string Long)> Platforms =
    [
        (Platform.Java, "Java", "Java Edition on PC (Windows, Mac, Linux), every version from Classic to 26.x"),
        (Platform.Bedrock, "Bedrock", "today's Minecraft on any device: Windows PC, Android, iPhone and iPad, Xbox One and Series X|S, PlayStation 4 and 5, Nintendo Switch (the same world files on all of them; the world has to be on this PC, as a world folder or a .mcworld)"),
        (Platform.Pocket, "Pocket 0.x", "Pocket Edition 0.1 – 0.8 and the Raspberry Pi Edition"),
        (Platform.Xbox360, "Xbox 360", "Xbox 360 Edition"),
        (Platform.XboxOne, "Xbox One Ed.", "Xbox One Edition (before Bedrock)"),
        (Platform.PS3, "PS3", "PlayStation 3 Edition"),
        (Platform.PS4, "PS4 Ed.", "PlayStation 4 Edition (before Bedrock)"),
        (Platform.PSVita, "PS Vita", "PlayStation Vita Edition"),
        (Platform.WiiU, "Wii U", "Wii U Edition"),
        (Platform.Switch, "Switch Ed.", "Nintendo Switch Edition (before Bedrock)"),
        (Platform.ThreeDS, "3DS", "New Nintendo 3DS Edition (a world dump from the 3DS's extra data)"),
        (Platform.Classic, "Classic/Indev", "Java Classic and Indev levels (one-file worlds of a fixed size)"),
    ];

    static readonly Platform[] Consoles = [Platform.Xbox360, Platform.XboxOne, Platform.PS3, Platform.PS4, Platform.PSVita, Platform.WiiU, Platform.Switch];
    static readonly Platform[] Everything = Enum.GetValues<Platform>();

    public static string ShortName(Platform p) => Platforms.First(x => x.Platform == p).Short;

    /// <summary>The platform a world is from (null for one made here, like land from a heightmap).</summary>
    public static Platform? Of(SaveWorld w) => w switch
    {
        JavaWorld => Platform.Java,
        FiniteWorld => Platform.Classic,
        BedrockWorld => Platform.Bedrock,
        PocketLegacyWorld => Platform.Pocket,
        ThreeDsWorld => Platform.ThreeDS,
        ConsoleWorld c => c.Platform switch
        {
            ConsolePlatform.XboxOne => Platform.XboxOne, ConsolePlatform.PS3 => Platform.PS3, ConsolePlatform.PS4 => Platform.PS4,
            ConsolePlatform.PSVita => Platform.PSVita, ConsolePlatform.WiiU => Platform.WiiU, ConsolePlatform.Switch => Platform.Switch,
            // Every console is alike in this table; a save without its console's wrapper counts as one of them.
            _ => Platform.Xbox360,
        },
        _ => null,
    };

    /// <summary>How a world's platform is named in plain words ("a PS3 save", "a Bedrock world").</summary>
    public static string Describe(SaveWorld w) => Of(w) switch
    {
        Platform.Java => "a Java world",
        Platform.Bedrock => "a Bedrock world",
        Platform.Pocket => "a Pocket Edition 0.x world",
        Platform.ThreeDS => "a 3DS world",
        Platform.Classic => "a Classic or Indev level",
        Platform p when w is ConsoleWorld { Platform: ConsolePlatform.Raw } => "a console save",
        Platform p => $"{Platforms.First(x => x.Platform == p).Long} save",
        _ => "this world",
    };

    /// <summary>
    /// Whether a feature can be used on a world (null when it can, with any limit in <paramref name="limit"/>), or why not.
    /// </summary>
    public static string? WhyNot(string featureId, SaveWorld w, out string? limit)
    {
        limit = null;
        var f = Get(featureId);
        if (Of(w) is not Platform p) return null;
        var cell = f[p];
        limit = cell.Support is Support.Limited or Support.Look ? cell.Note : null;
        return cell.Support == Support.No ? $"Not for {Describe(w)}{(cell.Note != null ? ": " + cell.Note : "")}. Works on: {WorksOn(f)}" : null;
    }

    public static Feature Get(string id) => All.FirstOrDefault(f => f.Id == id) ?? throw new ArgumentException($"There is no feature \"{id}\".");

    /// <summary>"Java, Bedrock, Pocket 0.x, consoles, Classic/Indev. Not 3DS (why)." — every platform, the same ones together.</summary>
    public static string WorksOn(Feature f)
    {
        var parts = new List<string>();
        var no = new List<string>();
        // In platform order (Java first), the ones that work before those that don't.
        foreach (var g in Everything.GroupBy(p => (f[p].Support, f[p].Note)).OrderBy(g => g.Key.Support == Support.No).ThenBy(g => Array.IndexOf(Everything, g.First())))
        {
            string names = Names(g.ToList());
            var (support, note) = g.Key;
            if (support == Support.No) no.Add(note != null ? $"{names} ({note})" : names);
            else if (support == Support.Look) parts.Add($"{names} ({note ?? "to look at"})");
            else parts.Add(note != null ? $"{names} ({note})" : names);
        }
        string text = parts.Count > 0 ? string.Join("; ", parts) : "none";
        if (no.Count > 0) text += ". Not " + string.Join("; not ", no);
        return text + ".";
    }

    // Platform names, the seven consoles as "consoles" when all are there, everything as "all".
    static string Names(List<Platform> ps)
    {
        if (ps.Count == Everything.Length) return "all";
        var names = new List<string>();
        bool allConsoles = Consoles.All(ps.Contains);
        foreach (var p in ps)
        {
            if (Consoles.Contains(p)) { if (allConsoles) { if (!names.Contains("consoles")) names.Add("consoles"); continue; } }
            names.Add(ShortName(p) switch { "Xbox One Ed." => "Xbox One Edition", "PS4 Ed." => "PS4 Edition", "Switch Ed." => "Switch Edition", var s => s });
        }
        return string.Join(", ", names);
    }

    // ------------------------------------------------------------------ the table

    static Feature F(string id, string group, string name, string what, params (Platform[] On, Support S, string? Note)[] cells) => F(id, group, name, what, false, cells);

    static Feature F(string id, string group, string name, string what, bool pcOnly, params (Platform[] On, Support S, string? Note)[] cells)
    {
        var map = new Dictionary<Platform, PlatformCell>();
        foreach (var (on, s, note) in cells)
            foreach (var p in on) map[p] = new PlatformCell(s, note);
        return new Feature(id, group, name, what, map, pcOnly);
    }

    static (Platform[], Support, string?) Yes(params Platform[] on) => (on, Support.Yes, null);
    static (Platform[], Support, string?) Yes(string note, params Platform[] on) => (on, Support.Yes, note);
    static (Platform[], Support, string?) Some(string note, params Platform[] on) => (on, Support.Limited, note);
    static (Platform[], Support, string?) Look(string? note, params Platform[] on) => (on, Support.Look, note);
    static (Platform[], Support, string?) Not(string? why, params Platform[] on) => (on, Support.No, why);

    const Platform J = Platform.Java, B = Platform.Bedrock, P = Platform.Pocket, T = Platform.ThreeDS, C = Platform.Classic;
    static Platform[] With(params object[] items) => items.SelectMany(i => i is Platform[] a ? a : [(Platform)i]).ToArray();
    static readonly Platform[] JBPC = With(J, B, P, Consoles);
    static readonly Platform[] NotThreeDs = With(J, B, P, Consoles, C);

    const string Open = "Open and find your worlds", Map = "The map", Look_ = "Look through the world", Build = "Build and change",
        People = "Players and items", Settings = "World settings (Cheats & game mode)", Packs = "Packs", Keep = "Convert and keep safe", Make = "Create";
    const string ThreeDsWrite = "a changed 3DS chunk makes the game delete the world";

    /// <summary>Every feature, in the menus' order.</summary>
    public static readonly IReadOnlyList<Feature> All =
    [
        F("open", Open, "Open any world", "a world folder or a save file of any edition; the kind is found from what is inside", Yes(Everything)),
        F("pcworlds", Open, "Worlds on this PC", "every Java and Bedrock world on this PC listed by itself, with recent worlds on the start page", true,
            Yes(J), Yes("on a Windows PC", B), Not("copy the save onto this PC and open it", With(P, Consoles, T, C))),
        F("phone", Open, "Worlds from a phone", "copies worlds off an Android phone by USB cable or Wi-Fi, without rooting it",
            Some("Android phones and tablets", B, P), Not(null, With(J, Consoles, T, C))),
        F("nbt", Open, "NBT editor", "edit any NBT file directly (level.dat, players, structures, chunks), Java's and Bedrock's kinds, SNBT", Yes(Everything)),
        F("leveldat", Open, "Edit level.dat", "the world's settings file in the NBT editor", Yes(J, B, P, T), Yes(Consoles), Some("Indev only: Classic levels have none", C)),
        F("database", Open, "Bedrock database browser", "every record of a Bedrock world's database", Yes(B), Not(null, With(J, P, Consoles, T, C))),
        F("region", Open, "Region file viewer", "one Java region file (blocks, entities and poi) chunk by chunk", Yes(J), Not(null, With(B, P, Consoles, T, C))),

        F("map", Map, "Map of every dimension", "Overworld, Nether, End and mod dimensions; zoom, pan, a cave view from any height, overlays of time spent, last update and biomes", Yes(Everything)),
        F("slime", Map, "Slime chunks", "the chunks slimes spawn in, also where nobody has been yet",
            Yes("from the seed", J), Yes(B, P), Not("their slime rule is not known", With(Consoles, T)), Not("no slimes", C)),
        F("structures", Map, "Structures on the map", "villages, outposts, shipwrecks, buried treasure, temples, monuments, strongholds, mansions, fortresses, bastions, end cities, ancient cities, trail ruins, trial chambers, dungeons …, with a gold ring where loot has never been taken",
            Yes(J), Yes("the structures a save keeps (newer saves keep them all; older ones are found from their loot chests and spawners)", Consoles),
            Some("fewer: most are found from their loot chests and spawners", With(B, P, T)), Not("they have none", C)),
        F("portals", Map, "Nether portals and where they lead", "every portal you built, where each one leads, whether that one leads back, or that it makes a new portal",
            Yes(J, B), Yes("their smaller Nether: 1:3 in the classic world, the save's own scale in bigger ones", Consoles),
            Some("the portals only, not where they lead: its Nether's scale isn't known", T), Not("no Nether", P, C)),
        F("deaths", Map, "Where players last died", "a red cross on the map where each player last died",
            Some("1.19 and newer", J), Yes(B), Not("not saved", With(P, Consoles, T, C))),
        F("picture", Map, "Picture of a dimension", "the whole map as one PNG", Yes(Everything)),
        F("webmap", Map, "Web map", "the whole dimension as a zoomable map that opens in any browser, offline, with the spawn, players and structures marked", Yes(Everything)),
        F("heights", Map, "Heights picture", "a dimension's ground as a 16-bit grey picture for terrain programs (water, plants and trees left out)", Yes(Everything)),
        F("view3d", Map, "3D view", "turn a piece of the world, or any structure, in 3D", Yes(Everything)),
        F("select", Map, "Select chunks", "by hand, by area around the spawn or a player, grow or shrink; save and load selections as MCA Selector .csv", Yes(Everything)),
        F("delete", Map, "Delete chunks", "so the game makes them again, picked by hand or by the pruner (far away, outside or inside a selection, unfinished, rarely visited)",
            Yes(J), Yes(Consoles), Some("not \"rarely visited\": no time-spent record", B, P, C), Not("the 3DS chunk index is only partly known", T)),
        F("chunknbt", Map, "Open a chunk's NBT", "a chunk's raw data in the NBT editor (right-click the map)",
            Yes(J, B), Yes("its mobs, chests and ticks (in older saves the whole chunk), saved back into the save", Consoles), Look("to look at", T), Not("no chunk NBT", P, C)),

        F("find", Look_, "Find blocks, items, mobs and text", "blocks, items in chests and in the shulker boxes inside them, mobs (riders too), words on signs and books, and every player's inventory and ender chest; marked on the map", Yes(Everything)),
        F("pets", Look_, "Lost pets and villagers", "every tamed dog, cat, parrot and horse with who owns it, and each villager's job and trades",
            Yes(J, B), Some("owners not named", With(P, Consoles, T)), Not("no tamed mobs", C)),
        F("petshome", Look_, "Bring the pets home", "puts lost pets beside their owner, in a copy",
            Yes(J, B), Not("their owners can't be matched to the players", With(P, Consoles, T, C))),
        F("busy", Look_, "Busiest chunks (lag finder)", "where mobs, dropped items, hoppers and chests pile up, marked on the map", Yes(Everything)),
        F("clearitems", Look_, "Clear dropped items and experience orbs", "out of the busiest chunks, in a copy", Yes(JBPC), Not(ThreeDsWrite, T), Not("no dropped items", C)),
        F("lag", Look_, "Lag machines", "only the devices built to make a world or server lag or crash: minecart and mob piles, item floods, solid blocks of pistons or observers, machines left running, summoning command blocks, book and chunk bans — never ordinary redstone or farms", Yes(Everything)),
        F("lagclean", Look_, "Clean up lag machines", "takes them out of a copy and checks none is left", Yes(JBPC), Not(ThreeDsWrite, T), Not("not built there", C)),
        F("mods", Look_, "Mods and add-ons in a world", "which mods, add-ons and data packs a world holds blocks, mobs, items, biomes or a dimension of, how much of each, and which mods on this PC have them",
            Yes("Forge, NeoForge, Fabric and Quilt mods and data packs; the mods in every launcher's folders on this PC", J), Yes("add-ons and the packs that bring them", B),
            Not("these games take no mods or add-ons", With(P, Consoles, T, C))),
        F("compare", Look_, "Compare with another world", "what changed since a backup, or what a conversion changed, block by block, marked on the map (even two different editions)", Yes(Everything)),
        F("restore", Look_, "Bring chunks back from a backup", "griefing, a creeper hole or a bad update undone, into a copy; mobs that walked away are not doubled; the backup may even be another edition or console",
            Yes(JBPC), Some("the blocks only (an Indev level's chests and mobs stay as they are)", C), Not(ThreeDsWrite, T)),
        F("check", Look_, "Check for problems", "what stops a world opening, crashes the game or throws players out, loses things or leaves a player stuck — damaged chunks, how they are stored, book and chunk bans and other lag machines, mods, packs, the game version and every player — each with its fix",
            Yes("in the most depth: region tables (Java), the database and how every chunk is stored (Bedrock), mods not on this PC (Java), packs, the game version (Java)", J, B),
            Yes("damaged chunks, the save's archive, lag machines, stuck players", Consoles), Yes("damaged chunks, lag machines, stuck players", P, C),
            Yes("damaged chunks, lag machines, the player (only shown: nothing is written into a 3DS world)", T)),

        F("crop", Build, "Save a structure (crop a building)", "drag a box on the map; save it as Java .nbt, WorldEdit .schem, Litematica .litematic, Bedrock .mcstructure or MCEdit .schematic, for any Java version", Yes(Everything)),
        F("place", Build, "Place a structure", "any structure or schematic into a copy of a world, turned or mirrored, standing on the ground; every block checked", Yes(NotThreeDs), Not(ThreeDsWrite, T)),
        F("buildcheck", Build, "Check a build", "a schematic laid over your build: every missing or wrong block on the map, how much is done, and what is still to gather (Place window)", Yes(NotThreeDs), Not("its window places into the world, which the 3DS can't take", T)),
        F("structurefile", Build, "Structure file", "see any schematic, its material list, turn or mirror it, save it as another format or Java version, or as a 3D model (.obj) for Blender", Yes(Everything)),
        F("replace", Build, "Replace or fill blocks", "rules like *_ore → stone or * → air in a box, in a copy, every block checked", Yes(NotThreeDs), Not(ThreeDsWrite, T)),
        F("biome", Build, "Change the biome", "any biome in a box (grass and leaf colour, weather, which mobs spawn), in a copy",
            Yes(J, B), Yes(Consoles), Not("its biomes aren't written here", P), Not(ThreeDsWrite, T), Not("no biomes", C)),
        F("shape", Build, "Shape the land", "flatten an area for a build, smooth it, raise it or lower it, with soft edges; trees, chests and mobs move with the ground and water keeps its level; in a copy, every column checked",
            Yes(JBPC), Some("the ground's blocks only: an Indev level's chests and its player's spot are kept out of it", C), Not(ThreeDsWrite, T)),
        F("mapsee", Build, "A world's maps as pictures", "every map item of a world as a PNG", Yes(J, B), Yes(Consoles), Yes("the maps its value store keeps", T), Not("they keep no maps", P, C)),
        F("mapart", Build, "Make map art", "any picture as map art (1 – 16 maps across and down) given to a player, or made from blocks you can place in survival and saved as schematics to build",
            Some("not 26.1's new folder layout yet", J), Yes(B), Not("map art is added to Java and Bedrock worlds", With(P, Consoles, T, C))),
        F("heightmap", Build, "New world from a heightmap", "land from any grey picture or real elevation data — grass, sand beaches, stone cliffs, snowy peaks and water — made as any edition and checked block by block",
            Yes(NotThreeDs), Not("no 3DS writer", T)),

        F("players", People, "Players and inventories", "every player, their inventory, armour, ender chest, health, food, XP and position; change and save",
            Yes(JBPC), Some("Indev only: Classic has no players", C), Look("to look at: nothing is written into a 3DS world", T)),
        F("copyitems", People, "Copy items to another world", "a player's inventory, armour, off-hand and ender chest into a player of a Java, Bedrock, console or Pocket 0.x world, each item changed into that world's form and version (what it can't hold becomes paper named after it)",
            Yes("into a Java, Bedrock, console or Pocket 0.x world", With(J, B, P, Consoles, T)), Some("Indev only, into a Java, Bedrock, console or Pocket 0.x world", C)),
        F("give", People, "Copy /give command", "right-click an item: the command that gives it in the world's own version's form, or the item as SNBT", Yes(J, B), Not("no commands to copy", With(P, Consoles, T, C))),
        F("move", People, "Move to safety", "a player stuck in a wall, lost in the void or trapped in the Nether put on safe ground (beside their bed, at the spawn or near x, z), with what they ride",
            Yes(J, B, P), Yes(Consoles), Some("Indev only: Classic levels keep no player", C), Not("3DS players are not read yet", T)),
        F("hardcore", People, "Restore a Hardcore world from death", "the player back in Survival with full health, beside their bed", Yes(J, B), Not("no Hardcore", With(P, Consoles, T, C))),
        F("server", People, "Single-player and server players", "a player's things between level.dat and a server's player files (offline-mode names, online UUIDs)", Yes(J), Not(null, With(B, P, Consoles, T, C))),
        F("progress", People, "Statistics and advancements", "change any statistic; mark any advancement (from the game and the world's data packs) done or not done", Yes(J), Not(null, With(B, P, Consoles, T, C))),

        F("cheats", Settings, "Game mode, difficulty, cheats, Hardcore and achievements", "including turning achievements back on",
            Yes(J, B, P, T), Yes("host privileges", Consoles), Not("no settings to change", C)),
        F("gamerules", Settings, "Game rules", "every rule the world's file has, in its own type",
            Yes(J, B, P, T), Some("most console saves keep their host options outside the file read here", Consoles), Not("none", C)),
        F("rename", Settings, "World name", "the name in the game's world list (Bedrock's levelname.txt too); the folder keeps its name",
            Yes(J, B, T), Some("0.2 – 0.8 (0.1 keeps no name)", P), Not("the console's list shows the save's own title", Consoles), Not("not kept", C)),
        F("spawn", Settings, "Spawn, time of day and weather", "set the world spawn (also by right-clicking the map), the time and the weather", Yes(J, B, P, T), Yes(Consoles),
            Some("the spawn point only, by right-clicking the map (Indev, and Classic levels from 0.0.14a)", C)),
        F("border", Settings, "World border", "the world border's centre and size", Yes(J), Not(null, With(B, P, Consoles, T, C))),

        F("worldpacks", Packs, "World packs", "a world's data packs and resource pack (Java) or behaviour and resource packs (Bedrock): install, switch on and off, change their order, uninstall (to a \"Removed packs\" folder, never deleted)",
            Yes(J), Yes("packs installed in the game are found on a Windows PC", B), Not(null, With(P, Consoles, T, C))),
        F("packspc", Packs, "Packs on this PC", "which worlds have which packs", true, Yes("worlds on this PC", J, B), Not(null, With(P, Consoles, T, C))),
        F("packcheck", Packs, "Check a pack", "what the game would ignore or show wrong in a resource pack or a data pack", Yes(J, B), Not(null, With(P, Consoles, T, C))),
        F("newpack", Packs, "New data pack or add-on", "set up right for any Java version (1.13 → 26.x) or Bedrock version, with an example recipe, loot table and advancement if you like", Yes(J, B), Not(null, With(P, Consoles, T, C))),

        F("convertfrom", Keep, "Convert a world (from)", "any world to any edition and version, in a new folder, checked afterwards", Yes(Everything)),
        F("convertto", Keep, "Convert a world (to)", "Java 26.1 → Infdev, Bedrock 26.x → 1.1, Pocket 0.9 – 0.16 and 0.2 – 0.8, all seven consoles (an Xbox 360 save goes into your own save package, rehashed), Classic, Indev",
            Yes(NotThreeDs), Not("no 3DS writer", T)),
        F("blockrules", Keep, "Change blocks as it converts", "say what a mod's blocks, or any blocks, become (thermal:ore → copper_ore)", Yes(NotThreeDs), Not("no 3DS writer", T)),
        F("backups", Keep, "Backups", "back up now; every backup of a world, newest first; restore one as a new world beside it, or compare with it", Yes(Everything)),
        F("autobackup", Keep, "Automatic backups", "each world backed up by itself after you play it (Settings)", true, Yes("worlds played on this PC", J, B), Not("played on another device", With(P, Consoles, T, C))),
        F("share", Keep, "Save a world as one file", ".mcworld (Bedrock: opening it imports it into the game) or .zip (Java)", Yes(J, B), Not("its save is one file already", With(P, Consoles, T, C))),
        F("rescue", Keep, "Rescue a damaged world", "everything that can still be read saved into a new world, including chunks the region tables have lost and older copies of damaged chunks; or a repaired copy",
            Yes(J, B), Some("what still reads, converted into a new world", With(P, Consoles, T, C))),
        F("optimize", Keep, "Optimize storage", "rewrites region files or the database compactly", Yes(J, B), Yes(Consoles), Not(null, P, T, C)),

        F("skins", Make, "Skin Studio", "paint skins on the 3D model or the flat picture (classic and slim arms, HD), save for any edition and version, or install into the games on this PC",
            Yes(J), Some("custom skins load on Windows, phones and tablets", B), Not("console skins come as DLC packs", With(P, Consoles, T, C))),
        F("textures", Make, "Texture Studio", "texture packs from the game's own textures, animated, saved for any Java version or for Bedrock, or installed; the texture shows on its block in 3D as you paint",
            Yes(J, B), Not("console packs come as DLC", With(P, Consoles, T, C))),
        F("models", Make, "Block and item models", "any Java block or item model with your pack's textures: change its boxes, faces and textures and save it into the pack (Texture Studio → Models)",
            Yes("models read from Java installed on this PC", J), Not(null, With(B, P, Consoles, T, C))),
        F("mobs", Make, "Mob Studio", "every animal and monster painted on its 3D model", Yes("3D models need Bedrock installed on this PC", J, B), Not(null, With(P, Consoles, T, C))),
        F("animations", Make, "Animation Studio", "see every mob move; design animations and models", Yes("models from Bedrock on this PC", B), Not(null, With(J, P, Consoles, T, C))),
    ];

    /// <summary>How a world gets from each device onto this PC and back: (where, the steps).</summary>
    public static readonly IReadOnlyList<(string Where, string[] Steps)> MovingWorlds =
    [
        ("Java Edition (Windows, Mac, Linux)",
        [
            "Worlds are folders in the game's saves folder: `%APPDATA%\\.minecraft\\saves` on Windows, `~/Library/Application Support/minecraft/saves` on a Mac, `~/.minecraft/saves` on Linux. In the game: Singleplayer → choose the world → Edit → Open World Folder.",
            "This program lists them by itself (Worlds on this PC). A world converted into the saves folder shows up in Singleplayer.",
        ]),
        ("Bedrock on a Windows PC",
        [
            "This program finds the worlds by itself. In the game, the pencil beside a world → Export World saves a .mcworld; opening a .mcworld puts it into the game.",
        ]),
        ("Bedrock on Android phones and tablets",
        [
            "File → Get Worlds from an Android Phone copies them by USB cable or Wi-Fi, without rooting the phone.",
            "Or in the game: the pencil beside the world → Export World, and send the .mcworld to the PC. To put a world back, copy the .mcworld to the phone and open it.",
        ]),
        ("Bedrock on iPhone and iPad",
        [
            "In the game: the pencil beside the world → Export World, save it to Files and copy the .mcworld to the PC. To put a world back, open the .mcworld on the device.",
        ]),
        ("Bedrock on Xbox, PlayStation and Switch",
        [
            "A console can't copy world files itself; a Realm can carry them. On the console, upload the world to your Realm (Realm settings → Replace World); on a Windows PC, download it (Realm settings → Download World) and it is among the worlds on this PC.",
            "After working on it, upload it from the PC to the Realm the same way and download it on the console.",
        ]),
        ("Xbox 360 Edition",
        [
            "In the Xbox's settings (System → Storage), copy the Minecraft save onto a USB drive; on the PC, open the save file from the drive's Xbox360 folder.",
            "To put a world back: Convert to Xbox 360 writes it into a save package of yours (choose one from the drive), then copy it back onto the Xbox the same way.",
        ]),
        ("PS3, PS Vita, Wii U, and the Xbox One, PS4 and Switch Editions",
        [
            "These consoles keep their saves where only the console's own save tools reach them. This program reads the save files you copy off with them (a PS3 save has to be decrypted first: this program doesn't decrypt saves), and the converted save goes back the same way.",
        ]),
        ("New Nintendo 3DS Edition",
        [
            "This program reads a dump of the 3DS's extra data (the world folder a 3DS save manager makes). It doesn't write to the 3DS: a changed 3DS world is deleted by the game.",
        ]),
    ];

    /// <summary>The moving-worlds steps as plain text (Help → Moving Worlds Between Devices).</summary>
    public static string MovingWorldsText() => string.Join("\n\n", MovingWorlds.Select(m => m.Where.ToUpperInvariant() + "\n" + string.Join("\n", m.Steps.Select(step => "  " + step.Replace("`", "")))));

    /// <summary>FEATURES.md: every feature with where it works, from this table.</summary>
    /// <summary>
    /// The whole table as one Markdown grid per group (the README's compatibility section): ✅ works, ⚠️ works with a limit,
    /// 👁️ to look at only, ❌ not there.
    /// </summary>
    public static string MarkdownGrid()
    {
        var sb = new System.Text.StringBuilder();
        string Head(Platform p) => ShortName(p).Replace(" Ed.", " Edition");
        foreach (var group in All.GroupBy(f => f.Group))
        {
            sb.Append("\n**").Append(group.Key).Append("**\n\n| Feature | ").Append(string.Join(" | ", Everything.Select(Head))).Append(" |\n|---|");
            sb.Append(string.Concat(Everything.Select(_ => ":---:|"))).Append('\n');
            foreach (var f in group)
                sb.Append("| ").Append(f.Name).Append(" | ").Append(string.Join(" | ", Everything.Select(p => f[p].Support switch
                {
                    Support.Yes => "✅", Support.Limited => "⚠️", Support.Look => "👁️", _ => "❌",
                }))).Append(" |\n");
        }
        return sb.ToString();
    }

    public static string Markdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("# ").Append(AppInfo.Name).Append(" — everything it does\n\n");
        sb.Append("One program for every Minecraft world: open it, look through it, fix it, change it, move it to another edition or console,\n");
        sb.Append("and make skins, textures, models and packs. Unofficial; not affiliated with Mojang or Microsoft.\n\n");
        sb.Append("**Your world is never changed behind your back.** Tools that change a world either save into it only when you press Save\n");
        sb.Append("(a backup is made first), or write a new copy and check every block of it. Converting, rescuing, placing, replacing,\n");
        sb.Append("reshaping and restoring always write a new copy.\n\n");
        sb.Append("## Where each feature works\n\nEvery feature below says what it **works on** (the app's Help → What Works Where shows the same as a table). The names mean:\n\n");
        sb.Append("| Name | Which games and devices |\n|---|---|\n");
        foreach (var (p, shortName, longName) in Platforms.Where(x => !Consoles.Contains(x.Platform) || x.Platform == Platform.Xbox360))
        {
            if (Consoles.Contains(p)) { sb.Append("| **consoles** | the seven Legacy Console Editions: ").Append(string.Join(", ", Consoles.Select(ShortName).Select(n => n.Replace(" Ed.", " Edition")))).Append(" — the save copied off the console |\n"); continue; }
            sb.Append("| **").Append(shortName).Append("** | ").Append(char.ToUpperInvariant(longName[0])).Append(longName[1..].Replace("|", "\\|")).Append(" |\n");
        }
        sb.Append("| **all** | every one of the above |\n| **On this PC** | needs the game installed on this Windows PC |\n\n");
        sb.Append("Xbox One, PS4 and Switch had their own console editions before Bedrock came to them (2017 – 2019). A world made in today's\n");
        sb.Append("game on those consoles is a **Bedrock** world; one made in the old edition is a **consoles** save.\n");
        foreach (var g in All.GroupBy(f => f.Group))
        {
            sb.Append("\n## ").Append(g.Key).Append("\n\n");
            foreach (var f in g) sb.Append("- **").Append(f.Name).Append("** — ").Append(f.What).Append(".\n  *Works on:* ").Append(WorksOn(f)).Append('\n');
        }
        sb.Append("\n## Moving worlds between devices\n\n");
        foreach (var (where, steps) in MovingWorlds)
        {
            sb.Append("- **").Append(where).Append("** — ").Append(steps[0]).Append('\n');
            foreach (var step in steps.Skip(1)) sb.Append("  ").Append(step).Append('\n');
        }
        sb.Append("\n## Command line (mcstudio)\n\n");
        sb.Append("`mcstudio.exe` does the same from scripts: `info`, `targets`, `convert`, `find`, `structures`, `lag`, `mods`, `restore`, `replace`, `biome`,\n");
        sb.Append("`shape`, `heights`, `heightmap`, `move`, `maps`, `share`, `crop`, `structure`, `materials`, `obj`, `packcheck`, `newpack`. Each works on\n");
        sb.Append("the same editions as the window it matches, only reads what it is given and writes new files and folders. Run `mcstudio help`.\n\n");
        sb.Append("## Not there yet\n\n");
        sb.Append("- Structures from the seed in land nobody has visited (needs the game's whole terrain generator to be right).\n");
        sb.Append("- Writing to the 3DS (changed 3DS chunks make the game delete the world).\n");
        sb.Append("- Where portals lead on 3DS saves (its Nether's scale isn't known; Pocket 0.x has no Nether).\n");
        sb.Append("- Slime chunks on console and 3DS saves, and console pets' owners (no saved slimes or tamed pets to check against yet).\n");
        sb.Append("- Copying items into Pocket Edition 0.x worlds (how they number the inventory's slots isn't checked against a real save yet).\n");
        return sb.ToString().Replace("\n", "\r\n");
    }
}
