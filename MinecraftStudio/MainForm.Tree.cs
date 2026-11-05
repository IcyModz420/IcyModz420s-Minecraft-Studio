// MinecraftStudio/MainForm.Tree.cs: tree for MainForm.
// Part of MainForm (main part in MainForm.cs).
using System.Diagnostics;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;
using MinecraftStudio.Documents;
using MinecraftStudio.Documents.WorldPages;
using MinecraftStudio.Phone;
using MinecraftStudio.Studio;
using MinecraftStudio.UI;

namespace MinecraftStudio;

partial class MainForm
{
    // ------------------------------------------------------------------ explorer tree

    sealed record WorldItem(SaveWorld World, string Kind, object? Extra = null);

    async Task ScanWorlds()
    {
        SetStatus("Looking for worlds…");
        _found = await Task.Run(() => WorldFinder.FindAll(_settings.ExtraWorldFolders));
        PopulateTree();
        RefreshRecentCards();
        SetStatus($"Found {_found.Count} worlds on this PC.");
    }

    internal static string IconFor(WorldKind k) => k switch
    {
        WorldKind.Java => "java",
        WorldKind.JavaFinite => "classic",
        WorldKind.Bedrock => "bedrock",
        WorldKind.PocketLegacy => "pocket",
        WorldKind.ThreeDS => "3ds",
        WorldKind.LegacyConsole => "console",
        _ => "folder",
    };

    internal static string IconFor(SaveWorld w) => w switch
    {
        JavaWorld => "java",
        FiniteWorld => "classic",
        BedrockWorld => "bedrock",
        PocketLegacyWorld => "pocket",
        ThreeDsWorld => "3ds",
        ConsoleWorld => "console",
        _ => "folder",
    };

    static TreeNode Node(string text, string icon, object? tag) => new(text) { ImageKey = icon, SelectedImageKey = icon, Tag = tag };

    void PopulateTree()
    {
        string filter = _treeFilter.Text.Trim();
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        if (_openWorlds.Count > 0)
        {
            var open = Node("Open worlds", "folder", null);
            foreach (var w in _openWorlds) open.Nodes.Add(WorldNode(w));
            _tree.Nodes.Add(open);
            open.Expand();
            foreach (TreeNode n in open.Nodes) n.Expand();
        }
        var pc = Node($"Worlds on this PC ({_found.Count})", "folder", null);
        foreach (var g in _found.GroupBy(f => f.Group))
        {
            var group = Node(g.Key, "folder", null);
            foreach (var f in g.OrderByDescending(f => f.LastWrite))
            {
                if (filter.Length > 0 && !f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) && !f.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                var n = Node(f.Name, IconFor(f.Kind), f);
                n.ToolTipText = $"{f.Path}\n{f.Detail ?? f.Kind.ToString()}\nLast changed {f.LastWrite:g}";
                group.Nodes.Add(n);
            }
            if (group.Nodes.Count > 0) pc.Nodes.Add(group);
        }
        _tree.Nodes.Add(pc);
        pc.Expand();
        foreach (TreeNode g in pc.Nodes) g.Expand();
        _tree.EndUpdate();
        // Expanding scrolls to the last node; start at the top.
        if (_tree.Nodes.Count > 0) _tree.TopNode = _tree.Nodes[0];
    }

