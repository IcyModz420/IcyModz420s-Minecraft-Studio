// MinecraftStudio.Core/World/HeightmapWorld.cs: land made from a heightmap picture, as a world the converter reads like
// any other (Create ▸ New World from a Heightmap: the Convert window writes it as any edition and checks it block by block).
// Owns: HeightmapLand (how the picture becomes land: its y range, water and snow lines, blocks per pixel, where it goes),
// HeightmapWorld (the read-only source: one overworld; FitTo a target's heights; the spawn on dry land near the middle;
// GroundAt) and its dimension, whose chunks are made as they are read — bedrock, deepslate under y 0, stone, three
// blocks of dirt under grass, sand beaches and shallows, gravel deeper down, bare stone on steep slopes, snow above the
// snow line, water to the water level — with biomes to match.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Rendering;

namespace MinecraftStudio.Core.World;

/// <summary>How a heightmap picture becomes land.</summary>
public sealed record HeightmapLand
{
    public required HeightPicture Picture { get; init; }
    /// <summary>The y of the darkest ground (brightness 0) and of the brightest (1).</summary>
    public int LowY { get; init; } = 40;
    public int HighY { get; init; } = 160;
    /// <summary>Water fills lower ground up to this y; null: no water.</summary>
    public int? WaterY { get; init; } = 62;
    /// <summary>Ground at or above this y is snow; null: none.</summary>
    public int? SnowY { get; init; }
    /// <summary>Blocks per pixel (2: each pixel is 2 × 2 blocks; 0.5: two pixels to a block).</summary>
    public double BlocksPerPixel { get; init; } = 1;
    /// <summary>The block of the picture's north-west corner; null: the picture is centred on x 0, z 0.</summary>
    public int? WestX { get; init; }
    public int? NorthZ { get; init; }
    public string Name { get; init; } = "Heightmap World";
    /// <summary>The game makes flat land around the picture (otherwise its own terrain).</summary>
    public bool FlatAround { get; init; }
    /// <summary>0 survival, 1 creative.</summary>
    public int GameType { get; init; } = 1;
}

/// <summary>Land from a heightmap as a world to convert from: one overworld whose chunks are made as they are read.</summary>
public sealed class HeightmapWorld : SaveWorld
{
    /// <summary>The most blocks the land may cover along each side.</summary>
    public const int MaxBlocks = 30_000;
    /// <summary>A slope this steep (blocks up or down to a neighbour) is bare stone.</summary>
    const int Steep = 3;

    readonly HeightmapDimension _dim;
    readonly string _location;

    public HeightmapWorld(HeightmapLand land, string picturePath)
    {
        if (land.HighY <= land.LowY) throw new ArgumentException("The highest ground must be above the lowest.");
        if (!(land.BlocksPerPixel >= 0.125 && land.BlocksPerPixel <= 64)) throw new ArgumentException("Blocks per pixel must be between 1/8 and 64.");
        if (land.Picture.Width <= 0 || land.Picture.Height <= 0) throw new ArgumentException("The picture is empty.");
        Land = land;
        _location = picturePath;
        Width = (int)Math.Ceiling(land.Picture.Width * land.BlocksPerPixel);
        Length = (int)Math.Ceiling(land.Picture.Height * land.BlocksPerPixel);
        if (Width > MaxBlocks || Length > MaxBlocks)
            throw new ArgumentException($"That makes land {Width:N0} × {Length:N0} blocks; the most is {MaxBlocks:N0} along each side. Use fewer blocks per pixel.");
        WestX = land.WestX ?? -Width / 2;
        NorthZ = land.NorthZ ?? -Length / 2;
        _dim = new HeightmapDimension(this);
    }

    public HeightmapLand Land { get; }
    /// <summary>The land's size in blocks and the block of its north-west corner.</summary>
    public int Width { get; }
    public int Length { get; }
    public int WestX { get; }
    public int NorthZ { get; }
    /// <summary>The heights the chunks are made for (bedrock at the bottom); FitTo sets a target's.</summary>
    public int Bottom { get; private set; } = -64;
    public int Top { get; private set; } = 319;

    public override Edition Edition => Edition.Java;
    public override string Name => Land.Name;
    public override string Location => _location;
    public override string FormatDescription =>
        $"Land from a heightmap: {Land.Picture.Width:N0} × {Land.Picture.Height:N0} pixels, {Width:N0} × {Length:N0} blocks, ground y {Land.LowY} to {Land.HighY}";
    public override IReadOnlyList<WorldDimension> Dimensions => [_dim];
    public override void Save() => throw new NotSupportedException("Land from a heightmap is only read, to be written as a new world.");

