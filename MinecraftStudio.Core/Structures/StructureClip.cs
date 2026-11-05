// Structures/StructureClip.cs: crop a structure out of a world, and place one back into a world.
// Owns: Extract (reads a block box out of any edition through CanonicalReader, with its block entities and entities)
// and Stamp (an edit applied to each canonical chunk as a world is converted to a NEW copy, writing the structure in
// at a chosen spot). Placement never changes the source world — it rides the normal, matrix-tested conversion to a
// fresh world, so a bad placement can only affect the copy. Blocks are canonical, so crop and place cross editions.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public static class StructureClip
{
    /// <summary>
    /// Lifts the inclusive block box (minX..maxX, minY..maxY, minZ..maxZ) in world coordinates out of a dimension.
    /// Works for every edition: the blocks come through <see cref="CanonicalReader"/> as canonical Java states.
    /// </summary>
    public static Structure Extract(SaveWorld world, WorldDimension dim, int minX, int minY, int minZ, int maxX, int maxY, int maxZ,
        bool includeBlockEntities = true, bool includeEntities = true, int dataVersion = 0)
    {
        if (maxX < minX || maxY < minY || maxZ < minZ) throw new ArgumentException("The area is empty.");
        var s = new Structure(maxX - minX + 1, maxY - minY + 1, maxZ - minZ + 1)
        {
            Origin = $"{world.Name} — {dim.DisplayName}",
            DataVersion = dataVersion,
        };
        var reader = new CanonicalReader(world, dim);
        for (int cz = minZ >> 4; cz <= (maxZ >> 4); cz++)
            for (int cx = minX >> 4; cx <= (maxX >> 4); cx++)
            {
                var c = reader.Get(new ChunkPos(cx, cz));
                if (c == null) continue;
                int x0 = Math.Max(minX, cx * 16), x1 = Math.Min(maxX, cx * 16 + 15);
                int z0 = Math.Max(minZ, cz * 16), z1 = Math.Min(maxZ, cz * 16 + 15);
                for (int wz = z0; wz <= z1; wz++)
                    for (int wx = x0; wx <= x1; wx++)
                        for (int wy = minY; wy <= maxY; wy++)
                            s.Set(wx - minX, wy - minY, wz - minZ, c.GetBlock(wx & 15, wy, wz & 15));

                if (includeBlockEntities)
                    foreach (var be in c.BlockEntities)
                    {
                        int bx = be.GetInt("x"), by = be.GetInt("y"), bz = be.GetInt("z");
                        if (bx < minX || bx > maxX || by < minY || by > maxY || bz < minZ || bz > maxZ) continue;
                        s.BlockEntities[s.Index(bx - minX, by - minY, bz - minZ)] = (NbtCompound)be.Clone();
                    }

                if (includeEntities)
                    foreach (var e in c.Entities)
                    {
                        if (Entities.Position(e) is not var (ex, ey, ez)) continue;
                        if (ex < minX || ex > maxX + 1 || ey < minY || ey > maxY + 1 || ez < minZ || ez > maxZ + 1) continue;
                        s.Entities.Add(new StructureEntity(ex - minX, ey - minY, ez - minZ, (NbtCompound)e.Clone()));
                    }
            }
        return s;
    }

    /// <summary>
    /// A <see cref="ConversionOptions.StampChunk"/> edit that writes <paramref name="s"/> into dimension
    /// <paramref name="dimId"/> with its low corner at world (ox, oy, oz). Applied while a world is converted to a new
    /// copy. Cells outside (<paramref name="minY"/>..<paramref name="maxY"/>) — the target's build height — are left
    /// out. With <paramref name="pasteAir"/> false, air in the structure is skipped so only the building's own blocks
    /// land and the surrounding terrain stays; true clears the whole box to exactly the structure.
    /// </summary>
    public static Action<CanonicalChunk, string> Stamp(Structure s, string dimId, int ox, int oy, int oz, int minY, int maxY,
        bool pasteAir = false, bool blockEntities = true, bool entities = true)
    {
        return (c, dim) =>
        {
            if (dim != dimId) return;
            int lx0 = Math.Max(0, c.X * 16 - ox), lx1 = Math.Min(s.SizeX - 1, c.X * 16 + 15 - ox);
            int lz0 = Math.Max(0, c.Z * 16 - oz), lz1 = Math.Min(s.SizeZ - 1, c.Z * 16 + 15 - oz);
            if (lx0 > lx1 || lz0 > lz1) return;

            var overwritten = new HashSet<(int X, int Y, int Z)>();
            for (int lz = lz0; lz <= lz1; lz++)
                for (int lx = lx0; lx <= lx1; lx++)
                    for (int ly = 0; ly < s.SizeY; ly++)
                    {
                        int wy = oy + ly;
                        if (wy < minY || wy > maxY) continue;
                        var st = s.Get(lx, ly, lz);
                        if (!pasteAir && st.IsAir) continue;
                        int wx = ox + lx, wz = oz + lz;
                        c.SetBlock(wx & 15, wy, wz & 15, st);
                        overwritten.Add((wx, wy, wz));
                    }
            if (overwritten.Count == 0) return;

            // Any block entity that was at a spot we overwrote no longer matches its block.
            c.BlockEntities.RemoveAll(be => overwritten.Contains((be.GetInt("x"), be.GetInt("y"), be.GetInt("z"))));

            if (blockEntities)
                foreach (var (index, be) in s.BlockEntities)
                {
                    int lx = index % s.SizeX, rem = index / s.SizeX, lz = rem % s.SizeZ, ly = rem / s.SizeZ;
                    int wx = ox + lx, wy = oy + ly, wz = oz + lz;
                    if (!overwritten.Contains((wx, wy, wz))) continue; // only where we actually placed the block
                    var clone = (NbtCompound)be.Clone();
                    // What it remembers inside the structure (a hive's flower…) moves with it, counted from where it was cropped.
                    if (be["x"] is { IsNumeric: true } && be["y"] is { IsNumeric: true } && be["z"] is { IsNumeric: true })
                        StructurePositions.BlockEntity(clone, Moved(s, be.GetInt("x") - lx, be.GetInt("y") - ly, be.GetInt("z") - lz, ox, oy, oz));
                    clone.SetInt("x", wx); clone.SetInt("y", wy); clone.SetInt("z", wz);
                    c.BlockEntities.Add(clone);
                }

            if (entities)
                foreach (var e in s.Entities)
                {
                    double wx = ox + e.X, wy = oy + e.Y, wz = oz + e.Z;
                    if ((int)Math.Floor(wx) >> 4 != c.X || (int)Math.Floor(wz) >> 4 != c.Z) continue;
                    if (wy < minY || wy > maxY + 1) continue;
                    var clone = (NbtCompound)e.Nbt.Clone();
                    var hanging = Hanging.OffsetOf(e.Nbt);
                    // Counted from the corner the entity's own Pos implies (where it was cropped): what it remembers inside the
                    // structure (its bed, its leash knot, a bee's hive) and its riders move with it.
                    if (Entities.Position(e.Nbt) is var (px, py, pz))
                    {
                        double dx = wx - px, dy = wy - py, dz = wz - pz;
                        StructurePositions.Entity(clone, Moved(s, (int)Math.Round(px - e.X), (int)Math.Round(py - e.Y), (int)Math.Round(pz - e.Z), ox, oy, oz));
                        StructurePositions.Passengers(clone, (rx, ry, rz) => (rx + dx, ry + dy, rz + dz));
                    }
                    clone["Pos"] = new NbtList(NbtType.Double, 3) { new NbtDouble(wx), new NbtDouble(wy), new NbtDouble(wz) };
                    // A painting, frame or leash knot hangs in the block its new centre is in (the game places it by that block).
                    if (Hanging.Is(clone.GetString("id") ?? "")) Hanging.SetBlockFromCentre(clone, hanging);
                    FreshIdentity(clone); // so it never clashes with the original (riders too)
                    c.Entities.Add(clone);
                }
        };
    }

    static void FreshIdentity(NbtCompound e)
    {
        e.Remove("UUID"); e.Remove("UUIDMost"); e.Remove("UUIDLeast");
        foreach (var rider in e.GetList("Passengers")?.OfType<NbtCompound>() ?? []) FreshIdentity(rider);
    }

    /// <summary>Remembered positions inside the structure's box (cropped at <paramref name="cx"/>, …) moved to the new corner.</summary>
    static StructurePositions.Map Moved(Structure s, int cx, int cy, int cz, int ox, int oy, int oz) => (x, y, z) =>
        s.InRange(x - cx, y - cy, z - cz) ? (x - cx + ox, y - cy + oy, z - cz + oz) : null;
}
