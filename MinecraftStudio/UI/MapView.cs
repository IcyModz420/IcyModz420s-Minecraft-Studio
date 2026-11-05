// MinecraftStudio/UI/MapView.cs: the world map view: renders chunk tiles, pans and zooms, and holds the chunk selection used for pruning
// (or, with a box tool, a box of blocks: MapView.Box.cs).
// Owns: MapOverlay, MapTool, MapBoxTool, MapView.
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.UI;

public enum MapOverlay { None, InhabitedTime, LastUpdate, Biomes, SlimeChunks }

/// <summary>A right click on the map: the block (its top height when the map has drawn it) and where on the control.</summary>
public readonly record struct MapBlockClick(int X, int? Y, int Z, Point Screen);

/// <summary>What a left-drag on the map does.</summary>
public enum MapTool { Pan, Rectangle, Circle, Lasso, Brush }

/// <summary>
/// What a left-drag does to the map's box of blocks instead: nothing (the chunk tools work), Draw (draw a new box, pull an
/// edge or corner, move it) or Place (a box of a fixed size, put down where it is clicked and dragged about).
/// </summary>
public enum MapBoxTool { None, Draw, Place }

/// <summary>Zoomable top-down world map with chunk selection. Tiles (32×32 chunks) render on a background thread.</summary>
[System.ComponentModel.DesignerCategory("Code")]
sealed partial class MapView : Control
{
    const int TileChunks = 32;
    const int TilePixels = TileChunks * 16;
    const int MaxCachedTiles = 96;

    sealed class Tile
    {
        public Bitmap? Image;
        public int[]? BlockIds;           // block state id per pixel (for hover info), -1 = none
        public short[]? Heights;
        public long[] Inhabited = new long[TileChunks * TileChunks];
        public long[] LastUpdate = new long[TileChunks * TileChunks];
        /// <summary>Biome per 4×4-block cell (128×128 cells; index into Biomes.All plus one, 0 = unknown).</summary>
        public ushort[]? Biomes;
        public Bitmap? BiomeImage;
        public bool Done;
        public long Generation;
        public long LastUsed;
    }

    readonly Dictionary<(int X, int Z), Tile> _tiles = [];
    readonly object _sync = new();
    readonly AutoResetEvent _wake = new(false);
    Thread? _worker;
    volatile bool _stop;
    long _generation;
    long _useCounter;

    WorldDimension? _dim;
    HashSet<(int, int)> _regionsWithChunks = [];
    HashSet<ChunkPos> _chunks = [];
    double _scale = 1;          // pixels per block
    double _originX, _originZ;  // world block coordinate at the control's top-left corner
    Point _dragStart;
    double _dragOriginX, _dragOriginZ;
    bool _dragging;
    ChunkPos? _hover;
    (int X, int Z)? _spawn;
    readonly List<(string Name, double X, double Z)> _players = [];
    readonly List<(string Name, double X, double Z)> _deaths = [];

    /// <summary>Where players last died (red crosses); null or empty for none.</summary>
    public void SetDeaths(IEnumerable<(string Name, double X, double Z)>? deaths)
    {
        _deaths.Clear();
        if (deaths != null) _deaths.AddRange(deaths);
        Invalidate();
    }
    List<(int X, int Z)> _found = [];
    (int X, int Z)? _foundFocus;
    MapRenderOptions _options = new();

    public HashSet<ChunkPos> Selection { get; } = [];

    MapTool _tool;
    /// <summary>Left-drag tool. Right- or middle-drag always pans; Alt (or <see cref="Subtract"/>) removes from the selection.</summary>
    public MapTool Tool
    {
        get => _tool;
        set { _tool = value; Cursor = value == MapTool.Pan ? Cursors.Default : Cursors.Cross; Invalidate(); }
    }
    /// <summary>Brush radius in chunks (0 paints one chunk).</summary>
    public int BrushRadius { get; set; } = 2;
    /// <summary>Shape tools remove chunks from the selection instead of adding them.</summary>
    public bool Subtract { get; set; }

    // A shape gesture in progress (left button down with a selection tool).
    MapTool? _gesture;
    bool _gestureSubtract;
    Point _shapeStart, _shapeNow;
    readonly List<Point> _lasso = [];
    Point _mouse;
    bool _mouseInside;
    public MapOverlay Overlay { get => _overlay; set { _overlay = value; Invalidate(); } }

    /// <summary>Whether a chunk is a slime chunk (the world's own rule; null where none is known, or not the overworld).</summary>
    public Func<int, int, bool>? SlimeChunks { get => _slime; set { _slime = value; _slimeImage?.Dispose(); _slimeImage = null; Invalidate(); } }
    Func<int, int, bool>? _slime;
    MapOverlay _overlay;
    public bool ShowChunkGrid { get; set; } = true;
    public bool ShowRegionGrid { get; set; } = true;
    /// <summary>Shown while there is no dimension to draw.</summary>
    public string EmptyText { get => _emptyText; set { _emptyText = value; Invalidate(); } }
    string _emptyText = "Open a world to see its map.";
    public int? SliceY { get => _options.StartY; set { _options = new MapRenderOptions { StartY = value }; ClearCache(); } }

    public event EventHandler? SelectionChanged;
    /// <summary>The undo or redo list changed (enable the buttons, update their tooltips).</summary>
    public event EventHandler? HistoryChanged;
    public event EventHandler<ChunkPos>? ChunkActivated;
    public event EventHandler<MapBlockClick>? BlockMenu;
    public event EventHandler<string>? HoverInfo;
    public event EventHandler<double>? Progress;
    bool _idleReported = true;

    public MapView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(22, 22, 24);
        TabStop = true;
    }

    public WorldDimension? Dimension => _dim;
    public double ZoomScale => _scale;
}
