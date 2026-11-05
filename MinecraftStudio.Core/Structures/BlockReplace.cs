// MinecraftStudio.Core/Structures/BlockReplace.cs: replace or fill blocks in a box of any world — into a new copy.
// Owns: ReplaceRule and BlockReplace. A rule is a pattern ("stone", "*_ore", "oak_log[axis=y]", "*" for every block, air
// too) and a block to put there ("deepslate", "spruce_stairs[half=top]"); the new block takes the old one's facing and
// other properties it also has (oak stairs → spruce stairs keep their shape). Apply edits a cropped structure (block
// entities stay with unchanged blocks, go with replaced ones); ReplaceIntoCopy crops the box, edits it and places it
// back with StructurePlacement — the world is copied, only those chunks rewritten, every block checked. Mobs are untouched.
using System.Text.RegularExpressions;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

/// <summary>Replace blocks matching <paramref name="Pattern"/> with <paramref name="With"/>.</summary>
public sealed record ReplaceRule(string Pattern, string With);

/// <summary>What an edit changes: each kind of change with its count, and the block entities that went with their blocks.</summary>
public sealed record ReplaceSummary(List<(string From, string To, long Count)> Changes, int BlockEntitiesRemoved)
{
    public long Total => Changes.Sum(c => c.Count);
}

