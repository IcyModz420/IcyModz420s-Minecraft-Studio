// MinecraftStudio/UI/MapView.Input.cs: input for MapView.
// Part of MapView (main part in MapView.cs).
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.UI;

partial class MapView
{
    // ------------------------------------------------------------------ input

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (BoxMouseDown(e)) return;
        // Left-drag uses the tool (Shift+drag is a rectangle with any tool); right/middle always pan.
        if (e.Button == MouseButtons.Left && (_tool != MapTool.Pan || (ModifierKeys & Keys.Shift) != 0))
        {
            _gesture = _tool == MapTool.Pan ? MapTool.Rectangle : _tool;
            _gestureSubtract = Subtract || (ModifierKeys & Keys.Alt) != 0;
            BeginChange(_gesture switch { MapTool.Brush => "Brush stroke", MapTool.Circle => "Circle", MapTool.Lasso => "Lasso", _ => "Rectangle" }
                + (_gestureSubtract ? " (remove)" : ""));
            _shapeStart = _shapeNow = e.Location;
            _lasso.Clear();
            _lasso.Add(e.Location);
            if (_gesture == MapTool.Brush) PaintBrush(e.Location);
            Invalidate();
            return;
        }
        if (e.Button is MouseButtons.Left or MouseButtons.Middle or MouseButtons.Right)
        {
            _dragging = true;
            _dragStart = e.Location;
            _dragOriginX = _originX;
            _dragOriginZ = _originZ;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouse = e.Location;
        _mouseInside = true;
        if (_gesture is MapTool g)
        {
            _shapeNow = e.Location;
            if (g == MapTool.Lasso && Math.Abs(e.X - _lasso[^1].X) + Math.Abs(e.Y - _lasso[^1].Y) >= 3) _lasso.Add(e.Location);
            if (g == MapTool.Brush) PaintBrush(e.Location);
            Invalidate();
            return;
        }
        if (_boxGrip != null) { DragBox(e.Location); ShowHover(e.Location); return; }
        if (_tool == MapTool.Brush) Invalidate();
        if (_dragging)
        {
            _originX = _dragOriginX - (e.X - _dragStart.X) / _scale;
            _originZ = _dragOriginZ - (e.Y - _dragStart.Y) / _scale;
            _wake.Set();
            Invalidate();
            return;
        }
        if (_boxTool != MapBoxTool.None) Cursor = RestCursor(e.Location);
        ShowHover(e.Location);
    }

    /// <summary>The block and chunk under the pointer: outlined, and described for the status line.</summary>
    void ShowHover(Point p)
    {
        var (wx, wz) = ToWorld(p);
        int bx = (int)Math.Floor(wx), bz = (int)Math.Floor(wz);
        var chunk = new ChunkPos(bx >> 4, bz >> 4);
        if (_hover != chunk) { _hover = chunk; Invalidate(); }
        if (_hoverBlock != (bx, bz)) { _hoverBlock = (bx, bz); if (_boxTool != MapBoxTool.None && OutlinesBlocks) Invalidate(); }
        HoverInfo?.Invoke(this, Describe(bx, bz));
    }

    /// <summary>The height of the top block drawn at a column (null where the map has not drawn it yet).</summary>
    public int? HeightAt(int bx, int bz)
    {
        lock (_sync)
        {
            int tx = (int)Math.Floor(bx / (double)TilePixels), tz = (int)Math.Floor(bz / (double)TilePixels);
            if (!_tiles.TryGetValue((tx, tz), out var t) || !t.Done || t.Heights == null) return null;
            short h = t.Heights[(bz - tz * TilePixels) * TilePixels + (bx - tx * TilePixels)];
            return h == short.MinValue ? null : h;
        }
    }

