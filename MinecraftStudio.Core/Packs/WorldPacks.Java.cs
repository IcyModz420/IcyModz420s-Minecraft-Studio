// MinecraftStudio.Core/Packs/WorldPacks.Java.cs: a Java Edition (PC) world's data packs and world resource pack.
// Owns: reading datapacks/ against level.dat's DataPacks lists (Enabled / Disabled), installing a data pack (a zip
// copied as it is; a folder or a zip with the pack in a subfolder unpacked as a folder) or a resource pack (as the
// world's resources.zip), turning data packs on and off, their order, and uninstalling. level.dat is written through
// JavaWorld.SaveLevelDat, which keeps the previous one as level.dat_old like the game does.
using System.IO.Compression;
using System.Text.Json;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Packs;

public static partial class WorldPacks
{
    /// <summary>The first data version with data packs (snapshot 17w43a, on the way to 1.13).</summary>
    const int FirstDataPackVersion = 1444;
    const string ResourcesZip = "resources.zip";

    static List<WorldPack> ReadJava(JavaWorld j) => ReadJava(j.Folder, j.Data, j.DataVersion);

    /// <summary>A Java world's packs from its folder and its level.dat's Data compound.</summary>
    static List<WorldPack> ReadJava(string worldFolder, NbtCompound? data, int? dataVersion)
    {
        var list = new List<WorldPack>();
        var dp = data?.GetCompound("DataPacks");
        var enabled = Strings(dp?.GetList("Enabled"));
        var disabled = Strings(dp?.GetList("Disabled"));
        var files = DataPackFiles(worldFolder);
        if (dp != null || (dataVersion ?? 0) >= FirstDataPackVersion || files.Count > 0)
            foreach (var id in enabled.Concat(disabled).Concat(files.Keys).Distinct(StringComparer.Ordinal))
            {
                bool builtIn = !id.StartsWith("file/", StringComparison.Ordinal);
                files.TryGetValue(id, out var path);
                var (description, format) = path != null ? ReadMcmeta(path) : ("", "");
                bool listed = enabled.Contains(id) || disabled.Contains(id);
                list.Add(new WorldPack
                {
                    Kind = WorldPackKind.DataPack,
                    Id = id,
                    Name = builtIn ? BuiltInName(id) : id["file/".Length..],
                    Description = builtIn ? "Built into the game" : description,
                    Version = format,
                    Location = path,
                    // A pack put into datapacks/ but not listed yet is turned on by the game when the world next opens.
                    Active = enabled.Contains(id) || (!listed && path != null),
                    BuiltIn = builtIn,
                    InWorld = path != null,
                    Where = builtIn ? "built into the game"
                        : path == null ? "missing — level.dat lists it, but it is not in the datapacks folder"
                        : listed ? "in this world's datapacks folder"
                        : "in this world's datapacks folder (turns on when the world next opens)",
                });
            }
        string res = Path.Combine(worldFolder, ResourcesZip);
        if (File.Exists(res))
        {
            var (description, format) = ReadMcmeta(res);
            list.Add(new WorldPack
            {
                Kind = WorldPackKind.WorldResourcePack, Id = ResourcesZip, Name = "World resource pack (resources.zip)",
                Description = description, Version = format, Location = res, Active = true, InWorld = true,
                Where = "this world's resources.zip — the game offers it when the world is opened",
            });
        }
        return list;
    }

