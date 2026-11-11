// MinecraftStudio.Core/Conversion/ConversionModel.cs: the conversion target descriptor (what to convert to) and ConversionOptions (how).
// Owns: TargetKind, JavaFormat, BedrockTerrain, ConversionTarget, ConversionOptions, WorldInfo, CanonicalPlayer, IWorldWriter.
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public enum TargetKind
{
    Java,
    Bedrock,
    LegacyConsole,
    /// <summary>Pocket Edition 0.2–0.8 / Pi Edition (chunks.dat).</summary>
    PocketLegacy,
    /// <summary>Java Classic level file.</summary>
    Classic,
    /// <summary>Java Indev .mclevel.</summary>
    Indev,
}

/// <summary>Java storage formats by era.</summary>
public enum JavaFormat
{
    /// <summary>Infdev 20100327 – Beta 1.2: one file per chunk.</summary>
    Alpha,
    /// <summary>Beta 1.3 – 1.1: .mcr regions, 128 high.</summary>
    McRegion,
    /// <summary>1.2 – 1.12: .mca regions, numeric ids in 16-high sections.</summary>
    AnvilNumeric,
    /// <summary>1.13 – 1.17: Level wrapper with palettes.</summary>
    AnvilPalette,
    /// <summary>1.18+: root-level sections with block_states/biomes containers.</summary>
    AnvilContainers,
}

/// <summary>How a Bedrock / Pocket Edition target stores blocks.</summary>
public enum BedrockTerrain
{
    /// <summary>1.13+: palettes of {name, states, version}.</summary>
    States,
    /// <summary>1.4–1.12: two-layer palettes of {name, val} (sub-chunk version 8).</summary>
    NameVal,
    /// <summary>1.2.13–1.3: one-layer palette of {name, val} (sub-chunk version 1).</summary>
    NameValSingle,
    /// <summary>1.1–1.2.10: sub-chunk version 0, block ids and data.</summary>
    Numeric,
    /// <summary>Pocket Edition 0.9–0.16: LegacyTerrain, 128 blocks high.</summary>
    LegacyTerrain,
}

/// <summary>A version a world can be converted to.</summary>
public sealed class ConversionTarget
{
    public required TargetKind Kind { get; init; }
    public required string Name { get; init; }
    /// <summary>Java: data version written to chunks and level.dat (0 for versions before 15w32a).</summary>
    public int DataVersion { get; init; }
    /// <summary>Java: block ids allowed for pre-flattening targets (see <see cref="LegacyLevel"/>).</summary>
    public double LegacyLevel { get; init; } = Conversion.LegacyLevel.Release_1_12;
    public JavaFormat JavaFormat { get; init; }
    /// <summary>Java: level.dat Version.Name / Bedrock: version list like 1.21.120.</summary>
    public string VersionName { get; init; } = "";
    /// <summary>Bedrock: chunk version record (42 = 1.21.120+ … 22 = 1.16.210/1.17).</summary>
    public int BedrockChunkVersion { get; init; }
    /// <summary>Bedrock: level.dat storage version header (8, 9 or 10).</summary>
    public int BedrockStorageVersion { get; init; }
    /// <summary>Bedrock: lastOpenedWithVersion / MinimumCompatibleClientVersion.</summary>
    public int[] BedrockVersion { get; init; } = [];
    public int BedrockNetworkVersion { get; init; }
    public BedrockTerrain BedrockTerrain { get; init; }
    /// <summary>
    /// Bedrock: Java data version with the same blocks as the target (Bedrock and Java gained blocks together); blocks
    /// newer than that become the closest older block. 0 = no limit.
    /// </summary>
    public int ContentDataVersion { get; init; }
    public ConsolePlatform Platform { get; init; }
    public override string ToString() => Name;
}

/// <summary>Choices the user makes in the convert dialog.</summary>
public sealed class ConversionOptions
{
    /// <summary>Overworld chunks that only use y 0–255: let the game add terrain below y=0 (as when it upgrades its own old worlds).</summary>
    public bool LetGameExtendOldTerrain { get; set; } = true;

