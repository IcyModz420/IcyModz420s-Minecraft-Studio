// MinecraftStudio.Core/Rendering/WebMap.cs: a dimension as a web map — a folder that opens in any browser, offline too.
// Owns: WebMap, WebMapMarker, WebMapResult. Tiles of 512 × 512 blocks (one region each, rendered with one chunk row north of
// it so the hill shading runs on across tiles), then each lower zoom level from four tiles of the one above, until the
// whole dimension fits in one; index.html (a small viewer: drag to pan, wheel to zoom, the block under the pointer, a
// "go to x z" box, markers to show or hide) and map.js (the tile list and the markers — spawn, players, structures —
// loaded as a script so it works from a file:// folder). Nothing is fetched from the internet. Only reads the world.
using System.Globalization;
using System.Text;
using System.Text.Json;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Rendering;

/// <summary>A marker on a web map: what (in words), where, what kind (its colour and whether it is shown at first).</summary>
public sealed record WebMapMarker(string Name, int X, int Z, string Kind, uint Color, string? Note = null);

public sealed class WebMapResult
{
    public string IndexPath { get; set; } = "";
    public int Tiles { get; set; }
    public int Levels { get; set; }
    public int Failed { get; set; }
}

public static class WebMap
{
    public const int TileBlocks = 512;

