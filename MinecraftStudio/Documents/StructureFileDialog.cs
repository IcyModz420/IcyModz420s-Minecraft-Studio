// Documents/StructureFileDialog.cs: one structure or schematic file, on its own — see it, count it, turn it, save it as.
// Owns: StructureFileDialog — opens a Java .nbt, WorldEdit .schem, Litematica .litematic, Bedrock .mcstructure or MCEdit
// .schematic; shows it from above and from the south, its size and what it holds, and its material list (copy it, or save
// it as text or CSV); turns or mirrors it; saves it in any of the formats, Java files for a chosen version. The file it
// opened is never changed (saving over it is refused). "Open as NBT" hands the raw file to the NBT editor instead.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class StructureFileDialog : DarkDialog
{
    readonly DarkTextField _file;
    readonly Label _info, _status;
    readonly StructurePreviewBox _preview;
    readonly StructureTurnChoice _turn;
    readonly DarkComboBox _version;
    readonly ListView _list;
    readonly Button _browse, _saveAs, _copyList, _saveList, _nbt, _model;
    Structure? _structure;
    List<MaterialLine> _materials = [];
    string? _path;
    int _shownFor;

    /// <summary>The file to open in the NBT editor once the dialog has closed (the user chose "Open as NBT").</summary>
    public string? OpenNbtAfterClose { get; private set; }

    public StructureFileDialog(string? path = null) : base("Structure file", 760)
    {
        var column = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        column.Controls.Add(new Label
        {
            Text = "Open any structure or schematic — a Java structure (.nbt), WorldEdit (.schem), Litematica (.litematic), Bedrock (.mcstructure) or MCEdit (.schematic). See it, count what it takes to build, turn or mirror it, and save it in another format or for another version. The file you open is never changed.",
            AutoSize = true, MaximumSize = new Size(this.Px(720), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });

        _file = new DarkTextField { Width = this.Px(520), Height = this.Px(28), AccessibleName = "Structure file" };
        _file.Box.ReadOnly = true;
        _file.Box.PlaceholderText = "Choose a structure or schematic file";
        _browse = Theme.Button("Open…");
        _browse.Click += (_, _) => PickFile();
        column.Controls.Add(Row("File", _file, _browse));

        _info = new Label { AutoSize = true, MaximumSize = new Size(this.Px(720), 0), ForeColor = C.FG_FAINT, UseMnemonic = false, Margin = new Padding(this.Px(120), 0, 0, this.Px(4)) };
        column.Controls.Add(_info);
        _preview = new StructurePreviewBox(260, 160) { Margin = new Padding(this.Px(120), 0, 0, this.Px(6)) };
        column.Controls.Add(_preview);

        _turn = new StructureTurnChoice(this);
        _turn.Changed += () => _ = ShowTurned();
        column.Controls.Add(Row("Turn and mirror", _turn.Turn, _turn.Mirror));

        _version = new DarkComboBox { Width = this.Px(300), AccessibleName = "Java version a saved file is made for" };
        foreach (var t in TargetCatalog.All.Where(t => t.Kind == TargetKind.Java && t.DataVersion >= StructureJava.Flattening).OrderByDescending(t => t.DataVersion))
            _version.Items.Add(new ComboItem(t.Name + (t.DataVersion == StructureJava.Newest ? " (newest)" : ""), null, t.DataVersion));
        if (_version.Items.Count > 0) _version.SelectedIndex = 0;
        column.Controls.Add(Row("Save Java files for", _version));

        column.Controls.Add(new Label
        {
            Text = "MATERIAL LIST — what it takes to build, as the items that place each block",
            AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Font = Theme.Ui(8.5f, FontStyle.Bold), Margin = new Padding(0, this.Px(10), 0, this.Px(4)),
        });
        _list = new ListView
        {
            View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false, Size = new Size(this.Px(720), this.Px(220)),
            BackColor = C.BG, ForeColor = C.FG, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(9f), AccessibleName = "Material list",
        };
        _list.Columns.Add("How many", this.Px(100), HorizontalAlignment.Right);
        _list.Columns.Add("Stacks", this.Px(170));
        _list.Columns.Add("Item", this.Px(420));
        Theme.DarkList(_list);
        Theme.FillLastColumn(_list);
        column.Controls.Add(_list);

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(720), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, 0) };

        var buttons = AddButtons(("Close", DialogResult.Cancel, false));
        _saveAs = Theme.Button("Save as…", true);
        _saveAs.Click += async (_, _) => await SaveAsAsync();
        _saveList = Theme.Button("Save the list…");
        _saveList.Click += (_, _) => SaveList();
        _copyList = Theme.Button("Copy the list");
        _copyList.Click += (_, _) => CopyList();
        _nbt = Theme.Button("Open as NBT");
        _nbt.Click += (_, _) => { if (_path != null) { OpenNbtAfterClose = _path; Close(); } };
        _model = Theme.Button("3D model…");
        _model.Click += async (_, _) => await SaveModelAsync();
        foreach (var b in new[] { _saveAs, _model, _saveList, _copyList, _nbt }) { b.MinimumSize = new Size(this.Px(110), this.Px(30)); buttons.Controls.Add(b); }

        Controls.Add(_status);
        Controls.Add(column);
        SizeToContent();
        UpdateButtons();
        if (path != null) LoadFile(path);
    }

    void PickFile()
    {
        using var dlg = new OpenFileDialog { Filter = StructureFile.OpenFilter, Title = "Open a structure or schematic" };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadFile(dlg.FileName);
    }

    void LoadFile(string path)
    {
        _file.Text = path;
        _path = path;
        try
        {
            _structure = StructureFile.Load(path);
            _materials = StructureMaterials.Count(_structure);
            // Saved Java files default to the version the file was made for (the closest one this program writes).
            if (_structure.DataVersion >= StructureJava.Flattening)
            {
                int best = -1;
                for (int i = 0; i < _version.Items.Count; i++)
                    if (_version.Items[i] is ComboItem { Tag: int dv } && dv <= _structure.DataVersion) { best = i; break; }
                if (best >= 0) _version.SelectedIndex = best;
            }
            _status.ForeColor = C.FG_DIM;
            _status.Text = "";
        }
        catch (Exception ex)
        {
            // Structure files come from anywhere: whatever is wrong with one is reported, never a crash.
            if (ex is not (IOException or SaveFormatException or UnauthorizedAccessException or ArgumentException)) Log.Error("Reading a structure file failed", ex);
            _structure = null;
            _materials = [];
            _info.ForeColor = C.ORANGE;
            _info.Text = "Could not read that file as a structure: " + ex.Message;
        }
        FillList();
        UpdateButtons();
        _ = ShowTurned();
    }

    /// <summary>The structure as turned and mirrored now (worked out in the background).</summary>
    async Task<Structure?> Turned()
    {
        var s = _structure;
        int turns = _turn.Turns;
        var mirror = _turn.MirrorChoice;
        return s == null ? null : await Task.Run(() => StructureTransform.Apply(s, turns, mirror));
    }

    async Task ShowTurned()
    {
        int ticket = ++_shownFor;
        if (_structure == null) { await _preview.ShowAsync(null); return; }
        Structure turned;
        try { turned = (await Turned())!; }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException) { Warn(ex.Message); return; }
        if (IsDisposed || ticket != _shownFor) return;
        string made = _structure.DataVersion >= StructureJava.Flattening ? $"; made for Java data version {_structure.DataVersion}" : _structure.DataVersion > 0 ? "; made for Java 1.12 or older" : "";
        _info.ForeColor = C.FG_FAINT;
        _info.Text = turned.Describe() + (_turn.IsIdentity ? "" : $", {_turn.Describe()}") + made + (_structure.Origin != null ? $" — from {_structure.Origin}" : "") + ". Click a picture to turn it in 3D.";
        await _preview.ShowAsync(turned);
    }

    void FillList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var m in _materials)
            _list.Items.Add(new ListViewItem([m.Count.ToString("N0"), m.Count >= m.StackSize && m.StackSize > 1 ? m.Stacks : "", m.Name]) { Tag = m });
        _list.EndUpdate();
    }

    void UpdateButtons()
    {
        bool have = _structure != null;
        _saveAs.Enabled = have;
        _model.Enabled = have;
        _copyList.Enabled = _saveList.Enabled = have && _materials.Count > 0;
        _nbt.Enabled = _path != null;
        _turn.Enabled = have;
    }

    string ListTitle() => $"Material list for {Path.GetFileName(_path)} ({_structure?.Describe()})";

    void CopyList()
    {
        // Only the chosen lines when some are chosen, else the whole list.
        var lines = _list.SelectedItems.Count > 0 ? _list.SelectedItems.Cast<ListViewItem>().Select(i => (MaterialLine)i.Tag!).ToList() : _materials;
        try
        {
            Clipboard.SetText(StructureMaterials.Text(lines, ListTitle()));
            _status.ForeColor = C.GREEN;
            _status.Text = $"✓  Copied {lines.Count:N0} lines.";
        }
        catch (System.Runtime.InteropServices.ExternalException) { Warn("The clipboard is busy; try again."); }
    }

    void SaveList()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Text|*.txt|Spreadsheet (CSV)|*.csv", FileName = SafeName.For(Path.GetFileNameWithoutExtension(_path ?? "structure") + " materials", "materials") + ".txt",
            Title = "Save the material list",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string text = dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? StructureMaterials.Csv(_materials) : StructureMaterials.Text(_materials, ListTitle());
            // UTF-8 with its mark, so spreadsheet programs read the names right.
            SafeFile.WriteAllBytes(dlg.FileName, [.. System.Text.Encoding.UTF8.GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes(text)]);
            _status.ForeColor = C.GREEN;
            _status.Text = $"✓  Saved the list to {dlg.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warn("Could not save the list: " + ex.Message); }
    }

    async Task SaveAsAsync()
    {
        if (_structure == null || _path == null) return;
        string name = Path.GetFileNameWithoutExtension(_path) + (_turn.IsIdentity ? " copy" : " turned");
        using var dlg = new SaveFileDialog { Filter = StructureFile.SaveFilter, FileName = SafeName.For(name, "structure") + Path.GetExtension(_path), Title = "Save the structure as" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string target = dlg.FileName;
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(_path), StringComparison.OrdinalIgnoreCase))
        {
            Warn("That is the file you opened; it is never changed here. Choose another name.");
            return;
        }
        int dataVersion = _version.SelectedItem is ComboItem { Tag: int v } ? v : StructureJava.Newest;
        _saveAs.Enabled = false;
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Saving…";
        try
        {
            var turned = (await Turned())!;
            var standIns = await Task.Run(() => StructureFile.SaveChecked(turned, target, dataVersion));
            var format = StructureFile.FormatOf(target);
            string version = StructureFile.HasJavaVersion(format) ? $" for {((ComboItem)_version.SelectedItem!).Text}" : format == StructureFormat.McEdit ? " for Java 1.12 and older" : "";
            _status.ForeColor = standIns.Count == 0 ? C.GREEN : C.ORANGE;
            _status.Text = $"✓  Saved {turned.Describe()}{version} to {target}. " + StandInText(standIns);
        }
        catch (Exception ex)
        {
            if (ex is not (IOException or SaveFormatException or UnauthorizedAccessException or ArgumentException or NotSupportedException)) Log.Error("Saving a structure failed", ex);
            Warn("Could not save it: " + ex.Message);
        }
        finally { UpdateButtons(); }
    }

    /// <summary>What a save check found, in plain words: every block as itself, or which became look-alikes.</summary>
    internal static string StandInText(List<(string From, string To, long Count)> standIns)
    {
        if (standIns.Count == 0) return "Read back and checked: every block is itself.";
        long all = standIns.Sum(c => c.Count);
        var shown = standIns.Take(4).Select(c => $"{WorldName(c.From)} → {WorldName(c.To)} ({c.Count:N0})");
        return $"Read back and checked: {all:N0} block{(all == 1 ? "" : "s")} that this file type or version doesn't have became the closest it has: "
            + string.Join(", ", shown) + (standIns.Count > 4 ? $", and {standIns.Count - 4} more kinds" : "") + ".";
    }

    static string WorldName(string id) => Core.World.WorldSearch.Nice(id);

    /// <summary>The structure (as turned) as a 3D model: .obj and its .mtl, for Blender and other 3D programs.</summary>
    async Task SaveModelAsync()
    {
        if (_structure == null || _path == null) return;
        using var dlg = new SaveFileDialog { Filter = "3D model (Wavefront .obj)|*.obj", FileName = SafeName.For(Path.GetFileNameWithoutExtension(_path), "structure") + ".obj", Title = "Save the structure as a 3D model" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string target = dlg.FileName;
        _model.Enabled = false;
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Making the model…";
        try
        {
            var turned = (await Turned())!;
            var r = await Task.Run(() => StructureModel.Save(turned, target));
            _status.ForeColor = C.GREEN;
            _status.Text = $"✓  Saved {target} and its .mtl: {r.Faces:N0} faces in {r.Materials} colours" + (r.BlocksLeftOut > 0 ? $" ({r.BlocksLeftOut:N0} plants, torches and other small blocks left out)" : "") + ". Open it in Blender with File → Import → Wavefront (.obj).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException) { Warn("Could not save the model: " + ex.Message); }
        finally { UpdateButtons(); }
    }

    FlowLayoutPanel Row(string label, params Control[] fields)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(2), 0, this.Px(2)) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MinimumSize = new Size(this.Px(120), 0), Margin = new Padding(0, this.Px(7), this.Px(6), 0) });
        foreach (var f in fields) row.Controls.Add(f);
        return row;
    }

    void Warn(string text) { _status.ForeColor = C.ORANGE; _status.Text = text; }
}
