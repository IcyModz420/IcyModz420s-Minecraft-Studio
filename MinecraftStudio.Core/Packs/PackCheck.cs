// MinecraftStudio.Core/Packs/PackCheck.cs: what the game would ignore or show wrong in a resource pack, found before the
// pack is used — for pack makers. Only reads the pack.
// Owns: PackSeverity, PackFinding, PackReport, PackCheck. Java resource packs: where pack.mcmeta is (the game sees only one
// at the top of the zip), its pack format and the versions that read it, folder names for that version (block / blocks),
// names the game refuses (capital letters, spaces), PNGs that do not open, textures neither square nor animated, sizes
// that are not powers of two, broken JSON (models, blockstates, animations, sounds, languages), frames an animation asks
// for that the picture lacks, and texture names no version of the game uses. Bedrock resource packs: manifest.json (where
// it is, format, uuids — valid and not repeated, version numbers, a module), the icon, the texture lists and what they
// point at, JSON (comments allowed, as in the game), pictures that do not open, names with capitals (phones are strict).
using System.Text.Json;
using System.Text.RegularExpressions;
using MinecraftStudio.Core.Imaging;

namespace MinecraftStudio.Core.Packs;

public enum PackSeverity { Problem, Warning, Note }

public sealed record PackFinding(PackSeverity Severity, string Message, string? File = null);

public sealed class PackReport
{
    public string Kind { get; set; } = "";
    public int Files { get; set; }
    public List<PackFinding> Findings { get; } = [];
    public int Problems => Findings.Count(f => f.Severity == PackSeverity.Problem);
    public int Warnings => Findings.Count(f => f.Severity == PackSeverity.Warning);

    /// <summary>The report as text: what the pack is, then problems, warnings and notes.</summary>
    public string Text()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Kind);
        sb.AppendLine($"{Files:N0} files. {(Problems == 0 && Warnings == 0 ? "Nothing wrong was found." : $"{Problems} problem(s), {Warnings} warning(s).")}");
        foreach (var (severity, title) in new[] { (PackSeverity.Problem, "PROBLEMS — the game ignores or rejects these"), (PackSeverity.Warning, "WARNINGS — these may look wrong"), (PackSeverity.Note, "NOTES") })
        {
            var list = Findings.Where(f => f.Severity == severity).ToList();
            if (list.Count == 0) continue;
            sb.AppendLine().AppendLine(title);
            foreach (var f in list) sb.AppendLine("• " + f.Message + (f.File != null ? $"  ({f.File})" : ""));
        }
        return sb.ToString();
    }
}

public static partial class PackCheck
{
    static readonly JsonDocumentOptions Strict = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    // Java resource locations: lower-case letters, digits and _ - . (since 1.11).
    static readonly Regex JavaName = new("^[a-z0-9_.\\-]+$", RegexOptions.Compiled);

    /// <summary>Checks a pack folder or file (.zip, .mcpack).</summary>
    public static PackReport Run(string path)
    {
        var report = new PackReport();
        var notes = new List<string>();
        var files = TexturePackReader.Files(path, null, notes);
        report.Files = files.Count;
        foreach (var n in notes) report.Findings.Add(new(PackSeverity.Problem, n));
        string? mcmeta = Shallowest(files.Keys, "pack.mcmeta"), manifest = Shallowest(files.Keys, "manifest.json");
        if (manifest != null && (mcmeta == null || Depth(manifest) <= Depth(mcmeta))) Bedrock(files, manifest, report);
        else if (mcmeta != null) Java(files, mcmeta, report);
        else { report.Kind = "Not a resource pack"; report.Findings.Add(new(PackSeverity.Problem, "There is no pack.mcmeta (Java) or manifest.json (Bedrock) in it, so the game does not see a pack.")); }
        return report;
    }