    /// <summary>
    /// Makes the land within a target's heights (no blocks to cut away below or above); says what had to give — ground or
    /// water outside them — or null.
    /// </summary>
    public string? FitTo(ConversionTarget target)
    {
        (Bottom, Top) = HeightsFor(target);
        var notes = new List<string>();
        if (Land.LowY <= Bottom) notes.Add($"ground below y {Bottom + 1} was raised to it");
        if (Land.HighY > Top) notes.Add($"ground above y {Top} was cut down to it");
        if (Land.WaterY is int w && (w <= Bottom || w > Top)) notes.Add($"the water level y {w} became y {Math.Clamp(w, Bottom + 1, Top)}");
        return notes.Count == 0 ? null : $"{target.Name} keeps y {Bottom} to {Top}: " + string.Join("; ", notes) + ".";
    }

    /// <summary>The block heights a target keeps of a source's overworld (a Classic level: the 64 around sea level).</summary>
    public static (int Bottom, int Top) HeightsFor(ConversionTarget target) => target.Kind is TargetKind.Classic or TargetKind.Indev
        ? FiniteWorldWriter.SourceHeights(target)
        : WorldConverter.TargetHeight(target, "minecraft:overworld");

    /// <summary>The ground's height at a block (blended between the four nearest pixels), or null where the picture is see-through.</summary>
    public int? GroundAt(int x, int z)
    {
        var p = Land.Picture;
        double s = Land.BlocksPerPixel;
        double u = Math.Clamp((x - WestX + 0.5) / s - 0.5, 0, p.Width - 1), v = Math.Clamp((z - NorthZ + 0.5) / s - 0.5, 0, p.Height - 1);
        int u0 = (int)u, v0 = (int)v, u1 = Math.Min(u0 + 1, p.Width - 1), v1 = Math.Min(v0 + 1, p.Height - 1);
        double fu = u - u0, fv = v - v0, sum = 0, weight = 0;
        void Add(int pu, int pv, double w)
        {
            float level = p.Levels[pv * p.Width + pu];
            if (w > 0 && !float.IsNaN(level)) { sum += level * w; weight += w; }
        }
        Add(u0, v0, (1 - fu) * (1 - fv));
        Add(u1, v0, fu * (1 - fv));
        Add(u0, v1, (1 - fu) * fv);
        Add(u1, v1, fu * fv);
        if (weight <= 0) return null;
        int y = Land.LowY + (int)Math.Round(sum / weight * (Land.HighY - Land.LowY));
        return Math.Clamp(y, Bottom + 1, Top);
    }

    int Water => Land.WaterY is int w ? Math.Clamp(w, Bottom + 1, Top) : int.MinValue;

    /// <summary>Where players start: dry ground nearest the middle (within 512 blocks), else the water's surface there.</summary>
    public (int X, int Y, int Z) Spawn()
    {
        int cx = WestX + Width / 2, cz = NorthZ + Length / 2, water = Water;
        int reach = Math.Min(512, Math.Max(Width, Length) / 2);
        for (int r = 0; r <= reach; r += 4)
            for (int dz = -r; dz <= r; dz += 4)
                for (int dx = -r; dx <= r; dx += Math.Abs(dz) == r ? 4 : 2 * r)
                {
                    if (GroundAt(cx + dx, cz + dz) is int g && g >= water) return (cx + dx, g + 1, cz + dz);
                    if (r == 0) break;
                }
        int ground = GroundAt(cx, cz) ?? Math.Clamp(Land.LowY, Bottom + 1, Top);
        return (cx, Math.Max(ground, water) + 1, cz);
    }

    /// <summary>level.dat's settings for the converter: name, spawn, game mode, what the game makes around the land.</summary>
    internal void Describe(WorldInfo info)
    {
        info.Name = Land.Name;
        (info.SpawnX, info.SpawnY, info.SpawnZ) = Spawn();
        info.GameType = Land.GameType;
        info.AllowCommands = Land.GameType == 1;
        info.Generator = Land.FlatAround ? "flat" : "default";
        info.Seed = Random.Shared.NextInt64();
        info.DayTime = 6000;
    }

    enum Kind : byte { Air, Bedrock, Deepslate, Stone, Dirt, Grass, Sand, Gravel, Snow, Water }

    static readonly BlockState[] States =
    [
        BlockState.Air, Full("minecraft:bedrock"), Full("minecraft:deepslate"), Full("minecraft:stone"), Full("minecraft:dirt"),
        Full("minecraft:grass_block"), Full("minecraft:sand"), Full("minecraft:gravel"), Full("minecraft:snow_block"), Full("minecraft:water"),
    ];

    // A block with its default properties (grass_block[snowy=false], water[level=0]), as the converter's tables name them.
    static BlockState Full(string name) =>
        JavaBlockData.Latest.Blocks.TryGetValue(name, out var def) ? BlockState.Get(name, def.Default) : BlockState.Get(name);

