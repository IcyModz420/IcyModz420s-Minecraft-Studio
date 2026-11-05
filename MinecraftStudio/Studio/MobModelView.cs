// MinecraftStudio/Studio/MobModelView.cs: the selected mob texture on its 3D model, painted on directly.
// Owns: MobModelView — drawing the mesh the Mob Studio hands it (ModelRenderer), painting where the mouse is on the
// model (the renderer says which texture pixel shows there), the eyedropper on what shows, turning (right-drag), moving
// (middle-drag), zooming (wheel), views from a right click, and pictures dropped on it. Lines, shapes and selections
// are drawn on the flat picture.
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

[System.ComponentModel.DesignerCategory("Code")]
sealed class MobModelView : Control
{
    readonly TextureEditor _editor;
    readonly Func<ModelMesh?> _mesh;
    readonly Func<string> _caption;
    readonly ModelRenderer _renderer = new();
    OrbitCamera _camera = OrbitCamera.Default;
    ModelMesh? _framed;
    Bitmap? _bitmap;
    bool _stale = true;

    enum DragKind { None, Paint, Turn, Move }
    DragKind _drag;
    Point _dragFrom, _lastMouse;
    OrbitCamera _cameraFrom;
    bool _moved;

    /// <param name="mesh">The model to draw the selected texture on (null: none).</param>
    /// <param name="caption">The line shown in the corner (which model).</param>
    public MobModelView(TextureEditor editor, Func<ModelMesh?> mesh, Func<string> caption)
    {
        _editor = editor;
        _mesh = mesh;
        _caption = caption;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = C.BG;
        AllowDrop = true;
        editor.ImageChanged += Redraw;
        editor.HoverChanged += Redraw;
        editor.StateChanged += Redraw;
        AccessibleName = "3D mob view";
        AccessibleDescription = "Left-drag paints on the model; right-drag turns it; the mouse wheel zooms.";
    }

    public event Action<IDataObject>? Dropped;

    public void Redraw() { _stale = true; Invalidate(); }

    /// <summary>The model changed (another mob or model): the camera frames it again.</summary>
    public void ModelChanged()
    {
        _framed = null;
        Redraw();
    }

    public void ResetView()
    {
        if (_mesh() is { } m) _camera = ModelRenderer.CameraFor(m, Aspect);
        Redraw();
    }

    void Look(float yaw, float pitch)
    {
        _camera = _camera with { Yaw = yaw, Pitch = pitch };
        Redraw();
    }

    float Aspect => ClientSize.Height > 0 ? ClientSize.Width / (float)ClientSize.Height : 1;

    (int X, int Y, int Width, int Height)? Area => _editor.Current is { } t ? FrameOf(t) : null;

    (int X, int Y, int Width, int Height) FrameOf(PackTexture t)
    {
        var r = TextureEditor.FrameRect(t, _editor.Frame);
        return (r.X, r.Y, r.Width, r.Height);
    }

