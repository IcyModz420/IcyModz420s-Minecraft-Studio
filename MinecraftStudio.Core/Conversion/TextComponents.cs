// MinecraftStudio.Core/Conversion/TextComponents.cs: Chat text in the forms the editions store it: plain
// strings (Bedrock, Java ≤1.7, Legacy Console), JSON strings (Java 1.8–1.21.4) and NBT components (Java
// 1.21.5+). The canonical form is a JSON string.
// Owns: TextComponents.
using System.Text;
using System.Text.Json;
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

/// <summary>
/// Chat text in the forms the editions store it: plain strings (Bedrock, Java ≤1.7, Legacy Console), JSON strings
/// (Java 1.8–1.21.4) and NBT components (Java 1.21.5+). The canonical form is a JSON string.
/// </summary>
public static class TextComponents
{
    /// <summary>Canonical JSON for plain text.</summary>
    public static string FromPlain(string text) => text.Length == 0 ? "\"\"" : JsonSerializer.Serialize(new { text });

    /// <summary>Canonical JSON from whatever a Java tag holds (JSON string, plain string or NBT component).</summary>
    public static string FromJavaTag(NbtTag? tag) => tag switch
    {
        null => "\"\"",
        NbtString s => LooksLikeJson(s.Value) ? s.Value : FromPlain(s.Value),
        NbtCompound or NbtList => NbtToJson(tag),
        _ => FromPlain(tag.ToString()),
    };

    static bool LooksLikeJson(string s)
    {
        s = s.TrimStart();
        if (s.Length == 0) return false;
        if (s[0] is '{' or '[' or '"')
        {
            try { using var _ = JsonDocument.Parse(s); return true; } catch { return false; }
        }
        return false;
    }

