// MinecraftStudio.Cli/TerrainCommands.cs: the terrain tools from the command line — heights out, land from a heightmap,
// and the land in a box shaped.
// Owns: TerrainCommands — Heights (a dimension's ground as a 16-bit grey PNG, never over a file), Heightmap (a PNG made
// into a new world of any edition by the converter, checked), Shape (flatten / smooth / raise / lower a box into a new copy,
// every column read back; --plan only says how far the land would move).
using System.Globalization;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Cli;

static class TerrainCommands
{
    public static int Heights(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        string file = Path.GetFullPath(a.Require(1, "the new .png"));
        if (File.Exists(file)) throw new IOException($"{file} is there already; choose a new name.");
        string dimId = a.Option("dim") ?? "minecraft:overworld";
        var dim = w.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimId) ?? throw new UsageException($"The world has no {dimId}.");
        var r = DimensionRenderer.Render(dim, new MapRenderOptions(), ground: true);
        if (r.Width == 0) throw new InvalidOperationException("The dimension has no chunks.");
        var ground = r.Ground ?? throw new InvalidOperationException("The heights could not be read.");
        var have = ground.Where(y => y != MapRenderer.NoHeight).ToList();
        if (have.Count == 0) throw new InvalidOperationException("No ground was found in this dimension.");
        int low = have.Min(), high = Math.Max(have.Max(), low + 1);
        SafeFile.WriteAllBytes(file, Core.Rendering.Heightmap.Encode(ground, r.Width, r.Height, low, high, r.MinChunkX * 16, r.MinChunkZ * 16));
        Console.WriteLine($"Saved the heights of {dimId} ({r.Width:N0} × {r.Height:N0} blocks from x {r.MinChunkX * 16}, z {r.MinChunkZ * 16}; black y {low}, white y {high}) to {file}.");
        return 0;
    }

    public static int Heightmap(Args a)
    {
        string file = Path.GetFullPath(a.Require(0, "the heightmap picture (.png)"));
        var target = WorldCommands.Target(a.Option("to") ?? throw new UsageException("Say what to make: --to <target> (mcstudio targets lists them)."));
        string output = Path.GetFullPath(a.Option("out") ?? throw new UsageException("Say where the new world goes: --out <new folder>."));
        if (File.Exists(output) || Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException($"{output} is not a new, empty folder.");
        var picture = Core.Rendering.Heightmap.Read(File.ReadAllBytes(file));
        int Int(string name, int fallback) => a.Option(name) is string s ? int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : throw new UsageException($"--{name} takes a whole number, not \"{s}\".") : fallback;
        string? water = a.Option("water");
        var land = new HeightmapLand
        {
            Picture = picture,
            LowY = Int("low", picture.LowY ?? 40), HighY = Int("high", picture.HighY ?? 160),
            WaterY = water is "none" ? null : Int("water", 62), SnowY = a.Option("snow") is string ? Int("snow", 0) : null,
            BlocksPerPixel = a.Option("scale") is string sc ? double.TryParse(sc, NumberStyles.Float, CultureInfo.InvariantCulture, out var k) ? k : throw new UsageException($"--scale takes a number of blocks per pixel, not \"{sc}\".") : 1,
            // Where its notes say it was saved from, unless asked for the middle at 0, 0.
            WestX = a.Has("centre") ? null : picture.WestX, NorthZ = a.Has("centre") ? null : picture.NorthZ,
            Name = a.Option("name") ?? Path.GetFileNameWithoutExtension(file), GameType = a.Has("survival") ? 0 : 1, FlatAround = a.Has("flat"),
        };
        var world = new HeightmapWorld(land, file);
        Console.WriteLine($"{world.Name}: {world.FormatDescription} → {target.Name}");
        string last = "";
        var report = WorldConverter.Run(world, target, output, new ConversionOptions(), new Progress<ConversionProgress>(p =>
        {
            if (p.Stage != last) { last = p.Stage; Console.WriteLine("  " + p.Stage); }
        }));
        return WorldCommands.PrintReport(report, output);
    }

    public static int Shape(Args a)
    {
        using var w = WorldLoader.Open(a.Require(0, "the world"));
        string how = a.Require(1, "how: flatten, smooth, raise or lower").ToLowerInvariant();
        var kind = how switch
        {
            "flatten" => LandShapeKind.Flatten, "smooth" => LandShapeKind.Smooth, "raise" => LandShapeKind.Raise, "lower" => LandShapeKind.Lower,
            _ => throw new UsageException($"\"{how}\" is not flatten, smooth, raise or lower."),
        };
        string amountText = a.Require(2, kind switch { LandShapeKind.Flatten => "the y to flatten to", LandShapeKind.Smooth => "the radius (1–16)", _ => "how many blocks" });
        if (!int.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount)) throw new UsageException($"\"{amountText}\" is not a whole number.");
        var box = WorldCommands.Numbers(a.Option("box") ?? throw new UsageException("Say where: --box x1,z1,x2,z2."), 4, "--box x1,z1,x2,z2");
        int edge = a.Option("edge") is string e ? int.TryParse(e, out int v) ? v : throw new UsageException($"--edge takes a whole number, not \"{e}\".") : 4;
        var options = new LandShapeOptions(kind, amount, edge);
        string dimId = a.Option("dim") ?? "minecraft:overworld";
        int x1 = Math.Min(box[0], box[2]), z1 = Math.Min(box[1], box[3]), x2 = Math.Max(box[0], box[2]), z2 = Math.Max(box[1], box[3]);
        if (a.Has("plan"))
        {
            var plan = LandShape.Plan(w, dimId, x1, z1, x2, z2, options);
            Console.WriteLine($"{plan.Changed:N0} of {plan.Columns:N0} columns would move: up to {plan.MostUp} blocks up and {plan.MostDown} down (nothing written).");
            return 0;
        }
        string output = Path.GetFullPath(a.Option("out") ?? throw new UsageException("Say where the copy goes: --out <new folder> (or --plan to only see it)."));
        var r = LandShape.ShapeIntoCopy(w, dimId, x1, z1, x2, z2, options, output);
        foreach (var n in r.Notes) Console.WriteLine("  note: " + n);
        foreach (var p in r.Problems) Console.WriteLine("  ✗ " + p);
        Console.WriteLine($"The land is shaped in {r.OpenPath} ({r.ChunksChanged:N0} chunks rewritten){(r.Verified ? ", every column checked" : "")}.");
        return r.Verified ? 0 : 1;
    }
}