    void Render()
    {
        int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
        if (_bitmap == null || _bitmap.Width != w || _bitmap.Height != h)
        {
            _bitmap?.Dispose();
            _bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        }
        var mesh = _mesh();
        if (mesh == null || _editor.Current is not { } t)
        {
            using var g = Graphics.FromImage(_bitmap);
            g.Clear(C.BG);
            _stale = false;
            return;
        }
        if (_framed != mesh)
        {
            if (_framed == null || _framed.Model != mesh.Model) _camera = ModelRenderer.CameraFor(mesh, Aspect);
            _framed = mesh;
        }
        var area = FrameOf(t);
        int hover = _editor.Hover is { } hv ? (area.Y + hv.Y) * t.Image.Width + area.X + hv.X : -1;
        _renderer.Render(mesh, t.Image, _camera, new ModelViewOptions { ShowGrid = _editor.ShowGrid, Hover = hover }, w, h);
        var data = _bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++) Marshal.Copy(_renderer.Frame, y * w, data.Scan0 + y * data.Stride, w);
        _bitmap.UnlockBits(data);
        _stale = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_stale || _bitmap == null || _bitmap.Width != Math.Max(1, ClientSize.Width) || _bitmap.Height != Math.Max(1, ClientSize.Height)) Render();
        e.Graphics.DrawImageUnscaled(_bitmap!, 0, 0);
        using var f = Theme.Ui(8.25f);
        if (_mesh() == null)
        {
            string why = _editor.Current == null ? "Pick a mob on the left." : "No 3D model for this texture: paint on the flat picture. (Models come from Minecraft Bedrock installed on this PC, or from the pack.)";
            TextRenderer.DrawText(e.Graphics, why, f, ClientRectangle, C.FG_DIM, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        TextRenderer.DrawText(e.Graphics, _caption(), f, new Point(this.Px(8), this.Px(6)), C.FG_DIM);
    }

    // ------------------------------------------------------------------ mouse

    /// <summary>The texture pixel to paint under a screen point, in the frame's coordinates (null outside the frame).</summary>
    (int X, int Y)? PickAt(Point p, bool seen = false)
    {
        if (_stale || _renderer.Width != Math.Max(1, ClientSize.Width) || _renderer.Height != Math.Max(1, ClientSize.Height)) Render();
        if (_mesh() == null || Area is not { } a) return null;
        var hit = seen ? _renderer.Seen(p.X, p.Y) : _renderer.Pick(p.X, p.Y);
        if (hit is not { } h || h.X < a.X || h.Y < a.Y || h.X >= a.X + a.Width || h.Y >= a.Y + a.Height) return null;
        return (h.X - a.X, h.Y - a.Y);
    }

    static bool FlatOnly(PaintTool t) => t is PaintTool.Select or PaintTool.Line or PaintTool.Rectangle or PaintTool.Ellipse;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _dragFrom = _lastMouse = e.Location;
        _moved = false;
        bool secondary = (ModifierKeys & Keys.Control) != 0;
        if (e.Button == MouseButtons.Left && (_editor.Tool == PaintTool.Picker || (ModifierKeys & Keys.Alt) != 0))
        {
            if (PickAt(e.Location, seen: true) is { } s) { _editor.PickColor(s.X, s.Y, secondary); return; }
        }
        if (e.Button == MouseButtons.Left && FlatOnly(_editor.Tool) && PickAt(e.Location) != null)
            _editor.Tell("Lines, shapes and selections are drawn on the flat picture (on the right). Dragging here turns the model.");
        else if (e.Button == MouseButtons.Left && PickAt(e.Location) is { } px)
        {
            _drag = DragKind.Paint;
            Capture = true;
            _editor.BeginStroke(px.X, px.Y, secondary);
            return;
        }
        _drag = e.Button == MouseButtons.Middle ? DragKind.Move : DragKind.Turn;
        _cameraFrom = _camera;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Math.Abs(e.X - _dragFrom.X) + Math.Abs(e.Y - _dragFrom.Y) > 3) _moved = true;
        switch (_drag)
        {
            case DragKind.Paint:
                {
                    // Every pixel between the last and this mouse position, so fast strokes leave no gaps.
                    int steps = Math.Max(1, Math.Max(Math.Abs(e.X - _lastMouse.X), Math.Abs(e.Y - _lastMouse.Y)));
                    for (int i = 1; i <= steps; i++)
                    {
                        var p = new Point(_lastMouse.X + (e.X - _lastMouse.X) * i / steps, _lastMouse.Y + (e.Y - _lastMouse.Y) * i / steps);
                        if (PickAt(p) is { } px) _editor.StrokeTo(px.X, px.Y, connect: false);
                    }
                    _lastMouse = e.Location;
                    _editor.SetHover(PickAt(e.Location));
                    break;
                }
            case DragKind.Turn:
                _camera = _cameraFrom with
                {
                    Yaw = _cameraFrom.Yaw - (e.X - _dragFrom.X) * 0.5f,
                    Pitch = Math.Clamp(_cameraFrom.Pitch + (e.Y - _dragFrom.Y) * 0.4f, -89f, 89f),
                };
                Redraw();
                break;
            case DragKind.Move:
                {
                    var (_, _, r, u) = _cameraFrom.Basis();
                    float k = _cameraFrom.Distance / Math.Max(1f, _renderer.Focal);
                    _camera = _cameraFrom with { Target = _cameraFrom.Target - r * ((e.X - _dragFrom.X) * k) + u * ((e.Y - _dragFrom.Y) * k) };
                    Redraw();
                    break;
                }
            default:
                _editor.SetHover(PickAt(e.Location));
                break;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var drag = _drag;
        _drag = DragKind.None;
        if (drag == DragKind.Paint) _editor.EndStroke();
        else if (drag == DragKind.Turn && e.Button == MouseButtons.Right && !_moved) ShowViewMenu(e.Location);
        Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && _drag == DragKind.Paint) _editor.EndStroke();
        if (!Capture) _drag = DragKind.None;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_drag == DragKind.None) _editor.SetHover(null);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _camera = _camera with { Distance = Math.Clamp(_camera.Distance * (e.Delta > 0 ? 0.9f : 1.1f), 6f, 600f) };
        Redraw();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && PickAt(e.Location) == null) ResetView();
    }

    void ShowViewMenu(Point at)
    {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer() };
        void Add(string text, Action a) => menu.Items.Add(new ToolStripMenuItem(text, null, (_, _) => a()) { ForeColor = C.FG });
        // Mobs face north: yaw 180 looks at their front.
        Add("Front", () => Look(180, 0));
        Add("Back", () => Look(0, 0));
        Add("Right side", () => Look(90, 0));
        Add("Left side", () => Look(-90, 0));
        Add("From above", () => Look(180, 70));
        Add("From below", () => Look(180, -70));
        menu.Items.Add(new ToolStripSeparator());
        Add("Reset view", ResetView);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(this, at);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (e.Data != null && PixelClipboard.HasPicture(e.Data)) e.Effect = DragDropEffects.Copy;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (e.Data != null) Dropped?.Invoke(e.Data);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor.ImageChanged -= Redraw;
            _editor.HoverChanged -= Redraw;
            _editor.StateChanged -= Redraw;
            _bitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
