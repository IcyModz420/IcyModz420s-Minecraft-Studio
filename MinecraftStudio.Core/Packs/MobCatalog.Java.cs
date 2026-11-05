// MinecraftStudio.Core/Packs/MobCatalog.Java.cs: the mobs of an installed Java Edition release (its game jar).
// Part of MobCatalog (main part in MobCatalog.cs).
// Owns: reading assets/minecraft/textures/entity from the jar and the English names (assets/minecraft/lang/en_us.json).
// Java keeps its mob models in the game's code, not in files, so the models come from the installed Bedrock game
// (BorrowModels): same texture path first, else a texture of the same mob and size.
using System.IO.Compression;
using System.Text.Json;

namespace MinecraftStudio.Core.Packs;

public sealed partial class MobCatalog
{
    /// <summary>The mob texture names of a Java game jar, read from its file list only (quick).</summary>
    public static HashSet<string> JavaNames(string jar)
    {
        const string Root = "assets/minecraft/textures/";
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var zip = ZipFile.OpenRead(jar);
            foreach (var e in zip.Entries)
            {
                string n = e.FullName.Replace('\\', '/');
                if (n.StartsWith(Root + Prefix, StringComparison.Ordinal) && n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) names.Add(n[Root.Length..^4]);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
        return names;
    }

    /// <summary>The mobs of a Java game jar (any release from 1.6, when textures moved to assets/).</summary>
    public static MobCatalog Java(string jar, string version)
    {
        var catalog = new MobCatalog { Edition = PackEdition.Java, Source = "Java Edition " + version };
        catalog.Textures.Edition = PackEdition.Java;
        catalog.Textures.Name = catalog.Source;
        catalog.Textures.JavaRelease = JavaReleases.Of(version);
        using var zip = ZipFile.OpenRead(jar);
        var lang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (zip.GetEntry("assets/minecraft/lang/en_us.json") is { } langEntry && langEntry.Length < 16 << 20)
        {
            try
            {
                using var s = langEntry.Open();
                using var doc = JsonDocument.Parse(s, Lenient);
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Name.StartsWith("entity.minecraft.", StringComparison.Ordinal) && p.Value.ValueKind == JsonValueKind.String) lang[p.Name["entity.minecraft.".Length..]] = p.Value.GetString()!;
            }
            catch (JsonException e) { catalog.Notes.Add("en_us.json: " + e.Message.Split('\n')[0]); }
        }
        const string Root = "assets/minecraft/textures/";
        foreach (var e in zip.Entries)
        {
            string name = e.FullName.Replace('\\', '/');
            if (!name.StartsWith(Root + Prefix, StringComparison.Ordinal) || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Split('/').Any(p => p == "..") || e.Length > 16 << 20 || IsMaterialMap(name[..^4])) continue;
            byte[] data;
            using (var s = e.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                data = ms.ToArray();
            }
            if (Decode(name, data, catalog.Notes) is not { } img) continue;
            string key = name[Root.Length..^4];
            catalog.Textures.Textures[key] = new PackTexture { Key = key, Image = img, SourcePath = name };
            catalog.Info[key] = Guess(key, folder => lang.GetValueOrDefault(folder));
        }
        return catalog;
    }
}