    TreeNode WorldNode(SaveWorld w)
    {
        var root = Node(w.Name, IconFor(w), new WorldItem(w, "world"));
        root.ToolTipText = w.Location + "\n" + w.FormatDescription;
        root.Nodes.Add(Node("Overview & tools", "map", new WorldItem(w, "map")));
        if (w.LevelData != null || w is PocketLegacyWorld) root.Nodes.Add(Node("level.dat", "nbt", new WorldItem(w, "level")));
        if (w.Players.Count > 0)
        {
            var players = Node($"Players ({w.Players.Count})", "player", null);
            foreach (var p in w.Players) players.Nodes.Add(Node(p.Name, "player", new WorldItem(w, "player", p)));
            root.Nodes.Add(players);
        }
        var dims = Node("Dimensions", "folder", null);
        foreach (var d in w.Dimensions)
            dims.Nodes.Add(Node($"{d.DisplayName} ({d.ChunkPositions.Count:N0} chunks)", d.Id.Contains("nether") ? "nether" : d.Id.Contains("end") ? "end" : "region", new WorldItem(w, "dim", d)));
        root.Nodes.Add(dims);
        switch (w)
        {
            case JavaWorld jw:
                {
                    var data = Node("Data files", "folder", null);
                    foreach (var f in jw.DataFiles()) data.Nodes.Add(Node(Path.GetRelativePath(Path.Combine(jw.Folder, "data"), f), "nbt", new WorldItem(w, "file", f)));
                    if (data.Nodes.Count > 0) root.Nodes.Add(data);
                    var regions = Node("Region files", "folder", null);
                    foreach (var d in jw.Dimensions.OfType<JavaRegionDimension>())
                    {
                        var dn = Node(d.DisplayName, "region", null);
                        foreach (var f in d.RegionFiles.OrderBy(f => f)) dn.Nodes.Add(Node(Path.GetFileName(f), "region", new WorldItem(w, "region", f)));
                        regions.Nodes.Add(dn);
                    }
                    if (regions.Nodes.Count > 0) root.Nodes.Add(regions);
                    break;
                }
            case BedrockWorld:
                root.Nodes.Add(Node("Database records", "database", new WorldItem(w, "db")));
                break;
            case ConsoleWorld cw:
                {
                    var files = Node($"Save archive ({cw.Archive.Files.Count(f => !f.Blank)} files)", "folder", null);
                    foreach (var f in cw.Archive.Files.Where(f => !f.Blank)) files.Nodes.Add(Node($"{f.Name}  ({f.Data.Length:N0} bytes)", f.Name.EndsWith(".mcr") ? "region" : "nbt", new WorldItem(w, "archive", f)));
                    root.Nodes.Add(files);
                    break;
                }
            case PocketLegacyWorld pw when pw.Entities != null:
                root.Nodes.Add(Node("entities.dat", "nbt", new WorldItem(w, "entities")));
                break;
        }
        return root;
    }

    SaveWorld? SelectedWorld()
    {
        for (var n = _tree.SelectedNode; n != null; n = n.Parent)
            if (n.Tag is WorldItem wi) return wi.World;
        if (_tabs.Selected?.Tag is WorldDocument wd) return wd.World;
        return _openWorlds.Count == 1 ? _openWorlds[0] : null;
    }

    async Task Activate(TreeNode? node)
    {
        switch (node?.Tag)
        {
            case FoundWorld f:
                await OpenPath(f.Path);
                break;
            case WorldItem { Kind: "world" or "map" } wi:
                OpenWorldDocument(wi.World);
                break;
            case WorldItem { Kind: "level" } wi:
                OpenLevelDat(wi.World);
                break;
            case WorldItem { Kind: "player", Extra: PlayerRecord p } wi:
                OpenPlayer(wi.World, p);
                break;
            case WorldItem { Kind: "dim", Extra: WorldDimension d } wi:
                {
                    var doc = OpenWorldDocument(wi.World);
                    doc?.SelectDimension(d);
                    break;
                }
            case WorldItem { Kind: "file", Extra: string path }:
                OpenDocument(new NbtDocument(new FileNbtSource(path)) { Key = path });
                break;
            case WorldItem { Kind: "region", Extra: string path }:
                OpenDocument(new RegionDocument(path) { Key = path });
                break;
            case WorldItem { Kind: "db", World: BedrockWorld bw }:
                OpenDocument(new DatabaseDocument(bw));
                break;
            case WorldItem { Kind: "archive", World: ConsoleWorld cw, Extra: ConsoleArchive.FileEntry entry }:
                OpenArchiveFile(cw, entry);
                break;
            case WorldItem { Kind: "entities", World: PocketLegacyWorld pw }:
                OpenDocument(new NbtDocument(new DelegateNbtSource("entities.dat", () => pw.Entities!.Roots, r => { Backup.WorldBeforeSave(pw); pw.SaveEntities(); }, $"{pw.Folder}\\entities.dat")) { Key = (pw, "entities") });
                break;
        }
    }
}
