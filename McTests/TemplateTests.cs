// McTests/TemplateTests.cs: "templates" — new packs set up right for their version.
// Owns: TemplateTests — a Java data pack for every release's data pack format, checked by PackCheck (no problems, the
// format it reads is the one asked for), pack_format before 1.21.9 and min_format/max_format from it, plural folders
// before 1.21 and singular from it, the load and tick tags naming the namespace's functions; a Bedrock add-on whose two
// manifests PackCheck finds right, with their own UUIDs and the behavior pack depending on the resource pack; namespaces
// made from names; nothing written over an existing folder.
using System.Text.Json;
using MinecraftStudio.Core.Packs;

static class TemplateTests
{
    public static void Run(string[] args)
    {
        string dir = Path.Combine(TestPaths.Output, "templates");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        try
        {
            int clean = 0, formatRead = 0, layout = 0;
            var wrong = new List<string>();
            foreach (var f in PackTemplates.DataPackFormats)
            {
                string folder = Path.Combine(dir, "java" + f.Text.Replace('.', '_'));
                // With the examples (a recipe, a loot table, an advancement), written the way each version reads them.
                var files = PackTemplates.JavaDataPack(folder, "Test Pack", "A test pack", "test_pack", f, examples: true);
                var report = PackCheck.Run(folder);
                if (report.Problems == 0) clean++; else wrong.Add($"{f.Versions}: {report.Text()}");
                if (report.Kind.Contains($"pack format {f.Major}")) formatRead++; else wrong.Add($"{f.Versions}: read as {report.Kind}");
                using var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "pack.mcmeta")));
                var pack = meta.RootElement.GetProperty("pack");
                bool fields = f.MinMax ? pack.TryGetProperty("min_format", out var min) && min[0].GetInt32() == f.Major && min[1].GetInt32() == f.Minor && pack.TryGetProperty("max_format", out _) && !pack.TryGetProperty("pack_format", out _)
                                       : pack.TryGetProperty("pack_format", out var pf) && pf.GetInt32() == f.Major && !pack.TryGetProperty("min_format", out _);
                string fn = f.Singular ? "function" : "functions";
                bool folders = files.Contains($"data/test_pack/{fn}/load.mcfunction") && files.Contains($"data/minecraft/tags/{fn}/tick.json");
                using var load = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "data", "minecraft", "tags", fn, "load.json")));
                bool tag = load.RootElement.GetProperty("values")[0].GetString() == "test_pack:load";
                if (fields && folders && tag) layout++; else wrong.Add($"{f.Versions}: fields {fields}, folders {folders}, tag {tag}");
            }
            foreach (var w in wrong.Take(6)) Program.Info("  " + w);
            int n = PackTemplates.DataPackFormats.Count;
            Program.Check(clean == n, $"a Java data pack for each of the {n} formats passes the pack checker with no problems ({clean} of {n})");
            Program.Check(formatRead == n, $"the pack checker reads each one's format as the one asked for ({formatRead} of {n})");
            Program.Check(layout == n, $"pack_format before 1.21.9, min/max_format from it; functions before 1.21, function from it; load/tick tags naming the functions ({layout} of {n})");

            var (bp, rp) = PackTemplates.BedrockAddOn(dir, "Test Add-on", "Made in a test", [1, 21, 0]);
            var bpReport = PackCheck.Run(bp);
            var rpReport = PackCheck.Run(rp);
            Program.Check(bpReport.Problems == 0 && rpReport.Problems == 0, $"the Bedrock add-on's behavior and resource packs pass the pack checker ({bpReport.Kind}; {rpReport.Kind})");
            using var bpm = JsonDocument.Parse(File.ReadAllText(Path.Combine(bp, "manifest.json")));
            using var rpm = JsonDocument.Parse(File.ReadAllText(Path.Combine(rp, "manifest.json")));
            string rpId = rpm.RootElement.GetProperty("header").GetProperty("uuid").GetString()!;
            var ids = new[]
            {
                bpm.RootElement.GetProperty("header").GetProperty("uuid").GetString(), bpm.RootElement.GetProperty("modules")[0].GetProperty("uuid").GetString(),
                rpId, rpm.RootElement.GetProperty("modules")[0].GetProperty("uuid").GetString(),
            };
            Program.Check(ids.Distinct().Count() == 4 && bpm.RootElement.GetProperty("dependencies")[0].GetProperty("uuid").GetString() == rpId,
                "the add-on's four UUIDs are all different, and the behavior pack depends on the resource pack");
            Program.Check(PackTemplates.NamespaceFor("My Cool Pack!") == "my_cool_pack" && PackTemplates.NamespaceFor("Minecraft") == "my_pack" && !PackTemplates.IsNamespace("Bad Space") && !PackTemplates.IsNamespace("minecraft"),
                "namespaces from names (\"My Cool Pack!\" → my_cool_pack); minecraft and spaces refused");
            bool refused = false;
            try { PackTemplates.JavaDataPack(Path.Combine(dir, "java4"), "Again", "x", "test_pack", PackTemplates.DataPackFormats[0]); }
            catch (IOException) { refused = true; }
            Program.Check(refused, "a template is never written over a folder that has files");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
