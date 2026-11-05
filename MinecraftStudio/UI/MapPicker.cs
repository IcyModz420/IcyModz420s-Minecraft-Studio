// MinecraftStudio/UI/MapPicker.cs: a map of a world to pick a box of blocks on (the crop and place windows).
// Owns: MapPicker — a MapView with a box tool (MapView.Box.cs), zoom, "Go to the box" and "Whole map" above it and, below,
// what is under the pointer (or how to use it). It reads a world of its own off disk in the background, so the world's
// tab keeps reading its own (a world opened for rescue cannot be opened twice: that one is drawn, as the windows' own
// work reads it too), shows the dimension it is told to, reads the heights it has drawn inside the box, and closes its
// world when it is closed. Should the world not open, the coordinates can still be typed.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;
using MinecraftStudio.Studio;

namespace MinecraftStudio.UI;

sealed class MapPicker : Panel
{
    readonly MapView _map;
    readonly Label _line;
    readonly string _hint;
    SaveWorld? _world;
    bool _ownWorld;
    string? _dimensionId;

    public MapPicker(MapBoxTool tool, string hint)
    {
        _hint = hint;
        BackColor = C.BG;
        // Two lines: the block under the pointer (with its height and biome), or how to use the map. As tall as two lines of
        // its own font, so neither is cut off at any display scaling.
        var lineFont = Theme.Ui(9f);
        _line = new Label
        {
            Dock = DockStyle.Bottom, Font = lineFont, ForeColor = C.FG_DIM, BackColor = C.BG3, UseMnemonic = false, Text = hint,
            Padding = new Padding(this.Px(6), this.Px(3), this.Px(6), this.Px(3)),
        };
        _line.Height = TextRenderer.MeasureText("Ag", lineFont).Height * 2 + _line.Padding.Vertical + this.Px(2);
        _map = new MapView { Dock = DockStyle.Fill, Font = Theme.Ui(9f), BoxTool = tool, ShowRegionGrid = false, EmptyText = "Reading the world for the map…", AccessibleName = "Map" };
        _map.BoxChanged += (_, _) => BoxChanged?.Invoke(this, EventArgs.Empty);
        _map.BoxDropped += (_, _) => BoxDropped?.Invoke(this, EventArgs.Empty);
        _map.Progress += (_, _) => Drawn?.Invoke(this, EventArgs.Empty);
        _map.HoverInfo += (_, s) => _line.Text = s;
        _map.MouseLeave += (_, _) => _line.Text = _hint;

        var bar = Theme.ToolStrip();
        bar.Dock = DockStyle.Top;
        bar.ImageScalingSize = new Size(this.Px(16), this.Px(16));
        bar.Items.AddRange(
        [
            Button("", Glyphs.ZoomIn, "Zoom in (wheel)", (_, _) => _map.ZoomBy(1.5)),
            Button("", Glyphs.ZoomOut, "Zoom out (wheel)", (_, _) => _map.ZoomBy(1 / 1.5)),
            Button("Go to the box", Glyphs.Select, "Show the box in the middle of the map", (_, _) => GoToBox()),
            Button("Whole map", Glyphs.Home, "Show the whole dimension", (_, _) => _map.ZoomToFit()),
        ]);
        Controls.Add(_map);
        Controls.Add(bar);
        Controls.Add(_line);
    }

    ToolStripButton Button(string text, string glyph, string tip, EventHandler click) => new(text, Theme.Glyph(glyph, C.FG, this.Px(16)), click)
    {
        ToolTipText = tip,
        ForeColor = C.FG,
        DisplayStyle = text.Length > 0 ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Image,
    };

    /// <summary>The box was drawn, moved or reshaped on the map (while it is dragged, too).</summary>
    public event EventHandler? BoxChanged;
    /// <summary>The mouse let go of a box it changed.</summary>
    public event EventHandler? BoxDropped;
    /// <summary>More of the map was drawn (the heights under the box may be known now), or reading the world for it failed.</summary>
    public event EventHandler? Drawn;

    /// <summary>The box outlined on the map (setting it raises no event).</summary>
    public (int MinX, int MinZ, int MaxX, int MaxZ)? Box { get => _map.Box; set => _map.Box = value; }

    /// <summary>The map shows a dimension (the world was read).</summary>
    public bool Ready => _map.Dimension != null;
    /// <summary>The world could not be read for the map (the coordinates can still be typed).</summary>
    public bool Failed { get; private set; }

