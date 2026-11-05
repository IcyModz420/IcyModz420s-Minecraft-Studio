// MinecraftStudio.Core/Packs/WorldPacks.Bedrock.cs: a Bedrock Edition (Windows PC) world's behavior and resource packs.
// Owns: reading world_behavior_packs.json / world_resource_packs.json and finding each pack (in the world's own
// behavior_packs / resource_packs, or installed in the game), installing .mcpack / .mcaddon / .zip / folder packs into
// the world, turning packs on and off, their priority, and uninstalling. The pack lists are the JSON the game writes,
// edited as JSON nodes so fields this program does not know (subpacks and the like) are kept.
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.Packs;

public static partial class WorldPacks
{
    /// <summary>A Bedrock pack as its manifest describes it (Folder: where it is unpacked).</summary>
    sealed record BedrockPackInfo(string Uuid, string Name, string Description, int[] Version, List<string> Modules, string Folder);

    static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    static string ListFile(BedrockWorld b, WorldPackKind kind) => ListFile(b.Folder, kind);
    static string ListFile(string worldFolder, WorldPackKind kind) =>
        Path.Combine(worldFolder, kind == WorldPackKind.BehaviorPack ? "world_behavior_packs.json" : "world_resource_packs.json");

    static string PackFolder(BedrockWorld b, WorldPackKind kind) => PackFolder(b.Folder, kind);
    static string PackFolder(string worldFolder, WorldPackKind kind) =>
        Path.Combine(worldFolder, kind == WorldPackKind.BehaviorPack ? "behavior_packs" : "resource_packs");

    static List<WorldPack> ReadBedrock(BedrockWorld b) => ReadBedrock(b.Folder);

