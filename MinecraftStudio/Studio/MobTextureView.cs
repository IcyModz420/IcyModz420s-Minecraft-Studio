// MinecraftStudio/Studio/MobTextureView.cs: a mob's texture as a flat picture, with where its model's faces take their
// pixels from.
// Owns: MobTextureView — the Texture Studio's canvas (painting, selections, floating pastes) plus every face of the
// model outlined in its part's colour and the pixels no face uses dimmed, so it is clear what each pixel paints.
using System.Drawing.Drawing2D;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Packs;
using PaintOps = MinecraftStudio.Core.Imaging.Paint;

namespace MinecraftStudio.Studio;

[System.ComponentModel.DesignerCategory("Code")]
sealed class MobTextureView : TextureCanvas
{
    readonly Func<ModelMesh?> _mesh;
    Bitmap? _unused;
    (ModelMesh Mesh, int W, int H)? _unusedFor;

    public MobTextureView(TextureEditor editor, Func<ModelMesh?> mesh) : base(editor)
    {
        _mesh = mesh;
        AccessibleName = "Mob texture";
    }

    protected override string EmptyText => "Pick a mob on the left to paint it.";

    /// <summary>The face rectangles of a mesh in a texture of the given size (pixels).</summary>
    static IEnumerable<(string Bone, RectangleF Rect)> Faces(ModelMesh mesh, int w, int h)
    {
        float sx = w / (float)mesh.Model.TextureWidth, sy = h / (float)mesh.Model.TextureHeight;
        foreach (var q in mesh.Quads)
        {
            var (x, y, rw, rh) = q.TexRect;
            if (rw <= 0 || rh <= 0) continue;
            yield return (q.Bone, new RectangleF(x * sx, y * sy, rw * sx, rh * sy));
        }
    }

    static Color BoneColor(string bone)
    {
        // A steady colour per part name, light enough to see on dark and light pixels.
        uint hash = 2166136261;
        foreach (char c in bone.ToLowerInvariant()) hash = (hash ^ c) * 16777619;
        uint rgb = PaintOps.FromHsv(hash % 360, 0.55, 1.0);
        return Color.FromArgb(170, RgbaImage.R(rgb), RgbaImage.G(rgb), RgbaImage.B(rgb));
    }

    Bitmap UnusedMask(ModelMesh mesh, int w, int h)
    {
        var used = new bool[w * h];
        foreach (var (_, r) in Faces(mesh, w, h))
            for (int y = Math.Max(0, (int)MathF.Floor(r.Top)); y < Math.Min(h, (int)MathF.Ceiling(r.Bottom)); y++)
                for (int x = Math.Max(0, (int)MathF.Floor(r.Left)); x < Math.Min(w, (int)MathF.Ceiling(r.Right)); x++)
                    used[y * w + x] = true;
        var mask = new RgbaImage(w, h);
        for (int i = 0; i < used.Length; i++) if (!used[i]) mask.Pixels[i] = 0xA0141414u;
        return PixelClipboard.ToBitmap(mask);
    }

    protected override void DrawOverlay(Graphics g, RectangleF rect)
    {
        base.DrawOverlay(g, rect);
        if (_mesh() is not { } mesh || View is not { } v) return;
        int w = v.Area.Width, h = v.Area.Height;
        if (_unusedFor is not { } f || f.Mesh != mesh || f.W != w || f.H != h)
        {
            _unused?.Dispose();
            _unused = UnusedMask(mesh, w, h);
            _unusedFor = (mesh, w, h);
        }
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_unused!, rect);
        g.PixelOffsetMode = PixelOffsetMode.Default;
        var pens = new Dictionary<string, Pen>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var (bone, r) in Faces(mesh, w, h))
            {
                if (!pens.TryGetValue(bone, out var pen)) pens[bone] = pen = new Pen(BoneColor(bone), 1);
                g.DrawRectangle(pen, rect.Left + r.X * Zoom, rect.Top + r.Y * Zoom, Math.Max(1, r.Width * Zoom - 1), Math.Max(1, r.Height * Zoom - 1));
            }
        }
        finally
        {
            foreach (var p in pens.Values) p.Dispose();
        }
    }

    protected override PixelRect? FaceAt(int x, int y)
    {
        if (_mesh() is not { } mesh || View is not { } v) return null;
        foreach (var (_, r) in Faces(mesh, v.Area.Width, v.Area.Height))
            if (x + 0.5f >= r.Left && x + 0.5f < r.Right && y + 0.5f >= r.Top && y + 0.5f < r.Bottom)
                return new PixelRect((int)MathF.Floor(r.Left), (int)MathF.Floor(r.Top), Math.Max(1, (int)MathF.Ceiling(r.Right) - (int)MathF.Floor(r.Left)), Math.Max(1, (int)MathF.Ceiling(r.Bottom) - (int)MathF.Floor(r.Top)));
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _unused?.Dispose();
        base.Dispose(disposing);
    }
}
