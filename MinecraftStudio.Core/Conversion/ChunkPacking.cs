// MinecraftStudio.Core/Conversion/ChunkPacking.cs: Helpers shared by the writers: palette remapping, heightmaps
// and simple sky light.
// Owns: ChunkPacking.
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>Helpers shared by the writers: palette remapping, heightmaps and simple sky light.</summary>
public static class ChunkPacking
{
    /// <summary>Maps a section's palette through <paramref name="map"/>, merging entries that become equal and dropping unused ones.</summary>
    public static (BlockState[] Palette, ushort[]? Indices) Remap(BlockSection s, Func<BlockState, BlockState> map)
    {
        var srcIdx = s.Indices;
        var used = new bool[s.Palette.Length];
        if (srcIdx == null) used[0] = true;
        else foreach (var i in srcIdx) used[i] = true;
        var palette = new List<BlockState>();
        var lookup = new Dictionary<BlockState, ushort>();
        var remap = new ushort[s.Palette.Length];
        for (int i = 0; i < s.Palette.Length; i++)
        {
            if (!used[i]) continue;
            var t = map(s.Palette[i]);
            if (!lookup.TryGetValue(t, out ushort idx))
            {
                idx = (ushort)palette.Count;
                palette.Add(t);
                lookup[t] = idx;
            }
            remap[i] = idx;
        }
        if (srcIdx == null || palette.Count == 1) return (palette.ToArray(), null);
        var output = new ushort[4096];
        for (int i = 0; i < 4096; i++) output[i] = remap[srcIdx[i]];
        return (palette.ToArray(), output);
    }

    static bool IsFluid(BlockState s) => s.BaseName is "water" or "lava" or "bubble_column" || s.GetProperty("waterlogged") == "true"
        || s.BaseName is "seagrass" or "tall_seagrass" or "kelp" or "kelp_plant";

    static bool BlocksMotion(BlockState s)
    {
        if (s.IsAir) return false;
        var def = JavaBlockData.Latest.Blocks.GetValueOrDefault(s.Name);
        return def == null || def.Solid;
    }

    /// <summary>Light a block absorbs (0 = transparent to sky light, 15 = opaque).</summary>
    public static int Opacity(BlockState s)
    {
        if (s.IsAir) return 0;
        if (s.BaseName is "water" or "ice" or "frosted_ice") return 1;
        if (s.BaseName.EndsWith("_leaves") || s.BaseName == "cobweb") return 1;
        var def = JavaBlockData.Latest.Blocks.GetValueOrDefault(s.Name);
        if (def == null) return 15;
        if (def.Transparent || !def.Solid) return def.FilterLight >= 15 ? (def.Solid ? 15 : 0) : def.FilterLight;
        return def.FilterLight;
    }

    public static int Emission(BlockState s)
    {
        if (s.IsAir) return 0;
        if (s.BaseName is "lava" or "fire" or "glowstone" or "sea_lantern" or "jack_o_lantern" or "beacon" or "end_portal" or "shroomlight" or "lantern" or "end_gateway") return 15;
        if (s.BaseName is "torch" or "wall_torch" or "end_rod") return 14;
        return JavaBlockData.Latest.Blocks.GetValueOrDefault(s.Name)?.EmitLight ?? 0;
    }

    /// <summary>Heightmaps of a canonical chunk as "highest block + 1 - minY" per column (index z*16+x).</summary>
    public static Dictionary<string, int[]> Heightmaps(CanonicalChunk c, int minY)
    {
        var world = new int[256];
        var motion = new int[256];
        var noLeaves = new int[256];
        var ocean = new int[256];
        var done = new int[256];
        foreach (var (sy, s) in c.Sections.Reverse())
        {
            if (s.IsEmpty) continue;
            for (int ly = 15; ly >= 0; ly--)
                for (int i = 0; i < 256; i++)
                {
                    if (done[i] == 15) continue;
                    var b = s.Get(i & 15, ly, i >> 4);
                    if (b.IsAir) continue;
                    int v = sy * 16 + ly + 1 - minY;
                    if ((done[i] & 1) == 0) { world[i] = v; done[i] |= 1; }
                    bool m = BlocksMotion(b) || IsFluid(b);
                    if ((done[i] & 2) == 0 && m) { motion[i] = v; done[i] |= 2; }
                    if ((done[i] & 4) == 0 && m && !b.BaseName.EndsWith("_leaves")) { noLeaves[i] = v; done[i] |= 4; }
                    if ((done[i] & 8) == 0 && BlocksMotion(b)) { ocean[i] = v; done[i] |= 8; }
                }
        }
        return new Dictionary<string, int[]>
        {
            ["MOTION_BLOCKING"] = motion, ["MOTION_BLOCKING_NO_LEAVES"] = noLeaves, ["OCEAN_FLOOR"] = ocean, ["WORLD_SURFACE"] = world,
        };
    }