    static int Depth(string p) => p.Count(c => c == '/');
    static string Dir(string p) => p[..(p.LastIndexOf('/') + 1)];
    static string? Shallowest(IEnumerable<string> paths, string name) =>
        paths.Where(p => p.Equals(name, StringComparison.OrdinalIgnoreCase) || p.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase)).OrderBy(Depth).FirstOrDefault();

    static JsonDocument? Json(Dictionary<string, Func<byte[]>> files, string path, PackReport r, bool comments)
    {
        try
        {
            var bytes = files[path]();
            // A byte-order mark is fine; JsonDocument wants it gone.
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return JsonDocument.Parse(bytes.AsMemory(start), comments ? Strict : new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            r.Findings.Add(new(PackSeverity.Problem, $"Broken JSON (line {e.LineNumber + 1}): the game cannot read it", path));
            return null;
        }
    }

    static void Group(PackReport r, PackSeverity s, string message, List<string> paths)
    {
        if (paths.Count == 0) return;
        string examples = string.Join(", ", paths.Take(6)) + (paths.Count > 6 ? $" and {paths.Count - 6} more" : "");
        r.Findings.Add(new(s, $"{message} ({paths.Count}: {examples})"));
    }

    // ---------------------------------------------------------------- Java

    static void Java(Dictionary<string, Func<byte[]>> files, string mcmeta, PackReport r)
    {
        string root = Dir(mcmeta);
        if (root.Length > 0)
            r.Findings.Add(new(PackSeverity.Problem, $"pack.mcmeta is inside \"{root.TrimEnd('/')}\": the game only sees a pack whose pack.mcmeta is at the top (zip the pack's contents, not the folder they are in)."));
        int format = -1;
        using (var doc = Json(files, mcmeta, r, comments: false))
        {
            if (doc != null)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("pack", out var pack))
                    r.Findings.Add(new(PackSeverity.Problem, "pack.mcmeta has no \"pack\" section.", mcmeta));
                else
                {
                    if (pack.TryGetProperty("pack_format", out var pf) && pf.TryGetInt32(out int f)) format = f;
                    else if (pack.TryGetProperty("min_format", out var mf)) format = mf.ValueKind == JsonValueKind.Array ? mf[0].GetInt32() : mf.TryGetInt32(out int m) ? m : -1;
                    if (format < 0) r.Findings.Add(new(PackSeverity.Problem, "pack.mcmeta says no pack_format, so no version takes the pack.", mcmeta));
                    if (!pack.TryGetProperty("description", out _)) r.Findings.Add(new(PackSeverity.Warning, "pack.mcmeta has no description (the pack list shows it blank).", mcmeta));
                }
            }
        }
        var assets = files.Keys.Where(k => k.StartsWith(root + "assets/", StringComparison.OrdinalIgnoreCase)).Select(k => k[root.Length..]).ToList();
        var data = files.Keys.Where(k => k.StartsWith(root + "data/", StringComparison.OrdinalIgnoreCase)).Select(k => k[root.Length..]).ToList();
        // A data pack (data/ and no assets/) numbers its formats differently: its version is not named from the table.
        bool dataPack = data.Count > 0 && assets.Count == 0;
        List<JavaRelease> releases = dataPack ? [] : JavaReleases.All.Where(x => x.Kind == JavaPackKind.ResourcePack && x.Major == format).ToList();
        string what = dataPack ? "Java data pack" : "Java resource pack";
        r.Kind = format < 0 ? $"{what} (no version)"
            : releases.Count > 0 ? $"{what}, pack format {format}: made for {string.Join(", ", releases.Select(x => x.Label))}"
            : $"{what}, pack format {format}";
        if (format >= 0 && releases.Count == 0 && !dataPack)
            r.Findings.Add(new(PackSeverity.Warning, $"Pack format {format} is not one a release of the game uses (the newest known is {JavaReleases.Newest.FormatText}): the game lists it as made for another version."));
        bool flattened = format is < 0 or >= 4;

        if (assets.Count == 0 && data.Count == 0) r.Findings.Add(new(PackSeverity.Warning, "There is no assets folder (or data folder): the pack changes nothing."));

        // Names the game refuses (system clutter such as Thumbs.db is only noted).
        static bool Clutter(string a) => Path.GetFileName(a) is "Thumbs.db" or ".DS_Store" or "desktop.ini" || a.Contains("/__MACOSX/");
        var both = assets.Concat(data).ToList();
        var badNames = both.Where(a => !Clutter(a) && a.Split('/').Skip(1).Any(part => !JavaName.IsMatch(part))).ToList();
        Group(r, PackSeverity.Problem, "Names with capital letters, spaces or other signs: the game (1.11 and later) cannot load these", badNames);
        Group(r, PackSeverity.Note, "System files that do not belong in a pack (harmless; the game skips them)", both.Where(Clutter).ToList());
        // A data pack's recipes, loot tables, advancements, tags … must be JSON the game can read.
        foreach (var d in data.Where(d => d.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            using (Json(files, root + d, r, comments: false)) { }
        if (dataPack) DataPack(files, root, format, data, r);

        // Folders of the wrong era for the version.
        var oldFolders = assets.Where(a => a.StartsWith("assets/minecraft/textures/blocks/", StringComparison.OrdinalIgnoreCase) || a.StartsWith("assets/minecraft/textures/items/", StringComparison.OrdinalIgnoreCase)).ToList();
        var newFolders = assets.Where(a => a.StartsWith("assets/minecraft/textures/block/", StringComparison.OrdinalIgnoreCase) || a.StartsWith("assets/minecraft/textures/item/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (flattened) Group(r, PackSeverity.Problem, "In textures/blocks or textures/items, the folders before 1.13: from 1.13 the game reads textures/block and textures/item, so these are ignored", oldFolders);
        else Group(r, PackSeverity.Problem, "In textures/block or textures/item, the folders of 1.13 and later: this pack's version reads textures/blocks and textures/items, so these are ignored", newFolders);

        // Pictures.
        var notPng = new List<string>();
        var squashed = new List<string>();
        var notPower = new List<string>();
        var huge = new List<string>();
        var unknown = new List<string>();
        foreach (var a in assets.Where(a => a.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && a.Contains("/textures/", StringComparison.OrdinalIgnoreCase)))
        {
            (int W, int H) size;
            try { size = Png.Size(files[root + a]()); }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException or IOException) { notPng.Add(a); continue; }
            if (size.W <= 0 || size.H <= 0) { notPng.Add(a); continue; }
            bool blockOrItem = a.Contains("/textures/block/") || a.Contains("/textures/item/") || a.Contains("/textures/blocks/") || a.Contains("/textures/items/");
            string meta = root + a + ".mcmeta";
            // A .png.mcmeta may hold only other settings (blur, gui scaling): animated is when it has an animation section.
            bool animated = blockOrItem && files.ContainsKey(meta) && Animation(files, meta, size, r);
            if (blockOrItem)
            {
                if (size.W != size.H && !animated) squashed.Add($"{a} ({size.W} × {size.H})");
                if ((size.W & (size.W - 1)) != 0) notPower.Add($"{a} ({size.W} wide)");
            }
            if (size.W > 1024 || size.H > 4096) huge.Add($"{a} ({size.W} × {size.H})");
            if (a.StartsWith("assets/minecraft/textures/block/") || a.StartsWith("assets/minecraft/textures/item/"))
            {
                string name = a["assets/minecraft/textures/".Length..^4];
                if (!TextureNames.Default.IsKnownJava(name)) unknown.Add(a);
            }
        }
        Group(r, PackSeverity.Problem, "Not pictures the game can open (only PNG is read)", notPng);
        Group(r, PackSeverity.Warning, "Block and item textures neither square nor animated (no .png.mcmeta): the game stretches them", squashed);
        Group(r, PackSeverity.Warning, "Widths that are not a power of two (16, 32, 64 …): mipmapping then blurs them, or every texture", notPower);
        Group(r, PackSeverity.Note, "Very large pictures (they take a lot of video memory)", huge);
        Group(r, PackSeverity.Note, "Names no version of the game uses for a block or item texture (a typo, or a mod's or newer version's texture)", unknown);

        // Every JSON file the game reads.
        foreach (var a in assets.Where(a => a.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".mcmeta", StringComparison.OrdinalIgnoreCase)))
            if (!a.EndsWith(".png.mcmeta", StringComparison.OrdinalIgnoreCase)) using (Json(files, root + a, r, comments: false)) { }

        if (!files.ContainsKey(root + "pack.png")) r.Findings.Add(new(PackSeverity.Note, "No pack.png: the pack shows without a picture in the game's list."));
        else
            try
            {
                var (w, h) = Png.Size(files[root + "pack.png"]());
                if (w != h) r.Findings.Add(new(PackSeverity.Warning, $"pack.png is {w} × {h}: the game shows it squeezed into a square.", "pack.png"));
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException) { r.Findings.Add(new(PackSeverity.Problem, "pack.png is not a PNG the game can open.", "pack.png")); }
    }

    // A texture's .png.mcmeta: readable, and an animation in it asks only for frames the picture has. True when it animates.
    static bool Animation(Dictionary<string, Func<byte[]>> files, string meta, (int W, int H) size, PackReport r)
    {
        using var doc = Json(files, meta, r, comments: false);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("animation", out var anim)) return false;
        int fw = anim.TryGetProperty("width", out var wv) && wv.TryGetInt32(out int w) ? w : Math.Min(size.W, size.H);
        int fh = anim.TryGetProperty("height", out var hv) && hv.TryGetInt32(out int h) ? h : fw;
        if (fw <= 0 || fh <= 0 || size.W % fw != 0 || size.H % fh != 0)
        {
            r.Findings.Add(new(PackSeverity.Problem, $"The picture ({size.W} × {size.H}) is not a whole number of {fw} × {fh} frames: the game shows the missing texture.", meta));
            return true;
        }
        int frames = size.W / fw * (size.H / fh);
        if (anim.TryGetProperty("frames", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var f in list.EnumerateArray())
            {
                int index = f.ValueKind == JsonValueKind.Number ? f.GetInt32() : f.ValueKind == JsonValueKind.Object && f.TryGetProperty("index", out var iv) ? iv.GetInt32() : 0;
                if (index < 0 || index >= frames) { r.Findings.Add(new(PackSeverity.Problem, $"The animation asks for frame {index}, but the picture has {frames} (0 to {frames - 1}).", meta)); break; }
            }
        return true;
    }

    // ---------------------------------------------------------------- Bedrock

    static void Bedrock(Dictionary<string, Func<byte[]>> files, string manifest, PackReport r)
    {
        string root = Dir(manifest);
        r.Kind = "Bedrock pack";
        if (root.Length > 0)
            r.Findings.Add(new(PackSeverity.Problem, $"manifest.json is inside \"{root.TrimEnd('/')}\": Bedrock imports a pack whose manifest.json is at the top of the .mcpack."));
        var uuids = new List<string>();
        using (var doc = Json(files, manifest, r, comments: true))
        {
            if (doc == null) return;
            var m = doc.RootElement;
            int format = m.TryGetProperty("format_version", out var fv) && fv.TryGetInt32(out int f) ? f : -1;
            if (format < 0) r.Findings.Add(new(PackSeverity.Problem, "manifest.json has no format_version.", manifest));
            if (!m.TryGetProperty("header", out var header) || header.ValueKind != JsonValueKind.Object)
            {
                r.Findings.Add(new(PackSeverity.Problem, "manifest.json has no header.", manifest));
                return;
            }
            string name = header.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            r.Kind = $"Bedrock pack \"{name}\"";
            Uuid(header, "header", manifest, r, uuids);
            Version(header, "version", "the pack's version", manifest, r, required: true);
            if (format >= 2) Version(header, "min_engine_version", "the oldest game version it needs (min_engine_version)", manifest, r, required: true);
            if (!m.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array || modules.GetArrayLength() == 0)
                r.Findings.Add(new(PackSeverity.Problem, "manifest.json has no modules: the game does not know what the pack is.", manifest));
            else
            {
                var types = new List<string>();
                foreach (var mod in modules.EnumerateArray())
                {
                    types.Add(mod.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "");
                    Uuid(mod, "module", manifest, r, uuids);
                    Version(mod, "version", "a module's version", manifest, r, required: true);
                }
                r.Kind += $" ({string.Join(", ", types.Where(t => t.Length > 0))})";
            }
        }
        var dupes = uuids.GroupBy(u => u, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        foreach (var d in dupes) r.Findings.Add(new(PackSeverity.Problem, $"The uuid {d} is used twice: every pack and module needs its own.", manifest));

        var inside = files.Keys.Where(k => k.StartsWith(root, StringComparison.OrdinalIgnoreCase)).Select(k => k[root.Length..]).ToList();
        if (!inside.Contains("pack_icon.png", StringComparer.OrdinalIgnoreCase)) r.Findings.Add(new(PackSeverity.Note, "No pack_icon.png: the pack shows without a picture."));
        // Block, item and mob textures are named in lower case wherever they are used; phones and consoles read names exactly
        // (the game's own language, material and menu files have capitals, and are fine).
        Group(r, PackSeverity.Warning, "Textures with capital letters in their names: phones and consoles read names exactly, and they are named in lower case where used",
            inside.Where(p => (p.StartsWith("textures/blocks/", StringComparison.OrdinalIgnoreCase) || p.StartsWith("textures/items/", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("textures/entity/", StringComparison.OrdinalIgnoreCase)) && p.Any(char.IsUpper)).ToList());

        var pictures = new HashSet<string>(inside.Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[..^4]), StringComparer.OrdinalIgnoreCase);
        var broken = new List<string>();
        foreach (var p in inside.Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            try { _ = Png.Size(files[root + p]()); }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException) { broken.Add(p); }
        Group(r, PackSeverity.Problem, "Pictures the game cannot open", broken);

        foreach (var p in inside.Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !p.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
            using (Json(files, root + p, r, comments: true)) { }

        // The texture lists: what they point at is in the pack, or one of the game's own textures.
        foreach (var list in new[] { "textures/terrain_texture.json", "textures/item_texture.json" })
        {
            string? key = inside.FirstOrDefault(p => p.Equals(list, StringComparison.OrdinalIgnoreCase));
            if (key == null) continue;
            using var doc = Json(files, root + key, r, comments: true);
            if (doc == null || !doc.RootElement.TryGetProperty("texture_data", out var data) || data.ValueKind != JsonValueKind.Object) continue;
            var missing = new List<string>();
            foreach (var entry in data.EnumerateObject())
                foreach (var target in Targets(entry.Value))
                    if (target.StartsWith("textures/", StringComparison.OrdinalIgnoreCase) && !pictures.Contains(target) && !Builtin(target) && !TextureNames.Default.IsKnownBedrock(target["textures/".Length..]))
                        missing.Add($"{entry.Name} → {target}");
            Group(r, PackSeverity.Warning, $"{list} points at pictures neither in the pack nor among the game's own", missing.Distinct().ToList());
        }
    }

    // Pictures the game has built in (not files of any pack).
    static bool Builtin(string target) => target.Equals("textures/misc/missing_texture", StringComparison.OrdinalIgnoreCase);

    // The texture paths an entry of terrain_texture.json / item_texture.json names (a string, a list, or {"path": …}).
    static IEnumerable<string> Targets(JsonElement entry)
    {
        if (!entry.TryGetProperty("textures", out var t)) yield break;
        foreach (var x in t.ValueKind == JsonValueKind.Array ? t.EnumerateArray().ToList() : [t])
        {
            if (x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 } s) yield return s;
            else if (x.ValueKind == JsonValueKind.Object && x.TryGetProperty("path", out var p) && p.GetString() is { Length: > 0 } ps) yield return ps;
        }
    }

    static void Uuid(JsonElement e, string what, string manifest, PackReport r, List<string> uuids)
    {
        string? u = e.TryGetProperty("uuid", out var v) ? v.GetString() : null;
        if (u == null) { r.Findings.Add(new(PackSeverity.Problem, $"The {what} has no uuid.", manifest)); return; }
        if (!Guid.TryParse(u, out _)) { r.Findings.Add(new(PackSeverity.Problem, $"The {what} uuid \"{u}\" is not a valid uuid.", manifest)); return; }
        uuids.Add(u);
    }

    static void Version(JsonElement e, string key, string what, string manifest, PackReport r, bool required)
    {
        if (!e.TryGetProperty(key, out var v)) { if (required) r.Findings.Add(new(PackSeverity.Problem, $"manifest.json does not say {what}.", manifest)); return; }
        bool ok = v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3 && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number)
            || v.ValueKind == JsonValueKind.String && System.Version.TryParse(v.GetString(), out _);
        if (!ok) r.Findings.Add(new(PackSeverity.Problem, $"{char.ToUpperInvariant(what[0]) + what[1..]} should be three numbers, like [1, 0, 0].", manifest));
    }
}
