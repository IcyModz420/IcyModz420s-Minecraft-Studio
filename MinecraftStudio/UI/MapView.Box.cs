// MinecraftStudio/UI/MapView.Box.cs: the box of blocks for MapView (the crop and place windows pick theirs on a map).
// Part of MapView (main part in MapView.cs). With a box tool a left-drag draws a new box, pulls an edge or a corner or
// moves the box (Draw), or puts a box of a fixed size down where it is clicked and drags it about (Place); right- or
// middle-drag still pans and the wheel zooms. The box is outlined with its size beside it (a picture inside: the structure
// seen from above), and the top blocks the map has drawn inside it can be read (TopBlocks).
using System.Drawing.Drawing2D;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.UI;

partial class MapView
{
    // ------------------------------------------------------------------ the box of blocks

    /// <summary>What a press on the box holds: edges (two at a corner), the inside, or nothing (a new box is drawn).</summary>
    [Flags]
    enum BoxGrip { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8, Inside = 16 }

    const double BlockOutlineScale = 3;     // pixels per block (at 96 dpi) from which the block under the pointer is outlined
    const int CoordinateLimit = 30_000_000; // as far as the coordinate fields go
    static readonly Color BoxColor = Color.FromArgb(255, 210, 60);

    (int MinX, int MinZ, int MaxX, int MaxZ)? _box;
    (int MinX, int MinZ, int MaxX, int MaxZ)? _pendingFrame;
    MapBoxTool _boxTool;
    Bitmap? _boxPicture;
    BoxGrip? _boxGrip;                                   // a press being dragged (None: drawing a new box)
    (int X, int Z) _boxAnchor;                           // the block it started on
    (int MinX, int MinZ, int MaxX, int MaxZ) _boxBefore; // the box when it started
    Point _boxPress;
    bool _boxMoved, _boxChanged;
    (int X, int Z)? _hoverBlock;

    /// <summary>The box of blocks outlined on the map, both corners inside it (null: none). Setting it raises no event.</summary>
    public (int MinX, int MinZ, int MaxX, int MaxZ)? Box
    {
        get => _box;
        set
        {
            _box = value is { } b ? (Math.Min(b.MinX, b.MaxX), Math.Min(b.MinZ, b.MaxZ), Math.Max(b.MinX, b.MaxX), Math.Max(b.MinZ, b.MaxZ)) : null;
            Invalidate();
        }
    }

    /// <summary>What a left-drag does to the box (None: the chunk tools, as on the world tab).</summary>
    public MapBoxTool BoxTool
    {
        get => _boxTool;
        set { _boxTool = value; Cursor = value != MapBoxTool.None || _tool != MapTool.Pan ? Cursors.Cross : Cursors.Default; Invalidate(); }
    }

    /// <summary>Drawn inside the box, its pixels spread over the box's blocks (the structure seen from above). The map disposes it when it is replaced.</summary>
    public Bitmap? BoxPicture
    {
        get => _boxPicture;
        set
        {
            if (!ReferenceEquals(_boxPicture, value)) _boxPicture?.Dispose();
            _boxPicture = value;
            Invalidate();
        }
    }

    // Close enough in that the block under the pointer is outlined.
    bool OutlinesBlocks => _scale >= BlockOutlineScale * DeviceDpi / 96.0;

    /// <summary>The box was drawn, moved or reshaped with the mouse (raised while it is dragged).</summary>
    public event EventHandler? BoxChanged;
    /// <summary>The mouse let go of a box it changed.</summary>
    public event EventHandler? BoxDropped;

