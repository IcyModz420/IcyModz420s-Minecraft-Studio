// MinecraftStudio.Core/Bedrock/BedrockKeys.cs: Per-chunk record tags in Bedrock LevelDB keys.
// Owns: ChunkTag, ChunkKey, BedrockKeys.
using System.Buffers.Binary;
using System.Text;

namespace MinecraftStudio.Core.Bedrock;

/// <summary>Per-chunk record tags in Bedrock LevelDB keys.</summary>
public enum ChunkTag : byte
{
    Data3D = 43,
    Version = 44,
    Data2D = 45,
    Data2DLegacy = 46,
    SubChunkPrefix = 47,
    LegacyTerrain = 48,
    BlockEntity = 49,
    Entity = 50,
    PendingTicks = 51,
    BlockExtraData = 52,
    BiomeState = 53,
    FinalizedState = 54,
    ConversionData = 55,
    BorderBlocks = 56,
    HardcodedSpawners = 57,
    RandomTicks = 58,
    Checksums = 59,
    GenerationSeed = 60,
    GeneratedPreCavesAndCliffsBlending = 61,
    BlendingBiomeHeight = 62,
    MetaDataHash = 63,
    BlendingData = 64,
    ActorDigestVersion = 65,
    LegacyVersion = 118,
    /// <summary>Structure volumes intersecting the chunk (1.21.10+).</summary>
    AABBVolumes = 119,
    /// <summary>Jigsaw structure start; 21-byte key with the dimension always present and a trailing u64 id.</summary>
    JigsawStructureBlueprint = 120,
}

public readonly record struct ChunkKey(int X, int Z, int Dimension, ChunkTag Tag, sbyte SubChunk)
{
    public World.ChunkPos Pos => new(X, Z);
    public bool IsVersion => Tag is ChunkTag.Version or ChunkTag.LegacyVersion;
}

public static class BedrockKeys
{
    public static string TagName(byte tag) => Enum.IsDefined(typeof(ChunkTag), tag) ? ((ChunkTag)tag).ToString() : $"Tag{tag}";

    static bool IsKnownTag(byte t) => t is >= 43 and <= 65 or >= 118 and <= 120;

    public static bool TryParseChunkKey(byte[] key, out ChunkKey ck)
    {
        ck = default;
        int len = key.Length;
        if (len is not (9 or 10 or 13 or 14 or 21)) return false;
        int x = BinaryPrimitives.ReadInt32LittleEndian(key);
        int z = BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(4));
        if (x is < -(1 << 22) or > (1 << 22) || z is < -(1 << 22) or > (1 << 22)) return false;
        int dim = 0, tagAt = 8;
        if (len >= 13)
        {
            dim = BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(8));
            if (dim is < 0 or > 1000) return false;
            tagAt = 12;
        }
        byte tag = key[tagAt];
        if (!IsKnownTag(tag)) return false;
        if (len == 21)
        {
            // Jigsaw blueprint: x z dim 0x78 u64. "PosTrackDB-0x..." is also 21 bytes with 'x' at byte 12.
            if (tag != (byte)ChunkTag.JigsawStructureBlueprint || IsPrintable(key)) return false;
            ck = new ChunkKey(x, z, dim, ChunkTag.JigsawStructureBlueprint, 0);
            return true;
        }
        bool hasSub = len == tagAt + 2;
        if (hasSub != (tag == (byte)ChunkTag.SubChunkPrefix)) return false;
        ck = new ChunkKey(x, z, dim, (ChunkTag)tag, hasSub ? (sbyte)key[tagAt + 1] : (sbyte)0);
        return true;
    }

    public static byte[] Make(int x, int z, int dim, ChunkTag tag, sbyte? sub = null)
    {
        int len = (dim != 0 ? 13 : 9) + (sub.HasValue ? 1 : 0);
        var k = new byte[len];
        BinaryPrimitives.WriteInt32LittleEndian(k, x);
        BinaryPrimitives.WriteInt32LittleEndian(k.AsSpan(4), z);
        int p = 8;
        if (dim != 0) { BinaryPrimitives.WriteInt32LittleEndian(k.AsSpan(8), dim); p = 12; }
        k[p] = (byte)tag;
        if (sub.HasValue) k[p + 1] = (byte)sub.Value;
        return k;
    }

    static readonly byte[] DigestPrefix = "digp"u8.ToArray();
    static readonly byte[] ActorPrefix = "actorprefix"u8.ToArray();

    /// <summary>"digp" + x + z (+ dim): list of 8-byte actor ids stored in that chunk (1.18.30+).</summary>
    public static byte[] DigestKey(int x, int z, int dim)
    {
        var k = new byte[dim != 0 ? 16 : 12];
        DigestPrefix.CopyTo(k, 0);
        BinaryPrimitives.WriteInt32LittleEndian(k.AsSpan(4), x);
        BinaryPrimitives.WriteInt32LittleEndian(k.AsSpan(8), z);
        if (dim != 0) BinaryPrimitives.WriteInt32LittleEndian(k.AsSpan(12), dim);
        return k;
    }

    public static bool TryParseDigestKey(byte[] key, out int x, out int z, out int dim)
    {
        x = z = dim = 0;
        if ((key.Length != 12 && key.Length != 16) || !key.AsSpan(0, 4).SequenceEqual(DigestPrefix)) return false;
        x = BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(4));
        z = BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(8));
        if (key.Length == 16) dim = BinaryPrimitives.ReadInt32LittleEndian(key.AsSpan(12));
        return true;
    }

    public static byte[] ActorKey(ReadOnlySpan<byte> actorId)
    {
        var k = new byte[ActorPrefix.Length + actorId.Length];
        ActorPrefix.CopyTo(k, 0);
        actorId.CopyTo(k.AsSpan(ActorPrefix.Length));
        return k;
    }

    public static bool IsActorKey(byte[] key) => key.Length == ActorPrefix.Length + 8 && key.AsSpan(0, ActorPrefix.Length).SequenceEqual(ActorPrefix);

    /// <summary>Readable description of any key (for the database browser).</summary>
    public static string Describe(byte[] key)
    {
        if (TryParseChunkKey(key, out var ck))
        {
            string dim = ck.Dimension switch { 0 => "", 1 => " nether", 2 => " end", _ => $" dim {ck.Dimension}" };
            string sub = ck.Tag == ChunkTag.SubChunkPrefix ? $" y={ck.SubChunk}" : "";
            return $"chunk {ck.X},{ck.Z}{dim} {ck.Tag}{sub}";
        }
        if (TryParseDigestKey(key, out int x, out int z, out int d)) return $"digp {x},{z}" + (d != 0 ? $" dim {d}" : "");
        if (IsActorKey(key)) return "actorprefix " + Convert.ToHexString(key, 11, 8);
        if (IsPrintable(key)) return Encoding.UTF8.GetString(key);
        return Convert.ToHexString(key);
    }

    public static bool IsPrintable(byte[] key)
    {
        foreach (byte b in key) if (b < 0x20 || b > 0x7E) return false;
        return key.Length > 0;
    }
}
