// MinecraftStudio.Core/Packs/PackInstaller.cs: Puts packs where the games look for them.
// Owns: PackInstaller.
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace MinecraftStudio.Core.Packs;

/// <summary>
/// Puts packs where the games look for them. Nothing the player made elsewhere is ever overwritten: a pack is only
/// replaced by a newer copy of itself (the same file made by this program for Java, the same pack id for Bedrock).
/// </summary>
public static class PackInstaller
{
    /// <summary>Marks Java packs made by this program (in the pack description).</summary>
    public const string Signature = AppInfo.Credit;

    /// <summary>
    /// Copies a Java pack (.zip) into a game folder's resourcepacks folder (texturepacks for Java 1.5.2 and older) and
    /// returns where it went.
    /// </summary>
    public static string InstallJava(string fileName, byte[] zip, string gameFolder, bool texturePack)
    {
        string folder = Path.Combine(gameFolder, texturePack ? "texturepacks" : "resourcepacks");
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, fileName);
        for (int n = 2; File.Exists(target); n++)
        {
            var existing = File.ReadAllBytes(target);
            if (existing.AsSpan().SequenceEqual(zip)) return target;
            if (MadeHere(existing)) break;
            target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(fileName)} ({n}){Path.GetExtension(fileName)}");
        }
        string temp = target + ".partial";
        File.WriteAllBytes(temp, zip);
        if (!File.ReadAllBytes(temp).AsSpan().SequenceEqual(zip)) { File.Delete(temp); throw new IOException("The copied pack does not match: the disk may be full."); }
        File.Move(temp, target, overwrite: true);
        return target;
    }

    /// <summary>Whether a Java pack zip was made by this program.</summary>
    static bool MadeHere(byte[] zip)
    {
        try
        {
            using var z = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            foreach (var name in new[] { "pack.mcmeta", "pack.txt" })
                if (z.GetEntry(name) is { } e)
                {
                    using var r = new StreamReader(e.Open(), Encoding.UTF8);
                    if (MentionsUs(r.ReadToEnd())) return true;
                }
        }
        catch (InvalidDataException) { }
        return false;
    }

    /// <summary>
    /// Whether a pack.mcmeta / pack.txt carries our credit (current or pre-rename). JSON escapes characters such as the
    /// apostrophe ('), so JSON is read value by value rather than searched as raw text.
    /// </summary>
    static bool MentionsUs(string text)
    {
        static bool Has(string s) => AppInfo.Credits.Any(s.Contains);
        if (Has(text)) return true;   // pack.txt (plain text), or JSON with nothing escaped
        try
        {
            using var doc = JsonDocument.Parse(text);
            return AnyString(doc.RootElement);
        }
        catch (JsonException) { return false; }

        static bool AnyString(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString() is { } s && Has(s),
            JsonValueKind.Object => e.EnumerateObject().Any(p => AnyString(p.Value)),
            JsonValueKind.Array => e.EnumerateArray().Any(AnyString),
            _ => false,
        };
    }

    /// <summary>The header id, name, version and module types in a Bedrock manifest.json.</summary>
    public sealed record BedrockManifest(string Uuid, string Name, string Version, IReadOnlyList<string> Modules);

    public static BedrockManifest ReadManifest(byte[] manifestJson)
    {
        using var doc = JsonDocument.Parse(manifestJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var header = doc.RootElement.GetProperty("header");
        string version = header.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Array ? string.Join(".", v.EnumerateArray().Select(x => x.ToString())) : "";
        var modules = doc.RootElement.TryGetProperty("modules", out var m) && m.ValueKind == JsonValueKind.Array
            ? m.EnumerateArray().Select(x => x.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "").ToList() : [];
        return new(header.GetProperty("uuid").GetString() ?? "", header.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "", version, modules);
    }

    /// <summary>
    /// Unpacks a Bedrock pack (.mcpack) into a com.mojang folder: skin packs go to skin_packs, resource packs to
    /// resource_packs. An installed copy with the same pack id is replaced; returns the pack's folder.
    /// </summary>
    public static string InstallBedrock(byte[] mcpack, string comMojang)
    {
        using var zip = new ZipArchive(new MemoryStream(mcpack), ZipArchiveMode.Read);
        var manifestEntry = zip.Entries.Where(e => e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName.Count(c => c == '/')).FirstOrDefault() ?? throw new InvalidDataException("The pack has no manifest.json.");
        string root = manifestEntry.FullName[..^manifestEntry.Name.Length];
        BedrockManifest manifest;
        using (var s = manifestEntry.Open())
        using (var ms = new MemoryStream()) { s.CopyTo(ms); manifest = ReadManifest(ms.ToArray()); }
        string kind = manifest.Modules.Contains("skin_pack") ? "skin_packs" : manifest.Modules.Contains("resources") ? "resource_packs"
            : throw new InvalidDataException("Only skin packs and resource packs can be installed here.");
        string parent = Path.Combine(comMojang, kind);
        Directory.CreateDirectory(parent);

        // Unpack next to the final place first, so a failure leaves the installed packs as they were.
        string temp = Path.Combine(parent, ".installing-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        try
        {
            string tempFull = Path.GetFullPath(temp) + Path.DirectorySeparatorChar;
            var written = new Dictionary<string, long>();
            foreach (var e in zip.Entries)
            {
                if (!e.FullName.StartsWith(root, StringComparison.Ordinal) || e.FullName.EndsWith('/')) continue;
                string rel = e.FullName[root.Length..];
                string dest = Path.GetFullPath(Path.Combine(temp, rel));
                if (!dest.StartsWith(tempFull, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"The pack holds a file that would land outside its folder: {e.FullName}");
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                e.ExtractToFile(dest, overwrite: true);
                written[dest] = e.Length;
            }
            foreach (var (path, length) in written)
                if (new FileInfo(path).Length != length) throw new IOException($"{Path.GetFileName(path)} was not unpacked completely.");

            string? old = Directory.EnumerateDirectories(parent).FirstOrDefault(d => !d.Equals(temp, StringComparison.OrdinalIgnoreCase) && SameId(d, manifest.Uuid));
            string target = old ?? UniqueFolder(parent, manifest.Name);
            if (old != null) Directory.Delete(old, recursive: true);
            Directory.Move(temp, target);
            return target;
        }
        catch
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            throw;
        }
    }

    static bool SameId(string folder, string uuid)
    {
        string m = Path.Combine(folder, "manifest.json");
        if (!File.Exists(m)) return false;
        try { return ReadManifest(File.ReadAllBytes(m)).Uuid.Equals(uuid, StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IOException) { return false; }
    }

    static string UniqueFolder(string parent, string name)
    {
        string clean = IO.SafeName.For(name, "pack");
        string path = Path.Combine(parent, clean);
        for (int n = 2; Directory.Exists(path) || File.Exists(path); n++) path = Path.Combine(parent, $"{clean} ({n})");
        return path;
    }
}
