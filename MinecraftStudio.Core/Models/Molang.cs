// MinecraftStudio.Core/Models/Molang.cs: Bedrock's expression language (Molang), enough to play mob animations.
// Owns: MolangContext (the values of query.*, variable.*, temp.*, "this") and Molang — compiling an expression once into a
// function: numbers, + − × ÷ %, comparisons, && || !, ?: and ??, brackets, statements with ";", assignments to
// variables and return, and the math functions (trigonometry in degrees, as the game does). Anything the game knows and
// this program does not (most queries) reads as 0, as in the game for unknown names; nothing here can loop or call out.
using System.Globalization;

namespace MinecraftStudio.Core.Models;

/// <summary>The values a Molang expression reads: queries, variables and "this".</summary>
public sealed class MolangContext
{
    readonly Dictionary<string, double> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The channel's own value in the model (what "this" means in an animation).</summary>
    public double This { get; set; }

    /// <summary>A query or variable by its full name ("query.anim_time", "variable.state"); 0 when unset.</summary>
    public double this[string name]
    {
        get => _values.TryGetValue(Canonical(name), out var v) ? v : 0;
        set => _values[Canonical(name)] = value;
    }

    public IEnumerable<string> Names => _values.Keys;

    /// <summary>The long form of a name ("q.x" → "query.x", "v.x" → "variable.x", "t.x" → "temp.x", "c.x" → "context.x").</summary>
    public static string Canonical(string name)
    {
        int dot = name.IndexOf('.');
        if (dot < 0) return name.ToLowerInvariant();
        string head = name[..dot].ToLowerInvariant(), rest = name[(dot + 1)..].ToLowerInvariant();
        head = head switch { "q" => "query", "v" => "variable", "t" => "temp", "c" => "context", _ => head };
        return head + "." + rest;
    }

    public MolangContext Clone()
    {
        var c = new MolangContext { This = This };
        foreach (var (k, v) in _values) c._values[k] = v;
        return c;
    }
}

/// <summary>Compiles Molang expressions; see <see cref="Compile"/>.</summary>
public static class Molang
{
    static readonly Dictionary<string, Func<MolangContext, double>> Cache = new(StringComparer.Ordinal);
    static readonly object Lock = new();

    /// <summary>
    /// The expression as a function of the context (cached by text). An expression that cannot be read gives null; an
    /// empty one gives 0.
    /// </summary>
    public static Func<MolangContext, double>? Compile(string text)
    {
        lock (Lock)
        {
            if (Cache.TryGetValue(text, out var f)) return f;
        }
        Func<MolangContext, double>? compiled;
        try { compiled = new Parser(text).Program(); }
        catch (FormatException) { compiled = null; }
        if (compiled != null) lock (Lock) { if (Cache.Count < 20000) Cache[text] = compiled; }
        return compiled;
    }

    /// <summary>Evaluates an expression (0 when it cannot be read).</summary>
    public static double Eval(string text, MolangContext ctx) => Compile(text) is { } f ? Safe(f(ctx)) : 0;

    static double Safe(double v) => double.IsFinite(v) ? v : 0;

    /// <summary>Whether an expression can be read (for the editor's red marks).</summary>
    public static bool IsValid(string text) => Compile(text) != null;

    // ------------------------------------------------------------------ parser

    sealed class Parser(string text)
    {
        readonly string _s = text;
        int _pos;

        char Peek(int ahead = 0) => _pos + ahead < _s.Length ? _s[_pos + ahead] : '\0';

        void Skip()
        {
            while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++;
        }

        bool Eat(string token)
        {
            Skip();
            if (string.CompareOrdinal(_s, _pos, token, 0, token.Length) != 0) return false;
            // A word must not run on into a longer name ("return" is not "returned").
            if (char.IsLetter(token[^1]) && _pos + token.Length < _s.Length && (char.IsLetterOrDigit(_s[_pos + token.Length]) || _s[_pos + token.Length] == '_')) return false;
            _pos += token.Length;
            return true;
        }

        static FormatException Bad(string why) => new(why);

