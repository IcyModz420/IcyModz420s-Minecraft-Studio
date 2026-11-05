// MinecraftStudio.Core/Conversion/PocketWorldWriter.cs: Writes a Pocket Edition 0.2–0.8 / Pi Edition world:
// level.dat (NBT, storage version 2), chunks.dat (16×16 chunks of 16×128×16 in 21-sector slots) and
// entities.dat with chests, furnaces and signs.
// Owns: PocketWorldWriter.
using System.Buffers.Binary;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Writes a Pocket Edition 0.2–0.8 / Pi Edition world: level.dat (NBT, storage version 2), chunks.dat (16×16 chunks of
/// 16×128×16 in 21-sector slots) and entities.dat with chests, furnaces and signs. The world is 256×256 blocks centred
/// on the source spawn; chunks the source does not have are left for the game to generate.
/// </summary>
public sealed class PocketWorldWriter : IWorldWriter, IAreaLimitedWriter
{
    const int SectorSize = 4096, SectorsPerChunk = 21, ChunkBytes = 32768 + 16384 * 3 + 256;

    /// <summary>Block ids Pocket Edition 0.8 knew (numbers shared with later Bedrock versions).</summary>
    static readonly HashSet<int> Pe08Blocks =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 24, 26, 27, 30, 31, 32, 35, 37, 38, 39, 40,
        41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 53, 54, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 71, 73, 74, 78, 79,
        80, 81, 82, 83, 85, 86, 87, 89, 91, 92, 95, 96, 98, 99, 100, 101, 102, 103, 104, 105, 107, 108, 109, 112, 114, 128, 134,
        135, 136, 139, 141, 142, 155, 156, 157, 158, 170, 171, 173, 244, 245, 246, 247,
    ];

    readonly string _folder;
    readonly ConversionTarget _target;
    readonly ConversionOptions _options;
    readonly byte[]?[] _chunks = new byte[256][];
    readonly List<NbtCompound> _tiles = [];
    readonly Dictionary<int, (int Id, int Data)> _blocks = [];
    readonly HashSet<string> _stone = [];
    readonly List<string> _notes = [];
    WorldInfo? _info;
    (int X, int Z) _offset;
    int _written;

    public PocketWorldWriter(string folder, ConversionTarget target, ConversionOptions options)
    {
        _folder = folder;
        _target = target;
        _options = options;
        Directory.CreateDirectory(folder);
    }

    public IReadOnlyList<string> Notes => _notes;
    public bool SupportsDimension(string id) => id == "minecraft:overworld";

    (int X, int Z) Offset(WorldInfo info) => (((_options.FiniteCenterX ?? info.SpawnX) >> 4) - 8, ((_options.FiniteCenterZ ?? info.SpawnZ) >> 4) - 8);

    public (int MinX, int MinZ, int MaxX, int MaxZ)? SourceArea(string dimensionId, WorldInfo info)
    {
        _info = info;
        var (ox, oz) = Offset(info);
        return (ox, oz, ox + 15, oz + 15);
    }

    public void BeginDimension(string id) => _offset = Offset(_info ?? new WorldInfo());
    public void EndDimension() { }

    // ---------------------------------------------------------------- blocks

    (int Id, int Data)? Numeric(BlockState java)
    {
        if (java.IsAir) return (0, 0);
        var (bedrock, _) = BedrockWorldWriter.ToBedrock(java);
        if (BedrockBlocks.ToLegacy(bedrock) is not var (id, meta, _) || !Pe08Blocks.Contains(id)) return null;
        return (id, meta);
    }

    (int Id, int Data) Block(BlockState java)
    {
        if (_blocks.TryGetValue(java.Id, out var b)) return b;
        // The blocks of the matching Java release first, then the closest block 0.8 had.
        var (lid, ldata) = JavaBlocks.ToLegacy(java, _target.LegacyLevel);
        var s = java.IsAir ? java : JavaBlocks.FromLegacy(lid, ldata);
        b = (1, 0);
        for (int guard = 0; guard < 8; guard++)
        {
            if (Numeric(s) is (int id, int data)) { b = (id, data); break; }
            var next = Substitutes.Next(s, n => Numeric(BlockState.Get(n)) != null, Substitutes.PlainCubes);
            if (next == null || next == s) { _stone.Add(java.Name); break; }
            s = next;
        }
        _blocks[java.Id] = b;
        return b;
    }

    static void SetNibble(byte[] arr, int offset, int index, int value)
    {
        int at = offset + (index >> 1);
        arr[at] = (index & 1) == 0 ? (byte)((arr[at] & 0xF0) | (value & 15)) : (byte)((arr[at] & 0x0F) | ((value & 15) << 4));
    }

    public void WriteChunk(CanonicalChunk source)
    {
        int px = source.X - _offset.X, pz = source.Z - _offset.Z;
        if (px is < 0 or > 15 || pz is < 0 or > 15) return;
        var c = source.FitHeight(0, 7, bedrockFloor: true);
        var data = new byte[ChunkBytes];
        const int MetaAt = 32768, SkyAt = MetaAt + 16384, LightAt = SkyAt + 16384, ColumnAt = LightAt + 16384;
        var sky = ChunkPacking.SkyLight(c, 0, 7, true);
        var light = ChunkPacking.BlockLight(c, 0, 7);
        for (int sy = 0; sy <= 7; sy++)
        {
            c.Sections.TryGetValue(sy, out var s);
            var ids = s?.Palette.Select(Block).ToArray();
            for (int i = 0; i < 4096; i++)
            {
                int x = i & 15, z = (i >> 4) & 15, y = sy * 16 + (i >> 8);
                int li = (x << 11) | (z << 7) | y;
                if (s != null && ids != null)
                {
                    var (id, d) = ids[s.Indices?[i] ?? 0];
                    data[li] = (byte)id;
                    SetNibble(data, MetaAt, li, d);
                }
                SetNibble(data, SkyAt, li, sky[sy][i]);
                SetNibble(data, LightAt, li, light[sy][i]);
            }
        }
        var heights = c.Heights();
        for (int i = 0; i < 256; i++) data[ColumnAt + i] = (byte)(heights[i] == int.MinValue ? 0 : Math.Clamp(heights[i] + 1, 0, 127));
        _chunks[px + pz * 16] = data;
        _written++;
        if (_options.ConvertBlockEntities)
            foreach (var be in c.BlockEntities)
            {
                if (TileEntity(be, -_offset.X * 16, -_offset.Z * 16) is NbtCompound t) _tiles.Add(t);
                else if (BlockEntities.CarriedItems(be).Count > 0) BlockEntities.Drop(be, "Pocket Edition 0.2–0.8 has chests and furnaces only");
            }
        if (_options.ConvertEntities)
            foreach (var e in c.Entities) Entities.Drop(e, "Pocket Edition 0.2–0.8 worlds are written without mobs and objects");
    }

    /// <summary>Chests, furnaces and signs in the 0.x layout (numeric item ids, Text1–Text4).</summary>
    static NbtCompound? TileEntity(NbtCompound be, int dx, int dz)
    {
        string id = BlockEntities.CanonicalId(be);
        string? pocketId = id switch
        {
            "minecraft:chest" or "minecraft:trapped_chest" => "Chest",
            "minecraft:furnace" => "Furnace",
            "minecraft:sign" => "Sign",
            _ => null,
        };
        if (pocketId == null) return null;
        var t = new NbtCompound();
        t.SetString("id", pocketId);
        t.SetInt("x", be.GetInt("x") + dx);
        t.SetInt("y", be.GetInt("y"));
        t.SetInt("z", be.GetInt("z") + dz);
        if (pocketId == "Sign")
        {
            var lines = be.GetCompound("front_text")?.GetList("messages")?.Select(m => TextComponents.ToPlain(m is NbtString s ? s.Value : "")).ToList() ?? [];
            for (int i = 0; i < 4; i++) t.SetString($"Text{i + 1}", i < lines.Count ? lines[i] : "");
            return t;
        }
        var items = new NbtList(NbtType.Compound);
        foreach (var it in be.GetList("Items")?.OfType<NbtCompound>() ?? [])
        {
            string name = it.GetString("id") ?? "";
            if (name.Length == 0) continue;
            var (num, damage) = Items.LegacyItemNumberFitted(it);
            var p = new NbtCompound();
            p.SetByte("Slot", (sbyte)it.GetInt("Slot"));
            p.SetShort("id", (short)num);
            p.SetShort("Damage", (short)damage);
            p.SetByte("Count", (sbyte)Math.Clamp(it.GetInt("count", it.GetInt("Count", 1)), 1, 64));
            items.Add(p);
        }
        t["Items"] = items;
        if (pocketId == "Furnace") { t.SetShort("BurnTime", 0); t.SetShort("CookTime", 0); }
        return t;
    }

    // ---------------------------------------------------------------- files

    public void Finish(WorldInfo info, IProgress<string>? progress = null)
    {
        progress?.Report("Writing chunks.dat");
        int count = _chunks.Count(c => c != null);
        var file = new byte[SectorSize + count * SectorsPerChunk * SectorSize];
        int slot = 0;
        for (int i = 0; i < 256; i++)
        {
            if (_chunks[i] is not byte[] data) continue;
            int x = i & 15, z = i >> 4;
            int sector = 1 + slot++ * SectorsPerChunk;
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan((x + z * 32) * 4), (uint)(sector << 8 | SectorsPerChunk));
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(sector * SectorSize), ChunkBytes + 4);
            data.CopyTo(file, sector * SectorSize + 4);
        }
        SafeFile.WriteAllBytes(Path.Combine(_folder, "chunks.dat"), file);

        var level = new NbtCompound();
        level.SetInt("GameType", info.GameType == 1 ? 1 : 0);
        level.SetLong("LastPlayed", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        level.SetString("LevelName", info.Name);
        level.SetInt("Platform", 2);
        level.SetLong("RandomSeed", (int)info.Seed);
        level.SetLong("SizeOnDisk", file.Length);
        int spawnX = Math.Clamp(info.SpawnX - _offset.X * 16, 0, 255), spawnZ = Math.Clamp(info.SpawnZ - _offset.Z * 16, 0, 255);
        int spawnY = Math.Clamp(info.SpawnY, 1, 127);
        level.SetInt("SpawnX", spawnX); level.SetInt("SpawnY", spawnY); level.SetInt("SpawnZ", spawnZ);
        level.SetInt("StorageVersion", 2);
        level.SetLong("Time", info.DayTime % 19200);
        level.SetByte("spawnMobs", 1);
        level["Player"] = Player(info, spawnX, spawnY, spawnZ);
        var levelFile = new NbtFile { Format = NbtFormat.Bedrock, HeaderVersion = 2 };
        levelFile.Roots.Add(new NamedTag("", level));
        levelFile.Save(Path.Combine(_folder, "level.dat"));

        var root = new NbtCompound { { "Entities", new NbtList(NbtType.Compound) } };
        var tiles = new NbtList(NbtType.Compound);
        foreach (var t in _tiles) tiles.Add(t);
        root["TileEntities"] = tiles;
        byte[] body = NbtBinary.Serialize(root, NbtFormat.Bedrock);
        var ent = new ByteWriter(body.Length + 12);
        ent.Bytes("ENT\0"u8);
        ent.I32LE(1);
        ent.I32LE(body.Length);
        ent.Bytes(body);
        SafeFile.WriteAllBytes(Path.Combine(_folder, "entities.dat"), ent.WrittenSpan);

        _notes.Add($"Wrote {_written} of the 256 chunks of a 256 × 256 Pocket Edition world centred on the spawn; the game generates any missing ones.");
        _notes.Add("Blocks Pocket Edition 0.8 did not have became the closest block it had; mobs are not carried over (0.x stored them by number).");
        if (_stone.Count > 0) _notes.Add($"{_stone.Count} block type(s) became stone: {string.Join(", ", _stone.Take(8))}");
    }

    /// <summary>The single player of a 0.x world, placed on the spawn (inventory with numeric ids).</summary>
    NbtCompound Player(WorldInfo info, int x, int y, int z)
    {
        var p = new NbtCompound();
        NbtList Floats(params float[] v) { var l = new NbtList(NbtType.Float); foreach (var f in v) l.Add(new NbtFloat(f)); return l; }
        var host = info.Players.FirstOrDefault();
        double px = x + 0.5, py = y + 1.62, pz = z + 0.5;
        if (host?.Data.GetList("Pos") is { Count: 3 } pos)
        {
            double hx = pos[0].AsDouble - _offset.X * 16, hz = pos[2].AsDouble - _offset.Z * 16;
            if (hx is >= 0 and < 256 && hz is >= 0 and < 256) { px = hx; py = Math.Clamp(pos[1].AsDouble, 1, 127) + 1.62; pz = hz; }
        }
        p["Pos"] = Floats((float)px, (float)py, (float)pz);
        p["Rotation"] = Floats(0, 0);
        p["Motion"] = Floats(0, 0, 0);
        p.SetShort("Health", 20); p.SetShort("Air", 300); p.SetShort("Fire", -20); p.SetFloat("FallDistance", 0); p.SetByte("OnGround", 1);
        p.SetShort("AttackTime", 0); p.SetShort("DeathTime", 0); p.SetShort("HurtTime", 0);
        p.SetInt("Dimension", 0); p.SetInt("Score", 0); p.SetByte("Sleeping", 0); p.SetShort("SleepTimer", 0);
        p.SetInt("SpawnX", x); p.SetInt("SpawnY", y); p.SetInt("SpawnZ", z);
        p["abilities"] = new NbtCompound
        {
            { "mayfly", new NbtByte((sbyte)(info.GameType == 1 ? 1 : 0)) }, { "flying", new NbtByte(0) },
            { "invulnerable", new NbtByte((sbyte)(info.GameType == 1 ? 1 : 0)) }, { "mayBuild", new NbtByte(1) },
            { "instabuild", new NbtByte((sbyte)(info.GameType == 1 ? 1 : 0)) },
        };
        var inventory = new NbtList(NbtType.Compound);
        if (_options.ConvertPlayers)
            foreach (var it in host?.Data.GetList("Inventory")?.OfType<NbtCompound>() ?? [])
            {
                int slot = it.GetInt("Slot");
                if (slot is < 0 or > 35 || it.GetString("id") is null) continue;
                var (num, damage) = Items.LegacyItemNumberFitted(it);
                var e = new NbtCompound();
                e.SetByte("Slot", (sbyte)(slot + 9));
                e.SetShort("id", (short)num);
                e.SetShort("Damage", (short)damage);
                e.SetByte("Count", (sbyte)Math.Clamp(it.GetInt("count", it.GetInt("Count", 1)), 1, 64));
                inventory.Add(e);
            }
        p["Inventory"] = inventory;
        var armor = new NbtList(NbtType.Compound);
        for (int i = 0; i < 4; i++) armor.Add(new NbtCompound { { "id", new NbtShort(0) }, { "Damage", new NbtShort(0) }, { "Count", new NbtByte(0) } });
        p["Armor"] = armor;
        return p;
    }

    public void Dispose() { }
}
