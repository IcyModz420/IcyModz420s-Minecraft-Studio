// MinecraftStudio/Studio/TileLibraryDialog.cs: "My tiles" — the player's own saved tiles and pictures, for any editor.
// Owns: TileLibraryDialog — every saved tile as a picture; using one as the whole picture (or the selected part), or
// pasting it to place; saving the picture being edited (or the selection, or what floats) as a tile; adding picture
// files; renaming; removing (to the Recycle Bin, never deleted outright); showing the folder. The folder is
// Documents\Minecraft Studio\My Tiles.
using System.Drawing.Drawing2D;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

sealed class TileLibraryDialog : DarkDialog
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Studio", "My Tiles");

    readonly IPixelEditor _editor;
    readonly TileLibrary _library = new(Folder);
    readonly ListView _list;
    readonly ImageList _thumbs;
    readonly Label _status;
    readonly Button _use, _paste, _rename, _remove;
    List<SavedTile> _tiles = [];

    /// <summary>Asks for a name and saves the picture being edited as soon as the dialog opens.</summary>
    public bool SaveOnOpen { get; init; }

    /// <summary>The tile chosen, and whether it replaces (true) or is pasted to place (false); null when none.</summary>
    public (RgbaImage Image, string Name, bool Replace)? Chosen { get; private set; }

    public TileLibraryDialog(IPixelEditor editor) : base("My tiles", 760)
    {
        _editor = editor;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(this.Px(560), this.Px(420));
        ClientSize = new Size(this.Px(760), this.Px(560));
        var intro = new Label
        {
            Text = $"Your own tiles and pictures, kept for later and usable in every editor. Save {editor.AreaName} (or the selected part) here, or add picture files; then use one as the whole picture or paste it where you want it.",
            Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(this.Px(720), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, 0, 0, this.Px(8)),
        };
        int thumb = this.Px(64);
        _thumbs = new ImageList { ImageSize = new Size(thumb, thumb), ColorDepth = ColorDepth.Depth32Bit };
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.LargeIcon, LargeImageList = _thumbs, MultiSelect = false, BackColor = C.BG, ForeColor = C.FG,
            BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Saved tiles", LabelWrap = true,
        };
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => Choose(replace: true);
        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0) };

        var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, BackColor = C.BG2, Padding = new Padding(0, this.Px(8), 0, 0) };
        Button Btn(string text, Action click, bool primary = false)
        {
            var b = Theme.Button(text, primary);
            b.Margin = new Padding(0, 0, this.Px(8), this.Px(6));
            b.Click += (_, _) => click();
            row.Controls.Add(b);
            return b;
        }
        _use = Btn("Use as the whole picture", () => Choose(replace: true), primary: true);
        _paste = Btn("Paste it to place", () => Choose(replace: false));
        Btn("Save this picture here…", SaveCurrent);
        Btn("Add picture files…", AddFiles);
        _rename = Btn("Rename…", Rename);
        _remove = Btn("Remove", Remove);
        Btn("Show the folder", () => { Directory.CreateDirectory(Folder); Explorer.Open(Folder); });
        AddButtons(("Close", DialogResult.Cancel, false));
        Controls.Add(_list);
        Controls.Add(row);
        Controls.Add(_status);
        Controls.Add(intro);
        Fill();
        Shown += (_, _) => { if (SaveOnOpen) SaveCurrent(); };
    }

    SavedTile? Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as SavedTile : null;

    void UpdateButtons()
    {
        bool any = Selected != null;
        _use.Enabled = _paste.Enabled = _rename.Enabled = _remove.Enabled = any;
    }

    void Fill(string? select = null)
    {
        try { _tiles = _library.List(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _tiles = []; _status.Text = e.Message; }
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (Image i in _thumbs.Images) i.Dispose();
        _thumbs.Images.Clear();
        foreach (var t in _tiles)
        {
            Bitmap? picture = null;
            try { picture = Thumb(_library.Load(t), _thumbs.ImageSize.Width); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
            if (picture == null) continue;
            _thumbs.Images.Add(picture);
            var item = new ListViewItem($"{t.Name}\n{t.Width} × {t.Height}", _thumbs.Images.Count - 1) { Tag = t, ToolTipText = t.Path };
            _list.Items.Add(item);
            if (t.Name == select) item.Selected = true;
        }
        _list.EndUpdate();
        _status.Text = _tiles.Count == 0 ? $"No tiles yet. They are kept in {Folder}." : $"{_tiles.Count} tile(s) in {Folder}";
        UpdateButtons();
    }

    /// <summary>A tile's picture fitted into a square, crisp, on a checkerboard (see-through parts show).</summary>
    static Bitmap Thumb(RgbaImage img, int size)
    {
        var b = new Bitmap(size, size);
        using var g = Graphics.FromImage(b);
        float k = Math.Min((float)size / img.Width, (float)size / img.Height);
        int w = Math.Max(1, (int)(img.Width * k)), h = Math.Max(1, (int)(img.Height * k));
        var r = new Rectangle((size - w) / 2, (size - h) / 2, w, h);
        ColorPanel.Checker(g, r, Math.Max(2, size / 8));
        using var bmp = PixelClipboard.ToBitmap(img);
        g.InterpolationMode = k >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(bmp, r);
        return b;
    }

    void Choose(bool replace)
    {
        if (Selected is not { } t) return;
        try
        {
            Chosen = (_library.Load(t), t.Name, replace);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { Dialogs.Error(this, $"Could not read {t.Name}:\n\n{e.Message}"); }
    }

    void SaveCurrent()
    {
        if (_editor.Session.Copy(_editor.AreaPixels()) is not { } pixels) { Dialogs.Info(this, "There is nothing to save yet: open a texture first."); return; }
        string part = _editor.Session.Floating != null ? "what floats" : _editor.Session.Selection != null ? "the selected part" : _editor.AreaName;
        string suggested = _editor.AreaName.StartsWith("the ", StringComparison.Ordinal) ? _editor.AreaName[4..] : _editor.AreaName;
        string? name = Dialogs.Input(this, "Save to My tiles", $"Name of the tile ({part}, {pixels.Width} × {pixels.Height}):", suggested);
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var saved = _library.Save(name, pixels);
            Fill(saved.Name);
            _status.Text = $"Saved {saved.Name} ({pixels.Width} × {pixels.Height}).";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { Dialogs.Error(this, "Could not save the tile:\n\n" + e.Message); }
    }

    void AddFiles()
    {
        using var dlg = new OpenFileDialog { Filter = PixelClipboard.OpenFilter, Title = "Add pictures to My tiles", Multiselect = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var failed = new List<string>();
        string? last = null;
        foreach (var file in dlg.FileNames)
        {
            try { last = _library.Save(Path.GetFileNameWithoutExtension(file), PixelClipboard.Load(file)).Name; }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { failed.Add($"{Path.GetFileName(file)}: {e.Message}"); }
        }
        Fill(last);
        if (failed.Count > 0) Dialogs.Error(this, "Some pictures could not be added:\n\n" + string.Join("\n", failed));
    }

    void Rename()
    {
        if (Selected is not { } t) return;
        string? name = Dialogs.Input(this, "Rename the tile", "New name:", t.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        try { Fill(_library.Rename(t, name).Name); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Dialogs.Error(this, "Could not rename the tile:\n\n" + e.Message); }
    }

    void Remove()
    {
        if (Selected is not { } t) return;
        if (Dialogs.Ask(this, $"Remove {t.Name} from My tiles?\n\nIt goes to the Recycle Bin, so you can still get it back.") != DialogResult.Yes) return;
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(t.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            Fill();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException) { Dialogs.Error(this, "Could not remove the tile:\n\n" + e.Message); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (Image i in _thumbs.Images) i.Dispose();
            _thumbs.Dispose();
        }
        base.Dispose(disposing);
    }
}