        /// <summary>Statements separated by ";": the value of a "return", else of the only (last) expression.</summary>
        public Func<MolangContext, double> Program()
        {
            var statements = new List<(Func<MolangContext, double> Run, bool Return)>();
            Skip();
            if (_pos >= _s.Length) return _ => 0;
            while (true)
            {
                Skip();
                if (_pos >= _s.Length) break;
                if (Peek() == ';') { _pos++; continue; }
                bool ret = Eat("return");
                statements.Add((Assignment(), ret));
                Skip();
                if (_pos >= _s.Length) break;
                if (Peek() != ';') throw Bad($"Unexpected '{Peek()}' at {_pos}.");
            }
            if (statements.Count == 1) return statements[0].Run;
            var list = statements.ToArray();
            // Several statements are a "complex" expression: without a return its value is 0, as in the game.
            return ctx =>
            {
                foreach (var (run, isReturn) in list)
                {
                    double v = run(ctx);
                    if (isReturn) return v;
                }
                return 0;
            };
        }

        Func<MolangContext, double> Assignment()
        {
            int start = _pos;
            Skip();
            if (Name() is { } name && (name.StartsWith("variable.", StringComparison.OrdinalIgnoreCase) || name.StartsWith("temp.", StringComparison.OrdinalIgnoreCase) ||
                                       name.StartsWith("v.", StringComparison.OrdinalIgnoreCase) || name.StartsWith("t.", StringComparison.OrdinalIgnoreCase)))
            {
                Skip();
                if (Peek() == '=' && Peek(1) != '=')
                {
                    _pos++;
                    var value = Conditional();
                    string key = MolangContext.Canonical(name);
                    return ctx => { double v = Safe(value(ctx)); ctx[key] = v; return v; };
                }
            }
            _pos = start;
            return Conditional();
        }

        Func<MolangContext, double> Conditional()
        {
            var cond = Coalesce();
            Skip();
            if (Peek() != '?' || Peek(1) == '?') return cond;
            _pos++;
            var yes = Conditional();
            Skip();
            // "a ? b" without ":" is Molang's "if a then b else 0".
            if (Peek() != ':') return ctx => cond(ctx) != 0 ? yes(ctx) : 0;
            _pos++;
            var no = Conditional();
            return ctx => cond(ctx) != 0 ? yes(ctx) : no(ctx);
        }

        Func<MolangContext, double> Coalesce()
        {
            var left = Or();
            while (Eat("??"))
            {
                var l = left;
                var right = Or();
                // Unknown values read as 0 here, so "a ?? b" is a when a is set (non-zero), else b.
                left = ctx => l(ctx) is var v && v != 0 ? v : right(ctx);
            }
            return left;
        }

        Func<MolangContext, double> Or()
        {
            var left = And();
            while (Eat("||")) { var l = left; var r = And(); left = ctx => l(ctx) != 0 || r(ctx) != 0 ? 1 : 0; }
            return left;
        }

        Func<MolangContext, double> And()
        {
            var left = Compare();
            while (Eat("&&")) { var l = left; var r = Compare(); left = ctx => l(ctx) != 0 && r(ctx) != 0 ? 1 : 0; }
            return left;
        }

        Func<MolangContext, double> Compare()
        {
            var left = Sum();
            while (true)
            {
                var l = left;
                if (Eat("==")) { var r = Sum(); left = ctx => Math.Abs(l(ctx) - r(ctx)) < 1e-9 ? 1 : 0; }
                else if (Eat("!=")) { var r = Sum(); left = ctx => Math.Abs(l(ctx) - r(ctx)) >= 1e-9 ? 1 : 0; }
                else if (Eat("<=")) { var r = Sum(); left = ctx => l(ctx) <= r(ctx) ? 1 : 0; }
                else if (Eat(">=")) { var r = Sum(); left = ctx => l(ctx) >= r(ctx) ? 1 : 0; }
                else if (Eat("<")) { var r = Sum(); left = ctx => l(ctx) < r(ctx) ? 1 : 0; }
                else if (Eat(">")) { var r = Sum(); left = ctx => l(ctx) > r(ctx) ? 1 : 0; }
                else return left;
            }
        }

