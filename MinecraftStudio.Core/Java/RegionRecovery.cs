// MinecraftStudio.Core/Java/RegionRecovery.cs: What a region file holds: terrain (region/), entities
// (entities/, 1.17+) or points of interest (poi/, 1.14+).
// Owns: RegionContent, ChunkCopy, RegionRecovery.
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using MinecraftStudio.Core.IO;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Java;

/// <summary>What a region file holds: terrain (region/), entities (entities/, 1.17+) or points of interest (poi/, 1.14+).</summary>
public enum RegionContent { Terrain, Entities, PointsOfInterest }

/// <summary>
/// Where the readable copy of a rescued chunk is: where the header points (<see cref="Listed"/>), at another sector of the
/// file, or in the chunk's own .mcc file (with the compression it was found to use).
/// </summary>
public readonly record struct ChunkCopy(int Sector, byte ExternalType)
{
    public static ChunkCopy Listed => default;
    public bool IsListed => Sector == 0 && ExternalType == 0;
    public bool IsExternal => ExternalType != 0;
}

/// <summary>What a rescue could read from one region file (see <see cref="RegionFile.Recover"/>).</summary>
public sealed class RegionRecovery
{
    /// <summary>Every chunk that can be read, per slot, with the copy to use.</summary>
    public Dictionary<int, ChunkCopy> Chunks { get; } = [];
    /// <summary>Chunks the header no longer lists that were found in the file.</summary>
    public int Unlisted { get; set; }
    /// <summary>Damaged chunks replaced by another copy still in the file (an older one: the newest is the damaged one).</summary>
    public int Older { get; set; }
    /// <summary>Damaged chunks without a readable copy.</summary>
    public int Lost { get; set; }
    /// <summary>The newest DataVersion among the chunks (0 when none says).</summary>
    public int DataVersion { get; private set; }
    /// <summary>The file's length and last write when it was searched (the result is only valid for that file).</summary>
    public (long Length, DateTime LastWriteUtc) Stamp { get; set; }

    public static (long Length, DateTime LastWriteUtc) StampOf(string path)
    {
        var info = new FileInfo(path);
        return (info.Length, info.LastWriteTimeUtc);
    }

    internal void NoteVersion(NbtCompound chunk)
    {
        if (chunk.GetLongOrNull("DataVersion") is long dv && dv > DataVersion && dv < int.MaxValue) DataVersion = (int)dv;
    }
}
