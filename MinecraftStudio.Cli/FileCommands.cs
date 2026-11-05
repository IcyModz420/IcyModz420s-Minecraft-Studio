// MinecraftStudio.Cli/FileCommands.cs: the commands for files — structure, materials, packcheck, newpack.
// Owns: FileCommands. structure reads any structure or schematic and writes a new file (never over the one it read;
// another existing file only with --replace), checked by reading it back; packcheck only reads; newpack makes new folders.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Structures;

namespace MinecraftStudio.Cli;

static class FileCommands
{
    public static int Structure(Args a)
    {
        string input = Path.GetFullPath(a.Require(0, "the structure file"));
        string output = Path.GetFullPath(a.Require(1, "the new file"));
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase)) throw new UsageException("The new file must be another file: the one read is never changed.");
        if (File.Exists(output) && !a.Has("replace")) throw new IOException($"{output} is there already (add --replace to write over it).");
        int turns = a.Int("turn", 0);
        if (turns is < 0 or > 3) throw new UsageException("--turn is 0 to 3 quarter turns clockwise.");
        var mirror = (a.Option("mirror") ?? "none").ToLowerInvariant() switch
        {
            "none" => StructureMirror.None,
            "ns" or "northsouth" or "north-south" => StructureMirror.NorthSouth,
            "ew" or "eastwest" or "east-west" => StructureMirror.EastWest,
            var m => throw new UsageException($"--mirror is ns or ew, not \"{m}\"."),
        };
        int dv = a.Option("version") is string version ? JavaVersion(version) : 0;
        var s = StructureTransform.Apply(StructureFile.Load(input), turns, mirror);
        var standIns = StructureFile.SaveChecked(s, output, dv);
        Console.WriteLine($"Saved {s.Describe()} to {output} ({StructureFile.FormatOf(output)}).");
        foreach (var c in standIns) Console.WriteLine($"  stand-in: {c.Count:N0} × {c.From} → {c.To} (the file's type or version has no {c.From})");
        return standIns.Count == 0 ? 0 : 1;
    }

    /// <summary>The data version of a Java release named as the targets list names it ("1.20.4", "1.21 – 1.21.1", "26.1").</summary>
    public static int JavaVersion(string version) =>
        TargetCatalog.All.FirstOrDefault(t => t.Kind == TargetKind.Java && t.DataVersion >= StructureJava.Flattening
            && (t.Name.Equals(version, StringComparison.OrdinalIgnoreCase) || t.Name.EndsWith(version, StringComparison.OrdinalIgnoreCase) || t.Name.Split(' ', '–').Contains(version)))?.DataVersion
        ?? throw new UsageException($"No Java version {version} (1.13 or newer) to make the file for. Run mcstudio targets to see them.");

    public static int Obj(Args a)
    {
        string input = Path.GetFullPath(a.Require(0, "the structure file"));
        string output = Path.GetFullPath(a.Require(1, "the new .obj"));
        if (!output.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)) throw new UsageException("The model is a .obj file.");
        if (File.Exists(output) && !a.Has("replace")) throw new IOException($"{output} is there already (add --replace to write over it).");
        int turns = a.Int("turn", 0);
        var mirror = (a.Option("mirror") ?? "none").ToLowerInvariant() switch { "ns" => StructureMirror.NorthSouth, "ew" => StructureMirror.EastWest, _ => StructureMirror.None };
        var s = StructureTransform.Apply(StructureFile.Load(input), turns & 3, mirror);
        var r = StructureModel.Save(s, output);
        Console.WriteLine($"Saved {output} and its .mtl: {r.Faces:N0} faces, {r.Vertices:N0} corners, {r.Materials} colours{(r.BlocksLeftOut > 0 ? $", {r.BlocksLeftOut:N0} small blocks left out" : "")}.");
        return 0;
    }

    public static int Materials(Args a)
    {
        var s = StructureFile.Load(a.Require(0, "the structure file"));
        var list = StructureMaterials.Count(s);
        Console.Write(a.Has("csv") ? StructureMaterials.Csv(list) : StructureMaterials.Text(list, $"{Path.GetFileName(a.Word(0))}: {s.Describe()}") + Environment.NewLine);
        return 0;
    }

    public static int PackCheck(Args a)
    {
        var report = Core.Packs.PackCheck.Run(a.Require(0, "the pack"));
        Console.WriteLine(report.Text());
        return report.Problems == 0 ? 0 : 1;
    }

    public static int NewPack(Args a)
    {
        string kind = a.Require(0, "java or bedrock").ToLowerInvariant();
        string name = a.Require(1, "the pack's name");
        string folder = Path.GetFullPath(a.Require(2, "the folder to make it in"));
        string description = a.Option("description") ?? "Made with " + Core.AppInfo.Name;
        if (kind == "java")
        {
            var formats = PackTemplates.DataPackFormats;
            var format = a.Option("version") is string v
                ? formats.FirstOrDefault(f => f.Versions.Split(' ', '–').Contains(v) || f.Versions.Equals(v, StringComparison.OrdinalIgnoreCase))
                  ?? throw new UsageException($"No Java release {v} with data packs (1.13 or newer).")
                : formats[^1];
            string ns = a.Option("namespace") ?? PackTemplates.NamespaceFor(name);
            var files = PackTemplates.JavaDataPack(folder, name, description, ns, format, a.Has("examples"));
            Console.WriteLine($"Made the data pack for Java {format.Versions} (format {format.Text}) in {folder}:");
            foreach (var f in files) Console.WriteLine("  " + f);
            return 0;
        }
        if (kind == "bedrock")
        {
            int[] version = [1, 21, 0];
            if (a.Option("version") is string v)
            {
                var parts = v.Split('.');
                if (parts.Length != 3 || parts.Any(p => !int.TryParse(p, out _))) throw new UsageException("--version for Bedrock is three numbers, like 1.21.0.");
                version = parts.Select(int.Parse).ToArray();
            }
            var (bp, rp) = PackTemplates.BedrockAddOn(folder, name, description, version);
            Console.WriteLine($"Made the add-on:\n  {bp}\n  {rp}");
            return 0;
        }
        throw new UsageException("Say java or bedrock.");
    }
}