        Func<MolangContext, double> Sum()
        {
            var left = Product();
            while (true)
            {
                Skip();
                var l = left;
                if (Peek() == '+') { _pos++; var r = Product(); left = ctx => l(ctx) + r(ctx); }
                else if (Peek() == '-') { _pos++; var r = Product(); left = ctx => l(ctx) - r(ctx); }
                else return left;
            }
        }

        Func<MolangContext, double> Product()
        {
            var left = Unary();
            while (true)
            {
                Skip();
                var l = left;
                if (Peek() == '*') { _pos++; var r = Unary(); left = ctx => l(ctx) * r(ctx); }
                else if (Peek() == '/') { _pos++; var r = Unary(); left = ctx => r(ctx) is var d && d != 0 ? l(ctx) / d : 0; }
                else if (Peek() == '%') { _pos++; var r = Unary(); left = ctx => r(ctx) is var d && d != 0 ? l(ctx) % d : 0; }
                else return left;
            }
        }

        Func<MolangContext, double> Unary()
        {
            Skip();
            if (Peek() == '-') { _pos++; var v = Unary(); return ctx => -v(ctx); }
            if (Peek() == '+') { _pos++; return Unary(); }
            if (Peek() == '!' && Peek(1) != '=') { _pos++; var v = Unary(); return ctx => v(ctx) == 0 ? 1 : 0; }
            return Primary();
        }

