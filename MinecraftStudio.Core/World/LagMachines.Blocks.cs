// MinecraftStudio.Core/World/LagMachines.Blocks.cs: blocks of LagMachines.
// Part of LagMachines (main part in LagMachines.cs). Owns: LagMachines.Parts — where the redstone parts of a dimension are
// (only chunks with a fair number of one part are kept), then the solid masses among them: a cube at least Limits.CubeSide
// wide that is nearly all one part (pistons, observers, hoppers, dispensers, lamps, sculk sensors, crafters — farms, sorters
// and doors use them in lines and floors, never as solid blocks), found across chunk borders and measured as the whole
// connected mass; and floors nearly all redstone dust (with repeaters, comparators, torches) or powered rails. Pistons caught
// mid-push count as a running machine; two observers watching each other (the fastest clock) only back up what is found.
using MinecraftStudio.Core.Conversion;

namespace MinecraftStudio.Core.World;

public static partial class LagMachines
{
    /// <summary>The redstone parts looked at, by the mass they form (canonical Java base names).</summary>
    static readonly Dictionary<string, LagKind> CubeParts = new(StringComparer.Ordinal)
    {
        ["piston"] = LagKind.PistonMass, ["sticky_piston"] = LagKind.PistonMass, ["piston_head"] = LagKind.PistonMass, ["moving_piston"] = LagKind.PistonMass,
        ["observer"] = LagKind.ObserverMass,
        ["hopper"] = LagKind.HopperMass,
        ["dispenser"] = LagKind.DispenserMass, ["dropper"] = LagKind.DispenserMass,
        ["redstone_lamp"] = LagKind.LampMass,
        ["sculk_sensor"] = LagKind.SculkMass, ["calibrated_sculk_sensor"] = LagKind.SculkMass,
        ["crafter"] = LagKind.CrafterMass,
    };

    static readonly Dictionary<string, LagKind> FloorParts = new(StringComparer.Ordinal)
    {
        ["redstone_wire"] = LagKind.RedstoneCarpet, ["repeater"] = LagKind.RedstoneCarpet, ["comparator"] = LagKind.RedstoneCarpet,
        ["redstone_torch"] = LagKind.RedstoneCarpet, ["redstone_wall_torch"] = LagKind.RedstoneCarpet,
        ["powered_rail"] = LagKind.RailCarpet, ["detector_rail"] = LagKind.RailCarpet, ["activator_rail"] = LagKind.RailCarpet,
    };

    static string PartWords(LagKind k) => k switch
    {
        LagKind.PistonMass => "pistons",
        LagKind.ObserverMass => "observers",
        LagKind.HopperMass => "hoppers",
        LagKind.DispenserMass => "dispensers and droppers",
        LagKind.LampMass => "redstone lamps",
        LagKind.SculkMass => "sculk sensors",
        LagKind.CrafterMass => "crafters",
        LagKind.RedstoneCarpet => "redstone dust, repeaters and torches",
        LagKind.RailCarpet => "powered rails",
        _ => "parts",
    };

    // A block position in one long (x and z within ±33 million, y within ±2048).
    static long Key(int x, int y, int z) => ((long)(x + (1 << 25)) << 38) | ((long)(z + (1 << 25)) << 12) | (long)(y + 2048);
    static (int X, int Y, int Z) Unkey(long k) => ((int)(k >> 38) - (1 << 25), (int)(k & 4095) - 2048, (int)((k >> 12) & ((1L << 26) - 1)) - (1 << 25));

    sealed class Parts
    {
        // Per kind of mass: every part kept, and the chunks holding them.
        readonly Dictionary<LagKind, HashSet<long>> _at = [];
        readonly Dictionary<LagKind, HashSet<ChunkPos>> _chunks = [];
        readonly List<(int X, int Y, int Z, string Facing)> _observers = [];