    /// <summary>The structure seen from above, laid inside the box (null: none).</summary>
    public void SetPicture(RgbaImage? top) => _map.BoxPicture = top == null ? null : PixelClipboard.ToBitmap(top);

    /// <summary>
    /// Reads the world for the map in the background — a world of its own off disk (one opened for rescue cannot be opened
    /// again: that one is drawn) — then shows the dimension with the box in the middle.
    /// </summary>
    public async Task OpenAsync(SaveWorld world, string? dimensionId)
    {
        _dimensionId = dimensionId;
        if (world.OpenedForRescue) { Show(world, own: false); return; }
        string location = world.Location;
        SaveWorld opened;
        try
        {
            opened = await Task.Run(() =>
            {
                var w = WorldLoader.Open(location);
                try
                {
                    // Every dimension's chunk list is read here rather than when it is first shown (a big world takes a moment).
                    foreach (var d in w.Dimensions) lock (d) _ = d.ChunkPositions.Count;
                    return w;
                }
                catch { w.Dispose(); throw; }
            });
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            // The map only helps: without it the corners are typed.
            Log.Error("Reading a world for the map failed", ex);
            Failed = true;
            _map.EmptyText = "The map could not be read: " + ex.Message + "\nThe coordinates can still be typed.";
            Drawn?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (IsDisposed) { opened.Dispose(); return; }
        Show(opened, own: true);
    }

    void Show(SaveWorld world, bool own)
    {
        _world = world;
        _ownWorld = own;
        ShowDimension(_dimensionId);
    }

    /// <summary>Shows a dimension (by id; the first when the world has no such one), the box in the middle.</summary>
    public void ShowDimension(string? dimensionId)
    {
        _dimensionId = dimensionId;
        if (_world == null) return;
        var d = _world.Dimensions.FirstOrDefault(x => x.Id == dimensionId) ?? _world.Dimensions.FirstOrDefault();
        _map.SetDimension(d, MapMarks.Spawn(_world, d), MapMarks.Players(_world, d));
        GoToBox();
    }

    /// <summary>The box in the middle of the map, zoomed so it is well in view.</summary>
    public void GoToBox()
    {
        if (_map.Box is { } b) _map.FrameBox(b);
    }

    /// <summary>Brings the box back into view when none of it is shown (after its corners were typed).</summary>
    public void KeepBoxInView() => _map.KeepBoxInView();

    /// <summary>Marks blocks on the map (the build check's wrong cells); null clears them.</summary>
    public void SetFound(IEnumerable<(int X, int Z)>? marks) => _map.SetFound(marks);

    /// <summary>
    /// The lowest and highest top block the map has drawn inside the box (null: none), and how many of the box's columns
    /// it has not drawn yet (long.MaxValue while the map is not shown at all).
    /// </summary>
    public (int Low, int High)? HeightRange((int MinX, int MinZ, int MaxX, int MaxZ) box, out long notDrawn)
    {
        if (!Ready) { notDrawn = long.MaxValue; return null; }
        int low = int.MaxValue, high = int.MinValue;
        notDrawn = _map.TopBlocks(box, (y, _) => { if (y < low) low = y; if (y > high) high = y; });
        return low <= high ? (low, high) : null;
    }

    /// <summary>The ground's height under the box (<see cref="StructureGround"/>; null: nothing drawn there), and how many columns are not drawn yet.</summary>
    public int? GroundY((int MinX, int MinZ, int MaxX, int MaxZ) box, out long notDrawn)
    {
        if (!Ready) { notDrawn = long.MaxValue; return null; }
        var columns = new List<(int, BlockState?)>();
        // Under a huge footprint a sample of the columns gives the same middle height.
        long area = (long)(box.MaxX - box.MinX + 1) * (box.MaxZ - box.MinZ + 1), every = Math.Max(1, area / 250_000), seen = 0;
        notDrawn = _map.TopBlocks(box, (y, b) => { if (seen++ % every == 0) columns.Add((y, b)); });
        return StructureGround.GroundY(columns, _world is BedrockWorld);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The map stops drawing first; then its world is closed.
            _map.Dispose();
            if (_ownWorld) _world?.Dispose();
            _world = null;
        }
        base.Dispose(disposing);
    }
}