    // The top block, the three under it and the biome for ground at height g with a slope (largest step to a neighbour).
    (Kind Top, Kind Under, string Biome) Surface(int g, int slope, int water)
    {
        if (g < water)
        {
            int depth = water - g;
            var floor = depth <= 8 ? Kind.Sand : Kind.Gravel;
            return (floor, floor, depth >= 20 ? "minecraft:deep_ocean" : "minecraft:ocean");
        }
        if (water != int.MinValue && g <= water + 2 && slope < Steep) return (Kind.Sand, Kind.Sand, "minecraft:beach");
        if (Land.SnowY is int snow && g >= snow) return (slope >= Steep ? Kind.Stone : Kind.Snow, Kind.Stone, "minecraft:snowy_plains");
        if (slope >= Steep) return (Kind.Stone, Kind.Stone, "minecraft:plains");
        return (Kind.Grass, Kind.Dirt, "minecraft:plains");
    }

    /// <summary>One chunk of the land (Java's newest block and biome names), or null when the picture has no land there.</summary>
    internal ChunkColumn? MakeChunk(ChunkPos pos)
    {
        int bx = pos.X * 16, bz = pos.Z * 16, water = Water;
        var heights = new int[18 * 18];
        bool any = false;
        for (int z = -1; z <= 16; z++)
            for (int x = -1; x <= 16; x++)
            {
                int? g = GroundAt(bx + x, bz + z);
                if (g != null && x is >= 0 and < 16 && z is >= 0 and < 16) any = true;
                heights[(z + 1) * 18 + x + 1] = g ?? Math.Clamp(Land.LowY, Bottom + 1, Top);
            }
        if (!any) return null;
        int minSection = Bottom >> 4, maxSection = Top >> 4, count = maxSection - minSection + 1;
        var kinds = new byte[count * 4096];
        var biomes = new string?[256];
        for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
            {
                int at = (z + 1) * 18 + x + 1, g = heights[at];
                int slope = Math.Max(Math.Max(Math.Abs(g - heights[at - 1]), Math.Abs(g - heights[at + 1])),
                    Math.Max(Math.Abs(g - heights[at - 18]), Math.Abs(g - heights[at + 18])));
                var (top, under, biome) = Surface(g, slope, water);
                biomes[z * 16 + x] = biome;
                for (int y = Bottom, end = Math.Max(g, water); y <= end; y++)
                {
                    var k = y == Bottom ? Kind.Bedrock : y > g ? Kind.Water : y == g ? top : y >= g - 3 ? under : y < 0 ? Kind.Deepslate : Kind.Stone;
                    kinds[((y >> 4) - minSection) * 4096 + BlockSection.Index(x, y & 15, z)] = (byte)k;
                }
            }
        var col = new ChunkColumn(pos.X, pos.Z, minSection, maxSection) { SurfaceBiomes = biomes, Status = "minecraft:full" };
        Span<int> slot = stackalloc int[States.Length];
        for (int s = 0; s < count; s++)
        {
            var cells = kinds.AsSpan(s * 4096, 4096);
            slot.Fill(-1);
            var palette = new List<BlockState>();
            foreach (byte k in cells)
                if (slot[k] < 0) { slot[k] = palette.Count; palette.Add(States[k]); }
            if (palette.Count == 1 && palette[0].IsAir) continue;
            ushort[]? indices = null;
            if (palette.Count > 1)
            {
                indices = new ushort[4096];
                for (int i = 0; i < 4096; i++) indices[i] = (ushort)slot[cells[i]];
            }
            col.Sections[minSection + s] = new BlockSection(minSection + s, [.. palette], indices);
        }
        return col;
    }

    sealed class HeightmapDimension(HeightmapWorld world) : WorldDimension("minecraft:overworld", "Overworld")
    {
        HashSet<ChunkPos>? _positions;

        public override IReadOnlyCollection<ChunkPos> ChunkPositions => _positions ??= Find();

        // Every chunk the land reaches into with at least one column of ground.
        HashSet<ChunkPos> Find()
        {
            var set = new HashSet<ChunkPos>();
            int cx0 = world.WestX >> 4, cz0 = world.NorthZ >> 4, cx1 = (world.WestX + world.Width - 1) >> 4, cz1 = (world.NorthZ + world.Length - 1) >> 4;
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                    if (HasGround(cx, cz)) set.Add(new ChunkPos(cx, cz));
            return set;
        }

        bool HasGround(int cx, int cz)
        {
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                    if (world.GroundAt(cx * 16 + x, cz * 16 + z) != null) return true;
            return false;
        }

        public override ChunkColumn? LoadChunk(ChunkPos pos) => ChunkPositions.Contains(pos) ? world.MakeChunk(pos) : null;
        public override ChunkInfo? GetChunkInfo(ChunkPos pos) => ChunkPositions.Contains(pos) ? new ChunkInfo { Pos = pos, Status = "minecraft:full" } : null;
        public override void DeleteChunks(IEnumerable<ChunkPos> chunks) => throw new NotSupportedException("Land from a heightmap is only read.");
        public override void SaveChanges() { }
    }
}
