// MinecraftStudio/Studio/PixelCanvas.Mouse.cs: what the mouse does on a pixel editor's flat picture.
// Part of PixelCanvas (main part in PixelCanvas.cs).
// Owns: painting strokes (shapes may be dragged past the edge; Shift keeps them straight or square), the eyedropper (Alt),
// the Select tool (drag to select, drag inside to move, Ctrl+drag to move a copy, click to deselect, double-click selects
// a whole face), dragging floating
// pixels, moving the picture (middle button or Ctrl+Shift), zooming at the mouse and fitting.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

partial class PixelCanvas
{
    enum DragKind { None, Paint, Pan, Select, Float }
    DragKind _drag;
    Point _panFrom;
    PointF _offsetFrom;
    (int X, int Y) _anchor, _grab;
    bool _dragMoved;

    /// <summary>The pixel under a screen point, relative to the area (it may be outside the area).</summary>
    protected (int X, int Y) RawPixel(Point p) => ((int)MathF.Floor((p.X - Offset.X) / Zoom), (int)MathF.Floor((p.Y - Offset.Y) / Zoom));

    bool Inside((int X, int Y) p) => View is { } v && p.X >= 0 && p.Y >= 0 && p.X < v.Area.Width && p.Y < v.Area.Height;

    (int X, int Y) Clamp((int X, int Y) p) => View is { } v ? (Math.Clamp(p.X, 0, v.Area.Width - 1), Math.Clamp(p.Y, 0, v.Area.Height - 1)) : p;

    static bool IsShape(PaintTool t) => t is PaintTool.Line or PaintTool.Rectangle or PaintTool.Ellipse;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (View == null) return;
        bool pan = e.Button == MouseButtons.Middle || e.Button == MouseButtons.Left && (ModifierKeys & (Keys.Shift | Keys.Control)) == (Keys.Shift | Keys.Control);
        if (pan)
        {
            _drag = DragKind.Pan;
            _panFrom = e.Location;
            _offsetFrom = Offset;
            Capture = true;
            return;
        }
        if (e.Button is not (MouseButtons.Left or MouseButtons.Right)) return;
        var raw = RawPixel(e.Location);
        bool secondary = e.Button == MouseButtons.Right;
        if (Inside(raw) && (Editor.Tool == PaintTool.Picker || (ModifierKeys & Keys.Alt) != 0))
        {
            Editor.PickColor(raw.X, raw.Y, secondary);
            return;
        }
        var session = Editor.Session;
        if (session.Floating is { } f)
        {
            // Floating pixels are dragged with any tool; a click beside them puts them down (and paints nothing).
            if (f.Bounds.Contains(raw.X, raw.Y)) { StartFloat(raw, f); return; }
            session.Commit();
            if (Editor.Tool != PaintTool.Select) return;
        }
        if (Editor.Tool == PaintTool.Select)
        {
            // The second press of a double-click is the face selection's (OnMouseDoubleClick), not a new drag.
            if (e.Clicks >= 2) return;
            if (session.Selection is { } sel && sel.Contains(raw.X, raw.Y) && session.Lift(keepCopy: (ModifierKeys & Keys.Control) != 0))
            {
                StartFloat(raw, session.Floating!);
                return;
            }
            _drag = DragKind.Select;
            _anchor = Clamp(raw);
            _dragMoved = false;
            Capture = true;
            return;
        }
        if (!Inside(raw)) return;
        _drag = DragKind.Paint;
        Capture = true;
        Editor.BeginStroke(raw.X, raw.Y, secondary);
    }

    void StartFloat((int X, int Y) raw, FloatingPixels f)
    {
        _drag = DragKind.Float;
        _grab = (raw.X - f.X, raw.Y - f.Y);
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (View == null) return;
        var raw = RawPixel(e.Location);
        switch (_drag)
        {
            case DragKind.Pan:
                Offset = new PointF(_offsetFrom.X + e.X - _panFrom.X, _offsetFrom.Y + e.Y - _panFrom.Y);
                Invalidate();
                return;
            case DragKind.Float:
                Editor.Session.MoveFloating(raw.X - _grab.X, raw.Y - _grab.Y);
                return;
            case DragKind.Select:
                {
                    var c = Clamp(raw);
                    if (c != _anchor) _dragMoved = true;
                    if (_dragMoved) Editor.Session.Select(PixelRect.FromCorners(_anchor.X, _anchor.Y, c.X, c.Y));
                    return;
                }
            case DragKind.Paint:
                Editor.SetHover(Inside(raw) ? raw : null);
                // Shapes may be pulled past the edge (they are cut there); freehand strokes only paint inside.
                if (IsShape(Editor.Tool)) { var c = Clamp(raw); Editor.StrokeTo(c.X, c.Y, connect: true, constrain: (ModifierKeys & Keys.Shift) != 0); }
                else if (Inside(raw)) Editor.StrokeTo(raw.X, raw.Y, connect: true);
                return;
        }
        Editor.SetHover(Inside(raw) ? raw : null);
        var s = Editor.Session;
        bool movable = s.Floating is { } f ? f.Bounds.Contains(raw.X, raw.Y) : Editor.Tool == PaintTool.Select && s.Selection is { } sel && sel.Contains(raw.X, raw.Y);
        Cursor = movable ? Cursors.SizeAll : Inside(raw) ? Cursors.Cross : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var drag = _drag;
        _drag = DragKind.None;
        if (drag == DragKind.Paint) Editor.EndStroke();
        else if (drag == DragKind.Select && !_dragMoved) Editor.Session.Select(null);
        Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        // Losing the mouse mid-stroke (a dialog, Alt+Tab) still ends the stroke as one undo step.
        if (!Capture && _drag == DragKind.Paint) Editor.EndStroke();
        if (!Capture) _drag = DragKind.None;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_drag == DragKind.None) Editor.SetHover(null);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float old = Zoom;
        Zoom = Math.Clamp(e.Delta > 0 ? Zoom * 1.25f : Zoom / 1.25f, 1f, MaxZoom);
        if (Zoom >= 4) Zoom = MathF.Round(Zoom);
        // Keep the point under the mouse where it is.
        Offset = new PointF(e.X - (e.X - Offset.X) * Zoom / old, e.Y - (e.Y - Offset.Y) * Zoom / old);
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var raw = RawPixel(e.Location);
        if (e.Button == MouseButtons.Middle || !Inside(raw)) { Fit(); return; }
        // With the Select tool, a double-click selects the whole face (or the whole picture) under the mouse.
        if (e.Button == MouseButtons.Left && Editor.Tool == PaintTool.Select && Editor.Session.Floating == null && View is { } v)
            Editor.Session.Select(FaceAt(raw.X, raw.Y) ?? new PixelRect(0, 0, v.Area.Width, v.Area.Height));
    }
}