        Func<MolangContext, double> Primary()
        {
            Skip();
            char c = Peek();
            if (c == '(')
            {
                _pos++;
                var inner = Conditional();
                Skip();
                if (Peek() != ')') throw Bad("A bracket is not closed.");
                _pos++;
                return inner;
            }
            if (c == '{')
            {
                // A block of statements: its value is 0 unless it returns.
                int depth = 0, start = _pos;
                do { if (Peek() == '{') depth++; else if (Peek() == '}') depth--; _pos++; } while (depth > 0 && _pos < _s.Length);
                var body = new Parser(_s[(start + 1)..(_pos - 1)]).Program();
                return body;
            }
            if (char.IsDigit(c) || c == '.' && char.IsDigit(Peek(1)))
            {
                int start = _pos;
                while (char.IsDigit(Peek()) || Peek() == '.') _pos++;
                if (Peek() == 'e' || Peek() == 'E') { _pos++; if (Peek() is '+' or '-') _pos++; while (char.IsDigit(Peek())) _pos++; }
                if (Peek() is 'f' or 'F') _pos++;
                if (!double.TryParse(_s.AsSpan(start, _pos - start).TrimEnd("fF"), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) throw Bad("Not a number.");
                return _ => n;
            }
            if (c == '\'')
            {
                // Text (names of items, bones …): compared nowhere here, so it counts as 0.
                _pos++;
                while (_pos < _s.Length && _s[_pos] != '\'') _pos++;
                if (_pos >= _s.Length) throw Bad("Text is not closed.");
                _pos++;
                return _ => 0;
            }
            if (Name() is not { } name) throw Bad($"Unexpected '{c}' at {_pos}.");
            Skip();
            if (Peek() == '-' && Peek(1) == '>')
            {
                // Another entity's value ("context.owning_entity->variable.x"): not known here, so 0.
                _pos += 2;
                if (Name() == null) throw Bad("A name is missing after ->.");
                return _ => 0;
            }
            string lower = name.ToLowerInvariant();
            if (lower == "this") return ctx => ctx.This;
            if (lower is "true") return _ => 1;
            if (lower is "false") return _ => 0;
            if (lower is "math.pi") return _ => Math.PI;
            Skip();
            var args = new List<Func<MolangContext, double>>();
            if (Peek() == '(')
            {
                _pos++;
                Skip();
                if (Peek() != ')')
                    while (true)
                    {
                        args.Add(Conditional());
                        Skip();
                        if (Peek() == ',') { _pos++; continue; }
                        break;
                    }
                Skip();
                if (Peek() != ')') throw Bad("A call is not closed.");
                _pos++;
            }
            if (lower.StartsWith("math.", StringComparison.Ordinal)) return MathFunction(lower[5..], args);
            // Arrays and members (geometry.x, texture.x, array.x[...]) and queries with arguments: the game's own; 0 here.
            Skip();
            if (Peek() == '[') { int depth = 0; do { if (Peek() == '[') depth++; else if (Peek() == ']') depth--; _pos++; } while (depth > 0 && _pos < _s.Length); return _ => 0; }
            string key = MolangContext.Canonical(name);
            return args.Count == 0 ? ctx => ctx[key] : _ => 0;
        }

        /// <summary>A dotted name ("query.anim_time", "v.speed", "math.cos"), or null.</summary>
        string? Name()
        {
            Skip();
            int start = _pos;
            while (_pos < _s.Length && (char.IsLetterOrDigit(_s[_pos]) || _s[_pos] == '_' || _s[_pos] == '.' && _pos > start)) _pos++;
            if (_pos == start || char.IsDigit(_s[start])) { _pos = start; return null; }
            return _s[start.._pos];
        }

        static Func<MolangContext, double> MathFunction(string fn, List<Func<MolangContext, double>> a)
        {
            const double D = Math.PI / 180;
            Func<MolangContext, double> A(int i) => i < a.Count ? a[i] : _ => 0;
            var a0 = A(0);
            var a1 = A(1);
            var a2 = A(2);
            return fn switch
            {
                "abs" => ctx => Math.Abs(a0(ctx)),
                "sin" => ctx => Math.Sin(a0(ctx) * D),
                "cos" => ctx => Math.Cos(a0(ctx) * D),
                "asin" => ctx => Math.Asin(Math.Clamp(a0(ctx), -1, 1)) / D,
                "acos" => ctx => Math.Acos(Math.Clamp(a0(ctx), -1, 1)) / D,
                "atan" => ctx => Math.Atan(a0(ctx)) / D,
                "atan2" => ctx => Math.Atan2(a0(ctx), a1(ctx)) / D,
                "clamp" => ctx => Math.Clamp(a0(ctx), Math.Min(a1(ctx), a2(ctx)), Math.Max(a1(ctx), a2(ctx))),
                "lerp" => ctx => a0(ctx) + (a1(ctx) - a0(ctx)) * a2(ctx),
                "lerprotate" => ctx =>
                {
                    double from = a0(ctx), to = a1(ctx), t = a2(ctx);
                    double diff = ((to - from) % 360 + 540) % 360 - 180;
                    return from + diff * t;
                },
                "inverse_lerp" => ctx => a1(ctx) - a0(ctx) is var d && d != 0 ? (a2(ctx) - a0(ctx)) / d : 0,
                "min" => ctx => Math.Min(a0(ctx), a1(ctx)),
                "max" => ctx => Math.Max(a0(ctx), a1(ctx)),
                "mod" => ctx => a1(ctx) is var d && d != 0 ? a0(ctx) % d : 0,
                "pow" => ctx => Math.Pow(a0(ctx), a1(ctx)),
                "sqrt" => ctx => Math.Sqrt(Math.Max(0, a0(ctx))),
                "exp" => ctx => Math.Exp(Math.Min(700, a0(ctx))),
                "ln" => ctx => a0(ctx) > 0 ? Math.Log(a0(ctx)) : 0,
                "floor" => ctx => Math.Floor(a0(ctx)),
                "ceil" => ctx => Math.Ceiling(a0(ctx)),
                "round" => ctx => Math.Round(a0(ctx), MidpointRounding.AwayFromZero),
                "trunc" => ctx => Math.Truncate(a0(ctx)),
                "sign" => ctx => Math.Sign(a0(ctx)),
                "min_angle" => ctx => ((a0(ctx) % 360 + 540) % 360) - 180,
                "hermite_blend" => ctx => a0(ctx) is var t ? 3 * t * t - 2 * t * t * t : 0,
                "pi" => _ => Math.PI,
                // Random values would make a preview flicker: the middle of the range is shown.
                "random" => ctx => (a0(ctx) + a1(ctx)) / 2,
                "random_integer" => ctx => Math.Round((a0(ctx) + a1(ctx)) / 2),
                "die_roll" => ctx => a0(ctx) * (a1(ctx) + a2(ctx)) / 2,
                "die_roll_integer" => ctx => Math.Round(a0(ctx) * (a1(ctx) + a2(ctx)) / 2),
                _ => _ => 0,
            };
        }
    }
}
