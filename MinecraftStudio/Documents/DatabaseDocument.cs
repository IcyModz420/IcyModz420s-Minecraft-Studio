// MinecraftStudio/Documents/DatabaseDocument.cs: Browses every record of a Bedrock world's LevelDB database.
// Owns: DatabaseDocument, HexDocument.
using System.Text;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.LevelDb;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

/// <summary>Browses every record of a Bedrock world's LevelDB database.</summary>
sealed class DatabaseDocument : Document
{
    readonly BedrockWorld _world;
    readonly ListView _list;
    readonly DarkTextField _filter;
    readonly ToolStripLabel _count;
    List<(byte[] Key, string Text, string Kind)> _all = [];
    List<(byte[] Key, string Text, string Kind)> _shown = [];
    readonly System.Windows.Forms.Timer _filterTimer = new() { Interval = 250 };

    public DatabaseDocument(BedrockWorld world)
    {
        _world = world;
        Key = (world, "db");
        var bar = Theme.ToolStrip();
        bar.ImageScalingSize = new Size(this.Px(16), this.Px(16));
        _filter = new DarkTextField { Padding = new Padding(this.Px(6), this.Px(4), this.Px(4), 0) };
        _filter.Box.AccessibleName = "Filter";
        _filter.Box.PlaceholderText = "player, map_, digp, chunk 0,0, SubChunkPrefix…";
        _filter.Box.TextChanged += (_, _) => { _filterTimer.Stop(); _filterTimer.Start(); };
        _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); ApplyFilter(); };
        _count = new ToolStripLabel("") { ForeColor = C.FG_DIM, Alignment = ToolStripItemAlignment.Right };
        bar.Items.AddRange(
        [
            new ToolStripLabel("Filter") { ForeColor = C.FG_DIM },
            new ToolStripHost(_filter, this.Px(300), this.Px(24)),
            new ToolStripSeparator(),
            new ToolStripButton("Open", Theme.Glyph(Glyphs.Open, C.FG, this.Px(16)), (_, _) => OpenSelected()) { ForeColor = C.FG, ToolTipText = "Open the record as NBT (or hex)" },
            new ToolStripButton("Hex", Theme.Glyph(Glyphs.Code, C.FG, this.Px(16)), (_, _) => OpenSelected(forceHex: true)) { ForeColor = C.FG },
            new ToolStripButton("Export…", Theme.Glyph(Glyphs.Export, C.FG, this.Px(16)), (_, _) => ExportSelected()) { ForeColor = C.FG },
            new ToolStripButton("Delete", Theme.Glyph(Glyphs.Delete, C.FG, this.Px(16)), (_, _) => DeleteSelected()) { ForeColor = C.FG, ToolTipText = "Delete the selected records (committed to the database immediately after confirmation)" },
            new ToolStripButton("Refresh", Theme.Glyph(Glyphs.Refresh, C.FG, this.Px(16)), (_, _) => Reload()) { ForeColor = C.FG },
            _count,
        ]);
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, VirtualMode = true, BackColor = C.BG, ForeColor = C.FG,
            BorderStyle = BorderStyle.None, Font = Theme.Mono(9f), HideSelection = false,
        };
        _list.Columns.Add("Record", this.Px(420));
        _list.Columns.Add("Kind", this.Px(160));
        _list.Columns.Add("Size", this.Px(90), HorizontalAlignment.Right);
        _list.Columns.Add("Key bytes", this.Px(260));
        _list.RetrieveVirtualItem += (_, e) =>
        {
            var (key, text, kind) = _shown[e.ItemIndex];
            var item = new ListViewItem(text);
            item.SubItems.Add(kind);
            item.SubItems.Add(_world.Db.ValueLength(key).ToString("N0"));
            item.SubItems.Add(Convert.ToHexString(key));
            e.Item = item;
        };
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) OpenSelected(); else if (e.KeyCode == Keys.Delete) DeleteSelected(); };
        Theme.DarkList(_list);
        Controls.Add(_list);
        Controls.Add(bar);
        Reload();
    }

    public override string Title => _world.Name + " — database";

    static string KindOf(byte[] key)
    {
        if (BedrockKeys.TryParseChunkKey(key, out var ck)) return ck.Tag.ToString();
        string d = BedrockKeys.Describe(key);
        if (d.StartsWith("digp")) return "Actor digest";
        if (d.StartsWith("actorprefix")) return "Actor";
        if (d.StartsWith("player_server_") || d == "~local_player") return "Player";
        if (d.StartsWith("player_")) return "Player id";
        if (d.StartsWith("map_")) return "Map";
        if (d.StartsWith("VILLAGE_")) return "Village";
        if (d.StartsWith("structuretemplate")) return "Structure";
        return BedrockKeys.IsPrintable(key) ? "Global" : "Binary key";
    }

    void Reload()
    {
        _all = _world.Db.Keys().OrderBy(k => k, ByteArrayComparer.Instance).Select(k => (k, BedrockKeys.Describe(k), KindOf(k))).ToList();
        ApplyFilter();
    }

    void ApplyFilter()
    {
        string f = _filter.Text.Trim();
        _shown = f.Length == 0 ? _all : _all.Where(e => e.Text.Contains(f, StringComparison.OrdinalIgnoreCase) || e.Kind.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        _list.VirtualListSize = _shown.Count;
        _list.Invalidate();
        _count.Text = $"{_shown.Count:N0} of {_all.Count:N0} records";
    }

    IEnumerable<int> SelectedIndices() => _list.SelectedIndices.Cast<int>();

    void OpenSelected(bool forceHex = false)
    {
        int i = SelectedIndices().FirstOrDefault(-1);
        if (i < 0) return;
        var (key, text, _) = _shown[i];
        var value = _world.Db.Get(key);
        if (value == null) return;
        if (!forceHex && value.Length > 0 && value[0] == (byte)NbtType.Compound)
        {
            try
            {
                var roots = NbtBinary.ReadAll(value, NbtFormat.Bedrock);
                var keyCopy = key;
                var source = new DelegateNbtSource(text,
                    () => NbtBinary.ReadAll(_world.Db.Get(keyCopy) ?? [], NbtFormat.Bedrock),
                    r =>
                    {
                        if (LevelDatabase.IsLocked(_world.Db.Directory_)) throw new IOException("The world is open in Minecraft; close it first.");
                        Backup.WorldBeforeSave(_world);
                        _world.Db.Put(keyCopy, NbtBinary.SerializeAll(r, NbtFormat.Bedrock));
                        _world.Save(CommitMode.AppendLog);
                    },
                    $"{text}  ·  {_world.Name}  ·  {roots.Count} root tag(s), {value.Length:N0} bytes");
                Main.OpenDocument(new NbtDocument(source) { Key = (_world, Convert.ToHexString(key)) });
                return;
            }
            catch { }
        }
        Main.OpenDocument(new HexDocument(text, value) { Key = (_world, "hex", Convert.ToHexString(key)) });
    }

    void ExportSelected()
    {
        int i = SelectedIndices().FirstOrDefault(-1);
        if (i < 0) return;
        var (key, text, _) = _shown[i];
        using var dlg = new SaveFileDialog { FileName = SafeName.For(text, "value") + ".bin", Filter = "Binary|*.bin|All files|*.*" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        File.WriteAllBytes(dlg.FileName, _world.Db.Get(key) ?? []);
    }

    void DeleteSelected()
    {
        var keys = SelectedIndices().Select(i => _shown[i].Key).ToList();
        if (keys.Count == 0) return;
        if (Dialogs.Ask(FindForm()!, $"Delete {keys.Count:N0} record(s) from the database? A backup of the world is made first.") != DialogResult.Yes) return;
        if (LevelDatabase.IsLocked(_world.Db.Directory_)) { Dialogs.Error(FindForm()!, "The world is open in Minecraft; close it first."); return; }
        Backup.WorldBeforeSave(_world);
        foreach (var k in keys) _world.Db.Delete(k);
        _world.Save(CommitMode.AppendLog);
        Reload();
        Main.SetStatus($"Deleted {keys.Count:N0} records.");
    }
}

/// <summary>Read-only hex dump.</summary>
sealed class HexDocument : Document
{
    readonly string _title;

    public HexDocument(string title, byte[] data)
    {
        _title = title;
        var box = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.None, Font = Theme.Mono(9.5f), Text = Dump(data),
        };
        Theme.DarkControl(box);
        Controls.Add(box);
    }

    public override string Title => _title;

    public static string Dump(byte[] data, int max = 1 << 20)
    {
        var sb = new StringBuilder();
        int n = Math.Min(data.Length, max);
        for (int i = 0; i < n; i += 16)
        {
            sb.Append(i.ToString("X8")).Append("  ");
            for (int k = 0; k < 16; k++)
            {
                if (i + k < n) sb.Append(data[i + k].ToString("X2")).Append(' ');
                else sb.Append("   ");
                if (k == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (int k = 0; k < 16 && i + k < n; k++)
            {
                byte b = data[i + k];
                sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }
            sb.Append("\r\n");
        }
        if (data.Length > n) sb.Append($"… {data.Length - n:N0} more bytes\r\n");
        return sb.ToString();
    }
}
