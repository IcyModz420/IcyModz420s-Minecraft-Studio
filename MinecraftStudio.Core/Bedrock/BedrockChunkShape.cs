// MinecraftStudio.Core/Bedrock/BedrockChunkShape.cs: is every chunk of a Bedrock world stored the way the game stores it?
// Owns: BedrockChunkShape — Check: every chunk's records held against the game's own rules (taken from 24 worlds the game
// made, which all pass): a Version record for every chunk with blocks or heights; today's chunks (version 39+) with
// Data3D; older-style chunks with Data2D (or the 1.17 experiment's Data3D) and, in the overworld of a world of today's
// chunks, at least one sub-chunk (blank old-style chunks made a converted world fail to open); sub-chunks of a known kind,
// at their own height, within the dimension's heights. What a repaired copy does about them: Blank and HeightsOnly hold
// nothing of the player's and are left out (the game makes them again); LostVersions are chunks whose blocks survived a
// damaged Version record, which is written again as the world's other chunks of their kind have it. The converter's checks
// run Check on every Bedrock world it writes, so a world the game would not open fails there instead of in the game.
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Bedrock;

/// <summary>
/// One kind of problem: how many chunks have it, the first in words, (up to a few thousand of) the chunks, and what a
/// repaired copy does about it — <see cref="Blank"/>: nothing of the player's, left out; <see cref="LostVersion"/>: blocks
/// whose Version record is gone, written again.
/// </summary>
public sealed record ChunkShapeProblem(string Problem, int Count, string First, bool Blank, IReadOnlyList<(int Dimension, int X, int Z)> Chunks, bool LostVersion = false);

public static class BedrockChunkShape
{
    public const string BlankProblem = "an old-style chunk with no blocks (the game has nothing to extend)";
    public const string HeightsOnlyProblem = "heights with no blocks and no Version record";
    public const string LostVersionProblem = "blocks whose Version record is gone (the game doesn't see the chunk, and makes it again over them)";

    /// <summary>The problems found, the commonest first; none for a world the game stores the way it stores its own.</summary>
    public static List<ChunkShapeProblem> Check(BedrockWorld world)
    {
        var (chunks, modern) = Read(world);
        var found = new Dictionary<string, List<(int, int, int)>>(StringComparer.Ordinal);
        void Add(string problem, (int Dim, int X, int Z) at)
        {
            if (!found.TryGetValue(problem, out var list)) found[problem] = list = [];
            list.Add(at);
        }
        foreach (var (at, r) in chunks)
        {
            // The game itself leaves side records (block entities, finalize marks, actor digests) of chunks it never saved; only
            // blocks or heights without a Version are wrong.
            if (r.Version < 0)
            {
                if (r.SubChunks > 0 || r.LegacyTerrain) Add(LostVersionProblem, at);
                else if (r.Data3D > 0 || r.Data2D) Add(HeightsOnlyProblem, at);
                continue;
            }
            if (r.LegacyTerrain) continue;   // the oldest (0.9–1.0) layout: one record of blocks
            if (r.Bad != null) Add("a sub-chunk the game cannot read (" + r.Bad + ")", at);
            if (r.Version >= 39)
            {
                if (r.Data3D < 512) Add("a chunk of today's kind without its Data3D (heights and biomes)", at);
                // (An upgraded world keeps a leftover Data2D beside the Data3D: the game reads that.)
                var (lo, hi) = at.Dim switch { 1 => (0, 7), 2 => (0, 15), _ => (-4, 19) };
                if (r.SubChunks > 0 && (r.MinSub < lo || r.MaxSub > hi)) Add($"sub-chunks outside the dimension's heights ({lo * 16} to {hi * 16 + 15})", at);
            }
            else
            {
                if (!r.Data2D && r.Data3D < 512) Add("an old-style chunk without its heights and biomes (Data2D)", at);
                if (IsBlank(at, r, modern)) Add(BlankProblem, at);
                // The 1.17 caves-and-cliffs experiment kept old-style chunks with Data3D and the new heights.
                if (r.SubChunks > 0 && r.Data3D == 0 && (r.MinSub < 0 || r.MaxSub > 15)) Add("an old-style chunk with blocks outside 0–255", at);
            }
        }
        return found.Select(kv =>
        {
            var first = kv.Value[0];
            string where = $"{(first.Item1 switch { 1 => "Nether", 2 => "End", 0 => "Overworld", _ => "dimension " + first.Item1 })} chunk {first.Item2}, {first.Item3}";
            return new ChunkShapeProblem(kv.Key, kv.Value.Count, where, kv.Key is BlankProblem or HeightsOnlyProblem, kv.Value.Take(4096).ToList(), kv.Key == LostVersionProblem);
        }).OrderByDescending(p => p.Count).ToList();
    }

    /// <summary>
    /// The blank old-style chunks (no blocks, no Data3D) of a world of today's chunks: the game cannot open the world with
    /// them, and makes them again when they are left out. Nothing of the player's is in them.
    /// </summary>
    public static HashSet<(int Dimension, int X, int Z)> Blank(BedrockWorld world)
    {
        var (chunks, modern) = Read(world);
        return chunks.Where(kv => kv.Value.Version >= 0 && !kv.Value.LegacyTerrain && kv.Value.Version < 39 && IsBlank(kv.Key, kv.Value, modern))
            .Select(kv => kv.Key).ToHashSet();
    }

    /// <summary>Chunks with heights but no blocks and no Version record (what damage leaves): the game makes them again.</summary>
    public static HashSet<(int Dimension, int X, int Z)> HeightsOnly(BedrockWorld world)
    {
        var (chunks, _) = Read(world);
        return chunks.Where(kv => kv.Value.Version < 0 && !kv.Value.LegacyTerrain && kv.Value.SubChunks == 0 && (kv.Value.Data3D > 0 || kv.Value.Data2D))
            .Select(kv => kv.Key).ToHashSet();
    }

