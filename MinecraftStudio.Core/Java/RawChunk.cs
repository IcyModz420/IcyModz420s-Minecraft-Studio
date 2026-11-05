// MinecraftStudio.Core/Java/RawChunk.cs: Chunk payload exactly as stored in a region file (still compressed).
// Owns: RawChunk.
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Java;

/// <summary>Chunk payload exactly as stored in a region file (still compressed).</summary>
public sealed class RawChunk
{
    public RawChunk(byte compressionType, byte[] payload, int timestamp, string? customAlgorithm = null)
    {
        CompressionType = compressionType;
        Payload = payload;
        Timestamp = timestamp;
        CustomAlgorithm = customAlgorithm;
    }

    /// <summary>1 gzip, 2 zlib, 3 none, 4 LZ4, 127 custom (without the external 0x80 flag).</summary>
    public byte CompressionType { get; }
    public byte[] Payload { get; }
    public int Timestamp { get; set; }
    public string? CustomAlgorithm { get; }

    public static CompressionKind KindOf(byte type) => type switch
    {
        1 => CompressionKind.Gzip,
        2 => CompressionKind.Zlib,
        3 => CompressionKind.None,
        4 => CompressionKind.Lz4Java,
        _ => throw new SaveFormatException(type == 127 ? "Chunk uses a custom compression algorithm." : $"Unknown chunk compression type {type}."),
    };

    public static byte TypeOf(CompressionKind kind) => kind switch
    {
        CompressionKind.Gzip => 1,
        CompressionKind.Zlib => 2,
        CompressionKind.None => 3,
        CompressionKind.Lz4Java => 4,
        _ => throw new ArgumentException($"{kind} cannot be used for region chunks."),
    };

    public byte[] Decompress() => Compression.Decompress(Payload, KindOf(CompressionType));

    public static RawChunk FromNbt(NbtCompound chunk, CompressionKind kind, int timestamp)
    {
        byte[] nbt = NbtBinary.Serialize(chunk, NbtFormat.Java);
        return new RawChunk(TypeOf(kind), Compression.Compress(nbt, kind), timestamp);
    }
}
