// Structures/StructurePlacement.Finite.cs: an edit of some chunks of a Classic or Indev level into a copy of it.
// Part of StructurePlacement (main part in StructurePlacement.cs; the chunk edit for every other edition in
// StructurePlacement.Edit.cs). These levels are one block array, so — as placing a structure into one does — each chunk is
// read the way every edit reads it (canonical), edited, and only the blocks that changed are set back into the copied
// level, in the level's own numbers. They keep no biomes; their few block entities (Indev's chests) and mobs aren't
// written, so an edit that would change a block where a chest is or would be (moving one, bringing one back from a backup:
// its contents would stay behind), or put blocks where the level's player stands, is refused before anything is saved.
// Checked: every changed block reads back as the level holds it, the rest of the level the same.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public static partial class StructurePlacement
{
    static PlacementResult EditFinite(SaveWorld world, string dimensionId, List<ChunkPos> present, Action<CanonicalChunk> edit, string output,
        PlacementResult result, IProgress<ConversionProgress>? progress, CancellationToken cancel)
    {
        EnsureRoomForCopy(world.Location, output);
        progress?.Report(new ConversionProgress { Stage = "Copying the level", Done = 0, Total = 1 });
        result.OpenPath = CopyWorld(world.Location, output, progress, cancel);
        try
        {
            var changed = new List<(int X, int Y, int Z, BlockState Expected)>();
            using (var copy = WorldLoader.Open(result.OpenPath))
            {
                if (copy is not FiniteWorld level || level.Dimensions.FirstOrDefault() is not FiniteDimension dim)
                    throw new IOException("The copied level could not be opened for changing it.");
                bool classic = level.Classic != null;
                // Where the level's player stands (Indev: its LocalPlayer; the height may be the feet's or the eyes', so both).
                var standing = new HashSet<(int, int, int)>();
                if (level.Players.FirstOrDefault()?.Data.GetList("Pos") is { Count: 3 } pos)
                {
                    int px = (int)Math.Floor(pos[0].AsDouble), pz = (int)Math.Floor(pos[2].AsDouble);
                    foreach (double feet in (double[])[pos[1].AsDouble, pos[1].AsDouble - 1.62])
                        for (int h = 0; h < 2; h++) standing.Add((px, (int)Math.Floor(feet) + h, pz));
                }
                var reader = new CanonicalReader(copy, dim);
                int done = 0;
                foreach (var cp in present)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (done++ % 16 == 0) progress?.Report(new ConversionProgress { Stage = "Changing the level's blocks", Done = done, Total = present.Count });
                    if (reader.Get(cp) is not { } c) continue;
                    int x0 = cp.X * 16, z0 = cp.Z * 16;
                    // The blocks as they are, to set back only what the edit changes.
                    var before = new BlockState[16 * 16 * dim.Height];
                    for (int y = 0; y < dim.Height; y++)
                        for (int z = 0; z < 16; z++)
                            for (int x = 0; x < 16; x++) before[(y * 16 + z) * 16 + x] = c.GetBlock(x, y, z);
                    var chests = c.BlockEntities.Select(be => (be.GetInt("x"), be.GetInt("y"), be.GetInt("z"))).ToHashSet();
                    edit(c);
                    // A chest where the edit leaves one too: its contents would not come with it.
                    foreach (var be in c.BlockEntities) chests.Add((be.GetInt("x"), be.GetInt("y"), be.GetInt("z")));
                    for (int y = 0; y < dim.Height; y++)
                        for (int z = 0; z < 16; z++)
                            for (int x = 0; x < 16; x++)
                            {
                                int wx = x0 + x, wz = z0 + z;
                                if ((uint)wx >= (uint)dim.Width || (uint)wz >= (uint)dim.Length) continue;
                                var now = c.GetBlock(x, y, z);
                                if (now == before[(y * 16 + z) * 16 + x]) continue;
                                if (chests.Contains((wx, y, wz)))
                                    throw new NotSupportedException($"This would change the chest at {wx}, {y}, {wz}: an Indev level's chests can't be moved or brought back yet (their contents would stay behind). Choose an area without chests.");
                                if (!now.IsAir && standing.Contains((wx, y, wz)))
                                    throw new NotSupportedException($"This would put blocks where the level's player stands ({wx}, {y}, {wz}). Move the player first (Players page → Move to safety) or choose an area beside them.");
                                var (id, data) = FiniteBlock(now, classic);
                                dim.SetBlock(wx, y, wz, id, data);
                                changed.Add((wx, y, wz, LegacyBlocks.Get(id, data, LegacyIdScheme.Classic)));
                            }
                }
                level.Save();
            }
            result.BlocksPlaced = changed.Count;
            result.ChunksChanged = changed.Select(p => (p.X >> 4, p.Z >> 4)).Distinct().Count();
            result.Notes.Add("Classic and Indev levels: only blocks were changed (they keep no biomes; their mobs stay where they were).");

            // Read the copy back: every changed block as the level holds it, and the chunks around unchanged.
            using (var check = WorldLoader.Open(result.OpenPath))
            {
                var cd = check.Dimensions[0];
                var cache = new Dictionary<ChunkPos, ChunkColumn?>();
                long wrong = 0;
                foreach (var (x, y, z, expected) in changed)
                {
                    var p = new ChunkPos(x >> 4, z >> 4);
                    if (!cache.TryGetValue(p, out var col)) cache[p] = col = cd.LoadChunk(p);
                    if (col?.GetBlock(x & 15, y, z & 15) != expected) wrong++;
                }
                if (wrong > 0) result.Problems.Add($"{wrong:N0} changed block(s) did not read back as written");
            }
            CheckUntouched(world, result.OpenPath, dimensionId, present, result, cancel);
        }
        catch
        {
            TryDelete(output);
            throw;
        }
        return result;
    }
}