        public void Add(CanonicalChunk c, List<Hit> hits)
        {
            var found = new Dictionary<LagKind, List<long>>();
            int moving = 0;
            long mx = 0, my = 0, mz = 0;
            foreach (var (sy, s) in c.Sections)
            {
                // The palette first: most sections hold no redstone parts at all.
                var kinds = new LagKind?[s.Palette.Length];
                bool any = false;
                for (int i = 0; i < s.Palette.Length; i++)
                {
                    string n = s.Palette[i].BaseName;
                    if (CubeParts.TryGetValue(n, out var k) || FloorParts.TryGetValue(n, out k)) { kinds[i] = k; any = true; }
                }
                if (!any || s.Indices is not { } idx) continue;
                for (int i = 0; i < 4096; i++)
                {
                    if (kinds[idx[i]] is not LagKind kind) continue;
                    int x = c.X * 16 + (i & 15), y = sy * 16 + (i >> 8), z = c.Z * 16 + ((i >> 4) & 15);
                    if (!found.TryGetValue(kind, out var list)) found[kind] = list = [];
                    list.Add(Key(x, y, z));
                    var state = s.Palette[idx[i]];
                    if (state.BaseName == "moving_piston") { moving++; mx += x; my += y; mz += z; }
                    else if (state.BaseName == "observer" && state.GetProperty("facing") is string facing) _observers.Add((x, y, z, facing));
                }
            }
            foreach (var (kind, list) in found)
            {
                if (list.Count < Limits.PartsWorthKeeping) continue;
                if (!_at.TryGetValue(kind, out var set)) { _at[kind] = set = []; _chunks[kind] = []; }
                set.UnionWith(list);
                _chunks[kind].Add(new ChunkPos(c.X, c.Z));
            }
            if (moving >= Limits.MovingPistonsInOneChunk)
            {
                int x = (int)(mx / moving), y = (int)(my / moving), z = (int)(mz / moving);
                hits.Add(new Hit(LagKind.MovingPistons, x, y, z, (c.X * 16, y, c.Z * 16, c.X * 16 + 15, y, c.Z * 16 + 15), moving,
                    $"{moving:N0} blocks being pushed by pistons when the world was saved"));
            }
        }

        public void FindMasses(List<Hit> hits, CancellationToken cancel)
        {
            foreach (var (kind, set) in _at)
            {
                var seen = new HashSet<long>();
                bool cube = kind < LagKind.RedstoneCarpet;
                foreach (var chunk in _chunks[kind])
                {
                    cancel.ThrowIfCancellationRequested();
                    if (cube) { if (SolidCube(set, chunk) is { } seed && !seen.Contains(seed.Key)) Mass(kind, set, seed.Key, seed.Side, seen, hits); }
                    else Floors(kind, set, chunk, seen, hits);
                }
            }
            Clocks(hits);
        }

        /// <summary>
        /// The largest cube (at least Limits.CubeSide wide) nearly filled with the part whose lowest corner lies in this
        /// chunk; the chunks beside it (+x, +z) are looked at too, so a cube across a border is found once.
        /// </summary>
        static (long Key, int Side)? SolidCube(HashSet<long> set, ChunkPos chunk)
        {
            int x0 = chunk.X * 16, z0 = chunk.Z * 16;
            const int W = 31; // the chunk and 15 blocks beyond it
            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (long k in set)
            {
                var (x, y, z) = Unkey(k);
                if (x < x0 || x >= x0 + W || z < z0 || z >= z0 + W) continue;
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            if (minY > maxY) return null;
            int H = maxY - minY + 1;
            if (H < Limits.CubeSide) return null;
            // Prefix sums over the window: P[x, y, z] = parts in [0, x) × [0, y) × [0, z).
            var P = new int[(W + 1) * (H + 1) * (W + 1)];
            int I(int x, int y, int z) => (y * (W + 1) + z) * (W + 1) + x;
            for (int y = 1; y <= H; y++)
                for (int z = 1; z <= W; z++)
                    for (int x = 1; x <= W; x++)
                        P[I(x, y, z)] = (set.Contains(Key(x0 + x - 1, minY + y - 1, z0 + z - 1)) ? 1 : 0)
                            + P[I(x - 1, y, z)] + P[I(x, y - 1, z)] + P[I(x, y, z - 1)]
                            - P[I(x - 1, y - 1, z)] - P[I(x - 1, y, z - 1)] - P[I(x, y - 1, z - 1)] + P[I(x - 1, y - 1, z - 1)];
            int Sum(int x, int y, int z, int k) => P[I(x + k, y + k, z + k)] - P[I(x, y + k, z + k)] - P[I(x + k, y, z + k)] - P[I(x + k, y + k, z)]
                + P[I(x, y, z + k)] + P[I(x, y + k, z)] + P[I(x + k, y, z)] - P[I(x, y, z)];
            (long, int)? best = null;
            for (int k = Limits.CubeSide; k <= 16 && k <= H; k++)
            {
                int need = (int)Math.Ceiling(k * k * k * Limits.SolidFill);
                (long, int)? at = null;
                for (int y = 0; y + k <= H && at == null; y++)
                    for (int z = 0; z < 16 && z + k <= W && at == null; z++)
                        for (int x = 0; x < 16 && x + k <= W && at == null; x++)
                            if (Sum(x, y, z, k) >= need)
                            {
                                // A part of the cube to start measuring the whole mass from.
                                for (int dy = 0; dy < k && at == null; dy++)
                                    for (int dz = 0; dz < k && at == null; dz++)
                                        for (int dx = 0; dx < k && at == null; dx++)
                                        {
                                            long key = Key(x0 + x + dx, minY + y + dy, z0 + z + dz);
                                            if (set.Contains(key)) at = (key, k);
                                        }
                            }
                if (at == null) break;
                best = at;
            }
            return best;
        }

        /// <summary>The whole connected mass of the part from one of its blocks: how many, and the box they fill.</summary>
        static void Mass(LagKind kind, HashSet<long> set, long seed, int side, HashSet<long> seen, List<Hit> hits)
        {
            var queue = new Queue<long>();
            queue.Enqueue(seed);
            seen.Add(seed);
            int n = 0;
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            while (queue.Count > 0 && n < Limits.MassMeasuredUpTo)
            {
                var (x, y, z) = Unkey(queue.Dequeue());
                n++;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); minZ = Math.Min(minZ, z);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); maxZ = Math.Max(maxZ, z);
                foreach (var (dx, dy, dz) in Neighbours)
                {
                    long k = Key(x + dx, y + dy, z + dz);
                    if (set.Contains(k) && seen.Add(k)) queue.Enqueue(k);
                }
            }
            int cx = (minX + maxX) / 2, cy = (minY + maxY) / 2, cz = (minZ + maxZ) / 2;
            hits.Add(new Hit(kind, cx, cy, cz, (minX, minY, minZ, maxX, maxY, maxZ), n,
                $"{n:N0} {PartWords(kind)} packed solid ({maxX - minX + 1}×{maxY - minY + 1}×{maxZ - minZ + 1} blocks, a solid {side}×{side}×{side} inside)"));
        }

