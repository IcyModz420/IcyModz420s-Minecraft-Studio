// MinecraftStudio.Core/World/LagMachines.Ticks.cs: waiting updates of LagMachines.
// Part of LagMachines (main part in LagMachines.cs). A machine running when the world was saved leaves its scheduled
// updates in the chunk: Java block_ticks/fluid_ticks (1.18+) or Level.TileTicks/LiquidTicks, Bedrock PendingTicks records,
// console TileTicks. Only redstone parts' updates count for a running machine (leaves, sand, fire and new terrain settle
// with updates of their own); water and lava have their own, higher limit.
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.World;

public static partial class LagMachines
{
    /// <summary>Blocks whose waiting updates come from redstone (canonical Java and Bedrock base names).</summary>
    static readonly HashSet<string> RedstoneTicks = new(StringComparer.Ordinal)
    {
        "observer", "repeater", "unpowered_repeater", "powered_repeater", "comparator", "unpowered_comparator", "powered_comparator",
        "redstone_torch", "redstone_wall_torch", "unlit_redstone_torch", "lit_redstone_torch", "redstone_lamp", "lit_redstone_lamp",
        "piston", "sticky_piston", "dispenser", "dropper", "note_block", "noteblock", "powered_rail", "golden_rail", "detector_rail", "activator_rail",
        "tripwire", "tripwire_hook", "sculk_sensor", "calibrated_sculk_sensor", "target", "crafter", "command_block", "repeating_command_block",
        "chain_command_block", "daylight_detector", "daylight_detector_inverted", "redstone_wire", "copper_bulb",
    };

    /// <summary>Waiting updates of one sort in a chunk: how many of each block, and where they are.</summary>
    sealed class Waiting
    {
        public readonly Dictionary<string, int> Blocks = new(StringComparer.Ordinal);
        public int Count;
        long _sx, _sy, _sz, _n;
        int _minX = int.MaxValue, _minY = int.MaxValue, _minZ = int.MaxValue, _maxX = int.MinValue, _maxY = int.MinValue, _maxZ = int.MinValue;

        public void Add(string bare, NbtCompound t)
        {
            Count++;
            Blocks[bare] = Blocks.GetValueOrDefault(bare) + 1;
            if (t["x"] is not { IsNumeric: true } x || t["y"] is not { IsNumeric: true } y || t["z"] is not { IsNumeric: true } z) return;
            int ix = (int)x.AsLong, iy = (int)y.AsLong, iz = (int)z.AsLong;
            _sx += ix; _sy += iy; _sz += iz; _n++;
            _minX = Math.Min(_minX, ix); _minY = Math.Min(_minY, iy); _minZ = Math.Min(_minZ, iz);
            _maxX = Math.Max(_maxX, ix); _maxY = Math.Max(_maxY, iy); _maxZ = Math.Max(_maxZ, iz);
        }

        /// <summary>The middle of them and the box around them (the chunk's middle when none says where it is).</summary>
        public (int X, int Y, int Z, (int, int, int, int, int, int) Box) Place(ChunkPos pos)
        {
            if (_n == 0) { int bx = pos.X * 16, bz = pos.Z * 16; return (bx + 8, 64, bz + 8, (bx, 64, bz, bx + 15, 64, bz + 15)); }
            return ((int)(_sx / _n), (int)(_sy / _n), (int)(_sz / _n), (_minX, _minY, _minZ, _maxX, _maxY, _maxZ));
        }
    }

    static void Ticks(WorldDimension dim, ChunkPos pos, List<Hit> hits)
    {
        var redstone = new Waiting();
        var fluids = new Waiting();
        void One(string id, NbtCompound t)
        {
            string bare = id.StartsWith("minecraft:", StringComparison.Ordinal) ? id[10..] : id;
            if (bare is "water" or "flowing_water" or "lava" or "flowing_lava") fluids.Add(bare, t);
            else if (RedstoneTicks.Contains(bare)) redstone.Add(bare, t);
        }
        if (dim is BedrockDimension b)
        {
            foreach (var key in b.KeysAt(pos))
            {
                if (!BedrockKeys.TryParseChunkKey(key, out var ck) || ck.Tag != ChunkTag.PendingTicks) continue;
                foreach (var rec in b.ReadRecords(key))
                    foreach (var t in rec.GetList("tickList")?.OfType<NbtCompound>() ?? [])
                        One(t.GetCompound("blockState")?.GetString("name") ?? "", t);
            }
        }
        else if (dim.LoadChunkNbt(pos) is { } raw)
        {
            var level = raw["Level"] as NbtCompound;
            foreach (var list in new[] { raw.GetList("block_ticks"), raw.GetList("fluid_ticks"), level?.GetList("TileTicks"), level?.GetList("LiquidTicks"), raw.GetList("TileTicks") })
                foreach (var t in list?.OfType<NbtCompound>() ?? [])
                    One(t["i"] is NbtString s ? s.Value : "", t);
        }
        if (redstone.Count >= Limits.RedstoneUpdatesInOneChunk)
        {
            var (x, y, z, box) = redstone.Place(pos);
            hits.Add(new Hit(LagKind.WaitingUpdates, x, y, z, box, redstone.Count,
                $"{redstone.Count:N0} redstone updates waiting in one chunk (from {Named(redstone.Blocks, 3)}): it was running when the world was saved"));
        }
        // New terrain leaves thousands of these too (springs and falls settling), so on their own they prove nothing.
        if (fluids.Count >= Limits.FluidUpdatesInOneChunk)
        {
            var (x, y, z, box) = fluids.Place(pos);
            hits.Add(new Hit(LagKind.WaitingFluids, x, y, z, box, fluids.Count, $"{fluids.Count:N0} water and lava updates waiting in one chunk", Supporting: true));
        }
    }
}
