// MinecraftStudio/UI/MapView.Painting.cs: painting for MapView.
// Part of MapView (main part in MapView.cs).
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.UI;

partial class MapView
{
    // ------------------------------------------------------------------ painting

    PointF ToScreen(double wx, double wz) => new((float)((wx - _originX) * _scale), (float)((wz - _originZ) * _scale));
    (double X, double Z) ToWorld(Point p) => (_originX + p.X / _scale, _originZ + p.Y / _scale);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_dim == null)
        {
            TextRenderer.DrawText(g, _emptyText, Font, ClientRectangle, C.FG_DIM, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        g.InterpolationMode = _scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        int x0 = (int)Math.Floor(_originX / TilePixels), x1 = (int)Math.Floor((_originX + Width / _scale) / TilePixels);
        int z0 = (int)Math.Floor(_originZ / TilePixels), z1 = (int)Math.Floor((_originZ + Height / _scale) / TilePixels);
        lock (_sync)
        {
            for (int tz = z0; tz <= z1; tz++)
                for (int tx = x0; tx <= x1; tx++)
                {
                    if (!_tiles.TryGetValue((tx, tz), out var t) || t.Image == null) continue;
                    t.LastUsed = ++_useCounter;
                    var p = ToScreen(tx * TilePixels, tz * TilePixels);
                    float size = (float)(TilePixels * _scale);
                    g.DrawImage(t.Image, p.X, p.Y, size, size);
                    if (_overlay is MapOverlay.InhabitedTime or MapOverlay.LastUpdate) DrawHeat(g, t, tx, tz);
                    if (_overlay == MapOverlay.Biomes && t.Biomes != null)
                    {
                        // One pixel per 4×4 cell, drawn over the map with the classic biome-map colours.
                        t.BiomeImage ??= BiomeImage(t.Biomes);
                        var was = g.InterpolationMode;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.DrawImage(t.BiomeImage, p.X, p.Y, size, size);
                        g.InterpolationMode = was;
                    }
                }
        }
        if (_overlay == MapOverlay.SlimeChunks && _slime != null) DrawSlimeChunks(g, _slime);
        g.PixelOffsetMode = PixelOffsetMode.Default;
        g.SmoothingMode = SmoothingMode.None;
        DrawGrids(g);
        DrawSelection(g);
        DrawBox(g);
        DrawStructures(g);
        DrawMarkers(g);
        DrawGesture(g);
        DrawBoxDragLabel(g);
        if (_tiles.Count == 0 && _chunks.Count == 0)
            TextRenderer.DrawText(g, "This dimension has no chunks.", Font, ClientRectangle, C.FG_DIM, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (_tilesFailed > 0)
            TextRenderer.DrawText(g, $"{_tilesFailed} map tile(s) failed to render: {LastError} (details in log.txt)", Font,
                new Rectangle(8, Height - this.Px(24), Width - 16, this.Px(20)), C.RED, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    // The slime chunks of the area in view and around it (one pixel each), worked out again only when the view leaves it.
    Bitmap? _slimeImage;
    (int X, int Z, int W, int H) _slimeArea;

    void DrawSlimeChunks(Graphics g, Func<int, int, bool> slime)
    {
        int cx0 = (int)Math.Floor(_originX / 16), cz0 = (int)Math.Floor(_originZ / 16);
        int cx1 = (int)Math.Floor((_originX + Width / _scale) / 16), cz1 = (int)Math.Floor((_originZ + Height / _scale) / 16);
        int w = cx1 - cx0 + 1, h = cz1 - cz0 + 1;
        if (w <= 0 || h <= 0 || (long)w * h > 4_000_000) return;
        var a = _slimeArea;
        if (_slimeImage == null || cx0 < a.X || cz0 < a.Z || cx1 >= a.X + a.W || cz1 >= a.Z + a.H || a.W > w * 3)
        {
            _slimeImage?.Dispose();
            int padX = w / 2 + 2, padZ = h / 2 + 2;
            _slimeArea = a = (cx0 - padX, cz0 - padZ, w + 2 * padX, h + 2 * padZ);
            _slimeImage = SlimeImage(slime, a.X, a.Z, a.W, a.H);
        }
        var p = ToScreen(a.X * 16, a.Z * 16);
        var was = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(_slimeImage, p.X, p.Y, (float)(a.W * 16 * _scale), (float)(a.H * 16 * _scale));
        g.InterpolationMode = was;
    }

    static Bitmap SlimeImage(Func<int, int, bool> slime, int x0, int z0, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var row = new int[w];
        const int green = unchecked((int)0x8C3CDC50);
        for (int z = 0; z < h; z++)
        {
            for (int x = 0; x < w; x++) row[x] = slime(x0 + x, z0 + z) ? green : 0;
            Marshal.Copy(row, 0, data.Scan0 + z * data.Stride, w);
        }
        bmp.UnlockBits(data);
        return bmp;
    }

    void DrawHeat(Graphics g, Tile t, int tx, int tz)
    {
        long[] values = _overlay == MapOverlay.InhabitedTime ? t.Inhabited : t.LastUpdate;
        long max = _overlay == MapOverlay.InhabitedTime ? 72000 * 4 : Math.Max(1, values.Max());
        for (int i = 0; i < values.Length; i++)
        {
            var pos = new ChunkPos(tx * TileChunks + (i % TileChunks), tz * TileChunks + i / TileChunks);
            if (!_chunks.Contains(pos)) continue;
            double v = Math.Clamp(values[i] / (double)max, 0, 1);
            var color = v <= 0 ? Color.FromArgb(110, 40, 60, 200) : HeatColor(v);
            var p = ToScreen(pos.X * 16, pos.Z * 16);
            using var b = new SolidBrush(color);
            g.FillRectangle(b, p.X, p.Y, (float)(16 * _scale), (float)(16 * _scale));
        }
    }

    static Bitmap BiomeImage(ushort[] cells)
    {
        int side = TilePixels / 4;
        var bmp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        var all = Core.Rendering.Biomes.All;
        var data = bmp.LockBits(new Rectangle(0, 0, side, side), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var row = new int[side];
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                int index = cells[z * side + x] - 1;
                if (index < 0 || index >= all.Count) { row[x] = 0; continue; }
                var b = all[index];
                uint rgb = b.MapColor != 0 ? b.MapColor : b.Tint.Grass;
                row[x] = unchecked((int)(0xB4000000u | (rgb & 0xFFFFFF)));
            }
            Marshal.Copy(row, 0, data.Scan0 + z * data.Stride, side);
        }
        bmp.UnlockBits(data);
        return bmp;
    }

    static Color HeatColor(double v)
    {
        // blue -> green -> yellow -> red
        int a = 130;
        if (v < 0.33) return Color.FromArgb(a, 30, (int)(80 + 500 * v), 90);
        if (v < 0.66) return Color.FromArgb(a, (int)(255 * (v - 0.33) / 0.33), 230, 40);
        return Color.FromArgb(a, 240, (int)(230 * (1 - (v - 0.66) / 0.34)), 30);
    }

    void DrawGrids(Graphics g)
    {
        double chunkPx = 16 * _scale;
        if (ShowChunkGrid && chunkPx >= 12)
        {
            using var pen = new Pen(Color.FromArgb(40, 255, 255, 255));
            double startX = Math.Floor(_originX / 16) * 16, startZ = Math.Floor(_originZ / 16) * 16;
            for (double x = startX; x <= _originX + Width / _scale; x += 16) { float sx = ToScreen(x, 0).X; g.DrawLine(pen, sx, 0, sx, Height); }
            for (double z = startZ; z <= _originZ + Height / _scale; z += 16) { float sz = ToScreen(0, z).Y; g.DrawLine(pen, 0, sz, Width, sz); }
        }
        if (ShowRegionGrid && TilePixels * _scale >= 24)
        {
            using var pen = new Pen(Color.FromArgb(90, 120, 170, 255)) { DashStyle = DashStyle.Dash };
            double startX = Math.Floor(_originX / TilePixels) * TilePixels, startZ = Math.Floor(_originZ / TilePixels) * TilePixels;
            for (double x = startX; x <= _originX + Width / _scale; x += TilePixels) { float sx = ToScreen(x, 0).X; g.DrawLine(pen, sx, 0, sx, Height); }
            for (double z = startZ; z <= _originZ + Height / _scale; z += TilePixels) { float sz = ToScreen(0, z).Y; g.DrawLine(pen, 0, sz, Width, sz); }
        }
    }

    void DrawSelection(Graphics g)
    {
        if (Selection.Count > 0)
        {
            using var b = new SolidBrush(Color.FromArgb(90, 255, 80, 80));
            float s = (float)(16 * _scale);
            double maxX = _originX + Width / _scale, maxZ = _originZ + Height / _scale;
            foreach (var p in Selection)
            {
                if (p.X * 16 + 16 < _originX || p.X * 16 > maxX || p.Z * 16 + 16 < _originZ || p.Z * 16 > maxZ) continue;
                var sp = ToScreen(p.X * 16, p.Z * 16);
                g.FillRectangle(b, sp.X, sp.Y, Math.Max(1, s), Math.Max(1, s));
            }
        }
        // A box tool outlines the block under the pointer instead (DrawBox).
        if (_hover is ChunkPos h && _boxTool == MapBoxTool.None)
        {
            var sp = ToScreen(h.X * 16, h.Z * 16);
            using var pen = new Pen(Color.FromArgb(220, 255, 255, 255));
            g.DrawRectangle(pen, sp.X, sp.Y, (float)(16 * _scale), (float)(16 * _scale));
        }
    }

    /// <summary>Outline of the shape being drawn (blue adds, red removes) and the brush under the mouse.</summary>
    void DrawGesture(Graphics g)
    {
        bool subtract = _gesture != null ? _gestureSubtract : Subtract || (ModifierKeys & Keys.Alt) != 0;
        Color line = subtract ? Color.FromArgb(230, 255, 110, 90) : Color.FromArgb(230, 120, 190, 255);
        using var fill = new SolidBrush(Color.FromArgb(55, line));
        using var pen = new Pen(line, Math.Max(1f, this.Px(1) * 1.5f));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        switch (_gesture)
        {
            case MapTool.Rectangle:
                {
                    var r = Rectangle.FromLTRB(Math.Min(_shapeStart.X, _shapeNow.X), Math.Min(_shapeStart.Y, _shapeNow.Y), Math.Max(_shapeStart.X, _shapeNow.X), Math.Max(_shapeStart.Y, _shapeNow.Y));
                    g.FillRectangle(fill, r);
                    g.DrawRectangle(pen, r);
                    var (a, b) = (ToWorld(r.Location), ToWorld(new Point(r.Right, r.Bottom)));
                    GestureLabel(g, $"{Math.Floor(b.X / 16) - Math.Floor(a.X / 16) + 1:0} × {Math.Floor(b.Z / 16) - Math.Floor(a.Z / 16) + 1:0} chunks", _shapeNow);
                    break;
                }
            case MapTool.Circle:
                {
                    float rad = (float)Math.Sqrt(Math.Pow(_shapeNow.X - _shapeStart.X, 2) + Math.Pow(_shapeNow.Y - _shapeStart.Y, 2));
                    g.FillEllipse(fill, _shapeStart.X - rad, _shapeStart.Y - rad, rad * 2, rad * 2);
                    g.DrawEllipse(pen, _shapeStart.X - rad, _shapeStart.Y - rad, rad * 2, rad * 2);
                    g.DrawLine(pen, _shapeStart, _shapeNow);
                    double blocks = rad / _scale;
                    GestureLabel(g, $"radius {blocks / 16:0.#} chunks ({blocks:0} blocks)", _shapeNow);
                    break;
                }
            case MapTool.Lasso when _lasso.Count > 1:
                {
                    var pts = _lasso.ToArray();
                    if (pts.Length > 2) g.FillPolygon(fill, pts);
                    g.DrawLines(pen, pts);
                    using var closing = new Pen(line, pen.Width) { DashStyle = DashStyle.Dash };
                    g.DrawLine(closing, pts[^1], pts[0]);
                    break;
                }
        }
        if ((_tool == MapTool.Brush || _gesture == MapTool.Brush) && _mouseInside)
        {
            float rad = (float)((BrushRadius + 0.5) * 16 * _scale);
            g.DrawEllipse(pen, _mouse.X - rad, _mouse.Y - rad, rad * 2, rad * 2);
        }
        g.SmoothingMode = SmoothingMode.None;
    }

    void GestureLabel(Graphics g, string text, Point at)
    {
        using var font = Theme.Ui(8.5f, FontStyle.Bold);
        var size = TextRenderer.MeasureText(text, font);
        var r = new Rectangle(at.X + this.Px(14), at.Y + this.Px(14), size.Width + this.Px(8), size.Height + this.Px(4));
        using var b = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
        g.FillRectangle(b, r);
        TextRenderer.DrawText(g, text, font, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    void DrawMarkers(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = Theme.Ui(8.5f, FontStyle.Bold);
        // What a search found: small diamonds (only those on screen), the one being looked at circled.
        if (_found.Count > 0)
        {
            int r = this.Px(4), drawn = 0;
            var view = ClientRectangle;
            view.Inflate(r, r);
            using var fill = new SolidBrush(Color.FromArgb(235, 255, 80, 200));
            using var edge = new Pen(Color.Black, 1f);
            foreach (var (x, z) in _found)
            {
                var p = ToScreen(x + 0.5, z + 0.5);
                if (!view.Contains((int)p.X, (int)p.Y)) continue;
                PointF[] diamond = [new(p.X, p.Y - r), new(p.X + r, p.Y), new(p.X, p.Y + r), new(p.X - r, p.Y)];
                g.FillPolygon(fill, diamond);
                g.DrawPolygon(edge, diamond);
                if (++drawn >= 5000) break;
            }
        }
        if (_foundFocus is (int fx, int fz))
        {
            var p = ToScreen(fx + 0.5, fz + 0.5);
            int r = this.Px(10);
            using var ring = new Pen(Color.FromArgb(255, 255, 80, 200), this.Px(2));
            g.DrawEllipse(ring, p.X - r, p.Y - r, 2 * r, 2 * r);
            Label(g, "Found", p, font);
        }
        if (_spawn is (int sx, int sz))
        {
            var p = ToScreen(sx + 0.5, sz + 0.5);
            using var b = new SolidBrush(Color.FromArgb(240, 255, 215, 0));
            using var pen = new Pen(Color.Black, 1.5f);
            var star = Star(p, this.Px(7));
            g.FillPolygon(b, star);
            g.DrawPolygon(pen, star);
            Label(g, "Spawn", p, font);
        }
        foreach (var (name, x, z) in _deaths)
        {
            var p = ToScreen(x, z);
            float r = this.Px(5);
            using var shadow = new Pen(Color.Black, this.Px(4));
            using var cross = new Pen(Color.FromArgb(255, 235, 60, 60), this.Px(2));
            foreach (var pen in new[] { shadow, cross })
            {
                g.DrawLine(pen, p.X - r, p.Y - r, p.X + r, p.Y + r);
                g.DrawLine(pen, p.X - r, p.Y + r, p.X + r, p.Y - r);
            }
            Label(g, name, p, font);
        }
        foreach (var (name, x, z) in _players)
        {
            var p = ToScreen(x, z);
            int r = this.Px(5);
            using var b = new SolidBrush(Color.FromArgb(240, 80, 200, 255));
            using var pen = new Pen(Color.Black, 1.5f);
            g.FillEllipse(b, p.X - r, p.Y - r, 2 * r, 2 * r);
            g.DrawEllipse(pen, p.X - r, p.Y - r, 2 * r, 2 * r);
            Label(g, name, p, font);
        }
    }

    void Label(Graphics g, string text, PointF p, Font font)
    {
        var size = TextRenderer.MeasureText(text, font);
        var r = new Rectangle((int)p.X - size.Width / 2, (int)p.Y + this.Px(8), size.Width, size.Height);
        using var b = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
        g.FillRectangle(b, r);
        TextRenderer.DrawText(g, text, font, r, Color.White, TextFormatFlags.HorizontalCenter);
    }

    static PointF[] Star(PointF c, float r)
    {
        var pts = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5;
            float rr = i % 2 == 0 ? r : r * 0.45f;
            pts[i] = new PointF(c.X + (float)(Math.Cos(a) * rr), c.Y + (float)(Math.Sin(a) * rr));
        }
        return pts;
    }
}
