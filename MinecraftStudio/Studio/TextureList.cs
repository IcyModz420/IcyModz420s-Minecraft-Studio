// MinecraftStudio/Studio/TextureList.cs: Every texture there is, with a picture and a name; textures in the
// pack are bold with a green mark. A filter box narrows the list; "Only this pack" shows just the pack's own.
// Owns: TextureList (also the Mob Studio's list: which textures it shows and what they are called are given to it).
using System.Drawing.Drawing2D;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

/// <summary>
/// Every texture there is, with a picture and a name; textures in the pack are bold with a green mark. A filter box
/// narrows the list; "Only this pack" shows just the pack's own.
/// </summary>
[System.ComponentModel.DesignerCategory("Code")]
sealed class TextureList : Panel
{
    readonly TextureEditor _editor;
    readonly DarkTextField _filter;
    readonly DarkCheckBox _onlyPack;
    readonly ListBox _list;
    readonly Dictionary<string, Bitmap> _thumbs = [];
    readonly Func<string, bool>? _include;
    readonly Func<string, string> _label;
    bool _filling;

    /// <param name="include">Which textures are listed (null: all).</param>
    /// <param name="label">What a texture is called (null: its readable name).</param>
    public TextureList(TextureEditor editor, Func<string, bool>? include = null, Func<string, string>? label = null, string find = "Find a texture…")
    {
        _editor = editor;
        _include = include;
        _label = label ?? (k => TextureNames.Display(k) + (k.StartsWith("bedrock:") ? "  (Bedrock only)" : k.StartsWith("java:") ? "  (not a game texture)" : ""));
        BackColor = C.BG2;
        _filter = new DarkTextField { Dock = DockStyle.Top, Height = this.Px(30), Padding = new Padding(this.Px(8), this.Px(6), this.Px(6), 0) };
        _filter.Box.PlaceholderText = find;
        _filter.Box.AccessibleName = find.TrimEnd('…');
        _onlyPack = new DarkCheckBox("Only this pack's textures", false) { Dock = DockStyle.Top, Padding = new Padding(this.Px(8), this.Px(4), 0, this.Px(4)), BackColor = C.BG2 };
        _list = new ListBox
        {
            Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = this.Px(30), BackColor = C.BG2, ForeColor = C.FG,
            BorderStyle = BorderStyle.None, IntegralHeight = false, AccessibleName = "Textures",
        };
        Theme.DarkControl(_list);
        _list.DrawItem += DrawItem;
        _list.SelectedIndexChanged += (_, _) => { if (!_filling && _list.SelectedItem is string key) _editor.Select(key); };
        _filter.Box.TextChanged += (_, _) => Refill();
        _onlyPack.CheckedChanged += (_, _) => Refill();
        Controls.Add(_list);
        Controls.Add(_onlyPack);
        Controls.Add(_filter);
        editor.PackChanged += OnPackChanged;
        editor.SelectionChanged += SyncSelection;
        editor.ImageChanged += OnImageChanged;
        Refill();
    }

    void OnPackChanged() { ClearThumbs(); Refill(); }

    void OnImageChanged()
    {
        if (_editor.Selected is { } key && _thumbs.Remove(key, out var b)) b.Dispose();
        _list.Invalidate();
    }

    void ClearThumbs()
    {
        foreach (var b in _thumbs.Values) b.Dispose();
        _thumbs.Clear();
    }

    public void Refill()
    {
        string f = _filter.Text.Trim();
        var keys = _editor.AllKeys().Where(k => (_include == null || _include(k)) && (!_onlyPack.Checked || _editor.InPack(k)) &&
            (f.Length == 0 || _label(k).Contains(f, StringComparison.OrdinalIgnoreCase) || k.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(k => _label(k), StringComparer.OrdinalIgnoreCase).ToList();
        _filling = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Items.AddRange(keys.Cast<object>().ToArray());
        _list.EndUpdate();
        _filling = false;
        SyncSelection();
    }

    void SyncSelection()
    {
        _filling = true;
        _list.SelectedItem = _editor.Selected;
        _filling = false;
        _list.Invalidate();
    }

    void DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count) return;
        string key = (string)_list.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0, inPack = _editor.InPack(key);
        using (var b = new SolidBrush(selected ? C.SELECTION : C.BG2)) e.Graphics.FillRectangle(b, e.Bounds);
        int pad = this.Px(4), icon = e.Bounds.Height - pad * 2;
        if (Thumb(key) is { } thumb)
        {
            // Kept in shape (mob textures are often twice as wide as tall).
            float k = Math.Min((float)icon / thumb.Width, (float)icon / thumb.Height);
            int tw = Math.Max(1, (int)(thumb.Width * k)), th = Math.Max(1, (int)(thumb.Height * k));
            var r = new Rectangle(e.Bounds.Left + this.Px(8) + (icon - tw) / 2, e.Bounds.Top + pad + (icon - th) / 2, tw, th);
            ColorPanel.Checker(e.Graphics, r, Math.Max(2, icon / 4));
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(thumb, r);
        }
        if (inPack)
        {
            using var dot = new SolidBrush(C.ACCENT2);
            e.Graphics.FillEllipse(dot, e.Bounds.Right - this.Px(14), e.Bounds.Top + e.Bounds.Height / 2 - this.Px(3), this.Px(6), this.Px(6));
        }
        using var font = Theme.Ui(9f, inPack ? FontStyle.Bold : FontStyle.Regular);
        var text = new Rectangle(e.Bounds.Left + this.Px(16) + icon, e.Bounds.Top, e.Bounds.Width - icon - this.Px(36), e.Bounds.Height);
        string label = _label(key);
        TextRenderer.DrawText(e.Graphics, label, font, text, inPack ? C.FG : C.FG_DIM, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    Bitmap? Thumb(string key)
    {
        if (_thumbs.TryGetValue(key, out var b)) return b;
        if (_editor.Texture(key) is not { } t) return null;
        var img = t.FirstFrame();
        b = new Bitmap(img.Width, img.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = b.LockBits(new Rectangle(0, 0, img.Width, img.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var row = new int[img.Width];
        for (int y = 0; y < img.Height; y++)
        {
            Buffer.BlockCopy(img.Pixels, y * img.Width * 4, row, 0, img.Width * 4);
            System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, img.Width);
        }
        b.UnlockBits(data);
        _thumbs[key] = b;
        return b;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor.PackChanged -= OnPackChanged;
            _editor.SelectionChanged -= SyncSelection;
            _editor.ImageChanged -= OnImageChanged;
            ClearThumbs();
        }
        base.Dispose(disposing);
    }
}