    /// <summary>Data packs in the world's datapacks folder: zips, and folders with a pack.mcmeta (id → path).</summary>
    static Dictionary<string, string> DataPackFiles(string worldFolder)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        string folder = Path.Combine(worldFolder, "datapacks");
        if (!Directory.Exists(folder)) return files;
        foreach (var f in Directory.EnumerateFiles(folder, "*.zip")) files["file/" + Path.GetFileName(f)] = f;
        foreach (var d in Directory.EnumerateDirectories(folder))
            if (!Path.GetFileName(d).StartsWith('.') && File.Exists(Path.Combine(d, "pack.mcmeta"))) files["file/" + Path.GetFileName(d)] = d;
        return files;
    }

    static string BuiltInName(string id) => id switch
    {
        "vanilla" => "Vanilla (the game's own data)",
        "fabric" => "Fabric (mod loader)",
        _ => "Built-in: " + id.Replace('_', ' '),
    };

    /// <summary>A Java pack's description and pack format from its pack.mcmeta ("" when unreadable).</summary>
    static (string Description, string Format) ReadMcmeta(string pathOrZip)
    {
        try
        {
            byte[]? json = null;
            if (Directory.Exists(pathOrZip))
            {
                string f = Path.Combine(pathOrZip, "pack.mcmeta");
                if (File.Exists(f) && new FileInfo(f).Length < (1 << 20)) json = File.ReadAllBytes(f);
            }
            else
            {
                using var zip = ZipFile.OpenRead(pathOrZip);
                if (zip.GetEntry("pack.mcmeta") is { Length: < (1 << 20) } e)
                {
                    using var s = e.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    json = ms.ToArray();
                }
            }
            if (json == null) return ("", "");
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty("pack", out var pack)) return ("", "");
            string description = pack.TryGetProperty("description", out var d)
                ? d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : TextComponents.ToPlain(d.GetRawText())
                : "";
            string format = pack.TryGetProperty("pack_format", out var pf) ? pf.ToString() : "";
            return (description, format);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { return ("", ""); }
    }

    /// <summary>What a Java pack is: whether it has pack.mcmeta, data/ (data pack) or assets/ (resource pack), and where inside a zip it starts.</summary>
    static (bool HasMcmeta, bool IsData, bool IsResource, string Root, bool IsZip) JavaShape(string path)
    {
        if (Directory.Exists(path))
            return (File.Exists(Path.Combine(path, "pack.mcmeta")), Directory.Exists(Path.Combine(path, "data")), Directory.Exists(Path.Combine(path, "assets")), "", false);
        using var zip = OpenZip(path);
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        string root = names.Contains("pack.mcmeta") ? "" : names.Where(n => n.Count(c => c == '/') == 1 && n.EndsWith("/pack.mcmeta", StringComparison.Ordinal)).ToList() is [var only] ? only[..^"pack.mcmeta".Length] : "";
        bool hasMcmeta = names.Contains(root + "pack.mcmeta");
        return (hasMcmeta, names.Any(n => n.StartsWith(root + "data/", StringComparison.Ordinal)), names.Any(n => n.StartsWith(root + "assets/", StringComparison.Ordinal)), root, true);
    }

    static ZipArchive OpenZip(string path)
    {
        try { return ZipFile.OpenRead(path); }
        catch (InvalidDataException) { throw new InvalidDataException("That file is not a pack (it is not a zip archive)."); }
    }

    static List<WorldPack> InstallJava(JavaWorld j, string path, string removedFolder)
    {
        var shape = JavaShape(path);
        if (!shape.HasMcmeta) throw new InvalidDataException("That is not a Java Edition pack: it has no pack.mcmeta. (Bedrock packs, .mcpack and .mcaddon, go into Bedrock worlds.)");
        if (shape.IsResource && !shape.IsData) return [InstallWorldResourcePack(j, path, shape, removedFolder)];
        if (!shape.IsData) throw new InvalidDataException("That pack has neither a data folder (a data pack) nor an assets folder (a resource pack).");
        if (j.DataVersion is int dv && dv > 0 && dv < FirstDataPackVersion)
            throw new InvalidOperationException($"Data packs need Java Edition 1.13 or newer; this world is {j.VersionName ?? "older"}.");

        string folder = Path.Combine(j.Folder, "datapacks");
        Directory.CreateDirectory(folder);
        bool asZip = shape.IsZip && shape.Root.Length == 0;
        string name = SafeName.For(Path.GetFileName(path.TrimEnd('\\', '/')), "datapack");
        if (asZip && !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name += ".zip";
        if (!asZip && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        string target = Path.Combine(folder, name);
        string temp = Path.Combine(folder, ".installing-" + Guid.NewGuid().ToString("N")[..8] + (asZip ? ".zip" : ""));
        try
        {
            if (asZip)
            {
                File.Copy(path, temp);
                if (new FileInfo(temp).Length != new FileInfo(path).Length) throw new IOException("The pack was not copied completely: the disk may be full.");
            }
            else if (shape.IsZip) { using var zip = OpenZip(path); Unpack(zip, shape.Root, temp); }
            else CopyFolder(path, temp);
            // The same pack again (an update): the old copy goes aside, not away.
            if (File.Exists(target) || Directory.Exists(target)) MoveAside(target, removedFolder);
            if (asZip) File.Move(temp, target); else Directory.Move(temp, target);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (IOException) { }
            throw;
        }
        string id = "file/" + name;
        var dp = DataPacks(j);
        RemoveFromLists(dp, id);
        dp.GetList("Enabled")!.Add(new NbtString(id));   // last in the list = applied on top
        j.SaveLevelDat();
        return [ReadJava(j).First(p => p.Id == id)];
    }

    /// <summary>A resource pack becomes the world's resources.zip; a folder or a nested zip is repacked so pack.mcmeta is at its top.</summary>
    static WorldPack InstallWorldResourcePack(JavaWorld j, string path, (bool HasMcmeta, bool IsData, bool IsResource, string Root, bool IsZip) shape, string removedFolder)
    {
        string target = Path.Combine(j.Folder, ResourcesZip);
        string temp = target + ".partial";
        if (File.Exists(temp)) File.Delete(temp);
        if (shape.IsZip && shape.Root.Length == 0) File.Copy(path, temp);
        else
        {
            string stage = Path.Combine(Path.GetTempPath(), "MinecraftStudio-pack-" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                if (shape.IsZip) { using var zip = OpenZip(path); Unpack(zip, shape.Root, stage); }
                else CopyFolder(path, stage);
                ZipFile.CreateFromDirectory(stage, temp, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            finally { try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (IOException) { } }
        }
        if (File.Exists(target)) MoveAside(target, removedFolder);
        File.Move(temp, target);
        return ReadJava(j).First(p => p.Kind == WorldPackKind.WorldResourcePack);
    }

    static void SetActiveJava(JavaWorld j, WorldPack pack, bool active)
    {
        if (pack.Kind != WorldPackKind.DataPack) throw new InvalidOperationException("The world resource pack is offered whenever it is there; uninstall it to stop that.");
        if (!active && pack.Id == "vanilla") throw new InvalidOperationException("The vanilla data pack is the game itself and has to stay on.");
        var dp = DataPacks(j);
        RemoveFromLists(dp, pack.Id);
        dp.GetList(active ? "Enabled" : "Disabled")!.Add(new NbtString(pack.Id));
        j.SaveLevelDat();
    }

    /// <summary>Java applies data packs in list order, the last on top: a positive delta moves the pack later (higher priority).</summary>
    static void MoveJava(JavaWorld j, WorldPack pack, int delta)
    {
        if (pack.Id == "vanilla") throw new InvalidOperationException("The vanilla data pack always comes first.");
        var enabled = DataPacks(j).GetList("Enabled")!;
        int i = IndexOf(enabled, pack.Id);
        if (i < 0) throw new InvalidOperationException("Turn the pack on first; only turned-on packs have an order.");
        int floor = IndexOf(enabled, "vanilla") + 1;
        int to = Math.Clamp(i + delta, floor, enabled.Count - 1);
        if (to == i) return;
        var tag = enabled[i];
        enabled.RemoveAt(i);
        enabled.Insert(to, tag);
        j.SaveLevelDat();
    }

    static string? UninstallJava(JavaWorld j, WorldPack pack, string removedFolder)
    {
        if (pack.Kind == WorldPackKind.WorldResourcePack)
        {
            string res = Path.Combine(j.Folder, ResourcesZip);
            return File.Exists(res) ? MoveAside(res, removedFolder) : null;
        }
        string? moved = null;
        if (pack.Location != null && (File.Exists(pack.Location) || Directory.Exists(pack.Location)))
        {
            // Only ever a pack inside this world's datapacks folder.
            string folder = Path.GetFullPath(Path.Combine(j.Folder, "datapacks")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(pack.Location).StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("That pack is not in this world's datapacks folder.");
            moved = MoveAside(pack.Location, removedFolder);
        }
        if (j.Data?.GetCompound("DataPacks") is NbtCompound dp)
        {
            RemoveFromLists(dp, pack.Id);
            j.SaveLevelDat();
        }
        return moved;
    }

    /// <summary>level.dat's DataPacks compound (made with vanilla on when a world has none yet).</summary>
    static NbtCompound DataPacks(JavaWorld j)
    {
        var data = j.Data ?? throw new InvalidOperationException("The world has no level.dat data.");
        if (data.GetCompound("DataPacks") is not NbtCompound dp)
            data["DataPacks"] = dp = new NbtCompound { ["Enabled"] = new NbtList(NbtType.String) { new NbtString("vanilla") }, ["Disabled"] = new NbtList(NbtType.String) };
        if (dp.GetList("Enabled") == null) dp["Enabled"] = new NbtList(NbtType.String);
        if (dp.GetList("Disabled") == null) dp["Disabled"] = new NbtList(NbtType.String);
        return dp;
    }

    static void RemoveFromLists(NbtCompound dp, string id)
    {
        foreach (var name in new[] { "Enabled", "Disabled" })
            if (dp.GetList(name) is NbtList list)
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i] is NbtString s && s.Value == id) list.RemoveAt(i);
    }

    static int IndexOf(NbtList list, string id)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] is NbtString s && s.Value == id) return i;
        return -1;
    }

    static List<string> Strings(NbtList? list) => list?.OfType<NbtString>().Select(s => s.Value).ToList() ?? [];
}
