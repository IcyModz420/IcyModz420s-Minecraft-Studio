// MinecraftStudio/Studio/TextureCanvas.cs: one frame of the selected texture, large, to paint on.
// Owns: TextureCanvas — the Texture Studio's (and Mob Studio's) flat view: what PixelCanvas shows and does, for the frame
// in view of the selected texture, plus the mirror axis. Pixels outside the frame are never touched.
using System.Drawing.Drawing2D;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

/// <summary>
/// One frame of the selected texture, large: left button paints with the first colour, right button with the second,
/// the wheel zooms at the mouse, middle-drag moves, double-click fits. Pixels outside the frame are never touched.
/// </summary>
[System.ComponentModel.DesignerCategory("Code")]
class TextureCanvas : PixelCanvas
{
    protected readonly TextureEditor TextureEditor;

    public TextureCanvas(TextureEditor editor) : base(editor)
    {
        TextureEditor = editor;
        editor.SelectionChanged += OnSelectionChanged;
        AccessibleName = "Texture";
        AccessibleDescription = "The selected texture. Left button paints with the first colour, right button with the second.";
    }

    void OnSelectionChanged() { Fit(); MarkStale(); }

    protected override (RgbaImage Image, PixelRect Area)? View
    {
        get
        {
            if (TextureEditor.Current is not { } t) return null;
            var a = TextureEditor.FrameRect(t, TextureEditor.Frame);
            return (t.Image, new PixelRect(a.X, a.Y, a.Width, a.Height));
        }
    }

    protected override string EmptyText => "Pick a texture on the left to change it.";

    protected override void DrawOverlay(Graphics g, RectangleF rect)
    {
        if (!TextureEditor.MirrorX) return;
        using var axis = new Pen(Color.FromArgb(90, C.ACCENT2), 1) { DashStyle = DashStyle.Dash };
        g.DrawLine(axis, rect.Left + rect.Width / 2, rect.Top - this.Px(6), rect.Left + rect.Width / 2, rect.Bottom + this.Px(6));
    }

    protected override IEnumerable<(int X, int Y)> MirrorMarks((int X, int Y) p)
    {
        if (!TextureEditor.MirrorX || View is not { } v || v.Area.Width - 1 - p.X == p.X) yield break;
        yield return (v.Area.Width - 1 - p.X, p.Y);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) TextureEditor.SelectionChanged -= OnSelectionChanged;
        base.Dispose(disposing);
    }
}
