// MinecraftStudio.Core/Packs/AnimationPack.cs: the Animation Studio's work as a Bedrock resource pack.
// Owns: AnimationPack — a pack (.mcpack) with the project's changed and new animations (animations/…) and, when it was
// changed, its model (models/entity/…), under the game's own ids so they replace the game's in every world the pack is
// on; checked by reading the pack back. Java Edition keeps its mob models and animations in the game's code, so a pack
// cannot change them there (the note says so).
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Skins;

namespace MinecraftStudio.Core.Packs;

public static class AnimationPack
{
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The pack for Bedrock Edition: one mob's changed and new animations, and its model when it changed.</summary>
    public static PackResult Bedrock(AnimationProject project, string name, string description, DateTime when, RgbaImage? icon = null) =>
        Bedrock([project], name, description, when, icon);

    /// <summary>The pack for Bedrock Edition: every mob's changed and new animations, and the models that changed.</summary>
    public static PackResult Bedrock(IReadOnlyList<AnimationProject> projects, string name, string description, DateTime when, RgbaImage? icon = null)
    {
        var result = new PackResult { FileName = SkinExport.SafeFileName(name) + ".mcpack" };
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var version = new JsonArray(when.Year, when.Month * 100 + when.Day, when.Hour * 10000 + when.Minute * 100 + when.Second);
        var manifest = new JsonObject
        {
            ["format_version"] = 2,
            ["header"] = new JsonObject
            {
                ["name"] = name, ["description"] = (description.Length > 0 ? description : name) + ". " + PackInstaller.Signature + ".",
                ["uuid"] = SkinExport.NameUuid("animation pack header:" + name.ToLowerInvariant()), ["version"] = version.DeepClone(), ["min_engine_version"] = new JsonArray(1, 16, 0),
            },
            ["modules"] = new JsonArray(new JsonObject { ["type"] = "resources", ["uuid"] = SkinExport.NameUuid("animation pack module:" + name.ToLowerInvariant()), ["version"] = version.DeepClone() }),
        };
        files["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString(Indented) + "\n");
        if (icon != null) files["pack_icon.png"] = icon.ToPng();
        var checks = new List<(string Path, List<EntityAnimation> Animations, EntityModel? Model)>();
        foreach (var project in projects)
        {
            string safe = new string(project.Mob.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray()).ToLowerInvariant();
            if (safe.Length == 0) safe = "mob";
            var animations = project.ChangedAnimations.Where(project.Animations.ContainsKey).Select(id => project.Animations[id]).OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
            string animPath = $"animations/studio_{safe}.animation.json", modelPath = $"models/entity/studio_{safe}.geo.json";
            if (animations.Count > 0) { files[animPath] = EntityAnimations.File(animations); checks.Add((animPath, animations, null)); }
            if (project.ModelChanged && project.Model != null) { files[modelPath] = EntityModels.File([project.Model]); checks.Add((modelPath, [], project.Model)); }
            foreach (var a in animations) result.Written.Add(a.Id);
            if (project.ModelChanged && project.Model != null) result.Written.Add(project.Model.Id);
            var fresh = animations.Where(a => project.NewIds.Contains(a.Id)).ToList();
            if (fresh.Count > 0)
                result.Notes.Add($"New {project.Mob} animations play when something plays them: in a world with cheats on, /playanimation @e[type={project.Mob}] {fresh[0].Id}" + (fresh.Count > 1 ? $" (and {fresh.Count - 1} more)." : "."));
        }
        if (result.Written.Count == 0) result.Problems.Add("Nothing has been changed yet, so the pack would be empty.");
        result.Notes.Insert(0, "Changed animations and models replace the game's own of the same name wherever the pack is turned on (Settings, Global Resources, or a world's Resource Packs).");
        result.Notes.Add("Java Edition keeps its mob models and animations in the game itself, so a pack cannot change them there.");
        result.Data = Zip(files);
        Verify(result, files, checks);
        return result;
    }

    static byte[] Zip(Dictionary<string, byte[]> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, data) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                var e = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var s = e.Open();
                s.Write(data);
            }
        return ms.ToArray();
    }

    /// <summary>Reads the pack back: every file is there and its animations and model are the ones meant.</summary>
    static void Verify(PackResult result, Dictionary<string, byte[]> files, List<(string Path, List<EntityAnimation> Animations, EntityModel? Model)> checks)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(result.Data), ZipArchiveMode.Read);
            foreach (var (path, data) in files)
            {
                if (zip.GetEntry(path) is not { } e) { result.Problems.Add($"{path} is missing from the pack."); continue; }
                using var s = e.Open();
                using var copy = new MemoryStream();
                s.CopyTo(copy);
                if (!copy.ToArray().AsSpan().SequenceEqual(data)) { result.Problems.Add($"{path} does not hold what was meant."); continue; }
                var check = checks.FirstOrDefault(c => c.Path == path);
                if (path.EndsWith(".animation.json"))
                {
                    var back = EntityAnimations.Read([(path, copy.ToArray())]);
                    foreach (var a in check.Animations ?? [])
                        if (!back.TryGetValue(a.Id, out var b) || EntityAnimations.ToJson(b).ToJsonString() != EntityAnimations.ToJson(a).ToJsonString())
                            result.Problems.Add($"{a.Id} would be read back differently.");
                }
                else if (path.EndsWith(".geo.json") && check.Model is { } model)
                {
                    var back = EntityModels.Read([(path, copy.ToArray())]);
                    if (!back.TryGetValue(model.Id, out var m) || m.CubeCount != model.CubeCount || m.Bones.Count != model.Bones.Count)
                        result.Problems.Add($"The model {model.Id} would be read back differently.");
                }
                else if (path.EndsWith(".json")) JsonDocument.Parse(copy.ToArray()).Dispose();
            }
        }
        catch (Exception e) when (e is InvalidDataException or JsonException)
        {
            result.Problems.Add("The pack cannot be read back: " + e.Message);
        }
    }
}
