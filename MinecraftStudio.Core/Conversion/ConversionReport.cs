// MinecraftStudio.Core/Conversion/ConversionReport.cs: progress and result of a conversion: chunk counts, losses, rescue chests, checks and timing.
// Owns: ConversionProgress, ConversionReport.
using System.Diagnostics;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;
using MinecraftStudio.Core.Legacy;
using MinecraftStudio.Core.LegacyConsole;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

public sealed class ConversionProgress
{
    public required string Stage { get; init; }
    public int Done { get; init; }
    public int Total { get; init; }
}

public sealed class ConversionReport
{
    public int Chunks { get; set; }
    public int FailedChunks { get; set; }
    public TimeSpan Elapsed { get; set; }
    /// <summary>What to open to look at the result: the output folder, or the level file for single-file targets.</summary>
    public string? OpenPath { get; set; }
    public List<string> Notes { get; } = [];
    public List<string> Warnings { get; } = [];
    /// <summary>Everything the conversion changed or could not carry over (items, mobs, containers, players, map colours …).</summary>
    public List<LossEntry> Losses { get; } = [];
    /// <summary>Checks of the reopened converted world.</summary>
    public List<VerifyCheck> Checks { get; } = [];
    /// <summary>Differences the checks found (blocks that changed, entities that did not arrive …).</summary>
    public List<string> Issues { get; } = [];
    /// <summary>Rescue chests placed at the spawn.</summary>
    public List<(int X, int Y, int Z, string Label)> RescueChests { get; } = [];
    /// <summary>The host player was written as the target's own player (otherwise their items are in a rescue chest).</summary>
    public bool HostWritten { get; set; }
    /// <summary>Every check passed.</summary>
    public bool Verified => Checks.Count > 0 && Checks.All(c => c.Passed);
    /// <summary>Something is gone from the converted world (not kept by a stand-in, placeholder or rescue chest).</summary>
    public bool AnythingLost => FailedChunks > 0 || Losses.Any(l => l.Lost);
}