    /// <summary>
    /// Chunks whose blocks are there but whose Version record is gone (damaged): without it the game doesn't see the chunk
    /// and makes it again over what was built there. The version to write back is the one the world's other chunks of the
    /// same kind have (Data3D: today's chunks; Data2D: old-style; one LegacyTerrain record: 0.9–1.0), the most common. A
    /// chunk that shows none of those (its heights went with the damage too), or of a kind no other chunk shows a version
    /// for, is not in the list: it is in <see cref="Unrestorable"/>.
    /// </summary>
    public static Dictionary<(int Dimension, int X, int Z), (byte Version, bool Modern)> LostVersions(BedrockWorld world)
    {
        var (chunks, _) = Read(world);
        var lost = new Dictionary<(int, int, int), (byte, bool)>();
        foreach (var (at, r, v) in Lost(chunks))
            if (v is int version) lost[at] = ((byte)version, r.Data3D >= 512);
        return lost;
    }

    /// <summary>
    /// Chunks whose blocks are there but whose Version record is gone, and nothing shows which to write back: a repaired copy
    /// leaves them for the game to make again (listed as lost; the damaged world itself still has them).
    /// </summary>
    public static HashSet<(int Dimension, int X, int Z)> Unrestorable(BedrockWorld world)
    {
        var (chunks, _) = Read(world);
        return Lost(chunks).Where(l => l.Version == null).Select(l => l.At).ToHashSet();
    }

    // Every chunk with blocks and no Version, with the version its kind has in the rest of the world (null: unknown).
    static IEnumerable<((int Dim, int X, int Z) At, Records R, int? Version)> Lost(Dictionary<(int Dim, int X, int Z), Records> chunks)
    {
        int? Common(Func<Records, bool> kind) => chunks.Values.Where(r => r.Version >= 0 && kind(r))
            .GroupBy(r => r.Version).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).Select(g => (int?)g.Key).FirstOrDefault();
        int? today = Common(r => !r.LegacyTerrain && r.Data3D >= 512), old = Common(r => !r.LegacyTerrain && r.Data2D && r.Data3D == 0), legacy = Common(r => r.LegacyTerrain);
        foreach (var (at, r) in chunks)
        {
            if (r.Version >= 0 || r.SubChunks == 0 && !r.LegacyTerrain) continue;
            yield return (at, r, r.LegacyTerrain ? legacy : r.Data3D >= 512 ? today : r.Data2D ? old : null);
        }
    }

    /// <summary>The records that make a chunk with nothing of the player's a chunk (Version, heights, finished mark): left out, it is made again.</summary>
    public static bool IsBlankChunkRecord(ChunkTag tag) =>
        tag is ChunkTag.Version or ChunkTag.LegacyVersion or ChunkTag.Data2D or ChunkTag.Data2DLegacy or ChunkTag.Data3D or ChunkTag.FinalizedState;

    // In a world of today's chunks the game extends old-style overworld chunks below 0 from their blocks; an older world's
    // empty chunks (void End land) are ordinary.
    static bool IsBlank((int Dim, int X, int Z) at, Records r, bool modern) => r.SubChunks == 0 && r.Data3D == 0 && at.Dim == 0 && modern;

    static (Dictionary<(int Dim, int X, int Z), Records> Chunks, bool Modern) Read(BedrockWorld world)
    {
        var chunks = new Dictionary<(int Dim, int X, int Z), Records>();
        // A damaged world (opened for rescue) can have records that don't read: those count as missing.
        byte[]? Get(byte[] key)
        {
            try { return world.Db.Get(key); }
            catch (Exception e) when (e is IOException or InvalidDataException or IO.SaveFormatException) { return null; }
        }
        foreach (var key in world.Db.Keys())
        {
            if (!BedrockKeys.TryParseChunkKey(key, out var ck)) continue;
            if (!chunks.TryGetValue((ck.Dimension, ck.X, ck.Z), out var r)) chunks[(ck.Dimension, ck.X, ck.Z)] = r = new Records();
            switch (ck.Tag)
            {
                case ChunkTag.Version or ChunkTag.LegacyVersion:
                    r.Version = Get(key) is { Length: > 0 } v ? v[0] : -1; break;
                case ChunkTag.Data3D: r.Data3D = Get(key)?.Length ?? 0; break;
                case ChunkTag.Data2D or ChunkTag.Data2DLegacy: r.Data2D = true; break;
                case ChunkTag.LegacyTerrain: r.LegacyTerrain = true; break;
                case ChunkTag.SubChunkPrefix:
                    r.SubChunks++;
                    var s = Get(key);
                    if (s == null || s.Length == 0 || s[0] > 9) r.Bad ??= $"sub-chunk {ck.SubChunk} of an unknown kind";
                    else if (s[0] == 9 && (s.Length < 3 || (sbyte)s[2] != ck.SubChunk)) r.Bad ??= $"sub-chunk {ck.SubChunk} says it is at {(s.Length < 3 ? "?" : ((sbyte)s[2]).ToString())}";
                    (r.MinSub, r.MaxSub) = (Math.Min(r.MinSub, ck.SubChunk), Math.Max(r.MaxSub, ck.SubChunk));
                    break;
            }
        }
        return (chunks, chunks.Values.Any(c => c.Version >= 39));
    }

    sealed class Records
    {
        public int Version = -1, Data3D, SubChunks, MinSub = int.MaxValue, MaxSub = int.MinValue;
        public bool Data2D, LegacyTerrain;
        public string? Bad;
    }
}