    /// <summary>
    /// Writes the web map of <paramref name="dim"/> into <paramref name="folder"/> (made if needed; refused when it holds
    /// anything but an earlier web map). <paramref name="title"/> names the page.
    /// </summary>
    public static WebMapResult Export(WorldDimension dim, string folder, string title, IReadOnlyList<WebMapMarker> markers, MapRenderOptions options,
        IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        folder = Path.GetFullPath(folder);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any(e => !Path.GetFileName(e).Equals("tiles", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(e).Equals("index.html", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(e).Equals("map.js", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("That folder has other files in it; choose an empty folder for the web map.");
        var regions = dim.ChunkPositions.Select(p => (p.RegionX, p.RegionZ)).Distinct().OrderBy(r => r.RegionZ).ThenBy(r => r.RegionX).ToList();
        if (regions.Count == 0) throw new InvalidOperationException("The dimension has no chunks.");
        var result = new WebMapResult();
        string tiles = Path.Combine(folder, "tiles");
        if (Directory.Exists(tiles)) Directory.Delete(tiles, true);
        Directory.CreateDirectory(Path.Combine(tiles, "0"));

        // Level 0: one pixel per block, a region per tile.
        var have = new HashSet<(int, int)>();
        int done = 0;
        foreach (var (rx, rz) in regions)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report($"Drawing the map: region {++done:N0} of {regions.Count:N0}");
            var r = DimensionRenderer.Render(dim, options, rx * 32, rz * 32 - 1, rx * 32 + 31, rz * 32 + 31, cancel: cancel);
            result.Failed += r.Failed;
            if (r.Width == 0) continue;
            var tile = new uint[TileBlocks * TileBlocks];
            bool any = false;
            for (int y = 0; y < r.Height; y++)
            {
                int wz = (r.MinChunkZ * 16) + y - rz * TileBlocks;
                if (wz < 0 || wz >= TileBlocks) continue;
                int wx0 = r.MinChunkX * 16 - rx * TileBlocks;
                for (int x = 0; x < r.Width; x++)
                {
                    uint p = r.Pixels[y * r.Width + x];
                    if (p >> 24 == 0) continue;
                    tile[wz * TileBlocks + wx0 + x] = p;
                    any = true;
                }
            }
            if (!any) continue;
            File.WriteAllBytes(Path.Combine(tiles, "0", $"{rx}_{rz}.png"), PngWriter.Encode(tile, TileBlocks, TileBlocks));
            have.Add((rx, rz));
            result.Tiles++;
        }

        // Each lower level from four tiles of the one above (a 2 × 2 average), until it no longer gets smaller: one tile, or
        // the four that meet at x 0, z 0 (every level has a tile edge there).
        var levels = new List<HashSet<(int, int)>> { have };
        while (levels[^1].Count > 1)
        {
            int k = levels.Count;
            var prev = levels[^1];
            var next = prev.Select(t => (X: Floor(t.Item1, 2), Z: Floor(t.Item2, 2))).Distinct().ToHashSet();
            if (next.Count == prev.Count && prev.All(t => Math.Abs(t.Item1) <= 1 && Math.Abs(t.Item2) <= 1)) break;
            Directory.CreateDirectory(Path.Combine(tiles, k.ToString(CultureInfo.InvariantCulture)));
            foreach (var (x, z) in next)
            {
                cancel.ThrowIfCancellationRequested();
                var outTile = new uint[TileBlocks * TileBlocks];
                for (int q = 0; q < 4; q++)
                {
                    int cx = x * 2 + (q & 1), cz = z * 2 + (q >> 1);
                    if (!prev.Contains((cx, cz))) continue;
                    var src = Imaging.Png.Decode(File.ReadAllBytes(Path.Combine(tiles, (k - 1).ToString(CultureInfo.InvariantCulture), $"{cx}_{cz}.png")));
                    Halve(src.Pixels, outTile, (q & 1) * (TileBlocks / 2), (q >> 1) * (TileBlocks / 2));
                }
                File.WriteAllBytes(Path.Combine(tiles, k.ToString(CultureInfo.InvariantCulture), $"{x}_{z}.png"), PngWriter.Encode(outTile, TileBlocks, TileBlocks));
                result.Tiles++;
            }
            progress?.Report($"Zoomed-out level {k} ({next.Count:N0} tiles)");
            levels.Add(next);
        }
        result.Levels = levels.Count;

        // The page and its data.
        var data = new
        {
            title,
            dimension = dim.DisplayName,
            tileBlocks = TileBlocks,
            levels = levels.Select(l => l.Select(t => $"{t.Item1}_{t.Item2}").ToArray()).ToArray(),
            markers = markers.Select(m => new { name = m.Name, x = m.X, z = m.Z, kind = m.Kind, color = $"#{m.Color & 0xFFFFFF:X6}", note = m.Note }).ToArray(),
            made = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        };
        File.WriteAllText(Path.Combine(folder, "map.js"), "window.MAP = " + JsonSerializer.Serialize(data) + ";\n", new UTF8Encoding(false));
        result.IndexPath = Path.Combine(folder, "index.html");
        File.WriteAllText(result.IndexPath, Page.Replace("{{TITLE}}", System.Net.WebUtility.HtmlEncode(title)), new UTF8Encoding(false));
        return result;
    }

    static int Floor(int v, int by) => (int)Math.Floor(v / (double)by);

    /// <summary>A tile shrunk to half its size into a quarter of another (each pixel the average of four; see-through ones left out).</summary>
    static void Halve(uint[] src, uint[] dst, int ox, int oz)
    {
        for (int z = 0; z < TileBlocks / 2; z++)
            for (int x = 0; x < TileBlocks / 2; x++)
            {
                int a = 0, r = 0, g = 0, b = 0, n = 0;
                for (int q = 0; q < 4; q++)
                {
                    uint p = src[(z * 2 + (q >> 1)) * TileBlocks + x * 2 + (q & 1)];
                    if (p >> 24 == 0) continue;
                    a += (int)(p >> 24); r += (int)(p >> 16) & 255; g += (int)(p >> 8) & 255; b += (int)p & 255; n++;
                }
                if (n == 0) continue;
                dst[(oz + z) * TileBlocks + ox + x] = (uint)(a / n) << 24 | (uint)(r / n) << 16 | (uint)(g / n) << 8 | (uint)(b / n);
            }
    }

    // The viewer: no libraries, no network. Tiles of the level nearest the zoom; markers on top.
    const string Page = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{TITLE}}</title>
<style>
  :root { --bg:#1e1f22; --panel:#2b2d31; --fg:#e8e8e8; --dim:#a0a3a8; --accent:#3fa34d; }
  html, body { margin:0; height:100%; background:var(--bg); color:var(--fg); font:14px "Segoe UI", system-ui, sans-serif; overflow:hidden; }
  #map { position:absolute; inset:0; cursor:grab; }
  #map.drag { cursor:grabbing; }
  .bar { position:absolute; left:12px; top:12px; right:12px; display:flex; gap:10px; flex-wrap:wrap; align-items:center; pointer-events:none; }
  .bar > * { pointer-events:auto; background:var(--panel); border-radius:8px; padding:6px 10px; box-shadow:0 2px 8px #0006; }
  .bar h1 { font-size:15px; margin:0; font-weight:600; }
  #goto { background:#1e1f22; color:var(--fg); border:1px solid #444; border-radius:6px; padding:4px 8px; width:150px; }
  .bar input[type=checkbox] { accent-color:var(--accent); margin:0 4px 0 0; vertical-align:-2px; }
  .bar label { color:var(--dim); margin-right:8px; white-space:nowrap; }
  #where { position:absolute; left:12px; bottom:12px; background:var(--panel); border-radius:8px; padding:6px 10px; color:var(--dim); }
  #tip { position:absolute; display:none; background:var(--panel); border-radius:6px; padding:4px 8px; pointer-events:none; box-shadow:0 2px 8px #0008; }
</style>
</head>
<body>
<canvas id="map"></canvas>
<div class="bar">
  <h1 id="title"></h1>
  <form id="go"><input id="goto" placeholder="Go to x z" aria-label="Go to x z"></form>
  <div id="kinds"></div>
</div>
<div id="where">Drag to move · wheel to zoom</div>
<div id="tip"></div>
<script src="map.js"></script>
<script>
(() => {
  const M = window.MAP, T = M.tileBlocks, canvas = document.getElementById('map'), g = canvas.getContext('2d');
  document.getElementById('title').textContent = M.title + ' — ' + M.dimension;
  document.title = M.title + ' — ' + M.dimension;
  const have = M.levels.map(l => new Set(l));
  const images = new Map();
  let scale = 1, cx = 0, cz = 0;               // pixels per block; the block in the middle
  const shown = new Set(M.markers.map(m => m.kind));
  // Start on the spawn, else the middle of the tiles.
  const spawn = M.markers.find(m => m.kind === 'Spawn');
  if (spawn) { cx = spawn.x; cz = spawn.z; } else if (M.levels[0].length) {
    const xs = M.levels[0].map(s => +s.split('_')[0]), zs = M.levels[0].map(s => +s.split('_')[1]);
    cx = (Math.min(...xs) + Math.max(...xs) + 1) * T / 2; cz = (Math.min(...zs) + Math.max(...zs) + 1) * T / 2;
  }
  const kinds = document.getElementById('kinds');
  [...shown].sort().forEach(k => {
    const l = document.createElement('label'), c = document.createElement('input');
    c.type = 'checkbox'; c.checked = true; c.onchange = () => { c.checked ? shown.add(k) : shown.delete(k); draw(); };
    l.append(c, ' ' + k); kinds.append(l);
  });
  function image(level, key) {
    const id = level + '/' + key;
    let im = images.get(id);
    if (!im) { im = new Image(); im.onload = draw; im.src = 'tiles/' + id + '.png'; images.set(id, im); }
    return im.complete && im.naturalWidth ? im : null;
  }
  function resize() { canvas.width = innerWidth * devicePixelRatio; canvas.height = innerHeight * devicePixelRatio; draw(); }
  function screen(x, z) { return [(x - cx) * scale * devicePixelRatio + canvas.width / 2, (z - cz) * scale * devicePixelRatio + canvas.height / 2]; }
  function world(px, py) { return [cx + (px * devicePixelRatio - canvas.width / 2) / (scale * devicePixelRatio), cz + (py * devicePixelRatio - canvas.height / 2) / (scale * devicePixelRatio)]; }
  function draw() {
    g.fillStyle = '#1e1f22'; g.fillRect(0, 0, canvas.width, canvas.height);
    g.imageSmoothingEnabled = scale < 1;
    const level = Math.max(0, Math.min(M.levels.length - 1, Math.floor(Math.log2(1 / scale))));
    const span = T * 2 ** level;
    const [x0, z0] = world(0, 0), [x1, z1] = world(innerWidth, innerHeight);
    for (let tz = Math.floor(z0 / span); tz <= Math.floor(z1 / span); tz++)
      for (let tx = Math.floor(x0 / span); tx <= Math.floor(x1 / span); tx++) {
        const key = tx + '_' + tz;
        if (!have[level].has(key)) continue;
        const im = image(level, key);
        if (!im) continue;
        const [sx, sy] = screen(tx * span, tz * span), size = span * scale * devicePixelRatio;
        g.drawImage(im, sx, sy, size, size);
      }
    for (const m of M.markers) {
      if (!shown.has(m.kind)) continue;
      const [sx, sy] = screen(m.x + 0.5, m.z + 0.5), r = 5 * devicePixelRatio;
      g.fillStyle = m.color; g.strokeStyle = '#000a'; g.lineWidth = 2 * devicePixelRatio;
      g.beginPath(); g.arc(sx, sy, r, 0, Math.PI * 2); g.fill(); g.stroke();
      if (scale >= 0.5 || m.kind === 'Spawn' || m.kind === 'Player') {
        g.font = (12 * devicePixelRatio) + 'px Segoe UI, sans-serif'; g.fillStyle = '#fff'; g.strokeStyle = '#000c'; g.lineWidth = 3 * devicePixelRatio;
        g.strokeText(m.name, sx + r + 3, sy + 4 * devicePixelRatio); g.fillText(m.name, sx + r + 3, sy + 4 * devicePixelRatio);
      }
    }
  }
  let drag = null;
  canvas.onmousedown = e => { drag = [e.clientX, e.clientY, cx, cz]; canvas.classList.add('drag'); };
  addEventListener('mouseup', () => { drag = null; canvas.classList.remove('drag'); });
  const where = document.getElementById('where'), tip = document.getElementById('tip');
  canvas.onmousemove = e => {
    if (drag) { cx = drag[2] - (e.clientX - drag[0]) / scale; cz = drag[3] - (e.clientY - drag[1]) / scale; draw(); }
    const [x, z] = world(e.clientX, e.clientY);
    where.textContent = 'x ' + Math.floor(x) + ', z ' + Math.floor(z) + '   ·   chunk ' + Math.floor(x / 16) + ', ' + Math.floor(z / 16);
    const near = M.markers.find(m => shown.has(m.kind) && Math.hypot((m.x + 0.5 - x) * scale, (m.z + 0.5 - z) * scale) < 8);
    if (near) { tip.style.display = 'block'; tip.style.left = (e.clientX + 14) + 'px'; tip.style.top = (e.clientY + 10) + 'px'; tip.textContent = near.name + ' (' + near.x + ', ' + near.z + ')' + (near.note ? ' — ' + near.note : ''); }
    else tip.style.display = 'none';
  };
  canvas.onwheel = e => {
    e.preventDefault();
    const [x, z] = world(e.clientX, e.clientY), f = e.deltaY < 0 ? 1.25 : 0.8;
    scale = Math.max(1 / 2 ** (M.levels.length + 1), Math.min(16, scale * f));
    const [nx, nz] = world(e.clientX, e.clientY); cx += x - nx; cz += z - nz; draw();
  };
  document.getElementById('go').onsubmit = e => {
    e.preventDefault();
    const n = document.getElementById('goto').value.match(/-?\d+/g);
    if (n && n.length >= 2) { cx = +n[0]; cz = +n[n.length - 1]; if (scale < 1) scale = 1; draw(); }
  };
  addEventListener('resize', resize);
  resize();
})();
</script>
</body>
</html>
""";
}
