// MinecraftStudio/Documents/RegionDocument.cs: Inspects one Java region file (.mca/.mcr, also entities/ and
// poi/ regions).
// Owns: RegionDocument.
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

/// <summary>Inspects one Java region file (.mca/.mcr, also entities/ and poi/ regions).</summary>
sealed class RegionDocument : Document
{
    readonly string _path;
    RegionFile _region;
    readonly ListView _list;
    readonly ToolStripLabel _info;

    public RegionDocument(string path)
    {
        _path = path;
        _region = RegionFile.Open(path);
        var bar = Theme.ToolStrip();
        bar.ImageScalingSize = new Size(this.Px(16), this.Px(16));
        _info = new ToolStripLabel("") { ForeColor = C.FG_DIM, Alignment = ToolStripItemAlignment.Right };
        bar.Items.AddRange(
        [
            new ToolStripButton("Open chunk", Theme.Glyph(Glyphs.Open, C.FG, this.Px(16)), (_, _) => OpenSelected()) { ForeColor = C.FG },
            new ToolStripButton("Delete chunks", Theme.Glyph(Glyphs.Delete, C.FG, this.Px(16)), (_, _) => DeleteSelected()) { ForeColor = C.FG },
            new ToolStripButton("Compact", Theme.Glyph(Glyphs.Repair, C.FG, this.Px(16)), (_, _) => Compact()) { ForeColor = C.FG, ToolTipText = "Rewrite the file without free sectors" },
            new ToolStripButton("Check", Theme.Glyph(Glyphs.Check, C.FG, this.Px(16)), (_, _) => Check()) { ForeColor = C.FG, ToolTipText = "Decompress and parse every chunk" },
            _info,
        ]);
        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.None, Font = Theme.Mono(9f), HideSelection = false };
        foreach (var (name, w) in new[] { ("Slot", 50), ("Chunk", 90), ("Sector", 70), ("Sectors", 60), ("Stored", 90), ("Compression", 100), ("Saved", 150), ("Data version", 100), ("Status", 120), ("Inhabited", 100) })
            _list.Columns.Add(name, this.Px(w));
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) DeleteSelected(); else if (e.KeyCode == Keys.Enter) OpenSelected(); };
        Theme.DarkList(_list);
        Controls.Add(_list);
        Controls.Add(bar);
        Fill();
    }

    public override string Title => Path.GetFileName(_path);
    public override string? ToolTipText => _path;

    static string CompressionName(byte t) => t switch { 1 => "gzip", 2 => "zlib", 3 => "none", 4 => "LZ4", 127 => "custom", _ => t.ToString() };

    void Fill()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        int n = 0;
        foreach (int i in _region.ChunkIndexes())
        {
            n++;
            var (x, z) = _region.ChunkCoords(i);
            var item = new ListViewItem(i.ToString()) { Tag = i };
            item.SubItems.Add($"{x}, {z}");
            item.SubItems.Add(_region.StoredSectorOffset(i).ToString());
            item.SubItems.Add(_region.StoredSectorCount(i).ToString());
            try
            {
                var raw = _region.GetRaw(i)!;
                item.SubItems.Add(raw.Payload.Length.ToString("N0"));
                item.SubItems.Add(CompressionName(raw.CompressionType) + (_region.IsExternal(i) ? " (.mcc)" : ""));
                item.SubItems.Add(raw.Timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(raw.Timestamp).LocalDateTime.ToString("g") : "");
                if (raw.CompressionType != 127)
                {
                    var nbt = NbtBinary.ReadCompound(raw.Decompress(), NbtFormat.Java);
                    var s = JavaChunk.Summarize(nbt);
                    item.SubItems.Add(s.DataVersion?.ToString() ?? "");
                    item.SubItems.Add(s.Status ?? "");
                    item.SubItems.Add(s.InhabitedTime >= 0 ? TimeSpan.FromSeconds(s.InhabitedTime / 20.0).ToString("g") : "");
                }
            }
            catch (Exception ex)
            {
                item.ForeColor = C.RED;
                item.SubItems.Add("");
                item.SubItems.Add("ERROR: " + ex.Message);
            }
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        _info.Text = $"Region {_region.RegionX}, {_region.RegionZ}  ·  {n} chunks  ·  {Core.IO.BinaryUtil.FormatSize(_region.FileLength)}  ·  {Core.IO.BinaryUtil.FormatSize(_region.WastedBytes())} free space";
    }

    IEnumerable<int> SelectedSlots() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (int)i.Tag!);

    void OpenSelected()
    {
        int slot = SelectedSlots().FirstOrDefault(-1);
        if (slot < 0) return;
        var (x, z) = _region.ChunkCoords(slot);
        var source = new DelegateNbtSource($"Chunk {x}, {z}",
            () => [new NamedTag("", _region.GetChunkNbt(slot)!)],
            roots =>
            {
                Backup.FileBeforeWrite(_path);
                _region.SetChunk(slot, (NbtCompound)roots[0].Tag);
                _region.Save();
                _region = RegionFile.Open(_path);
                Fill();
            },
            $"Chunk {x}, {z} in {_path}");
        Main.OpenDocument(new NbtDocument(source) { Key = (_path, slot) });
    }

    void DeleteSelected()
    {
        var slots = SelectedSlots().ToList();
        if (slots.Count == 0) return;
        if (Dialogs.Ask(FindForm()!, $"Delete {slots.Count} chunk(s) from {Title}? The game regenerates deleted chunks. A backup of the file is made first.") != DialogResult.Yes) return;
        Backup.FileBeforeWrite(_path);
        foreach (int s in slots) _region.DeleteChunk(s);
        bool kept = _region.Save();
        if (!kept) { Dialogs.Info(FindForm()!, "The region is now empty, so the file was removed."); return; }
        _region = RegionFile.Open(_path);
        Fill();
    }

    void Compact()
    {
        Backup.FileBeforeWrite(_path);
        long before = _region.FileLength;
        _region.Save(deleteIfEmpty: false);
        _region = RegionFile.Open(_path);
        Fill();
        Main.SetStatus($"{Title}: {Core.IO.BinaryUtil.FormatSize(before)} → {Core.IO.BinaryUtil.FormatSize(_region.FileLength)}");
    }

    void Check()
    {
        var issues = _region.Validate(true);
        Dialogs.ShowText(FindForm()!, "Region check — " + Title, issues.Count == 0 ? "No problems found." : string.Join("\n", issues.Select(i => $"slot {i.Index}: {i.Message}")));
    }
}