    /// <summary>Centres the map on a box, zoomed so it takes about a quarter of the view (whole pixels per block when close in).</summary>
    public void FrameBox((int MinX, int MinZ, int MaxX, int MaxZ) box)
    {
        // Before the control has its real size the zoom cannot be worked out; it is done on the first resize.
        if (Width <= 1 || Height <= 1) { _pendingFrame = box; return; }
        _pendingFrame = null;
        double w = Math.Max(16, box.MaxX - box.MinX + 1.0), h = Math.Max(16, box.MaxZ - box.MinZ + 1.0);
        // Worked out at 96 dpi and scaled to the screen, so the map opens the same size on any display scaling.
        double dpi = DeviceDpi / 96.0;
        double s = Math.Min(Width / (w * 4), Height / (h * 4)) / dpi;
        if (s >= 1) s = Math.Pow(2, Math.Floor(Math.Log2(s)));
        _scale = Math.Clamp(s, 1 / 16.0, 4) * dpi;
        CenterOn((box.MinX + box.MaxX + 1) / 2.0, (box.MinZ + box.MaxZ + 1) / 2.0);
    }

    /// <summary>Centres the map on the box when none of it is in view (after its corners were typed far away).</summary>
    public void KeepBoxInView()
    {
        if (_box is not { } b || Width <= 1 || Height <= 1) return;
        var r = BoxOnScreen(b);
        if (r.Right < 0 || r.Bottom < 0 || r.Left > Width || r.Top > Height) CenterOn((b.MinX + b.MaxX + 1) / 2.0, (b.MinZ + b.MaxZ + 1) / 2.0);
    }

    /// <summary>Where a block corner is on the control (for the hidden-window checks, which press the map).</summary>
    internal PointF ScreenOf(double x, double z) => ToScreen(x, z);
    /// <summary>The block under a point of the control.</summary>
    internal (int X, int Z) BlockUnder(Point p) => BlockAt(p);
    /// <summary>The world coordinates (in blocks, with fractions) under a point of the control.</summary>
    internal (double X, double Z) WorldAt(Point p) => ToWorld(p);

    /// <summary>Whether every map tile in view is drawn (or there is nothing to draw).</summary>
    public bool AllDrawn
    {
        get
        {
            if (_dim == null) return false;
            lock (_sync) return TilesInView().All(t => _tiles.TryGetValue(t, out var tile) && tile.Done);
        }
    }