public static class BlockReplace
{
    /// <summary>
    /// The rules as one function: a block's replacement, or null to leave it. The first rule that matches wins. Throws
    /// ArgumentException for a pattern or block that is not right (an unknown block name, a property it doesn't have).
    /// </summary>
    public static Func<BlockState, BlockState?> Compile(IReadOnlyList<ReplaceRule> rules)
    {
        var blocks = JavaBlockData.For(StructureJava.Newest);
        var compiled = new List<(Func<BlockState, bool> Match, string Name, Dictionary<string, string> Props)>();
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern) || string.IsNullOrWhiteSpace(rule.With)) continue;
            var target = BlockState.Parse(rule.With.Trim());
            if (!target.IsAir && !blocks.Blocks.ContainsKey(target.Name)) throw new ArgumentException($"There is no block called \"{rule.With.Trim()}\".");
            var props = target.PropertyPairs().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            if (blocks.Blocks.TryGetValue(target.Name, out var def))
                foreach (var (k, v) in props)
                    if (!def.Properties.TryGetValue(k, out var allowed) || !allowed.Contains(v))
                        throw new ArgumentException($"{WorldSearch.Nice(target.Name)} has no {k}={v}.");
            compiled.Add((Matcher(rule.Pattern.Trim()), target.Name, props));
        }
        if (compiled.Count == 0) throw new ArgumentException("Give at least one block to replace and what to put there.");
        var cache = new Dictionary<int, BlockState?>();
        return b =>
        {
            lock (cache)
            {
                if (cache.TryGetValue(b.Id, out var hit)) return hit;
                BlockState? result = null;
                foreach (var (match, name, props) in compiled)
                    if (match(b)) { result = Make(b, name, props, blocks); break; }
                if (result == b) result = null;
                cache[b.Id] = result;
                return result;
            }
        };
    }

    /// <summary>
    /// The new block: its own defaults, then the old block's properties it also has (facing, half, waterlogged…), then the
    /// ones the rule gave.
    /// </summary>
    static BlockState Make(BlockState old, string name, Dictionary<string, string> given, JavaBlockData.VersionBlocks blocks)
    {
        if (name is "minecraft:air" or "minecraft:cave_air" or "minecraft:void_air") return BlockState.Get(name);
        var def = blocks.Blocks[name];
        var props = new Dictionary<string, string>(def.Default, StringComparer.Ordinal);
        foreach (var (k, v) in old.PropertyPairs())
            if (def.Properties.TryGetValue(k, out var allowed) && allowed.Contains(v)) props[k] = v;
        foreach (var (k, v) in given) props[k] = v;
        return BlockState.Get(name, props);
    }

    /// <summary>"stone", "minecraft:stone", "*_ore", "*" (anything, air too), with [key=value,…] to match only those states.</summary>
    static Func<BlockState, bool> Matcher(string pattern)
    {
        string name = pattern, props = "";
        int bracket = pattern.IndexOf('[');
        if (bracket >= 0) { name = pattern[..bracket].Trim(); props = pattern[(bracket + 1)..].TrimEnd(']', ' '); }
        var want = props.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .Select(p => (Key: p[0].Trim(), Value: p[1].Trim())).ToList();
        bool full = name.Contains(':');
        if (name == "*" && want.Count == 0) return _ => true;
        var regex = new Regex("^" + Regex.Escape(name.ToLowerInvariant()).Replace("\\*", ".*") + "$", RegexOptions.CultureInvariant);
        return b => regex.IsMatch(full ? b.Name : b.BaseName) && want.All(w => b.GetProperty(w.Key) == w.Value);
    }

    /// <summary>
    /// The structure with the rules applied (a new one; <paramref name="s"/> is not changed) and what changed. Block entities
    /// of replaced blocks go with them (a chest made stone keeps no chest contents); unchanged blocks keep theirs.
    /// </summary>
    public static (Structure Edited, ReplaceSummary Summary) Apply(Structure s, Func<BlockState, BlockState?> map)
    {
        var t = new Structure(s.SizeX, s.SizeY, s.SizeZ) { DataVersion = s.DataVersion, Author = s.Author, Origin = s.Origin };
        var changes = new Dictionary<(string, string), long>();
        for (int y = 0; y < s.SizeY; y++)
            for (int z = 0; z < s.SizeZ; z++)
                for (int x = 0; x < s.SizeX; x++)
                {
                    var b = s.Get(x, y, z);
                    var n = map(b);
                    t.Set(x, y, z, n ?? b);
                    if (n != null) changes[(b.Name, n.Name)] = changes.GetValueOrDefault((b.Name, n.Name)) + 1;
                }
        int removed = 0;
        foreach (var (index, be) in s.BlockEntities)
        {
            int x = index % s.SizeX, rem = index / s.SizeX, z = rem % s.SizeZ, y = rem / s.SizeZ;
            if (map(s.Get(x, y, z)) is BlockState n && n.Name != s.Get(x, y, z).Name) { removed++; continue; }
            t.BlockEntities[index] = (NbtCompound)be.Clone();
        }
        foreach (var e in s.Entities) t.Entities.Add(e);
        var list = changes.Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value)).OrderByDescending(c => c.Value).ToList();
        return (t, new ReplaceSummary(list, removed));
    }

    /// <summary>What the rules would change in the box, without writing anything.</summary>
    public static ReplaceSummary Count(SaveWorld world, WorldDimension dim, int minX, int minY, int minZ, int maxX, int maxY, int maxZ, IReadOnlyList<ReplaceRule> rules)
    {
        var box = StructureClip.Extract(world, dim, minX, minY, minZ, maxX, maxY, maxZ, includeBlockEntities: true, includeEntities: false);
        return Apply(box, Compile(rules)).Summary;
    }

    /// <summary>
    /// Replaces in the inclusive box (world coordinates) of a NEW copy of the world at <paramref name="output"/>: the box is
    /// cropped (blocks and block entities), edited, and placed back where it was through StructurePlacement (the world is
    /// copied byte for byte, only the box's chunks are rewritten in the world's own format, and every block is checked).
    /// Entities are left exactly as they are. The world itself is only read.
    /// </summary>
    public static (PlacementResult Result, ReplaceSummary Summary) ReplaceIntoCopy(SaveWorld world, string dimensionId, int minX, int minY, int minZ, int maxX, int maxY, int maxZ,
        IReadOnlyList<ReplaceRule> rules, string output, IProgress<ConversionProgress>? progress = null, CancellationToken cancel = default)
    {
        var map = Compile(rules);
        var dim = world.Dimensions.FirstOrDefault(d => CanonicalReader.DimensionId(d) == dimensionId) ?? throw new ArgumentException($"The world has no {dimensionId}.");
        var (lowY, highY) = StructurePlacement.Height(world, dimensionId);
        minY = Math.Max(minY, lowY); maxY = Math.Min(maxY, highY);
        if (maxX < minX || maxY < minY || maxZ < minZ) throw new ArgumentException("The box is empty (or outside the world's height).");
        long volume = (long)(maxX - minX + 1) * (maxY - minY + 1) * (maxZ - minZ + 1);
        if (volume > Structure.MaxVolume) throw new ArgumentException($"That box is too large ({volume:N0} blocks; the limit is {Structure.MaxVolume:N0}). Choose a smaller area.");
        progress?.Report(new ConversionProgress { Stage = "Reading the blocks in the box", Done = 0, Total = 1 });
        var box = StructureClip.Extract(world, dim, minX, minY, minZ, maxX, maxY, maxZ, includeBlockEntities: true, includeEntities: false);
        var (edited, summary) = Apply(box, map);
        if (summary.Total == 0) throw new ArgumentException("Nothing in the box matches: no block would change.");
        cancel.ThrowIfCancellationRequested();
        var result = StructurePlacement.PlaceIntoCopy(world, edited, dimensionId, minX, minY, minZ, pasteAir: true, output, progress, cancel);
        if (summary.BlockEntitiesRemoved > 0)
            result.Notes.Add($"{summary.BlockEntitiesRemoved:N0} block entit{(summary.BlockEntitiesRemoved == 1 ? "y" : "ies")} (chest contents, signs …) went with the blocks that were replaced.");
        return (result, summary);
    }
}