    /// <summary>Packs 256 heightmap values (9 bits; spanning before 20w17a).</summary>
    public static long[] PackHeightmap(int[] values, bool spanning)
    {
        var v = values.Select(x => (ushort)Math.Clamp(x, 0, 511)).ToArray();
        return spanning ? PackedArray.PackSpanning(v, 9) : PackedArray.PackNonSpanning(v, 9);
    }

    /// <summary>
    /// Simple sky light: full above the highest light-absorbing block of each column, reduced by each block's
    /// opacity going down (no sideways spread). Returns light per block for the requested section range.
    /// </summary>
    public static Dictionary<int, byte[]> SkyLight(CanonicalChunk c, int minSection, int maxSection, bool hasSky)
    {
        var result = new Dictionary<int, byte[]>();
        for (int sy = minSection; sy <= maxSection; sy++) result[sy] = new byte[4096];
        if (!hasSky) return result;
        for (int i = 0; i < 256; i++)
        {
            int x = i & 15, z = i >> 4;
            int light = 15;
            for (int y = maxSection * 16 + 15; y >= minSection * 16; y--)
            {
                var b = c.GetBlock(x, y, z);
                int op = Opacity(b);
                if (op > 0) light = Math.Max(0, light - Math.Max(1, op));
                result[y >> 4][BlockSection.Index(x, y & 15, z)] = (byte)light;
                if (light == 0)
                {
                    // Everything below stays dark.
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>Block light from light sources, spread inside the chunk (6-neighbour flood fill).</summary>
    public static Dictionary<int, byte[]> BlockLight(CanonicalChunk c, int minSection, int maxSection)
    {
        var result = new Dictionary<int, byte[]>();
        for (int sy = minSection; sy <= maxSection; sy++) result[sy] = new byte[4096];
        var queue = new Queue<(int X, int Y, int Z, int L)>();
        foreach (var (sy, s) in c.Sections)
        {
            if (sy < minSection || sy > maxSection) continue;
            bool any = false;
            foreach (var p in s.Palette) if (Emission(p) > 0) { any = true; break; }
            if (!any) continue;
            for (int i = 0; i < 4096; i++)
            {
                var b = s.Indices == null ? s.Palette[0] : s.Palette[s.Indices[i]];
                int e = Emission(b);
                if (e <= 0) continue;
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                result[sy][i] = (byte)e;
                queue.Enqueue((x, y, z, e));
            }
        }
        int minY = minSection * 16, maxY = maxSection * 16 + 15;
        while (queue.Count > 0)
        {
            var (x, y, z, l) = queue.Dequeue();
            if (l <= 1) continue;
            foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
            {
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if (nx is < 0 or > 15 || nz is < 0 or > 15 || ny < minY || ny > maxY) continue;
                int nl = l - Math.Max(1, Opacity(c.GetBlock(nx, ny, nz)));
                if (nl <= 0) continue;
                var arr = result[ny >> 4];
                int idx = BlockSection.Index(nx, ny & 15, nz);
                if (arr[idx] >= nl) continue;
                arr[idx] = (byte)nl;
                queue.Enqueue((nx, ny, nz, nl));
            }
        }
        return result;
    }

    /// <summary>Packs a per-block 0..15 array (YZX) into a 2048-byte nibble array.</summary>
    public static byte[] Nibbles(byte[] values, Func<int, int>? index = null)
    {
        var o = new byte[values.Length / 2];
        for (int i = 0; i < values.Length; i++)
        {
            int at = index?.Invoke(i) ?? i;
            PackedArray.SetNibble(o, at, values[i]);
        }
        return o;
    }
}