    /// <summary>
    /// Calls <paramref name="column"/> with the height and the block (null: not known) at the top of every column inside the
    /// box that the map has drawn. Returns how many columns of chunks inside the box it has not drawn yet (their tiles are
    /// drawn while they are in view).
    /// </summary>
    public long TopBlocks((int MinX, int MinZ, int MaxX, int MaxZ) box, Action<int, BlockState?> column)
    {
        long notDrawn = 0;
        int cx0 = box.MinX >> 4, cx1 = box.MaxX >> 4, cz0 = box.MinZ >> 4, cz1 = box.MaxZ >> 4;
        lock (_sync)
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    if (!_chunks.Contains(new ChunkPos(cx, cz))) continue;
                    int x0 = Math.Max(box.MinX, cx * 16), x1 = Math.Min(box.MaxX, cx * 16 + 15);
                    int z0 = Math.Max(box.MinZ, cz * 16), z1 = Math.Min(box.MaxZ, cz * 16 + 15);
                    int tx = cx >> 5, tz = cz >> 5;
                    if (!_tiles.TryGetValue((tx, tz), out var t) || !t.Done || t.Heights == null)
                    {
                        notDrawn += (long)(x1 - x0 + 1) * (z1 - z0 + 1);
                        continue;
                    }
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int i = (z - tz * TilePixels) * TilePixels + (x - tx * TilePixels);
                            short h = t.Heights[i];
                            if (h == short.MinValue) continue;
                            int id = t.BlockIds?[i] ?? -1;
                            column(h, id >= 0 ? BlockState.FromId(id) : null);
                        }
                }
        return notDrawn;
    }

    // ------------------------------------------------------------------ box mouse handling

    (int X, int Z) BlockAt(Point p)
    {
        var (wx, wz) = ToWorld(p);
        return ((int)Math.Clamp(Math.Floor(wx), -CoordinateLimit - 1.0, CoordinateLimit + 1.0), (int)Math.Clamp(Math.Floor(wz), -CoordinateLimit - 1.0, CoordinateLimit + 1.0));
    }

    /// <summary>The box on screen (it may reach far past the view).</summary>
    RectangleF BoxOnScreen((int MinX, int MinZ, int MaxX, int MaxZ) b)
    {
        var a = ToScreen(b.MinX, b.MinZ);
        var c = ToScreen(b.MaxX + 1.0, b.MaxZ + 1.0);
        return RectangleF.FromLTRB(a.X, a.Y, c.X, c.Y);
    }

    /// <summary>The box as drawn and held: never smaller than a few pixels, so a small box far out stays in sight and in reach.</summary>
    RectangleF ShownBox((int MinX, int MinZ, int MaxX, int MaxZ) b)
    {
        var r = BoxOnScreen(b);
        float least = this.Px(6);
        if (r.Width < least) r = new RectangleF(r.X + (r.Width - least) / 2, r.Y, least, r.Height);
        if (r.Height < least) r = new RectangleF(r.X, r.Y + (r.Height - least) / 2, r.Width, least);
        return r;
    }

    /// <summary>What a press at a point would hold: an edge or a corner (within a few pixels), the inside, or nothing.</summary>
    BoxGrip GripAt(Point p)
    {
        if (_box is not { } b || _dim == null) return BoxGrip.None;
        var r = ShownBox(b);
        float m = this.Px(5);
        if (p.X < r.Left - m || p.X > r.Right + m || p.Y < r.Top - m || p.Y > r.Bottom + m) return BoxGrip.None;
        // A box of a fixed size only moves.
        if (_boxTool == MapBoxTool.Place) return BoxGrip.Inside;
        // Edges are held only where the box is big enough on screen to be moved by its inside too.
        var grip = BoxGrip.None;
        if (r.Width >= 3 * m)
        {
            float left = Math.Abs(p.X - r.Left), right = Math.Abs(p.X - r.Right);
            if (Math.Min(left, right) <= m) grip |= left <= right ? BoxGrip.Left : BoxGrip.Right;
        }
        if (r.Height >= 3 * m)
        {
            float top = Math.Abs(p.Y - r.Top), bottom = Math.Abs(p.Y - r.Bottom);
            if (Math.Min(top, bottom) <= m) grip |= top <= bottom ? BoxGrip.Top : BoxGrip.Bottom;
        }
        if (grip != BoxGrip.None) return grip;
        return r.Contains(p) ? BoxGrip.Inside : BoxGrip.None;
    }

    /// <summary>The pointer when no button is down: what a press there would do.</summary>
    Cursor RestCursor(Point p)
    {
        if (_boxTool == MapBoxTool.None) return _tool == MapTool.Pan ? Cursors.Default : Cursors.Cross;
        return GripAt(p) switch
        {
            BoxGrip.Inside => Cursors.SizeAll,
            BoxGrip.Left or BoxGrip.Right => Cursors.SizeWE,
            BoxGrip.Top or BoxGrip.Bottom => Cursors.SizeNS,
            (BoxGrip.Left | BoxGrip.Top) or (BoxGrip.Right | BoxGrip.Bottom) => Cursors.SizeNWSE,
            (BoxGrip.Right | BoxGrip.Top) or (BoxGrip.Left | BoxGrip.Bottom) => Cursors.SizeNESW,
            _ => Cursors.Cross,
        };
    }

    bool BoxMouseDown(MouseEventArgs e)
    {
        if (_boxTool == MapBoxTool.None || e.Button != MouseButtons.Left || _dim == null) return false;
        var grip = GripAt(e.Location);
        _boxPress = e.Location;
        _boxMoved = _boxChanged = false;
        _boxAnchor = BlockAt(e.Location);
        if (_boxTool == MapBoxTool.Place && grip == BoxGrip.None)
        {
            // Pressed beside the footprint: it is put down there, centred on that block, and follows the drag.
            var b = _box ?? (_boxAnchor.X, _boxAnchor.Z, _boxAnchor.X, _boxAnchor.Z);
            int sx = b.MaxX - b.MinX, sz = b.MaxZ - b.MinZ;
            int x0 = _boxAnchor.X - sx / 2, z0 = _boxAnchor.Z - sz / 2;
            SetBoxFromMouse((x0, z0, x0 + sx, z0 + sz));
            grip = BoxGrip.Inside;
        }
        _boxGrip = grip;
        _boxBefore = _box ?? (_boxAnchor.X, _boxAnchor.Z, _boxAnchor.X, _boxAnchor.Z);
        Invalidate();
        return true;
    }

    void DragBox(Point p)
    {
        if (_boxGrip is not BoxGrip grip) return;
        // A press that has not moved yet is a click: nothing changes.
        if (!_boxMoved && Math.Abs(p.X - _boxPress.X) + Math.Abs(p.Y - _boxPress.Y) <= 3) return;
        _boxMoved = true;
        var at = BlockAt(p);
        var b = _boxBefore;
        if (grip == BoxGrip.None)
            SetBoxFromMouse((Math.Min(_boxAnchor.X, at.X), Math.Min(_boxAnchor.Z, at.Z), Math.Max(_boxAnchor.X, at.X), Math.Max(_boxAnchor.Z, at.Z)));
        else if (grip == BoxGrip.Inside)
        {
            int dx = at.X - _boxAnchor.X, dz = at.Z - _boxAnchor.Z;
            SetBoxFromMouse((b.MinX + dx, b.MinZ + dz, b.MaxX + dx, b.MaxZ + dz));
        }
        else
        {
            // An edge goes to the block boundary nearest the pointer, never past the opposite edge.
            var (wx, wz) = ToWorld(p);
            int ex = (int)Math.Clamp(Math.Floor(wx + 0.5), -CoordinateLimit, CoordinateLimit), ez = (int)Math.Clamp(Math.Floor(wz + 0.5), -CoordinateLimit, CoordinateLimit);
            int x0 = b.MinX, z0 = b.MinZ, x1 = b.MaxX, z1 = b.MaxZ;
            if ((grip & BoxGrip.Left) != 0) x0 = Math.Min(ex, x1);
            if ((grip & BoxGrip.Right) != 0) x1 = Math.Max(ex - 1, x0);
            if ((grip & BoxGrip.Top) != 0) z0 = Math.Min(ez, z1);
            if ((grip & BoxGrip.Bottom) != 0) z1 = Math.Max(ez - 1, z0);
            SetBoxFromMouse((x0, z0, x1, z1));
        }
    }

    void SetBoxFromMouse((int MinX, int MinZ, int MaxX, int MaxZ) b)
    {
        static int Limit(int v) => Math.Clamp(v, -CoordinateLimit, CoordinateLimit);
        b = (Limit(b.MinX), Limit(b.MinZ), Limit(b.MaxX), Limit(b.MaxZ));
        if (_box == b) return;
        _box = b;
        _boxChanged = true;
        Invalidate();
        BoxChanged?.Invoke(this, EventArgs.Empty);
    }

    bool BoxMouseUp(MouseEventArgs e)
    {
        if (_boxGrip == null || e.Button != MouseButtons.Left) return false;
        EndBoxDrag();
        Cursor = RestCursor(e.Location);
        return true;
    }

    /// <summary>The press is over (let go, or the mouse was taken away): the box stays where it was dragged.</summary>
    void EndBoxDrag()
    {
        _boxGrip = null;
        Invalidate();
        if (!_boxChanged) return;
        _boxChanged = false;
        BoxDropped?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ box painting

    void DrawBox(Graphics g)
    {
        // Close in, the block under the pointer is outlined (a box picks single blocks).
        if (_boxTool != MapBoxTool.None && _hoverBlock is (int hx, int hz) && _mouseInside && _boxGrip == null && OutlinesBlocks)
        {
            var p = ToScreen(hx, hz);
            using var pen = new Pen(Color.FromArgb(200, 255, 255, 255));
            g.DrawRectangle(pen, p.X, p.Y, (float)_scale, (float)_scale);
        }
        if (_box is not { } b) return;
        var real = BoxOnScreen(b);
        var r = ShownBox(b);
        // Only the part in view is drawn: a box far out would overflow the drawing.
        float edge = this.Px(8);
        var view = new RectangleF(-edge, -edge, Width + 2 * edge, Height + 2 * edge);
        var shown = RectangleF.Intersect(r, view);
        if (shown.Width <= 0 || shown.Height <= 0) return;
        var inView = RectangleF.Intersect(real, view);
        using (var fill = new SolidBrush(Color.FromArgb(_boxPicture != null ? 70 : 50, BoxColor)))
            g.FillRectangle(fill, shown);
        if (_boxPicture is { } pic && real.Width >= 2 && real.Height >= 2 && inView.Width > 0 && inView.Height > 0)
        {
            // The picture's part in view, its pixels spread over the box (crisp when close in).
            float fx = pic.Width / real.Width, fz = pic.Height / real.Height;
            var src = new RectangleF((inView.X - real.X) * fx, (inView.Y - real.Y) * fz, inView.Width * fx, inView.Height * fz);
            var was = g.InterpolationMode;
            g.InterpolationMode = _scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(pic, inView, src, GraphicsUnit.Pixel);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.InterpolationMode = was;
        }
        g.SmoothingMode = SmoothingMode.None;
        using (var halo = new Pen(Color.FromArgb(170, 0, 0, 0), this.Px(1) * 4f))
            g.DrawRectangle(halo, shown.X, shown.Y, shown.Width, shown.Height);
        using (var line = new Pen(BoxColor, Math.Max(1.5f, this.Px(1) * 2f)))
            g.DrawRectangle(line, shown.X, shown.Y, shown.Width, shown.Height);
        // Handles at the corners and the middle of each edge, where the box can be pulled.
        if (_boxTool == MapBoxTool.Draw && r.Width >= this.Px(15) && r.Height >= this.Px(15))
        {
            float h = this.Px(7);
            using var face = new SolidBrush(Color.White);
            using var rim = new Pen(Color.FromArgb(200, 0, 0, 0));
            foreach (var (x, y) in new[] { (r.Left, r.Top), (r.Right, r.Top), (r.Left, r.Bottom), (r.Right, r.Bottom),
                ((r.Left + r.Right) / 2, r.Top), ((r.Left + r.Right) / 2, r.Bottom), (r.Left, (r.Top + r.Bottom) / 2), (r.Right, (r.Top + r.Bottom) / 2) })
            {
                if (!view.Contains(x, y)) continue;
                g.FillRectangle(face, x - h / 2, y - h / 2, h, h);
                g.DrawRectangle(rim, x - h / 2, y - h / 2, h, h);
            }
        }
        // Its size above it (inside it, at the top of the view, when there is no room above).
        string text = $"{b.MaxX - b.MinX + 1} × {b.MaxZ - b.MinZ + 1} blocks";
        using var font = Theme.Ui(8.5f, FontStyle.Bold);
        var size = TextRenderer.MeasureText(text, font);
        int pad = this.Px(3);
        var label = new Rectangle(0, 0, size.Width + 2 * pad, size.Height + this.Px(2));
        label.X = (int)Math.Clamp(r.Left, pad, Math.Max(pad, Width - label.Width - pad));
        label.Y = (int)r.Top - label.Height - pad;
        if (label.Y < pad) label.Y = (int)Math.Max(r.Top + pad, pad);
        if (label.Y > Height) return;
        using var back = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
        g.FillRectangle(back, label);
        TextRenderer.DrawText(g, text, font, label, BoxColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    /// <summary>While the box is dragged: its corners beside the pointer.</summary>
    void DrawBoxDragLabel(Graphics g)
    {
        if (_boxGrip == null || _box is not { } b || (!_boxMoved && _boxTool != MapBoxTool.Place)) return;
        string text = _boxTool == MapBoxTool.Place ? $"Low corner x {b.MinX}, z {b.MinZ}" : $"x {b.MinX} to {b.MaxX}, z {b.MinZ} to {b.MaxZ}";
        GestureLabel(g, text, _mouse);
    }
}
