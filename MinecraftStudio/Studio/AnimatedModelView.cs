// MinecraftStudio/Studio/AnimatedModelView.cs: the Animation Studio's 3D view — the mob in the pose of the moment, the
// bone being worked on tinted — and the 3D view of structures and pieces of worlds (Structure3DWindow).
// Owns: AnimatedModelView — drawing the mesh and texture it is handed (ModelRenderer), a click on a part selects its bone,
// turning (left or right drag), moving (middle drag), zooming (wheel), views from a right click, and a ground line so
// moves and jumps show. For a structure ("world" view): views named by the compass, no ground line, zoomed out as far
// as its size needs.
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

[System.ComponentModel.DesignerCategory("Code")]
sealed class AnimatedModelView : Control
{
    readonly Func<ModelMesh?> _mesh;
    readonly Func<RgbaImage?> _texture;
    readonly Func<string?> _highlight;
    readonly Func<string> _caption;
    readonly string? _empty;
    readonly bool _world;
    float _farthest = 800f;
    readonly ModelRenderer _renderer = new();
    OrbitCamera _camera = OrbitCamera.Default;
    EntityModel? _framed;
    Bitmap? _bitmap;
    bool _stale = true;
    enum DragKind { None, Turn, Move }
    DragKind _drag;
    Point _dragFrom;
    OrbitCamera _cameraFrom;
    bool _moved;

    /// <param name="empty">What the view says with nothing to show (the mob prompt when null).</param>
    /// <param name="world">A structure in the world's directions: views from the south, north … and no ground line.</param>
    public AnimatedModelView(Func<ModelMesh?> mesh, Func<RgbaImage?> texture, Func<string?> highlight, Func<string> caption, string? empty = null, bool world = false)
    {
        _mesh = mesh;
        _texture = texture;
        _highlight = highlight;
        _caption = caption;
        _empty = empty;
        _world = world;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = C.BG;
        AccessibleName = world ? "3D view" : "Animated mob";
        AccessibleDescription = world ? "Drag to turn it; middle-drag to move; the wheel zooms; right-click for the views from each side." : "Click a part to choose its bone; drag to turn the mob; the wheel zooms.";
    }

    /// <summary>A part was clicked: its bone.</summary>
    public event Action<string>? BonePicked;

    public void Redraw() { _stale = true; Invalidate(); }

    float Aspect => ClientSize.Height > 0 ? ClientSize.Width / (float)ClientSize.Height : 1;

    public void ResetView()
    {
        if (_mesh() is { } m) _camera = Frame(m);
        Redraw();
    }

    // The camera for a newly shown model: a mob from its front, a structure from the south-east and above.
    OrbitCamera Frame(ModelMesh m)
    {
        var camera = ModelRenderer.CameraFor(m, Aspect);
        _farthest = Math.Max(800f, camera.Distance * 4);
        return _world ? camera with { Yaw = 35, Pitch = 28 } : camera;
    }

    void Render()
    {
        int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
        if (_bitmap == null || _bitmap.Width != w || _bitmap.Height != h)
        {
            _bitmap?.Dispose();
            _bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        }
        if (_mesh() is not { } mesh || _texture() is not { } tex)
        {
            using var g = Graphics.FromImage(_bitmap);
            g.Clear(C.BG);
            _stale = false;
            return;
        }
        // A new model is framed once; the pose moving does not move the camera.
        if (_framed != mesh.Model) { _camera = Frame(mesh); _framed = mesh.Model; }
        _renderer.Render(mesh, tex, _camera, new ModelViewOptions { ShowGrid = false, Highlight = _highlight() }, w, h);
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
            TextRenderer.DrawText(e.Graphics, _empty ?? "Choose a mob on the left. (Models come from Minecraft Bedrock installed on this PC, or from a pack.)", f, ClientRectangle, C.FG_DIM,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        // The ground (y = 0) under the mob, so steps, hops and jumps show.
        if (!_world && _renderer.Project(new System.Numerics.Vector3(-24, 0, 0)) is { } a && _renderer.Project(new System.Numerics.Vector3(24, 0, 0)) is { } b)
        {
            using var ground = new Pen(Color.FromArgb(70, 255, 255, 255)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            e.Graphics.DrawLine(ground, a.X, a.Y, b.X, b.Y);
        }
        TextRenderer.DrawText(e.Graphics, _caption(), f, new Point(this.Px(8), this.Px(6)), C.FG_DIM);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _dragFrom = e.Location;
        _moved = false;
        _cameraFrom = _camera;
        _drag = e.Button == MouseButtons.Middle ? DragKind.Move : DragKind.Turn;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Math.Abs(e.X - _dragFrom.X) + Math.Abs(e.Y - _dragFrom.Y) > 3) _moved = true;
        if (!_moved) return;
        switch (_drag)
        {
            case DragKind.Turn:
                _camera = _cameraFrom with
                {
                    Yaw = _cameraFrom.Yaw - (e.X - _dragFrom.X) * 0.5f,
                    Pitch = Math.Clamp(_cameraFrom.Pitch + (e.Y - _dragFrom.Y) * 0.4f, -89f, 89f),
                };
                Redraw();
                break;
            case DragKind.Move:
                var (_, _, r, u) = _cameraFrom.Basis();
                float k = _cameraFrom.Distance / Math.Max(1f, _renderer.Focal);
                _camera = _cameraFrom with { Target = _cameraFrom.Target - r * ((e.X - _dragFrom.X) * k) + u * ((e.Y - _dragFrom.Y) * k) };
                Redraw();
                break;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var drag = _drag;
        _drag = DragKind.None;
        Capture = false;
        if (_moved) return;
        // A click (no drag): left picks the bone there, right offers views.
        if (e.Button == MouseButtons.Left && _renderer.QuadAt(e.X, e.Y) is { } q) BonePicked?.Invoke(q.Bone);
        else if (e.Button == MouseButtons.Right && drag == DragKind.Turn) ShowViewMenu(e.Location);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _camera = _camera with { Distance = Math.Clamp(_camera.Distance * (e.Delta > 0 ? 0.9f : 1.1f), 6f, _farthest) };
        Redraw();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_renderer.QuadAt(e.X, e.Y) == null) ResetView();
    }

    void Look(float yaw, float pitch) { _camera = _camera with { Yaw = yaw, Pitch = pitch }; Redraw(); }

    void ShowViewMenu(Point at)
    {
        var menu = new ContextMenuStrip { Renderer = new DarkRenderer() };
        void Add(string text, Action a) => menu.Items.Add(new ToolStripMenuItem(text, null, (_, _) => a()) { ForeColor = C.FG });
        if (_world)
        {
            Add("From the south", () => Look(0, 20));
            Add("From the north", () => Look(180, 20));
            Add("From the east", () => Look(90, 20));
            Add("From the west", () => Look(-90, 20));
            Add("From above", () => Look(0, 89));
        }
        else
        {
            Add("Front", () => Look(180, 0));
            Add("Back", () => Look(0, 0));
            Add("Right side", () => Look(90, 0));
            Add("Left side", () => Look(-90, 0));
            Add("From above", () => Look(180, 70));
        }
        menu.Items.Add(new ToolStripSeparator());
        Add("Reset view", ResetView);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(this, at);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bitmap?.Dispose();
        base.Dispose(disposing);
    }
}
