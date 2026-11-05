// MinecraftStudio/Studio/SkinModelView.cs: The skin on the player model (drawn by SkinRenderer, which also
// tells which skin pixel is under the mouse, so painting lands exactly there).
// Owns: SkinModelView.
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

/// <summary>
/// The skin on the player model (drawn by <see cref="SkinRenderer"/>, which also tells which skin pixel is under the
/// mouse, so painting lands exactly there). Left-drag paints (or turns the model when it starts beside it), Ctrl+left
/// paints with the second colour, right-drag turns, middle-drag moves, the wheel zooms, a right click offers views.
/// </summary>
[System.ComponentModel.DesignerCategory("Code")]
sealed class SkinModelView : Control
{
    readonly SkinEditor _editor;
    readonly SkinRenderer _renderer = new();
    OrbitCamera _camera = OrbitCamera.Default;
    Bitmap? _bitmap;
    bool _stale = true;

    enum DragKind { None, Paint, Turn, Move }
    DragKind _drag;
    Point _dragFrom, _lastMouse;
    OrbitCamera _cameraFrom;
    bool _moved;

    public SkinModelView(SkinEditor editor)
    {
        _editor = editor;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = C.BG;
        editor.ImageChanged += Redraw;
        editor.LayoutChanged += Redraw;
        editor.HoverChanged += Redraw;
        editor.StateChanged += Redraw;
        AllowDrop = true;
        AccessibleName = "3D skin view";
        AccessibleDescription = "Left-drag paints on the model; right-drag turns it; the mouse wheel zooms.";
    }

    public void Redraw()
    {
        _stale = true;
        Invalidate();
    }

    public void ResetView()
    {
        _camera = OrbitCamera.Default;
        Redraw();
    }

    public void Look(float yaw, float pitch)
    {
        _camera = _camera with { Yaw = yaw, Pitch = pitch };
        Redraw();
    }

    SkinViewOptions Options()
    {
        int size = _editor.Skin.Image.Width;
        int hover = _editor.Hover is { } h ? h.Y * size + h.X : -1;
        int mirror = _editor.Mirror && _editor.Hover is { } hm && _editor.Pixels.Mirror(hm.X, hm.Y) is { } m ? m.Y * size + m.X : -1;
        return new SkinViewOptions
        {
            PaintOverlay = _editor.PaintOverlay, ShowOverlay = _editor.ShowOverlay, ShowGrid = _editor.ShowGrid,
            HiddenParts = _editor.HiddenParts, Hover = hover, HoverMirror = mirror,
        };
    }

    void Render()
    {
        int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
        _renderer.Render(_editor.Skin, _camera, Options(), w, h);
        if (_bitmap == null || _bitmap.Width != w || _bitmap.Height != h)
        {
            _bitmap?.Dispose();
            _bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        }
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
        TextRenderer.DrawText(e.Graphics, _editor.PaintOverlay ? "Painting the second layer" : "Painting the base layer", f, new Point(this.Px(8), this.Px(6)), C.FG_DIM);
    }

    // ------------------------------------------------------------------ mouse

    (int X, int Y)? PickAt(Point p)
    {
        if (_stale || _renderer.Width != Math.Max(1, ClientSize.Width) || _renderer.Height != Math.Max(1, ClientSize.Height)) Render();
        return _renderer.PickAt(p.X, p.Y);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _dragFrom = _lastMouse = e.Location;
        _moved = false;
        bool secondary = (ModifierKeys & Keys.Control) != 0;
        if (e.Button == MouseButtons.Left && (_editor.Tool == PaintTool.Picker || (ModifierKeys & Keys.Alt) != 0))
        {
            // The eyedropper (or Alt+click with any tool) takes the colour that is seen there, on either layer.
            PickAt(e.Location);
            if (_renderer.VisibleAt(e.X, e.Y) is { } seen) { _editor.PickColor(seen.X, seen.Y, secondary); return; }
        }
        if (e.Button == MouseButtons.Left && _editor.Tool is PaintTool.Select or PaintTool.Line or PaintTool.Rectangle or PaintTool.Ellipse && PickAt(e.Location) != null)
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
        // Losing the mouse mid-stroke (a dialog, Alt+Tab) still ends the stroke as one undo step.
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
        _camera = _camera with { Distance = Math.Clamp(_camera.Distance * (e.Delta > 0 ? 0.9f : 1.1f), 18f, 220f) };
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
        Add("Front", () => Look(0, 0));
        Add("Back", () => Look(180, 0));
        Add("Right side", () => Look(-90, 0));
        Add("Left side", () => Look(90, 0));
        Add("From above", () => Look(0, 70));
        Add("From below", () => Look(0, -70));
        menu.Items.Add(new ToolStripSeparator());
        Add("Reset view", ResetView);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(this, at);
    }

    /// <summary>Something was dragged onto the view and let go (picture files, or a picture from another program).</summary>
    public event Action<IDataObject>? Dropped;

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
            _editor.LayoutChanged -= Redraw;
            _editor.HoverChanged -= Redraw;
            _editor.StateChanged -= Redraw;
            _bitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