    /// <summary>Writes chunks a Java world had not finished making as they are. A conversion leaves them for the target to
    /// make; an edit into a copy of the world in its own format (placing, replacing, shaping) keeps every chunk it touches.</summary>
    public bool KeepUnfinishedChunks { get; set; }
    public bool ConvertEntities { get; set; } = true;
    public bool ConvertBlockEntities { get; set; } = true;
    public bool ConvertPlayers { get; set; } = true;
    /// <summary>Only these dimensions (canonical ids); null = all.</summary>
    public HashSet<string>? Dimensions { get; set; }
    /// <summary>Chunk area limit (inclusive chunk coordinates), null = everything.</summary>
    public (int MinX, int MinZ, int MaxX, int MaxZ)? Area { get; set; }
    /// <summary>Finite targets (Classic, Indev, Pocket 0.x, console sizes): the block area to keep, centred on spawn unless given.</summary>
    public int FiniteWidth { get; set; } = 256;
    public int FiniteLength { get; set; } = 256;
    public int? FiniteCenterX { get; set; }
    public int? FiniteCenterZ { get; set; }
    /// <summary>Legacy Console: world size in chunks (54 classic, 64 small, 192 medium, 320 large).</summary>
    public int ConsoleWorldChunks { get; set; } = 54;
    /// <summary>Xbox 360: one of the user's own save packages; the converted world replaces its savegame.dat (rehashed).</summary>
    public string? ConsoleTemplatePackage { get; set; }
    public string? WorldName { get; set; }
    /// <summary>
    /// Blocks to move the overworld up (a multiple of 16): 64 keeps y −64…191 of a 1.18+ world in a version that is only
    /// 256 blocks high, instead of cutting everything below y 0.
    /// </summary>
    public int VerticalShift { get; set; }
    /// <summary>Reopen and check the converted world afterwards.</summary>
    public bool Verify { get; set; } = true;

    /// <summary>
    /// Optional edit applied to each canonical chunk (chunk, dimension id) just before it is written — used to stamp a
    /// structure into the new world. Null in an ordinary conversion, so it changes nothing there.
    /// </summary>
    public Action<CanonicalChunk, string>? StampChunk { get; set; }

    /// <summary>
    /// Blocks changed as the world is converted ("pattern → block", the Replace window's rules: the first that matches
    /// wins, the new block keeps the properties it shares): what a mod's blocks, or blocks the target lacks, become. A
    /// changed block's chest contents or sign go with it. Null or empty: no block is changed.
    /// </summary>
    public IReadOnlyList<Structures.ReplaceRule>? BlockRules { get; set; }
}

/// <summary>World-level settings in edition-neutral form.</summary>
public sealed class WorldInfo
{
    public string Name { get; set; } = "Converted World";
    public long Seed { get; set; }
    public int SpawnX { get; set; }
    public int SpawnY { get; set; } = 64;
    public int SpawnZ { get; set; }
    /// <summary>0 survival, 1 creative, 2 adventure, 3 spectator.</summary>
    public int GameType { get; set; }
    /// <summary>0 peaceful … 3 hard.</summary>
    public int Difficulty { get; set; } = 2;
    public bool Hardcore { get; set; }
    public bool AllowCommands { get; set; }
    public long Time { get; set; }
    public long DayTime { get; set; } = 6000;
    public bool Raining { get; set; }
    public bool Thundering { get; set; }
    public int RainTime { get; set; } = 12000;
    public int ThunderTime { get; set; } = 12000;
    public DateTime LastPlayed { get; set; } = DateTime.UtcNow;
    /// <summary>default, flat, amplified, large_biomes, void, old (limited Pocket/console size).</summary>
    public string Generator { get; set; } = "default";
    /// <summary>Java game rule names (keepInventory, doDaylightCycle …) with string values.</summary>
    public Dictionary<string, string> GameRules { get; } = new(StringComparer.Ordinal);
    /// <summary>Players in canonical (newest Java) player NBT; the first one is the single-player / host.</summary>
    public List<CanonicalPlayer> Players { get; } = [];
    /// <summary>Map art (filled maps) of the world.</summary>
    public List<CanonicalMap> Maps { get; } = [];
    /// <summary>
    /// Entities (canonical) to put into the world where they stand, with their dimension: what a Java player rode, kept in
    /// their own file, when the target can't keep it with the player (PlayerPlan).
    /// </summary>
    public List<(string Dimension, NbtCompound Entity)> LooseEntities { get; } = [];
}

public sealed class CanonicalPlayer
{
    public string Name { get; set; } = "Player";
    public Guid Uuid { get; set; } = Guid.NewGuid();
    /// <summary>Canonical Java player compound (Pos, Rotation, Health, Inventory, EnderItems, XpLevel, abilities …).</summary>
    public NbtCompound Data { get; set; } = new();
    /// <summary>Dimension the player is in (canonical id).</summary>
    public string Dimension { get; set; } = "minecraft:overworld";
    /// <summary>The file the player came from in the source (Legacy Console: players/&lt;xuid&gt;.dat).</summary>
    public string? SourceFile { get; set; }
}

/// <summary>Receives canonical chunks and produces a world of one edition/version.</summary>
public interface IWorldWriter : IDisposable
{
    /// <summary>False when the target has no such dimension (custom dimensions on Bedrock, the End on Pocket 0.x …).</summary>
    bool SupportsDimension(string dimensionId);
    /// <summary>Called once before the first chunk with the world settings and players (after any vertical shift).</summary>
    void Begin(WorldInfo info) { }
    void BeginDimension(string dimensionId);
    void WriteChunk(CanonicalChunk chunk);
    void EndDimension();
    void Finish(WorldInfo info, IProgress<string>? progress = null);
    /// <summary>Lossy decisions made while writing (shown in the report).</summary>
    IReadOnlyList<string> Notes { get; }
}
