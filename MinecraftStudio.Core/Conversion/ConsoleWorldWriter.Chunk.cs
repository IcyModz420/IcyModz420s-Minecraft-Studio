// MinecraftStudio.Core/Conversion/ConsoleWorldWriter.Chunk.cs: chunk for ConsoleWorldWriter.
// Part of ConsoleWorldWriter (main part in ConsoleWorldWriter.cs).
using System.Buffers.Binary;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

public partial class ConsoleWorldWriter
{
    // ---------------------------------------------------------------- grid-128 v8 chunk

    static (int Id, int Data) Numeric(BlockState s)
    {
        if (s.IsAir) return (0, 0);
        var (id, data) = JavaBlocks.ToLegacy(s, LegacyLevel.Release_1_12);
        // Command blocks never existed on Legacy Console.
        if (id is 137 or 210 or 211) return (1, 0);
        return (id, data);
    }

    byte[] EncodeChunk(CanonicalChunk c, int cx, int cz, int shiftX, int shiftZ)
    {
        // Flatten to 16x256x16 numeric arrays (index x*16*256... built per half below).
        var ids = new byte[65536];
        var data = new byte[65536];
        foreach (var (sy, s) in c.Sections)
        {
            if (sy is < 0 or > 15) continue;
            var numeric = s.Palette.Select(Numeric).ToArray();
            for (int i = 0; i < 4096; i++)
            {
                var (id, d) = numeric[s.Indices?[i] ?? 0];
                if (id == 0) continue;
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                int at = (x * 16 + z) * 256 + y;
                ids[at] = (byte)id;
                data[at] = (byte)d;
            }
        }
        var sky = ChunkPacking.SkyLight(c, 0, 15, _dim != "minecraft:the_nether");
        var blockLight = ChunkPacking.BlockLight(c, 0, 15);
        byte Light(Dictionary<int, byte[]> arr, int x, int y, int z) => arr[y >> 4][BlockSection.Index(x, y & 15, z)];

        var w = new ByteWriter(40000);
        w.U16BE(ChunkVersion);
        w.I32BE(cx);
        w.I32BE(cz);
        w.I64BE(c.LastUpdate);
        for (int half = 0; half < 2; half++) WriteBlockStorage(w, ids, half);
        for (int half = 0; half < 2; half++) WriteSparse(w, (x, y, z) => data[(x * 16 + z) * 256 + half * 128 + y]);
        for (int half = 0; half < 2; half++) WriteSparse(w, (x, y, z) => Light(sky, x, half * 128 + y, z));
        for (int half = 0; half < 2; half++) WriteSparse(w, (x, y, z) => Light(blockLight, x, half * 128 + y, z));
        // Height map: first y above the top light-blocking block, index z*16+x.
        for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
            {
                int h = 0;
                for (int y = 255; y >= 0; y--)
                    if (ids[(x * 16 + z) * 256 + y] != 0 && ChunkPacking.Opacity(c.GetBlock(x, y, z)) > 0) { h = y + 1; break; }
                w.U8((byte)Math.Min(255, h));
            }
        w.U16BE(0x07FE);
        var surface = c.Heights();
        for (int i = 0; i < 256; i++) w.U8((byte)Math.Clamp(BiomeNames.ToJavaId(c.SurfaceBiome(i & 15, i >> 4, surface), console: true), 0, 254));
        w.Bytes(NbtBinary.Serialize(ChunkNbt(c, shiftX, shiftZ), NbtFormat.Java));
        return w.ToArray();
    }

    /// <summary>One 128-high half: 512 grids of 4×4×4 (uniform when one id, else 64 raw bytes).</summary>
    static void WriteBlockStorage(ByteWriter w, byte[] ids, int half)
    {
        var index = new ushort[512];
        var grids = new ByteWriter(16384);
        var grid = new byte[64];
        for (int gx = 0; gx < 4; gx++)
            for (int gz = 0; gz < 4; gz++)
                for (int gy = 0; gy < 32; gy++)
                {
                    int t = (gx * 4 + gz) * 32 + gy;
                    bool uniform = true;
                    for (int k = 0; k < 64; k++)
                    {
                        int lx = k >> 4, lz = (k >> 2) & 3, ly = k & 3;
                        int x = gx * 4 + lx, z = gz * 4 + lz, y = half * 128 + gy * 4 + ly;
                        grid[k] = ids[(x * 16 + z) * 256 + y];
                        if (grid[k] != grid[0]) uniform = false;
                    }
                    if (uniform) index[t] = (ushort)((grid[0] << 8) | 0x07);
                    else
                    {
                        index[t] = (ushort)(((grids.Length >> 1) << 2) | 0x03);
                        grids.Bytes(grid);
                    }
                }
        w.I32BE(1024 + grids.Length);
        foreach (var e in index) w.U16LE(e);
        w.Bytes(grids.WrittenSpan);
    }

