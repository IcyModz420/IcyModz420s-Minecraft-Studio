// MinecraftStudio.Cli/Program.cs: mcstudio — the studio from the command line, for scripts and builds.
// Owns: Program — the command list and help, dispatch, and exit codes (0 done, 1 done but something was lost or a check
// failed, 2 the command line was not right, 3 it could not be done). Every command only reads the worlds and files it is
// given and writes new ones: nothing is ever written into a world or over a file it read.
using MinecraftStudio.Cli;
using MinecraftStudio.Core;

static class Program
{
    static readonly (string Name, string Usage, string What, Func<Args, int> Run)[] Commands =
    [
        ("info", "info <world>", "What a world is: edition, version, name, seed, dimensions, chunks, players.", WorldCommands.Info),
        ("targets", "targets", "Every edition and version a world can be converted to (names for convert --to).", WorldCommands.Targets),
        ("convert", "convert <world> --to <target> --out <new folder> [--dims id,id] [--area x1,z1,x2,z2] [--blocks pattern=block;…]",
            "Converts a world to any edition or version, in a new folder, and checks the result.", WorldCommands.Convert),
        ("find", "find <world> <what> [--dim id] [--whole]", "Finds blocks, items (in chests and shulker boxes too), mobs and text.", WorldCommands.Find),
        ("structures", "structures <world> [--dim id] [--kind village|shipwreck|buried_treasure …]",
            "Lists the world's structures — villages, outposts, shipwrecks, buried treasure, temples, fortresses … — with the loot not taken yet.", WorldCommands.Structures),
        ("lag", "lag <world> [--dim id] [--clean --out <new folder>]",
            "Finds lag machines: piles no survival play makes, solid masses of redstone parts, machines left running, summoning command blocks, book and chunk bans (exit 1 when any is found); --clean takes them out of a new copy, checked.", WorldCommands.Lag),
        ("mods", "mods <world> [--all]",
            "Which mods, add-ons and data packs the world holds blocks, mobs, items, biomes or dimensions of, how many of each, and which mods on this PC have them (exit 1 when a Java world needs one that isn't on this PC); --all lists every kind.", WorldCommands.Mods),
        ("restore", "restore <world> <backup> --out <new folder> [--dim id] [--area x1,z1,x2,z2] [--keep-new-mobs]",
            "Brings chunks back from a backup (every chunk that differs, or those in an area) into a new copy of the world, checked.", WorldCommands.Restore),
        ("replace", "replace <world> <pattern> <block> --box x1,y1,z1,x2,y2,z2 --out <new folder> [--dim id]",
            "Replaces blocks in a box (*_ore, * for every block) in a new copy of the world, checked.", WorldCommands.Replace),
        ("biome", "biome <world> <biome> --box x1,y1,z1,x2,y2,z2 --out <new folder> [--dim id]",
            "Changes the biome of a box (cherry_grove, desert …) in a new copy of the world, checked.", WorldCommands.Biome),
        ("shape", "shape <world> flatten <y>|smooth <radius>|raise <n>|lower <n> --box x1,z1,x2,z2 --out <new folder> [--edge n] [--dim id] [--plan]",
            "Flattens, smooths, raises or lowers the land in a box (trees, chests and mobs move with it, water keeps its level) in a new copy, every column checked; --plan only says how far it would move.", TerrainCommands.Shape),
        ("heights", "heights <world> <new .png> [--dim id]",
            "Saves a dimension's ground heights as a 16-bit grey PNG (water, plants and trees left out; the range and place in the file).", TerrainCommands.Heights),
        ("heightmap", "heightmap <picture.png> --to <target> --out <new folder> [--low y] [--high y] [--water y|none] [--snow y] [--scale blocks] [--centre] [--name text] [--survival] [--flat]",
            "Makes a new world of any edition from a heightmap (bright = high; a heights file comes back where it was saved from), checked.", TerrainCommands.Heightmap),
        ("move", "move <world> <player|uuid|host> --out <new folder> [--near x,z | --spawn]",
            "Puts a stuck or lost player on safe ground (beside their bed, near the spawn, or near x, z) in a new copy of the world, checked.", WorldCommands.Move),
        ("maps", "maps <world> <folder>", "Saves every map (map art) of a world as a PNG picture.", WorldCommands.Maps),
        ("share", "share <world> <file>", "Packs a world into one file: .mcworld (Bedrock) or .zip (Java).", WorldCommands.Share),
        ("crop", "crop <world> <new file> --box x1,y1,z1,x2,y2,z2 [--dim id] [--version <java version>]",
            "Saves a box of a world as a structure or schematic (the file type by its extension), checked.", WorldCommands.Crop),
        ("structure", "structure <file> <new file> [--version <java version>] [--turn 0-3] [--mirror ns|ew]",
            "Converts a structure or schematic (.nbt .schem .litematic .mcstructure .schematic), turned or mirrored.", FileCommands.Structure),
        ("materials", "materials <structure file> [--csv]", "The material list: the items that place every block.", FileCommands.Materials),
        ("obj", "obj <structure file> <new .obj> [--turn 0-3] [--mirror ns|ew]", "A structure as a 3D model (.obj and .mtl) for Blender and other 3D programs.", FileCommands.Obj),
        ("packcheck", "packcheck <pack folder or zip>", "What the game would ignore or show wrong in a resource or data pack.", FileCommands.PackCheck),
        ("newpack", "newpack java|bedrock <name> <folder> [--version <release>] [--namespace ns] [--description text] [--examples]",
            "Starts a Java data pack or a Bedrock add-on set up right for a version.", FileCommands.NewPack),
    ];

    static int Main(string[] argv)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (argv.Length == 0 || argv[0] is "help" or "--help" or "-h" or "/?")
        {
            Help(argv.Length > 1 ? argv[1] : null);
            return argv.Length == 0 ? 2 : 0;
        }
        var command = Commands.FirstOrDefault(c => c.Name.Equals(argv[0], StringComparison.OrdinalIgnoreCase));
        if (command.Name == null)
        {
            Console.Error.WriteLine($"There is no command \"{argv[0]}\". Run mcstudio help to see them.");
            return 2;
        }
        var args = new Args(argv.Skip(1));
        if (args.Has("help")) { Help(command.Name); return 0; }
        try { return command.Run(args); }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage: mcstudio " + command.Usage);
            return 2;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException
            or InvalidDataException or MinecraftStudio.Core.IO.SaveFormatException)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
    }

    static void Help(string? name)
    {
        Console.WriteLine($"{AppInfo.Name} — command line (mcstudio). Unofficial; not affiliated with Mojang or Microsoft.");
        Console.WriteLine();
        foreach (var c in Commands.Where(c => name == null || c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("  mcstudio " + c.Usage);
            Console.WriteLine("      " + c.What);
        }
        Console.WriteLine();
        Console.WriteLine("Worlds and files given are only read; results go to new folders and files. Exit codes: 0 done, 1 done but");
        Console.WriteLine("something was lost or a check failed, 2 the command line was not right, 3 it could not be done.");
    }
}
