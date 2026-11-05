// Structures/BuildCheck.cs: how far a build in a world is from its structure or schematic (the build checker).
// Owns: BuildCheck, BuildCheckResult, BuildProblem, BuildProblemKind. The structure (as turned and mirrored) laid at a spot
// of a world is compared block by block with what the world has there (canonical names, so any edition): right, missing
// (air or a liquid where a block belongs), the wrong block, the right block set wrong (a property that a builder chooses
// differs: facing, half, shape, a repeater's delay — not what changes by itself in play, like powered, lit, open, a crop's
// age or leaves' distance), and, when the structure's air counts, blocks that should not be there. Structure voids are
// skipped. What is left to gather is the material list of the missing and wrong cells (StructureMaterials). Only reads.
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

public enum BuildProblemKind { Missing, WrongBlock, WrongState, Extra }

/// <summary>One cell that is not as the structure has it: what belongs there and what the world has.</summary>
public sealed record BuildProblem(BuildProblemKind Kind, int X, int Y, int Z, BlockState Want, BlockState Have);

public sealed class BuildCheckResult
{
    public long Checked { get; set; }
    public long Right { get; set; }
    public long Missing { get; set; }
    public long WrongBlock { get; set; }
    public long WrongState { get; set; }
    public long Extra { get; set; }
    /// <summary>Cells in chunks the world has not generated (nothing can be built there yet).</summary>
    public long NotGenerated { get; set; }
    /// <summary>The cells that are wrong, lowest layer first (at most <see cref="BuildCheck.MaxProblems"/>).</summary>
    public List<BuildProblem> Problems { get; } = [];
    /// <summary>What is still to gather: the items for the missing and wrong cells.</summary>
    public List<MaterialLine> StillNeeded { get; set; } = [];
    public long Wrong => Missing + WrongBlock + WrongState + Extra;
    /// <summary>How much of the structure is built right (of the cells that count).</summary>
    public double Done => Checked == 0 ? 0 : Right / (double)Checked;
}

public static class BuildCheck
{
    public const int MaxProblems = 20_000;

    /// <summary>Properties that change by themselves in play or when a block is used, never by how it was built.</summary>
    static readonly HashSet<string> LiveProperties = new(StringComparer.Ordinal)
    {
        "powered", "triggered", "lit", "power", "distance", "persistent", "waterlogged", "age", "moisture", "snowy", "occupied", "enabled",
        "extended", "open", "in_wall", "has_bottle_0", "has_bottle_1", "has_bottle_2", "has_book", "has_record", "honey_level", "stage",
        "hatch", "signal_fire", "sculk_sensor_phase", "shrieking", "can_summon", "bloom", "cracked", "short", "attached", "disarmed",
        "unstable", "bottom", "drag", "level", "bites", "berries", "tip", "thickness", "vertical_direction", "trial_spawner_state",
        "vault_state", "ominous", "crafting", "creaking_heart_state", "natural", "tilt",
    };

    static bool Empty(BlockState b) => b.IsAir || b.BaseName is "water" or "lava" or "bubble_column" or "cave_air" or "void_air";