    /// <summary>Sparse nibble storage for one half: per y a 128-byte plane (x*16+z nibbles) or an all-0 / all-15 marker.</summary>
    static void WriteSparse(ByteWriter w, Func<int, int, int, int> value)
    {
        var markers = new byte[128];
        var planes = new List<byte[]>();
        var seen = new Dictionary<string, int>();
        for (int y = 0; y < 128; y++)
        {
            var plane = new byte[128];
            bool zero = true, full = true;
            for (int x = 0; x < 16; x++)
                for (int z = 0; z < 16; z++)
                {
                    int v = value(x, y, z) & 15;
                    if (v != 0) zero = false;
                    if (v != 15) full = false;
                    PackedArray.SetNibble(plane, x * 16 + z, v);
                }
            if (zero) { markers[y] = 0x80; continue; }
            if (full) { markers[y] = 0x81; continue; }
            string key = Convert.ToBase64String(plane);
            if (!seen.TryGetValue(key, out int n))
            {
                n = planes.Count;
                planes.Add(plane);
                seen[key] = n;
            }
            markers[y] = (byte)n;
        }
        w.I32BE(planes.Count);
        w.Bytes(markers);
        foreach (var p in planes) w.Bytes(p);
    }

    NbtCompound ChunkNbt(CanonicalChunk c, int shiftX, int shiftZ)
    {
        var root = new NbtCompound();
        var entities = new NbtList(NbtType.Compound);
        if (_options.ConvertEntities)
        {
            // Riders that get off (a console seats one rider on each, the old way) stand beside it.
            var setDown = new List<NbtCompound>();
            foreach (var e in c.Entities)
                if (Entities.ToJava(e, 0, setDown) is NbtCompound j) { Shift(j, shiftX, shiftZ, entity: true); entities.Add(j); }
            foreach (var j in setDown) { Shift(j, shiftX, shiftZ, entity: true); entities.Add(j); }
        }
        if (entities.Count > 0) root["Entities"] = entities;
        var tiles = new NbtList(NbtType.Compound);
        var have = new HashSet<(int, int, int)>();
        if (_options.ConvertBlockEntities)
            foreach (var be in c.BlockEntities)
            {
                if (StateEdit.Local(c, be) is not var (x, y, z)) continue;
                var block = c.GetBlock(x, y, z);
                if (BlockEntities.ToLegacyJava(be, be.GetString("id") ?? "", 0, block) is NbtCompound j) { Shift(j, shiftX, shiftZ, entity: false); tiles.Add(j); have.Add((x, y, z)); }
            }
        foreach (var (sy, s) in c.Sections)
        {
            if (!s.Palette.Any(p => BlockEntities.ImpliedForLegacyJava(p, 0, 0, 0) != null)) continue;
            for (int i = 0; i < 4096; i++)
            {
                var b = s.Indices == null ? s.Palette[0] : s.Palette[s.Indices[i]];
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                if (have.Contains((x, y, z)) || BlockEntities.ImpliedForLegacyJava(b, c.X * 16 + x, y, c.Z * 16 + z) is not NbtCompound implied) continue;
                if (BlockEntities.ToLegacyJava(implied, implied.GetString("id")!, 0, b) is NbtCompound j) { Shift(j, shiftX, shiftZ, entity: false); tiles.Add(j); }
            }
        }
        root["TileEntities"] = tiles;
        root["TileTicks"] = new NbtList(NbtType.Compound);
        return root;
    }

    static void Shift(NbtCompound nbt, int dx, int dz, bool entity)
    {
        if (dx == 0 && dz == 0) return;
        if (entity && nbt.GetList("Pos") is { Count: 3 } p)
        {
            p[0] = new NbtDouble(p[0].AsDouble - dx);
            p[2] = new NbtDouble(p[2].AsDouble - dz);
            foreach (var (k, d) in new[] { ("TileX", dx), ("TileZ", dz) })
                if (nbt.GetLongOrNull(k) is long v) nbt.SetInt(k, (int)(v - d));
        }
        else if (!entity)
        {
            nbt.SetInt("x", nbt.GetInt("x") - dx);
            nbt.SetInt("z", nbt.GetInt("z") - dz);
        }
        // What it rides, and who rides it, move with it.
        if (!entity) return;
        if (nbt.GetCompound("Riding") is NbtCompound mount) Shift(mount, dx, dz, entity: true);
        foreach (var r in nbt.GetList("Passengers")?.OfType<NbtCompound>() ?? []) Shift(r, dx, dz, entity: true);
    }
}