        static readonly (int, int, int)[] Neighbours = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

        /// <summary>
        /// Floors of the part found in this chunk: each connected patch on one level, kept when it is big, at least
        /// Limits.FloorSide across both ways and nearly fills the rectangle around it.
        /// </summary>
        static void Floors(LagKind kind, HashSet<long> set, ChunkPos chunk, HashSet<long> seen, List<Hit> hits)
        {
            foreach (long start in set.Where(k => { var (x, _, z) = Unkey(k); return x >> 4 == chunk.X && z >> 4 == chunk.Z; }).ToList())
            {
                if (!seen.Add(start)) continue;
                var queue = new Queue<long>();
                queue.Enqueue(start);
                int n = 0, level = Unkey(start).Y;
                int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
                while (queue.Count > 0 && n < Limits.MassMeasuredUpTo)
                {
                    var (x, y, z) = Unkey(queue.Dequeue());
                    n++;
                    minX = Math.Min(minX, x); minZ = Math.Min(minZ, z); maxX = Math.Max(maxX, x); maxZ = Math.Max(maxZ, z);
                    foreach (var (dx, dz) in (ReadOnlySpan<(int, int)>)[(1, 0), (-1, 0), (0, 1), (0, -1)])
                    {
                        long k = Key(x + dx, y, z + dz);
                        if (set.Contains(k) && seen.Add(k)) queue.Enqueue(k);
                    }
                }
                int w = maxX - minX + 1, d = maxZ - minZ + 1;
                if (n < Limits.FloorParts || Math.Min(w, d) < Limits.FloorSide || n < w * d * Limits.SolidFill) continue;
                hits.Add(new Hit(kind, (minX + maxX) / 2, level, (minZ + maxZ) / 2, (minX, level, minZ, maxX, level, maxZ), n,
                    $"{n:N0} {PartWords(kind)} covering a {w}×{d} floor"));
            }
        }

        /// <summary>Two observers watching each other: the fastest redstone clock. Only backs up a machine found beside it.</summary>
        void Clocks(List<Hit> hits)
        {
            var at = new Dictionary<long, string>(_observers.Count);
            foreach (var (x, y, z, f) in _observers) at[Key(x, y, z)] = f;
            foreach (var (x, y, z, f) in _observers)
            {
                var (dx, dy, dz, back) = f switch
                {
                    "north" => (0, 0, -1, "south"), "south" => (0, 0, 1, "north"), "east" => (1, 0, 0, "west"),
                    "west" => (-1, 0, 0, "east"), "up" => (0, 1, 0, "down"), "down" => (0, -1, 0, "up"), _ => (0, 0, 0, ""),
                };
                if (back.Length == 0 || !at.TryGetValue(Key(x + dx, y + dy, z + dz), out var other) || other != back) continue;
                // Each pair once: from the observer facing a positive direction.
                if (f is "north" or "west" or "down") continue;
                hits.Add(new Hit(LagKind.WaitingUpdates, x, y, z, Around(x, y, z), 0, $"two observers watching each other at {x}, {y}, {z} (the fastest redstone clock)", Supporting: true));
            }
        }
    }
}