    /// <summary>Plain text of a canonical component (text + extra, translate keys kept as-is).</summary>
    public static string ToPlain(string json)
    {
        if (!LooksLikeJson(json)) return json;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            Append(doc.RootElement, sb);
            return sb.ToString();
        }
        catch { return json; }
    }

    static void Append(JsonElement e, StringBuilder sb)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: sb.Append(e.GetString()); break;
            case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) Append(x, sb); break;
            case JsonValueKind.Object:
                if (e.TryGetProperty("text", out var t)) sb.Append(t.ValueKind == JsonValueKind.String ? t.GetString() : t.ToString());
                else if (e.TryGetProperty("translate", out var tr)) sb.Append(tr.GetString());
                if (e.TryGetProperty("extra", out var extra)) Append(extra, sb);
                break;
            case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False: sb.Append(e.ToString()); break;
        }
    }

    /// <summary>Text for a Java target: JSON string up to 1.21.4, NBT component from 25w02a (DV 4298).</summary>
    public static NbtTag ToJavaTag(string json, int dataVersion)
    {
        if (dataVersion < 4298) return new NbtString(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonToNbt(doc.RootElement);
        }
        catch { return new NbtString(json); }
    }

    /// <summary>Plain-string form for Java ≤1.7 / Legacy Console / Bedrock targets.</summary>
    public static NbtTag ToPlainTag(string json) => new NbtString(ToPlain(json));

    static NbtTag JsonToNbt(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => new NbtString(e.GetString() ?? ""),
        JsonValueKind.True => new NbtByte(true),
        JsonValueKind.False => new NbtByte(false),
        JsonValueKind.Number => e.TryGetInt32(out int i) ? new NbtInt(i) : new NbtDouble(e.GetDouble()),
        JsonValueKind.Array => ToList(e),
        JsonValueKind.Object => ToCompound(e),
        _ => new NbtString(""),
    };

    static NbtList ToList(JsonElement e) => JavaList(e.EnumerateArray().Select(JsonToNbt));

    /// <summary>
    /// A list for a Java target from 25w02a on (sign lines, lore, a component's extra): an NBT list holds one type, so
    /// when plain lines (strings) and formatted ones (compounds) are mixed, every one that is not a compound is wrapped as
    /// {"": value} — as the game itself writes such a list.
    /// </summary>
    public static NbtList JavaList(IEnumerable<NbtTag> items)
    {
        var all = items.ToList();
        bool mixed = all.Select(t => t.Type).Distinct().Count() > 1;
        var list = new NbtList();
        foreach (var t in all)
            list.Add(mixed && t is not NbtCompound ? new NbtCompound { { "", t } } : t);
        return list;
    }

    static NbtCompound ToCompound(JsonElement e)
    {
        var c = new NbtCompound();
        foreach (var p in e.EnumerateObject()) c[p.Name] = JsonToNbt(p.Value);
        return c;
    }

    static string NbtToJson(NbtTag t)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) WriteJson(w, t);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    static void WriteJson(Utf8JsonWriter w, NbtTag t)
    {
        switch (t)
        {
            case NbtString s: w.WriteStringValue(s.Value); break;
            case NbtByte b when b.Value is 0 or 1: w.WriteBooleanValue(b.Value == 1); break;
            case NbtCompound c when c.Count == 1 && c.ContainsKey(""): WriteJson(w, c[""]!); break;
            case NbtCompound c:
                w.WriteStartObject();
                foreach (var (k, v) in c) { w.WritePropertyName(k); WriteJson(w, v); }
                w.WriteEndObject();
                break;
            case NbtList l:
                w.WriteStartArray();
                foreach (var v in l) WriteJson(w, v);
                w.WriteEndArray();
                break;
            case { IsNumeric: true } n:
                if (n.Type is NbtType.Float or NbtType.Double) w.WriteNumberValue(n.AsDouble); else w.WriteNumberValue(n.AsLong);
                break;
            default: w.WriteStringValue(t.ToString()); break;
        }
    }

    // ---------------------------------------------------------------- § formatting codes

    static readonly (string Name, char Code, int Rgb)[] ColorCodes =
    [
        ("black", '0', 0x000000), ("dark_blue", '1', 0x0000AA), ("dark_green", '2', 0x00AA00), ("dark_aqua", '3', 0x00AAAA),
        ("dark_red", '4', 0xAA0000), ("dark_purple", '5', 0xAA00AA), ("gold", '6', 0xFFAA00), ("gray", '7', 0xAAAAAA),
        ("dark_gray", '8', 0x555555), ("blue", '9', 0x5555FF), ("green", 'a', 0x55FF55), ("aqua", 'b', 0x55FFFF),
        ("red", 'c', 0xFF5555), ("light_purple", 'd', 0xFF55FF), ("yellow", 'e', 0xFFFF55), ("white", 'f', 0xFFFFFF),
    ];

    sealed record Style(string? Color = null, bool Bold = false, bool Italic = false, bool Underlined = false, bool Strike = false, bool Obfuscated = false)
    {
        public bool IsPlain => Color == null && !Bold && !Italic && !Underlined && !Strike && !Obfuscated;
    }

    /// <summary>The § colour code for a component colour name or #RRGGBB (the nearest of the 16 colours).</summary>
    static char ColorCode(string color)
    {
        foreach (var (name, code, _) in ColorCodes) if (name == color) return code;
        if (color.StartsWith('#') && int.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int rgb))
        {
            char best = 'f';
            long bestD = long.MaxValue;
            foreach (var (_, code, c) in ColorCodes)
            {
                long dr = ((rgb >> 16) & 255) - ((c >> 16) & 255), dg = ((rgb >> 8) & 255) - ((c >> 8) & 255), db = (rgb & 255) - (c & 255);
                long d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = code; }
            }
            return best;
        }
        return 'f';
    }

    sealed class FormattedWriter(bool bedrock)
    {
        public readonly StringBuilder Text = new();
        Style _current = new();

        public void Run(string text, Style s)
        {
            if (text.Length == 0) return;
            // Bedrock has no underline or strikethrough (its §m and §n are colours).
            if (bedrock) s = s with { Underlined = false, Strike = false };
            if (s != _current)
            {
                if (s.Color != null && s.Color != "reset") Text.Append('§').Append(ColorCode(s.Color));
                else if (!_current.IsPlain) Text.Append("§r");
                if (s.Obfuscated) Text.Append("§k");
                if (s.Bold) Text.Append("§l");
                if (s.Strike) Text.Append("§m");
                if (s.Underlined) Text.Append("§n");
                if (s.Italic) Text.Append("§o");
                _current = s;
            }
            Text.Append(text);
        }
    }

    static bool Flag(JsonElement e, string name, bool inherited) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0) : inherited;

    static Style StyleOf(JsonElement e, Style parent) => e.ValueKind != JsonValueKind.Object ? parent : new Style(
        e.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : parent.Color,
        Flag(e, "bold", parent.Bold), Flag(e, "italic", parent.Italic), Flag(e, "underlined", parent.Underlined),
        Flag(e, "strikethrough", parent.Strike), Flag(e, "obfuscated", parent.Obfuscated));

    static void Walk(JsonElement e, Style parent, FormattedWriter w)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: w.Run(e.GetString() ?? "", parent); break;
            case JsonValueKind.Array:
                {
                    // The first element styles the rest.
                    Style first = parent;
                    bool isFirst = true;
                    foreach (var x in e.EnumerateArray())
                    {
                        if (isFirst) { first = StyleOf(x, parent); Walk(x, parent, w); isFirst = false; }
                        else Walk(x, first, w);
                    }
                    break;
                }
            case JsonValueKind.Object:
                {
                    var s = StyleOf(e, parent);
                    if (e.TryGetProperty("text", out var t)) w.Run(t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : t.ToString(), s);
                    else if (e.TryGetProperty("translate", out var tr)) w.Run(tr.GetString() ?? "", s);
                    if (e.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
                        foreach (var x in extra.EnumerateArray()) Walk(x, s, w);
                    break;
                }
            case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False: w.Run(e.ToString(), parent); break;
        }
    }

    /// <summary>
    /// Text with § formatting codes for a canonical component (Bedrock signs, names and lore; Java ≤1.7 and Legacy
    /// Console): colours, bold, italic and obfuscated survive (on Java also underline and strikethrough); hex colours
    /// become the nearest of the 16 colours. Click actions cannot be kept and are reported.
    /// </summary>
    public static string ToFormatted(string json, bool bedrock)
    {
        if (!LooksLikeJson(json)) return json;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (json.Contains("clickEvent") || json.Contains("click_event"))
                ConversionLog.Note(LossKind.Block, "Text click action", "removed: plain formatted text cannot run commands or open links", lost: true);
            var w = new FormattedWriter(bedrock);
            Walk(doc.RootElement, new Style(), w);
            return w.Text.ToString();
        }
        catch { return ToPlain(json); }
    }

    /// <summary>The § codes still in effect at the end of a text (the last colour or reset and the formats after it).</summary>
    public static string TrailingCodes(string text)
    {
        // The last colour or reset starts the style in effect; format codes after it add to it.
        int start = -1;
        for (int i = 0; i + 1 < text.Length; i++)
            if (text[i] == '§' && IsColorOrReset(char.ToLowerInvariant(text[i + 1]))) start = i;
        var sb = new StringBuilder();
        for (int i = Math.Max(0, start); i + 1 < text.Length; i++)
            if (text[i] == '§') { sb.Append('§').Append(text[i + 1]); i++; }
        return sb.ToString();
    }

    /// <summary>Bedrock colour codes (including its material colours §g–§u) and reset.</summary>
    static bool IsColorOrReset(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'j') or (>= 'm' and <= 'n') or (>= 'p' and <= 'u');

    /// <summary>Canonical component for text with § formatting codes (Bedrock text, Java ≤1.7, Legacy Console).</summary>
    public static string FromFormatted(string text, bool bedrock)
    {
        if (!text.Contains('§')) return FromPlain(text);
        var runs = new List<Dictionary<string, object>>();
        var style = new Style();
        var sb = new StringBuilder();
        void Flush()
        {
            if (sb.Length == 0) return;
            var d = new Dictionary<string, object> { ["text"] = sb.ToString() };
            if (style.Color != null) d["color"] = style.Color;
            if (style.Bold) d["bold"] = true;
            if (style.Italic) d["italic"] = true;
            if (style.Underlined) d["underlined"] = true;
            if (style.Strike) d["strikethrough"] = true;
            if (style.Obfuscated) d["obfuscated"] = true;
            runs.Add(d);
            sb.Clear();
        }
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '§' || i + 1 >= text.Length) { sb.Append(text[i]); continue; }
            char code = char.ToLowerInvariant(text[++i]);
            Flush();
            int ci = "0123456789abcdef".IndexOf(code);
            if (ci >= 0) { style = new Style(ColorCodes[ci].Name); continue; }
            style = (code, bedrock) switch
            {
                ('k', _) => style with { Obfuscated = true },
                ('l', _) => style with { Bold = true },
                ('o', _) => style with { Italic = true },
                ('r', _) => new Style(),
                ('m', false) => style with { Strike = true },
                ('n', false) => style with { Underlined = true },
                // Bedrock material colours (§g minecoin gold, §h quartz … §u amethyst): the nearest Java colour.
                ('g' or 'p', true) => new Style("gold"),
                ('h' or 'i', true) => new Style("white"),
                ('j', true) => new Style("dark_gray"),
                ('m', true) => new Style("dark_red"),
                ('n', true) => new Style("gold"),
                ('q', true) => new Style("green"),
                ('s', true) => new Style("aqua"),
                ('t', true) => new Style("dark_blue"),
                ('u', true) => new Style("light_purple"),
                _ => style,
            };
        }
        Flush();
        if (runs.Count == 0) return FromPlain("");
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["text"] = "", ["extra"] = runs });
    }
}