    string Describe(int bx, int bz)
    {
        var chunk = new ChunkPos(bx >> 4, bz >> 4);
        string s = $"Block {bx}, {bz}   ·   Chunk {chunk.X}, {chunk.Z}   ·   Region r.{chunk.X >> 5}.{chunk.Z >> 5}";
        string slime = _overlay == MapOverlay.SlimeChunks && _slime?.Invoke(chunk.X, chunk.Z) == true ? "   ·   slime chunk" : "";
        if (!_chunks.Contains(chunk)) return s + slime + "   ·   (not generated)";
        s += slime;
        s += StructuresAt(bx, bz);
        lock (_sync)
        {
            int tx = (int)Math.Floor(bx / (double)TilePixels), tz = (int)Math.Floor(bz / (double)TilePixels);
            if (_tiles.TryGetValue((tx, tz), out var t) && t.Done && t.BlockIds != null)
            {
                int px = bx - tx * TilePixels, pz = bz - tz * TilePixels;
                int i = pz * TilePixels + px;
                int id = t.BlockIds[i];
                if (id >= 0) s += $"   ·   {BlockState.FromId(id)} at y={t.Heights![i]}";
                long inh = t.Inhabited[(chunk.Z - tz * TileChunks) * TileChunks + (chunk.X - tx * TileChunks)];
                if (inh > 0) s += $"   ·   inhabited {TimeSpan.FromSeconds(inh / 20.0):g}";
                if (t.Biomes is { } cells && cells[(pz >> 2) * (TilePixels / 4) + (px >> 2)] is ushort b and > 0 && b - 1 < Core.Rendering.Biomes.All.Count)
                    s += $"   ·   {Core.Rendering.Biomes.All[b - 1].Name.Replace('_', ' ')}";
            }
        }
        return s;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (BoxMouseUp(e)) return;
        if (_gesture is MapTool g && e.Button == MouseButtons.Left)
        {
            _gesture = null;
            _shapeNow = e.Location;
            switch (g)
            {
                case MapTool.Rectangle:
                    {
                        var a = ToWorld(new Point(Math.Min(_shapeStart.X, _shapeNow.X), Math.Min(_shapeStart.Y, _shapeNow.Y)));
                        var b = ToWorld(new Point(Math.Max(_shapeStart.X, _shapeNow.X), Math.Max(_shapeStart.Y, _shapeNow.Y)));
                        int cx0 = (int)Math.Floor(a.X / 16), cz0 = (int)Math.Floor(a.Z / 16), cx1 = (int)Math.Floor(b.X / 16), cz1 = (int)Math.Floor(b.Z / 16);
                        Apply(p => p.X >= cx0 && p.X <= cx1 && p.Z >= cz0 && p.Z <= cz1);
                        break;
                    }
                case MapTool.Circle:
                    {
                        var c = ToWorld(_shapeStart);
                        double r = Math.Sqrt(Math.Pow(_shapeNow.X - _shapeStart.X, 2) + Math.Pow(_shapeNow.Y - _shapeStart.Y, 2)) / _scale;
                        Apply(p => Math.Pow(p.X * 16 + 8 - c.X, 2) + Math.Pow(p.Z * 16 + 8 - c.Z, 2) <= r * r || (p.X == (int)Math.Floor(c.X / 16) && p.Z == (int)Math.Floor(c.Z / 16)));
                        break;
                    }
                case MapTool.Lasso:
                    {
                        _lasso.Add(e.Location);
                        var poly = _lasso.Select(ToWorld).ToArray();
                        if (poly.Length >= 3) Apply(p => Inside(poly, p.X * 16 + 8, p.Z * 16 + 8));
                        _lasso.Clear();
                        break;
                    }
            }
            EndChange();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }
        if (_dragging)
        {
            _dragging = false;
            Cursor = RestCursor(e.Location);
            bool moved = Math.Abs(e.X - _dragStart.X) + Math.Abs(e.Y - _dragStart.Y) > 3;
            // A right click that did not pan: the menu for that block.
            if (!moved && e.Button == MouseButtons.Right && _dim != null)
            {
                var (wx, wz) = ToWorld(e.Location);
                int bx = (int)Math.Floor(wx), bz = (int)Math.Floor(wz);
                BlockMenu?.Invoke(this, new MapBlockClick(bx, HeightAt(bx, bz), bz, e.Location));
                return;
            }
            if (!moved && e.Button == MouseButtons.Left && (ModifierKeys & Keys.Control) != 0 && _hover is ChunkPos h && _chunks.Contains(h))
            {
                BeginChange("Toggle chunk");
                if (!Selection.Remove(h)) Selection.Add(h);
                EndChange();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (_boxGrip != null && !Capture) EndBoxDrag();
        if (_gesture == null || Capture) return;
        _gesture = null;
        _lasso.Clear();
        EndChange();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && _hover is ChunkPos h && _chunks.Contains(h)) ChunkActivated?.Invoke(this, h);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomBy(e.Delta > 0 ? 1.25 : 0.8, e.Location);
        // The wheel zooms the map only: a scrolling window around it (a dialog on a small screen) stays where it is.
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        _hoverBlock = null;
        _mouseInside = false;
        Invalidate();
    }

    /// <summary>Adds (or removes) every existing chunk the test accepts.</summary>
    void Apply(Func<ChunkPos, bool> inShape)
    {
        foreach (var p in _chunks)
            if (inShape(p))
            {
                if (_gestureSubtract) Selection.Remove(p); else Selection.Add(p);
            }
    }

    /// <summary>Brush: every chunk whose centre is within the brush circle around the mouse.</summary>
    void PaintBrush(Point at)
    {
        var (wx, wz) = ToWorld(at);
        int cx = (int)Math.Floor(wx / 16), cz = (int)Math.Floor(wz / 16), r = Math.Max(0, BrushRadius);
        bool changed = false;
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dz * dz > (r + 0.5) * (r + 0.5)) continue;
                var p = new ChunkPos(cx + dx, cz + dz);
                if (!_chunks.Contains(p)) continue;
                changed |= _gestureSubtract ? Selection.Remove(p) : Selection.Add(p);
            }
        if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Point-in-polygon (even-odd) in world block coordinates.</summary>
    static bool Inside((double X, double Z)[] poly, double x, double z)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].Z > z) != (poly[j].Z > z) && x < (poly[j].X - poly[i].X) * (z - poly[i].Z) / (poly[j].Z - poly[i].Z) + poly[i].X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Adds every existing chunk within <paramref name="by"/> chunks of the selection.</summary>
    public void GrowSelection(int by = 1)
    {
        BeginChange("Grow selection");
        var add = new List<ChunkPos>();
        foreach (var p in Selection)
            for (int dz = -by; dz <= by; dz++)
                for (int dx = -by; dx <= by; dx++)
                {
                    var n = new ChunkPos(p.X + dx, p.Z + dz);
                    if (_chunks.Contains(n)) add.Add(n);
                }
        Selection.UnionWith(add);
        EndChange();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    /// <summary>Removes selected chunks that are within <paramref name="by"/> chunks of an unselected one.</summary>
    public void ShrinkSelection(int by = 1)
    {
        BeginChange("Shrink selection");
        var remove = Selection.Where(p =>
        {
            for (int dz = -by; dz <= by; dz++)
                for (int dx = -by; dx <= by; dx++)
                    if (!Selection.Contains(new ChunkPos(p.X + dx, p.Z + dz))) return true;
            return false;
        }).ToList();
        Selection.ExceptWith(remove);
        EndChange();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    /// <summary>Adds (or replaces the selection with) the existing chunks a test accepts.</summary>
    public void SelectWhere(Func<ChunkPos, bool> test, bool add, string step = "Select by rule")
    {
        BeginChange(step);
        if (!add) Selection.Clear();
        Selection.UnionWith(_chunks.Where(test));
        EndChange();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = 64 / _scale;
        switch (e.KeyCode)
        {
            case Keys.Left: _originX -= step; break;
            case Keys.Right: _originX += step; break;
            case Keys.Up: _originZ -= step; break;
            case Keys.Down: _originZ += step; break;
            case Keys.Add or Keys.Oemplus: ZoomBy(1.25); return;
            case Keys.Subtract or Keys.OemMinus: ZoomBy(0.8); return;
            case Keys.Escape when _gesture == null:
                BeginChange("Clear selection");
                Selection.Clear();
                EndChange();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                break;
            case Keys.Z when e.Control && !e.Shift: Undo(); e.Handled = true; return;
            case Keys.Y when e.Control:
            case Keys.Z when e.Control && e.Shift: Redo(); e.Handled = true; return;
            default: return;
        }
        _wake.Set();
        Invalidate();
    }

    public void SelectAll()
    {
        BeginChange("Select all");
        Selection.UnionWith(_chunks);
        EndChange();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void InvertSelection()
    {
        BeginChange("Invert selection");
        var inv = _chunks.Where(p => !Selection.Contains(p)).ToList();
        Selection.Clear();
        Selection.UnionWith(inv);
        EndChange();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    /// <summary>Replaces the selection; <paramref name="step"/> names it in the undo history (null: not recorded).</summary>
    public void SetSelection(IEnumerable<ChunkPos> chunks, string? step = "Change selection")
    {
        if (step != null) BeginChange(step);
        Selection.Clear();
        Selection.UnionWith(chunks.Where(_chunks.Contains));
        if (step != null) EndChange();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_pendingCenter is (double x, double z) && Width > 1 && Height > 1) CenterOn(x, z);
        if (_pendingFrame is { } box && Width > 1 && Height > 1) FrameBox(box);
        _wake.Set();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stop = true;
            _wake.Set();
            _worker?.Join(2000);
            lock (_sync) foreach (var t in _tiles.Values) { t.Image?.Dispose(); t.BiomeImage?.Dispose(); }
            _wake.Dispose();
            _boxPicture?.Dispose();
            _slimeImage?.Dispose();
        }
        base.Dispose(disposing);
    }
}