    /// <summary>A Bedrock world's packs from its folder (the pack lists and pack folders; the world database is not needed).</summary>
    static List<WorldPack> ReadBedrock(string worldFolder)
    {
        var result = new List<WorldPack>();
        foreach (var kind in new[] { WorldPackKind.ResourcePack, WorldPackKind.BehaviorPack })
        {
            var listed = ReadPackList(ListFile(worldFolder, kind));
            var inWorld = ScanPacks(PackFolder(worldFolder, kind));
            var installed = InstalledPacks(kind);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in listed.OfType<JsonObject>())
            {
                string uuid = Text(entry["pack_id"]);
                if (uuid.Length == 0 || !seen.Add(uuid)) continue;
                inWorld.TryGetValue(uuid, out var here);
                installed.TryGetValue(uuid, out var there);
                var info = here ?? there.Info;
                result.Add(new WorldPack
                {
                    Kind = kind,
                    Id = uuid,
                    Name = info is { Name.Length: > 0 } ? info.Name : uuid,
                    Description = info?.Description ?? "",
                    Version = info != null ? string.Join(".", info.Version) : VersionText(entry["version"]),
                    Location = info?.Folder,
                    Active = true,
                    InWorld = here != null,
                    Where = here != null ? "in this world" : there.Info != null ? "installed in " + there.Label + " (shared with other worlds)" : "missing — the world lists it, but it is not on this PC",
                });
            }
            foreach (var (uuid, info) in inWorld)
                if (!seen.Contains(uuid))
                    result.Add(new WorldPack
                    {
                        Kind = kind, Id = uuid, Name = info.Name.Length > 0 ? info.Name : uuid, Description = info.Description,
                        Version = string.Join(".", info.Version), Location = info.Folder, Active = false, InWorld = true,
                        Where = "in this world (turned off)",
                    });
        }
        return result;
    }

    /// <summary>A world's pack list (an empty list when the file is not there). A damaged file is reported, never overwritten.</summary>
    static JsonArray ReadPackList(string file)
    {
        if (!File.Exists(file)) return [];
        try
        {
            var text = File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(text)) return [];
            return JsonNode.Parse(text, documentOptions: Lenient) as JsonArray ?? throw new JsonException("not a list");
        }
        catch (JsonException e) { throw new InvalidDataException($"{Path.GetFileName(file)} in the world is damaged ({e.Message}), so it was left as it is."); }
    }

    static void WritePackList(string file, JsonArray list) =>
        SafeFile.WriteAllBytes(file, Encoding.UTF8.GetBytes(list.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));

    /// <summary>Packs unpacked in a folder (each subfolder with a manifest.json), by UUID.</summary>
    static Dictionary<string, BedrockPackInfo> ScanPacks(string folder)
    {
        var map = new Dictionary<string, BedrockPackInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return map;
        foreach (var dir in Directory.EnumerateDirectories(folder))
        {
            if (Path.GetFileName(dir).StartsWith('.')) continue;   // a pack still being installed
            string manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest) || new FileInfo(manifest).Length > (1 << 20)) continue;
            if (ParseManifest(File.ReadAllBytes(manifest), key => Lang(Path.Combine(dir, "texts", "en_US.lang"), key), dir) is BedrockPackInfo info)
                map.TryAdd(info.Uuid, info);
        }
        return map;
    }

    /// <summary>Packs installed in the game itself (every Bedrock install on this PC), by UUID, with where they are.</summary>
    static Dictionary<string, (BedrockPackInfo Info, string Label)> InstalledPacks(WorldPackKind kind)
    {
        var map = new Dictionary<string, (BedrockPackInfo, string)>(StringComparer.OrdinalIgnoreCase);
        var subs = kind == WorldPackKind.BehaviorPack ? new[] { "behavior_packs", "development_behavior_packs" } : new[] { "resource_packs", "development_resource_packs" };
        foreach (var (label, comMojang) in GameFolders.Bedrock())
            foreach (var sub in subs)
                foreach (var (uuid, info) in ScanPacks(Path.Combine(comMojang, sub)))
                    map.TryAdd(uuid, (info, label));
        return map;
    }

    /// <summary>A manifest's UUID, name, description, version and module types; pack.name / pack.description keys are looked up in the pack's English texts.</summary>
    static BedrockPackInfo? ParseManifest(byte[] json, Func<string, string?> lang, string folder)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, Lenient);
            if (!doc.RootElement.TryGetProperty("header", out var header) || !header.TryGetProperty("uuid", out var u) || u.GetString() is not string uuid) return null;
            string name = header.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string description = header.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            if (lang(name) is string ln) name = ln;
            if (lang(description) is string ld) description = ld;
            int[] version = header.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Select(x => x.TryGetInt32(out int i) ? i : 0).ToArray() : [1, 0, 0];
            var modules = doc.RootElement.TryGetProperty("modules", out var m) && m.ValueKind == JsonValueKind.Array
                ? m.EnumerateArray().Select(x => x.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "").ToList() : [];
            return new BedrockPackInfo(uuid, name, description, version, modules, folder);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>A localisation key's text from a .lang file ("key=text" lines), or null.</summary>
    static string? Lang(string file, string key)
    {
        if (key.Length == 0 || key.Contains(' ') || !File.Exists(file) || new FileInfo(file).Length > (1 << 20)) return null;
        foreach (var line in File.ReadLines(file))
            if (line.StartsWith(key + "=", StringComparison.Ordinal)) return line[(key.Length + 1)..].Split('\t', '#')[0].Trim();
        return null;
    }

    static WorldPackKind? KindOf(BedrockPackInfo info) =>
        info.Modules.Contains("resources") ? WorldPackKind.ResourcePack
        : info.Modules.Any(m => m is "data" or "script" or "javascript") ? WorldPackKind.BehaviorPack
        : null;

    static List<WorldPack> InstallBedrock(BedrockWorld b, string path, string removedFolder)
    {
        var installed = new List<(WorldPackKind Kind, string Uuid)>();
        if (Directory.Exists(path))
        {
            string manifest = Path.Combine(path, "manifest.json");
            var info = File.Exists(manifest) ? ParseManifest(File.ReadAllBytes(manifest), k => Lang(Path.Combine(path, "texts", "en_US.lang"), k), path) : null;
            if (info == null) throw new InvalidDataException("That folder is not a Bedrock pack: it has no manifest.json. (Java packs, with a pack.mcmeta, go into Java worlds.)");
            var kind = KindOf(info) ?? throw new InvalidDataException("That pack is neither a behavior pack nor a resource pack (skin packs and world templates are not added to worlds).");
            PutPack(b, kind, info, removedFolder, temp => CopyFolder(path, temp));
            installed.Add((kind, info.Uuid));
        }
        else
        {
            using var zip = OpenZip(path);
            var roots = ManifestRoots(zip);
            if (roots.Count == 0) throw new InvalidDataException("That file holds no Bedrock pack (no manifest.json). (Java packs, with a pack.mcmeta, go into Java worlds.)");
            foreach (var root in roots)
            {
                var entry = zip.Entries.First(e => e.FullName.Replace('\\', '/') == root + "manifest.json");
                if (entry.Length > (1 << 20)) continue;
                byte[] json;
                using (var s = entry.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); json = ms.ToArray(); }
                var info = ParseManifest(json, k => ZipLang(zip, root, k), "");
                if (info == null || KindOf(info) is not WorldPackKind kind) continue;   // skin packs and world templates are not world packs
                PutPack(b, kind, info, removedFolder, temp => Unpack(zip, root, temp));
                installed.Add((kind, info.Uuid));
            }
            if (installed.Count == 0) throw new InvalidDataException("That file holds no behavior or resource pack (skin packs and world templates are not added to worlds).");
        }
        // Turn each on, at the top (highest priority), as the game does with a newly added pack.
        foreach (var (kind, uuid) in installed)
        {
            var list = ReadPackList(ListFile(b, kind));
            RemoveEntry(list, uuid);
            var info = ScanPacks(PackFolder(b, kind))[uuid];
            list.Insert(0, Entry(uuid, info.Version));
            WritePackList(ListFile(b, kind), list);
        }
        var now = ReadBedrock(b);
        return installed.Select(i => now.First(p => p.Kind == i.Kind && p.Id.Equals(i.Uuid, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>
    /// Unpacks a pack into a fresh folder beside its final place (so a failure leaves the world as it was), puts an
    /// older copy of the same pack aside, and moves the new one in. Returns its folder.
    /// </summary>
    static string PutPack(BedrockWorld b, WorldPackKind kind, BedrockPackInfo info, string removedFolder, Action<string> fill)
    {
        string parent = PackFolder(b, kind);
        Directory.CreateDirectory(parent);
        string temp = Path.Combine(parent, ".installing-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            fill(temp);
            foreach (var (uuid, old) in ScanPacks(parent))
                if (uuid.Equals(info.Uuid, StringComparison.OrdinalIgnoreCase)) MoveAside(old.Folder, removedFolder);
            string target = Unique(parent, SafeName.For(info.Name.Length > 0 ? info.Name : info.Uuid, "pack"));
            Directory.Move(temp, target);
            return target;
        }
        catch
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>Where each pack starts inside an archive: every manifest.json not inside another pack's folder.</summary>
    static List<string> ManifestRoots(ZipArchive zip)
    {
        var roots = zip.Entries.Select(e => e.FullName.Replace('\\', '/'))
            .Where(n => n == "manifest.json" || n.EndsWith("/manifest.json", StringComparison.Ordinal))
            .Select(n => n[..^"manifest.json".Length])
            .OrderBy(r => r.Length).ToList();
        var kept = new List<string>();
        foreach (var r in roots) if (!kept.Any(k => r.StartsWith(k, StringComparison.Ordinal))) kept.Add(r);
        return kept;
    }

    static string? ZipLang(ZipArchive zip, string root, string key)
    {
        if (key.Length == 0 || key.Contains(' ')) return null;
        if (zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/') == root + "texts/en_US.lang") is not { Length: < (1 << 20) } e) return null;
        using var r = new StreamReader(e.Open());
        string? line;
        while ((line = r.ReadLine()) != null)
            if (line.StartsWith(key + "=", StringComparison.Ordinal)) return line[(key.Length + 1)..].Split('\t', '#')[0].Trim();
        return null;
    }

    static void SetActiveBedrock(BedrockWorld b, WorldPack pack, bool active)
    {
        string file = ListFile(b, pack.Kind);
        var list = ReadPackList(file);
        bool listed = IndexOf(list, pack.Id) >= 0;
        if (active == listed) return;
        if (active) list.Insert(0, Entry(pack.Id, ParseVersion(pack.Version)));
        else RemoveEntry(list, pack.Id);
        WritePackList(file, list);
    }

    /// <summary>Bedrock applies the first listed pack on top: a positive delta moves the pack up (higher priority).</summary>
    static void MoveBedrock(BedrockWorld b, WorldPack pack, int delta)
    {
        string file = ListFile(b, pack.Kind);
        var list = ReadPackList(file);
        int i = IndexOf(list, pack.Id);
        if (i < 0) throw new InvalidOperationException("Turn the pack on first; only turned-on packs have an order.");
        int to = Math.Clamp(i - delta, 0, list.Count - 1);
        if (to == i) return;
        var node = list[i];
        list.RemoveAt(i);
        list.Insert(to, node);
        WritePackList(file, list);
    }

    static string? UninstallBedrock(BedrockWorld b, WorldPack pack, string removedFolder)
    {
        string file = ListFile(b, pack.Kind);
        var list = ReadPackList(file);
        if (RemoveEntry(list, pack.Id)) WritePackList(file, list);
        if (!pack.InWorld || pack.Location == null || !Directory.Exists(pack.Location)) return null;
        string folder = Path.GetFullPath(PackFolder(b, pack.Kind)) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(pack.Location).StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That pack is not in this world's own pack folder.");
        return MoveAside(pack.Location, removedFolder);
    }

    static JsonObject Entry(string uuid, int[] version) => new()
    {
        ["pack_id"] = uuid,
        ["version"] = new JsonArray(version.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()),
    };

    static int IndexOf(JsonArray list, string uuid)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] is JsonObject o && Text(o["pack_id"]).Equals(uuid, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    static bool RemoveEntry(JsonArray list, string uuid)
    {
        bool removed = false;
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i] is JsonObject o && Text(o["pack_id"]).Equals(uuid, StringComparison.OrdinalIgnoreCase)) { list.RemoveAt(i); removed = true; }
        return removed;
    }

    static string Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    static string VersionText(JsonNode? node) => node is JsonArray a ? string.Join(".", a.Select(x => x?.ToString() ?? "0")) : "";
    static int[] ParseVersion(string text)
    {
        var parts = text.Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();
        return parts.Length >= 3 ? parts : [1, 0, 0];
    }
}
