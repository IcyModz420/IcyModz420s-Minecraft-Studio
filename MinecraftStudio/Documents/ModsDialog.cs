// MinecraftStudio/Documents/ModsDialog.cs: "Mods and add-ons" — which mods, add-ons and data packs a world needs.
// Owns: ModsDialog. A window beside the world's tab (not modal, like Find) that reads a world of its own off disk
// (WorldMods.Read: every dimension's blocks, block entities, mobs, items and biomes counted by the mod they come from, the
// mod list and number tables Forge keeps in level.dat, the world's own packs) and, for a Java world, the mods on this PC
// (InstalledMods: every launcher instance's mods folder, the world's own game or server folder first). Each mod: how much
// of it, where it comes from, its details below; "Find its things" opens Find with its name pattern, "Show on the map" marks
// its chunks, "Show the mod's file" selects its jar in File Explorer, and the whole report can be copied. Only reads.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class ModsDialog : DarkDialog
{
    readonly WorldDocument _doc;
    readonly Button _read, _stop, _copy, _find, _show, _jar;
    readonly ListView _list;
    readonly TextBox _details;
    readonly Label _summary, _status;
    ModsReport? _report;
    CancellationTokenSource? _cts;

    public ModsDialog(WorldDocument doc) : base("Mods and add-ons in " + doc.World.Name, 900)
    {
        _doc = doc;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = MaximizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(this.Px(900), this.Px(680));
        MinimumSize = new Size(this.Px(620), this.Px(420));

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(new Label
        {
            Text = "Every block, mob, item and biome whose name isn't the game's own, counted by the mod, add-on or data pack it comes from — and, for a Java world, which mods on this PC have them (every launcher's mods folders). The world is only read.",
            AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        _read = Theme.Button("Read the world", true);
        _read.MinimumSize = new Size(this.Px(130), this.Px(30));
        _read.Click += async (_, _) => await ReadAsync();
        _stop = Theme.Button("Stop");
        _stop.MinimumSize = new Size(this.Px(80), this.Px(30));
        _stop.Enabled = false;
        _stop.Click += (_, _) => _cts?.Cancel();
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange([_read, _stop]);
        top.Controls.Add(row);

        _summary = new Label { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, this.Px(6)) };
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(9f), AccessibleName = "Mods and add-ons",
        };
        foreach (var (text, width, right) in new[] { ("Mod, add-on or pack", 250, false), ("Blocks", 100, true), ("Mobs", 70, true), ("Items", 70, true), ("Biomes", 64, true), ("Where it comes from", 300, false) })
            _list.Columns.Add(text, this.Px(width), right ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        Theme.DarkList(_list);
        Theme.FillLastColumn(_list);
        _list.SelectedIndexChanged += (_, _) => { ShowDetails(); UpdateButtons(); };
        _list.DoubleClick += (_, _) => ShowOnMap();

        _details = new TextBox
        {
            Dock = DockStyle.Bottom, Height = this.Px(170), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
            BackColor = C.BG3, ForeColor = C.FG, BorderStyle = BorderStyle.None, Font = Theme.Ui(9f), AccessibleName = "What the world holds of it",
        };
        Theme.DarkControl(_details);
        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(860), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0) };
        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _copy = Theme.Button("Copy the report");
        _copy.Click += (_, _) => CopyReport();
        _find = Theme.Button("Find its things");
        _find.Click += (_, _) => FindThings();
        _show = Theme.Button("Show on the map");
        _show.Click += (_, _) => ShowOnMap();
        _jar = Theme.Button("Show the mod's file");
        _jar.Click += (_, _) => ShowJar();
        foreach (var b in new[] { _copy, _jar, _find, _show }) { b.MinimumSize = new Size(this.Px(130), this.Px(30)); buttons.Controls.Add(b); }

        // Docked last first: the buttons and status at the bottom, the details above them, the controls on top, the list filling the rest.
        Controls.Add(_list);
        Controls.Add(_details);
        Controls.Add(_summary);
        Controls.Add(top);
        Controls.Add(_status);
        Controls.Add(buttons);
        _status.Text = PlatformSupport.WhyNot("mods", doc.World, out _) ?? "Press Read the world: every chunk is read, which takes about a minute for a big world.";
        UpdateButtons();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Reading is what the window is for: it starts by itself.
        if (_report == null && PlatformSupport.WhyNot("mods", _doc.World, out _) == null) _ = ReadAsync();
    }

    ModUse? Chosen => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ModUse : null;

    void UpdateButtons()
    {
        bool idle = _cts == null;
        _read.Enabled = idle && PlatformSupport.WhyNot("mods", _doc.World, out _) == null;
        _stop.Enabled = !idle;
        _copy.Enabled = idle && _report != null;
        var m = Chosen;
        _find.Enabled = idle && m is { Found: true };
        _show.Enabled = idle && m is { ChunkCount: > 0 };
        _jar.Enabled = idle && m is { OnThisPc.Count: > 0 };
    }

    /// <summary>Reads a world of its own off disk (the tab may be reading the same files) and the mods on this PC.</summary>
    internal async Task ReadAsync()
    {
        if (_cts != null) return;
        string location = _doc.World.Location;
        bool rescue = _doc.World.OpenedForRescue;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        UpdateButtons();
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Reading the world…";
        var progress = new Progress<string>(s => _status.Text = s);
        try
        {
            var report = await Task.Run(() =>
            {
                using var own = rescue ? null : WorldLoader.Open(location);
                var w = own ?? _doc.World;
                ((IProgress<string>)progress).Report("Looking for the mods on this PC…");
                var installed = w is JavaWorld ? InstalledMods.Find(WorldMods.NearbyGameFolders(w), token) : [];
                return WorldMods.Read(w, installed, progress, token);
            }, token);
            _report = report;
            Fill();
            _status.ForeColor = report.Found.Any(m => m.Namespace.Length > 0 && m.OnThisPc.Count == 0 && m.Pack == null && !report.Bedrock) ? C.ORANGE : C.FG_DIM;
            var missing = report.Found.Where(m => m.Namespace.Length > 0 && m.OnThisPc.Count == 0 && m.Pack == null).Select(m => m.Namespace).ToList();
            _status.Text = $"Read {report.ChunksRead:N0} chunks." + (report.Warnings.Count > 0 ? $" {report.Warnings.Count} damaged part(s) were skipped." : "")
                + (!report.Bedrock && missing.Count > 0 ? $" Not found on this PC: {string.Join(", ", missing)}." : "");
        }
        catch (OperationCanceledException) { _status.ForeColor = C.YELLOW; _status.Text = "Stopped."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or Core.IO.SaveFormatException or InvalidOperationException or UnauthorizedAccessException)
        {
            _status.ForeColor = C.ORANGE;
            _status.Text = "The world could not be read: " + ex.Message;
        }
        finally { _cts.Dispose(); _cts = null; UpdateButtons(); }
    }

    internal ModsReport? Report => _report;

    void Fill()
    {
        if (_report is not { } r) return;
        _list.BeginUpdate();
        _list.Items.Clear();
        static string N(long n) => n == 0 ? "" : n.ToString("N0");
        foreach (var m in r.Mods)
        {
            bool missing = m.Found && !r.Bedrock && m.Namespace.Length > 0 && m.OnThisPc.Count == 0 && m.Pack == null;
            var item = new ListViewItem([m.Title, N(m.BlockCount), N(m.Entities.Values.Sum()), N(m.Items.Values.Sum()), N(m.Biomes.Count),
                m.Source + (m.Found ? "" : " · nothing of it is in the world")]) { Tag = m, UseItemStyleForSubItems = true };
            item.ForeColor = missing ? C.ORANGE : m.Found ? C.FG : C.FG_DIM;
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        var about = r.About().ToList();
        _summary.Text = string.Join("  ", about) + (about.Count > 0 ? "  " : "")
            + (r.Found.Any() ? $"{r.Counted()} found. {(r.Bedrock || r.Found.Any(m => m.Pack == null) ? r.Without : "")}" : "Nothing from a mod, an add-on or a data pack: every block, mob, item and biome is the game's own.");
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
        ShowDetails();
    }

    void ShowDetails()
    {
        if (Chosen is not { } m) { _details.Text = ""; return; }
        var lines = new List<string> { m.Title, m.Source, "" };
        lines.AddRange(m.Lines(true));
        if (!m.Found) lines.Add("Nothing of it is in the world's chunks or with its players.");
        if (m.OnThisPc.Count > 0)
        {
            lines.Add("");
            lines.Add("On this PC:");
            lines.AddRange(m.OnThisPc.Select(x => $"  {x.Describe()} — {x.File}"));
        }
        _details.Text = string.Join("\r\n", lines);
        _details.Select(0, 0);
    }

    // The chunks to show: the map's dimension when the mod has some there, else where it has the most.
    (string DimensionId, HashSet<ChunkPos> Chunks)? Where(ModUse m)
    {
        if (m.Chunks.Count == 0) return null;
        var onMap = _doc.World.Dimensions.FirstOrDefault(d => d.Id == _doc.MapDimensionId);
        if (onMap != null && m.Chunks.TryGetValue(CanonicalReader.DimensionId(onMap), out var here) && here.Count > 0) return (CanonicalReader.DimensionId(onMap), here);
        var most = m.Chunks.OrderByDescending(kv => kv.Value.Count).First();
        return (most.Key, most.Value);
    }

    string? RawDimension(string canonicalId) => _doc.World.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == canonicalId)?.Id;

    const int MostMarked = 4000;

    void ShowOnMap()
    {
        if (Chosen is not { } m || Where(m) is not { } where || RawDimension(where.DimensionId) is not string raw) return;
        var chunks = where.Chunks;
        var hits = chunks.OrderBy(p => p.X).ThenBy(p => p.Z).Take(MostMarked)
            .Select(p => new SearchHit(SearchKind.Block, m.Title, p.X * 16 + 8, 64, p.Z * 16 + 8, $"chunk {p.X}, {p.Z}", 1)).ToList();
        _doc.ShowFound(raw, hits, null);
        _status.ForeColor = C.FG_DIM;
        _status.Text = $"Marked {hits.Count:N0} chunk{(hits.Count == 1 ? "" : "s")} with {m.Title}'s things on the map" + (chunks.Count > MostMarked ? $" (the first {MostMarked:N0} of {chunks.Count:N0})." : ".");
    }

    void FindThings()
    {
        if (Chosen is not { Found: true } m) return;
        string? raw = Where(m) is { } where ? RawDimension(where.DimensionId) : null;
        _doc.ShowFind().FindNow(m.Namespace.Length == 0 ? "legacy_block_*" : m.Namespace + ":*", raw);
    }

    void ShowJar()
    {
        if (Chosen is not { OnThisPc: [var mod, ..] }) return;
        if (File.Exists(mod.File)) Explorer.Select(mod.File);
        else { _status.ForeColor = C.ORANGE; _status.Text = "That file isn't there any more: " + mod.File; }
    }

    void CopyReport()
    {
        if (_report == null) return;
        try { Clipboard.SetText(_report.Text()); _status.ForeColor = C.FG_DIM; _status.Text = "Copied the report."; }
        catch (System.Runtime.InteropServices.ExternalException) { _status.Text = "The clipboard is busy; try again."; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosing(e);
    }
}
