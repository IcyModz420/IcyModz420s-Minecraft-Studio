// MinecraftStudio/UI/MapView.Rendering.cs: rendering for MapView.
// Part of MapView (main part in MapView.cs).
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.UI;

partial class MapView
{
    // ------------------------------------------------------------------ background rendering

    void StartWorker()
    {
        if (_worker != null) return;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Map renderer", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    /// <summary>Tiles with chunks in (and one tile around) the view. Call under <c>_sync</c>.</summary>
    IEnumerable<(int X, int Z)> TilesInView()
    {
        int x0 = (int)Math.Floor(_originX / TilePixels) - 1, x1 = (int)Math.Floor((_originX + Width / _scale) / TilePixels) + 1;
        int z0 = (int)Math.Floor(_originZ / TilePixels) - 1, z1 = (int)Math.Floor((_originZ + Height / _scale) / TilePixels) + 1;
        for (int tz = z0; tz <= z1; tz++)
            for (int tx = x0; tx <= x1; tx++)
                if (_regionsWithChunks.Contains((tx, tz))) yield return (tx, tz);
    }

    (int X, int Z)? NextTile()
    {
        if (_dim == null || Width == 0) return null;
        double cx = _originX + Width / 2.0 / _scale, cz = _originZ + Height / 2.0 / _scale;
        (int, int)? best = null;
        double bestD = double.MaxValue;
        lock (_sync)
        {
            foreach (var (tx, tz) in TilesInView())
            {
                if (_tiles.TryGetValue((tx, tz), out var t) && (t.Done || t.Generation == _generation)) continue;
                double dx = (tx + 0.5) * TilePixels - cx, dz = (tz + 0.5) * TilePixels - cz;
                double d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = (tx, tz); }
            }
            if (best != null) _tiles[best.Value] = new Tile { Generation = _generation };
        }
        return best;
    }

    void WorkerLoop()
    {
        var pixels = new uint[TilePixels * TilePixels];
        var tileBuf = new uint[256];
        var heights = new int[256];
        while (!_stop)
        {
            (int X, int Z)? next = null;
            try
            {
                next = NextTile();
                if (next == null)
                {
                    // Everything in view is drawn: let the status bar hide its progress.
                    if (!_idleReported) { _idleReported = true; try { BeginInvoke(() => Progress?.Invoke(this, 1)); } catch { } }
                    _wake.WaitOne(500);
                    continue;
                }
                _idleReported = false;
                RenderTile(next.Value.X, next.Value.Z, pixels, tileBuf, heights);
            }
            catch (Exception ex)
            {
                // Never let one bad tile stop the renderer: mark it done (empty) and carry on.
                _tilesFailed++;
                LastError = ex.Message;
                Log.Error($"Map tile {next}", ex);
                if (next is (int fx, int fz))
                    lock (_sync) _tiles[(fx, fz)] = new Tile { Done = true, Generation = _generation, LastUsed = ++_useCounter };
                try { BeginInvoke(Invalidate); } catch { }
            }
        }
    }

    void RenderTile(int tx, int tz, uint[] pixels, uint[] tileBuf, int[] heights)
    {
        {
            long gen;
            lock (_sync) gen = _generation;
            var dim = _dim;
            if (dim == null) return;
            Array.Clear(pixels);
            var ids = new int[TilePixels * TilePixels];
            Array.Fill(ids, -1);
            var hts = new short[TilePixels * TilePixels];
            var biomes = new ushort[TilePixels / 4 * (TilePixels / 4)];
            var tile = new Tile { Generation = gen, Biomes = biomes };
            var bottomRows = new int[TileChunks][];
            var opt = _options;
            // Stops within a chunk when the map closes (its world may be closed right after).
            for (int cz = 0; cz < TileChunks && !_stop; cz++)
                for (int cx = 0; cx < TileChunks && !_stop; cx++)
                {
                    var pos = new ChunkPos(tx * TileChunks + cx, tz * TileChunks + cz);
                    if (!_chunks.Contains(pos)) continue;
                    ChunkColumn? col;
                    try { lock (dim) col = dim.LoadChunk(pos); }
                    catch (Exception ex) { Log.Error($"Chunk {pos}", ex); FillError(pixels, cx, cz); continue; }
                    if (col == null) continue;
                    try { MapRenderer.RenderChunk(col, tileBuf, heights, opt, bottomRows[cx]); }
                    catch (Exception ex) { Log.Error($"Rendering chunk {pos}", ex); FillError(pixels, cx, cz); continue; }
                    bottomRows[cx] = heights.AsSpan(240, 16).ToArray();
                    tile.Inhabited[cz * TileChunks + cx] = Math.Max(0, col.InhabitedTime);
                    tile.LastUpdate[cz * TileChunks + cx] = Math.Max(0, col.LastUpdate);
                    for (int z = 0; z < 16; z++)
                        for (int x = 0; x < 16; x++)
                        {
                            int pi = (cz * 16 + z) * TilePixels + cx * 16 + x;
                            pixels[pi] = tileBuf[z * 16 + x];
                            int h = heights[z * 16 + x];
                            hts[pi] = h == MapRenderer.NoHeight ? short.MinValue : (short)h;
                            if (h != MapRenderer.NoHeight)
                            {
                                var top = col.GetBlock(x, h, z);
                                ids[pi] = top.Id;
                            }
                            // The biome of each 4×4 cell, read at its middle column's surface.
                            if ((x & 3) == 2 && (z & 3) == 2)
                            {
                                int by = h == MapRenderer.NoHeight ? 64 : h;
                                if (MapRenderer.BiomeInfoAt(col, x, by, z) is { } biome)
                                    biomes[(cz * 4 + (z >> 2)) * (TilePixels / 4) + cx * 4 + (x >> 2)] = (ushort)(biome.Index + 1);
                            }
                        }
                }
            var bmp = new Bitmap(TilePixels, TilePixels, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, TilePixels, TilePixels), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < TilePixels; y++)
                Marshal.Copy(MemoryMarshal.Cast<uint, int>(pixels.AsSpan(y * TilePixels, TilePixels)).ToArray(), 0, data.Scan0 + y * data.Stride, TilePixels);
            bmp.UnlockBits(data);
            tile.Image = bmp;
            tile.BlockIds = ids;
            tile.Heights = hts;
            tile.Done = true;
            lock (_sync)
            {
                if (gen != _generation) { bmp.Dispose(); return; }
                tile.LastUsed = ++_useCounter;
                _tiles[(tx, tz)] = tile;
                Evict();
            }
            int done = 0, total = 0;
            lock (_sync)
                foreach (var t in TilesInView())
                {
                    total++;
                    if (_tiles.TryGetValue(t, out var rendered) && rendered.Done) done++;
                }
            double fraction = total == 0 ? 1 : done / (double)total;
            try { BeginInvoke(() => { Invalidate(); Progress?.Invoke(this, fraction); }); } catch { }
        }
    }

    static void FillError(uint[] pixels, int cx, int cz)
    {
        for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
                pixels[(cz * 16 + z) * TilePixels + cx * 16 + x] = ((x + z) & 2) == 0 ? 0xFFB02020u : 0xFF601010u;
    }

    void Evict()
    {
        if (_tiles.Count <= MaxCachedTiles) return;
        foreach (var key in _tiles.Where(kv => kv.Value.Done).OrderBy(kv => kv.Value.LastUsed).Take(_tiles.Count - MaxCachedTiles).Select(kv => kv.Key).ToList())
        {
            _tiles[key].Image?.Dispose();
            _tiles[key].BiomeImage?.Dispose();
            _tiles.Remove(key);
        }
    }
}
