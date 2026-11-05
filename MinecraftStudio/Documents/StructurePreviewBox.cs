// Documents/StructurePreviewBox.cs: two pictures of a structure — from above and from the south — in the map's colours.
// Owns: StructurePreviewBox — drawn in the background (a big structure takes a moment), only the newest structure's pictures
// shown, whole pixels when small (crisp blocks), shrunk smoothly when large; a click on either turns it in 3D
// (Structure3DWindow). Used by the place and structure-file dialogs.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Studio;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class StructurePreviewBox : FlowLayoutPanel
{
    readonly PictureBox _top, _front;
    readonly ToolTip _tip = new();
    Structure? _shown;
    int _ticket;

    public StructurePreviewBox(int pictureWidth = 220, int pictureHeight = 140)
    {
        AutoSize = true;
        WrapContents = false;
        _top = Picture("The structure seen from above", pictureWidth, pictureHeight);
        _front = Picture("The structure seen from the south", pictureWidth, pictureHeight);
        Controls.AddRange([_top, _front]);
    }

    PictureBox Picture(string name, int w, int h)
    {
        var box = new PictureBox
        {
            Size = new Size(this.Px(w), this.Px(h)), SizeMode = PictureBoxSizeMode.CenterImage, BackColor = C.BG,
            Margin = new Padding(0, 0, this.Px(8), 0), AccessibleName = name, AccessibleDescription = "Click to turn it in 3D.",
        };
        _tip.SetToolTip(box, "Click to turn it in 3D");
        box.Click += (_, _) => Show3D();
        return box;
    }

    /// <summary>The structure shown now, in the 3D view.</summary>
    public void Show3D()
    {
        if (_shown is { } s) Structure3DWindow.Open(FindForm(), s, "3D view — " + s.Describe());
    }

    /// <summary>
    /// Draws the structure (or clears the pictures for null). Returns the view from above at one pixel per block (the place
    /// window lays it on its map), or null when there is none or a newer structure came first.
    /// </summary>
    public async Task<RgbaImage?> ShowAsync(Structure? s)
    {
        int ticket = ++_ticket;
        (RgbaImage Top, RgbaImage Front)? views = null;
        if (s != null)
            try { views = await Task.Run(() => (StructurePreview.Top(s), StructurePreview.Front(s))); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Log.Error("Drawing a structure preview failed", ex); }
        if (IsDisposed || ticket != _ticket) return null;
        _shown = views != null ? s : null;
        _top.Cursor = _front.Cursor = _shown != null ? Cursors.Hand : Cursors.Default;
        SetImage(_top, views?.Top);
        SetImage(_front, views?.Front);
        return views?.Top;
    }

    void SetImage(PictureBox box, RgbaImage? img)
    {
        var old = box.Image;
        box.Image = img == null ? null : Fit(img, box.Width - this.Px(6), box.Height - this.Px(6));
        old?.Dispose();
    }

    static Bitmap Fit(RgbaImage img, int maxW, int maxH)
    {
        double f = Math.Min(maxW / (double)img.Width, maxH / (double)img.Height);
        if (f >= 1) f = Math.Floor(f);
        int w = Math.Max(1, (int)(img.Width * f)), h = Math.Max(1, (int)(img.Height * f));
        return PixelClipboard.ToBitmap(Resample.Scale(img, w, h, smooth: f < 1));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _top.Image?.Dispose(); _front.Image?.Dispose(); _tip.Dispose(); }
        base.Dispose(disposing);
    }
}
