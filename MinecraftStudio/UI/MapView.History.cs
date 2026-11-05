// MinecraftStudio/UI/MapView.History.cs: history for MapView.
// Part of MapView (main part in MapView.cs).
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.UI;

partial class MapView
{
    // ------------------------------------------------------------------ undo / redo

    /// <summary>One undoable step: a selection change (the chunks it added and removed) or an action such as a deletion.</summary>
    abstract record Step(string Name);
    sealed record SelectionStep(string Name, ChunkPos[] Added, ChunkPos[] Removed) : Step(Name);
    sealed record ActionStep(string Name, Action UndoAction, Action RedoAction) : Step(Name);

    const int HistoryLimit = 500;
    readonly List<Step> _undo = [], _redo = [];
    /// <summary>The history of each dimension shown before, so switching dimensions keeps every undo.</summary>
    readonly Dictionary<WorldDimension, (Step[] Undo, Step[] Redo)> _histories = [];
    HashSet<ChunkPos>? _before;
    string _beforeName = "";
    WorldDimension? _previousDim;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    /// <summary>What Undo would take back ("Brush stroke", "Delete 325 chunks" …).</summary>
    public string? UndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? RedoName => _redo.Count > 0 ? _redo[^1].Name : null;

    /// <summary>Remembers the selection before a change; <see cref="EndChange"/> records what the change did.</summary>
    void BeginChange(string name)
    {
        if (_before != null) return;
        _before = [.. Selection];
        _beforeName = name;
    }

    void EndChange()
    {
        if (_before == null) return;
        var added = Selection.Where(p => !_before.Contains(p)).ToArray();
        var removed = _before.Where(p => !Selection.Contains(p)).ToArray();
        _before = null;
        if (added.Length > 0 || removed.Length > 0) Push(new SelectionStep(_beforeName, added, removed));
    }

    void Push(Step step)
    {
        _undo.Add(step);
        if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
        _redo.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Records an action done outside the map (deleting chunks) so Undo can take it back.</summary>
    public void RecordAction(string name, Action undo, Action redo) => Push(new ActionStep(name, undo, redo));

    /// <summary>Forgets the history (after saving: deletions written to disk cannot be taken back).</summary>
    public void ClearHistory()
    {
        _histories.Clear();
        _undo.Clear();
        _redo.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_gesture != null || _undo.Count == 0) return;
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        switch (step)
        {
            case SelectionStep s:
                Selection.ExceptWith(s.Added);
                Selection.UnionWith(s.Removed.Where(_chunks.Contains));
                break;
            case ActionStep a:
                a.UndoAction();
                break;
        }
        _redo.Add(step);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void Redo()
    {
        if (_gesture != null || _redo.Count == 0) return;
        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        switch (step)
        {
            case SelectionStep s:
                Selection.UnionWith(s.Added.Where(_chunks.Contains));
                Selection.ExceptWith(s.Removed);
                break;
            case ActionStep a:
                a.RedoAction();
                break;
        }
        _undo.Add(step);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void SetDimension(WorldDimension? dim, (int X, int Z)? spawn = null, IEnumerable<(string, double, double)>? players = null)
    {
        _dim = dim;
        _spawn = spawn;
        _players.Clear();
        if (players != null) _players.AddRange(players);
        _found = [];
        _foundFocus = null;
        Selection.Clear();
        // Each dimension keeps its own undo history (a deletion in the Nether stays undoable after looking at the End).
        if (_previousDim != null) _histories[_previousDim] = ([.. _undo], [.. _redo]);
        _undo.Clear();
        _redo.Clear();
        if (dim != null && _histories.Remove(dim, out var history)) { _undo.AddRange(history.Undo); _redo.AddRange(history.Redo); }
        _previousDim = dim;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        _options = new MapRenderOptions { StartY = dim != null && !dim.HasSky ? 100 : null };
        RefreshChunkList();
        ClearCache();
        if (dim != null)
        {
            var c = spawn != null && _chunks.Contains(new ChunkPos(spawn.Value.X >> 4, spawn.Value.Z >> 4))
                ? (spawn.Value.X, spawn.Value.Z)
                : _chunks.Count > 0 ? ((int)_chunks.Average(p => (double)p.X) * 16, (int)_chunks.Average(p => (double)p.Z) * 16) : (0, 0);
            CenterOn(c.Item1, c.Item2);
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        StartWorker();
    }

    /// <summary>Re-reads the chunk list (after pruning) and redraws.</summary>
    public void RefreshChunkList()
    {
        if (_dim == null) { _chunks = []; _regionsWithChunks = []; return; }
        lock (_dim) _chunks = [.. _dim.ChunkPositions];
        _regionsWithChunks = [.. _chunks.Select(p => (p.X >> 5, p.Z >> 5))];
        Selection.RemoveWhere(p => !_chunks.Contains(p));
    }

    public IReadOnlyCollection<ChunkPos> Chunks => _chunks;

    public void ClearCache()
    {
        lock (_sync)
        {
            _generation++;
            foreach (var t in _tiles.Values) { t.Image?.Dispose(); t.BiomeImage?.Dispose(); }
            _tiles.Clear();
        }
        _wake.Set();
        Invalidate();
    }

    (double X, double Z)? _pendingCenter;

    /// <summary>Marks what a search found on this dimension (and circles the one being looked at); null clears them.</summary>
    public void SetFound(IEnumerable<(int X, int Z)>? marks, (int X, int Z)? focus = null)
    {
        _found = marks?.Distinct().ToList() ?? [];
        _foundFocus = focus;
        Invalidate();
    }

    public void CenterOn(double blockX, double blockZ)
    {
        // Before the control has its real size the centre cannot be computed; apply it on the first resize.
        if (Width <= 1 || Height <= 1) { _pendingCenter = (blockX, blockZ); return; }
        _pendingCenter = null;
        _originX = blockX - Width / 2.0 / _scale;
        _originZ = blockZ - Height / 2.0 / _scale;
        _wake.Set();
        Invalidate();
    }

    /// <summary>Last rendering error (shown in the map corner) for diagnostics.</summary>
    public string? LastError { get; private set; }
    int _tilesFailed;

    public void ZoomBy(double factor, Point? around = null)
    {
        var p = around ?? new Point(Width / 2, Height / 2);
        double wx = _originX + p.X / _scale, wz = _originZ + p.Y / _scale;
        _scale = Math.Clamp(_scale * factor, 1 / 16.0, 32);
        _originX = wx - p.X / _scale;
        _originZ = wz - p.Y / _scale;
        _wake.Set();
        Invalidate();
    }

    public void ZoomToFit()
    {
        if (_chunks.Count == 0 || Width == 0) return;
        int minX = _chunks.Min(p => p.X), maxX = _chunks.Max(p => p.X), minZ = _chunks.Min(p => p.Z), maxZ = _chunks.Max(p => p.Z);
        double w = (maxX - minX + 1) * 16.0, h = (maxZ - minZ + 1) * 16.0;
        _scale = Math.Clamp(Math.Min(Width / w, Height / h) * 0.95, 1 / 16.0, 32);
        CenterOn((minX + maxX + 1) * 8.0, (minZ + maxZ + 1) * 8.0);
    }
}
