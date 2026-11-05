// MinecraftStudio/Documents/WorldDocument.Structures.cs: structures on the world's map.
// Part of WorldDocument (main part in WorldDocument.cs). The map toolbar's "Structures" button marks every structure of the
// dimension shown (StructureMap: read in the background from a world of its own off disk, once per dimension and kept until
// the world is saved); the Structures window lists them. Gold rings: loot not taken yet.
using MinecraftStudio.Core.World;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

partial class WorldDocument
{
    // The structures of each dimension: read once (the same task for whoever asks meanwhile), forgotten when the world is saved.
    readonly Dictionary<string, Task<List<StructureSite>>> _structureScans = [];
    readonly CancellationTokenSource _structuresCts = new();
    ToolStripMenuItem? _showStructures;
    StructuresDialog? _structuresWindow;

    /// <summary>The structures of one dimension (read once in the background, then kept).</summary>
    public Task<List<StructureSite>> StructuresOf(string dimensionId, IProgress<string>? progress = null)
    {
        if (_structureScans.TryGetValue(dimensionId, out var running) && !running.IsFaulted && !running.IsCanceled) return running;
        string location = _world.Location;
        bool rescue = _world.OpenedForRescue;
        var token = _structuresCts.Token;
        var task = Task.Run(() =>
        {
            // A world of its own off disk: the map is reading this one (a world opened for rescue as it is, a chunk at a time).
            using var own = rescue ? null : WorldLoader.Open(location);
            var w = own ?? _world;
            var dim = w.Dimensions.FirstOrDefault(d => d.Id == dimensionId);
            return dim == null ? [] : StructureMap.Find(w, [dim], progress, token, shared: rescue).Sites;
        }, token);
        _structureScans[dimensionId] = task;
        return task;
    }

    /// <summary>Whether the structures are marked on the map; turning it on marks them (read first if needed).</summary>
    public bool StructuresShown
    {
        get => _showStructures?.Checked == true;
        set { if (_showStructures != null && _showStructures.Checked != value) _showStructures.Checked = value; }
    }

    /// <summary>The structures of the dimension shown marked on the map, when the button is on (else none).</summary>
    async Task ShowStructuresAsync()
    {
        if (!StructuresShown || CurrentDim is not { } d) { _map.SetStructures(null); return; }
        string dimId = d.Id;
        if (!_structureScans.TryGetValue(dimId, out var have) || !have.IsCompletedSuccessfully) _map.SetStructures(null);
        var progress = new Progress<string>(s => { if (StructuresShown && CurrentDim?.Id == dimId) Main?.SetStatus("Structures: " + s); });
        try
        {
            var sites = await StructuresOf(dimId, progress);
            if (IsDisposed || !StructuresShown || CurrentDim?.Id != dimId) return;
            _map.SetStructures(sites.Select(Marker).ToList());
            int loot = sites.Count(s => s.Unlooted > 0);
            Main?.SetStatus(sites.Count == 0 ? $"No structures saved in {d.DisplayName}'s generated land."
                : $"{sites.Count:N0} structures on the map" + (loot > 0 ? $"; {loot:N0} with loot not taken yet (gold ring)" : "") + ". Only generated land holds them.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or Core.IO.SaveFormatException or InvalidOperationException)
        {
            Main?.SetStatus("The structures could not be read: " + ex.Message);
        }
    }

    static MapStructure Marker(StructureSite s) =>
        new(s.Name, s.X, s.Y, s.Z, (s.Box.MinX, s.Box.MinZ, s.Box.MaxX, s.Box.MaxZ), Color.FromArgb(unchecked((int)s.Color)), s.Unlooted);

    /// <summary>After a save: chunks may be gone, so every dimension's structures are read again when next asked for.</summary>
    void ForgetStructures()
    {
        _structureScans.Clear();
        _ = ShowStructuresAsync();
    }

    /// <summary>The Structures window of this world (one per world, beside the map like Find).</summary>
    public StructuresDialog ShowStructuresWindow()
    {
        if (_structuresWindow == null || _structuresWindow.IsDisposed)
        {
            _structuresWindow = new StructuresDialog(this);
            _structuresWindow.Show(FindForm());
        }
        else _structuresWindow.Activate();
        return _structuresWindow;
    }
}