    /// <summary>
    /// Compares <paramref name="s"/> laid with its low corner at (<paramref name="ox"/>, <paramref name="oy"/>,
    /// <paramref name="oz"/>) in <paramref name="dimensionId"/> with the world. <paramref name="airCounts"/>: the
    /// structure's air must be empty in the world too (else only its blocks are checked).
    /// </summary>
    public static BuildCheckResult Run(SaveWorld world, string dimensionId, Structure s, int ox, int oy, int oz, bool airCounts,
        IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        var dim = world.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId) ?? throw new ArgumentException($"The world has no {dimensionId}.");
        var reader = new CanonicalReader(world, dim);
        var stored = dim.ChunkPositions.ToHashSet();
        var (minY, maxY) = StructurePlacement.Height(world, dimensionId);
        var result = new BuildCheckResult();
        var needed = new Structure(s.SizeX, s.SizeY, s.SizeZ);
        var problems = new List<BuildProblem>();
        var footprint = StructurePlacement.Footprint(s, ox, oz);
        int done = 0;
        foreach (var pos in footprint)
        {
            cancel.ThrowIfCancellationRequested();
            if (done++ % 16 == 0) progress?.Report($"Checking chunk {done:N0} of {footprint.Count:N0}");
            int x0 = Math.Max(ox, pos.X * 16), x1 = Math.Min(ox + s.SizeX - 1, pos.X * 16 + 15);
            int z0 = Math.Max(oz, pos.Z * 16), z1 = Math.Min(oz + s.SizeZ - 1, pos.Z * 16 + 15);
            var c = stored.Contains(pos) ? reader.Get(pos) : null;
            for (int sy = 0; sy < s.SizeY; sy++)
            {
                int wy = oy + sy;
                if (wy < minY || wy > maxY) continue;
                for (int wz = z0; wz <= z1; wz++)
                    for (int wx = x0; wx <= x1; wx++)
                    {
                        var want = s.Get(wx - ox, sy, wz - oz);
                        if (want.BaseName == "structure_void" || want.IsAir && !airCounts) continue;
                        if (c == null) { result.NotGenerated++; continue; }
                        var have = c.GetBlock(wx & 15, wy, wz & 15);
                        result.Checked++;
                        BuildProblemKind? kind = null;
                        if (want.IsAir) { if (!Empty(have)) kind = BuildProblemKind.Extra; }
                        else if (have.BaseName != want.BaseName) kind = Empty(have) ? BuildProblemKind.Missing : BuildProblemKind.WrongBlock;
                        else if (!SameBuild(want, have)) kind = BuildProblemKind.WrongState;
                        switch (kind)
                        {
                            case null: result.Right++; continue;
                            case BuildProblemKind.Missing: result.Missing++; break;
                            case BuildProblemKind.WrongBlock: result.WrongBlock++; break;
                            case BuildProblemKind.WrongState: result.WrongState++; break;
                            case BuildProblemKind.Extra: result.Extra++; break;
                        }
                        if (kind is BuildProblemKind.Missing or BuildProblemKind.WrongBlock) needed.Set(wx - ox, sy, wz - oz, want);
                        if (problems.Count < MaxProblems) problems.Add(new BuildProblem(kind.Value, wx, wy, wz, want, have));
                    }
            }
        }
        result.Problems.AddRange(problems.OrderBy(p => p.Y).ThenBy(p => p.Z).ThenBy(p => p.X));
        result.StillNeeded = StructureMaterials.Count(needed);
        return result;
    }

    /// <summary>The same block set the same way, apart from what changes by itself in play.</summary>
    static bool SameBuild(BlockState want, BlockState have)
    {
        if (want.Properties == have.Properties) return true;
        var a = want.PropertyPairs().Where(p => !LiveProperties.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        foreach (var (k, v) in have.PropertyPairs())
        {
            if (LiveProperties.Contains(k)) continue;
            if (!a.Remove(k, out var w) || w != v) return false;
        }
        return a.Count == 0;
    }

    /// <summary>The result in words: how much is done, what is wrong by kind, the first wrong cells layer by layer.</summary>
    public static string Report(BuildCheckResult r, int listed = 200)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{r.Done:P1} built right: {r.Right:N0} of {r.Checked:N0} blocks." + (r.NotGenerated > 0 ? $" ({r.NotGenerated:N0} lie where the world is not generated yet.)" : ""));
        if (r.Missing > 0) sb.AppendLine($"  Missing: {r.Missing:N0}");
        if (r.WrongBlock > 0) sb.AppendLine($"  The wrong block: {r.WrongBlock:N0}");
        if (r.WrongState > 0) sb.AppendLine($"  The right block, set wrong (facing, half, shape …): {r.WrongState:N0}");
        if (r.Extra > 0) sb.AppendLine($"  In the way (where the structure has air): {r.Extra:N0}");
        if (r.Problems.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Layer by layer (x, y, z — what belongs there; what is there):");
            foreach (var p in r.Problems.Take(listed))
                sb.AppendLine($"  {p.X}, {p.Y}, {p.Z}  {p.Kind switch { BuildProblemKind.Missing => "missing", BuildProblemKind.WrongBlock => "wrong block", BuildProblemKind.WrongState => "set wrong", _ => "in the way" }}: {p.Want}; {p.Have}");
            if (r.Wrong > listed) sb.AppendLine($"  … and {r.Wrong - listed:N0} more");
        }
        if (r.StillNeeded.Count > 0)
        {
            sb.AppendLine();
            sb.Append(StructureMaterials.Text(r.StillNeeded, "Still to gather"));
        }
        return sb.ToString();
    }
}
