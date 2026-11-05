// MinecraftStudio.Cli/Args.cs: the command line split into words and --options.
// Owns: Args — positional words in order, --name value and --flag options (case does not matter), Require (a missing
// word is a usage error, exit code 2) and UsageException.
namespace MinecraftStudio.Cli;

/// <summary>A command line that does not say enough: shown with the command's usage, exit code 2.</summary>
sealed class UsageException(string message) : Exception(message);

sealed class Args
{
    readonly List<string> _words = [];
    readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Options that take no value.</summary>
    static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase) { "csv", "dither", "whole", "replace", "json", "help", "keep-new-mobs", "clean", "examples", "centre", "survival", "flat", "plan", "all" };

    public Args(IEnumerable<string> args)
    {
        var list = args.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            string a = list[i];
            if (a.StartsWith("--", StringComparison.Ordinal) && a.Length > 2)
            {
                string name = a[2..];
                int eq = name.IndexOf('=');
                if (eq > 0) { _options[name[..eq]] = name[(eq + 1)..]; continue; }
                if (Flags.Contains(name) || i + 1 >= list.Count || list[i + 1].StartsWith("--", StringComparison.Ordinal)) _options[name] = null;
                else _options[name] = list[++i];
            }
            else _words.Add(a);
        }
    }

    public IReadOnlyList<string> Words => _words;
    public string? Word(int i) => i < _words.Count ? _words[i] : null;
    public string Require(int i, string what) => Word(i) ?? throw new UsageException($"Missing {what}.");
    public bool Has(string name) => _options.ContainsKey(name);
    public string? Option(string name) => _options.TryGetValue(name, out var v) ? v : null;

    public int Int(string name, int fallback)
    {
        if (Option(name) is not string s) return fallback;
        return int.TryParse(s, out int v) ? v : throw new UsageException($"--{name} takes a whole number, not \"{s}\".");
    }
}
