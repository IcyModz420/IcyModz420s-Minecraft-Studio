// MinecraftStudio.Core/Conversion/Effects.cs: potion effects across Java versions — on mobs, on players, in beacons.
// Owns: Effects — canonical = the newest form: active_effects [{id name, amplifier, duration, ambient, show_particles,
// show_icon, hidden_effect, factor_calculation_data}] and a beacon's primary_effect/secondary_effect names. Before 1.20.2
// (data version 3578) effects were ActiveEffects with a number for Id and CamelCase keys, and beacons kept numbers
// (Primary/Secondary, 0 = none; Bedrock's beacon numbers are the same). An effect a version lacks is reported.
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public static class Effects
{
    /// <summary>The first Java data version (1.20.2) naming effects (active_effects, primary_effect …) instead of numbering them.</summary>
    public const int NamedSince = 3578;

    // Java's effect numbers (the same in every version that numbered them).
    static readonly string[] ByNumber =
    [
        "", "speed", "slowness", "haste", "mining_fatigue", "strength", "instant_health", "instant_damage", "jump_boost", "nausea",
        "regeneration", "resistance", "fire_resistance", "water_breathing", "invisibility", "blindness", "night_vision", "hunger",
        "weakness", "poison", "wither", "health_boost", "absorption", "saturation", "glowing", "levitation", "luck", "unluck",
        "slow_falling", "conduit_power", "dolphins_grace", "bad_omen", "hero_of_the_village", "darkness",
    ];

    // The data version each effect newer than 1.8 came in.
    static readonly Dictionary<string, int> Since = new(StringComparer.Ordinal)
    {
        ["glowing"] = 169, ["levitation"] = 169, ["luck"] = 169, ["unluck"] = 169,
        ["slow_falling"] = 1519, ["conduit_power"] = 1519, ["dolphins_grace"] = 1519,
        ["bad_omen"] = 1952, ["hero_of_the_village"] = 1952, ["darkness"] = 3105,
        ["trial_omen"] = 3953, ["raid_omen"] = 3953, ["wind_charged"] = 3953, ["weaving"] = 3953, ["oozing"] = 3953, ["infested"] = 3953,
    };

    static string Bare(string id) => id.StartsWith("minecraft:", StringComparison.Ordinal) ? id[10..] : id;

    /// <summary>"minecraft:speed" for Java's (and a beacon's) effect number, or null for 0 and numbers no effect has.</summary>
    public static string? Name(long number) => number > 0 && number < ByNumber.Length ? "minecraft:" + ByNumber[number] : null;

    /// <summary>The number of an effect, or 0 when it never had one (effects added after 1.20.2).</summary>
    public static int Number(string? name) => name == null ? 0 : Math.Max(0, Array.IndexOf(ByNumber, Bare(name)));

    /// <summary>Whether a Java data version has the effect.</summary>
    public static bool Known(string name, int dv) => !Since.TryGetValue(Bare(name), out int since) || dv >= since;

    /// <summary>The effects of a Java entity or player <paramref name="e"/> (either form) as canonical active_effects on <paramref name="c"/>.</summary>
    public static void FromJava(NbtCompound c, NbtCompound e)
    {
        var list = e.GetList("active_effects") ?? e.GetList("ActiveEffects");
        c.Remove("ActiveEffects");
        c.Remove("active_effects");
        if (list is not { Count: > 0 }) return;
        var canonical = new NbtList(NbtType.Compound);
        foreach (var fx in list.OfType<NbtCompound>())
            if (Canonical(fx) is NbtCompound x) canonical.Add(x);
        if (canonical.Count > 0) c["active_effects"] = canonical;
    }

    static readonly (string New, string Old)[] Keys =
        [("amplifier", "Amplifier"), ("duration", "Duration"), ("ambient", "Ambient"), ("show_particles", "ShowParticles"), ("show_icon", "ShowIcon")];

    static NbtCompound? Canonical(NbtCompound fx)
    {
        string? id = fx.GetString("id") ?? (fx["Id"] is { IsNumeric: true } n ? Name(n.AsLong & 0xFF) : null);
        if (id == null) return null;
        var c = new NbtCompound { ["id"] = new NbtString(id.Contains(':') ? id : "minecraft:" + id) };
        foreach (var (newKey, oldKey) in Keys)
            if ((fx[newKey] ?? fx[oldKey]) is NbtTag v) c[newKey] = v.Clone();
        if ((fx.GetCompound("hidden_effect") ?? fx.GetCompound("HiddenEffect")) is NbtCompound hidden && Canonical(hidden) is NbtCompound h) c["hidden_effect"] = h;
        if ((fx.GetCompound("factor_calculation_data") ?? fx.GetCompound("FactorCalculationData")) is NbtCompound f) c["factor_calculation_data"] = f.Clone();
        return c;
    }

    /// <summary>Canonical active_effects on <paramref name="c"/> (the output copy) in the form of data version <paramref name="dv"/>.</summary>
    public static void ToJava(NbtCompound c, int dv, string who)
    {
        if (c.GetList("active_effects") is not { Count: > 0 } list) { c.Remove("active_effects"); return; }
        c.Remove("active_effects");
        var written = new NbtList(NbtType.Compound);
        foreach (var fx in list.OfType<NbtCompound>())
            if (Written(fx, dv, who) is NbtCompound w) written.Add(w);
        if (written.Count > 0) c[dv >= NamedSince ? "active_effects" : "ActiveEffects"] = written;
    }

    static NbtCompound? Written(NbtCompound fx, int dv, string who)
    {
        string id = fx.GetString("id") ?? "";
        int number = Number(id);
        if (!Known(id, dv) || (dv < NamedSince && number == 0))
        {
            ConversionLog.Note(LossKind.Entity, who + " effect", $"{ConversionLog.Friendly(id)} left out: {ConversionLog.Current?.Target.Name ?? $"data version {dv}"} has no such effect", lost: true);
            return null;
        }
        var w = new NbtCompound();
        if (dv >= NamedSince) w["id"] = new NbtString(id);
        else w.SetByte("Id", (sbyte)number);
        foreach (var (newKey, oldKey) in Keys)
            if (fx[newKey] is NbtTag v) w[dv >= NamedSince ? newKey : oldKey] = v.Clone();
        if (fx.GetCompound("hidden_effect") is NbtCompound hidden && Written(hidden, dv, who) is NbtCompound h) w[dv >= NamedSince ? "hidden_effect" : "HiddenEffect"] = h;
        if (fx.GetCompound("factor_calculation_data") is NbtCompound f) w[dv >= NamedSince ? "factor_calculation_data" : "FactorCalculationData"] = f.Clone();
        return w;
    }

    /// <summary>A beacon (as stored by Java, any version) with its effects as canonical names, on <paramref name="c"/>.</summary>
    public static void BeaconFromJava(NbtCompound c, NbtCompound be)
    {
        foreach (var (named, numbered) in BeaconKeys)
        {
            string? name = be.GetString(named) ?? (be[numbered] is { IsNumeric: true } n ? Name(n.AsLong) : null);
            c.Remove(named);
            c.Remove(numbered);
            if (name != null) c.SetString(named, name.Contains(':') ? name : "minecraft:" + name);
        }
    }

    /// <summary>A canonical beacon's effects in the form of data version <paramref name="dv"/>, on <paramref name="c"/> (the output copy).</summary>
    public static void BeaconToJava(NbtCompound c, NbtCompound canonical, int dv)
    {
        foreach (var (named, numbered) in BeaconKeys)
        {
            c.Remove(named);
            c.Remove(numbered);
            string? name = canonical.GetString(named);
            if (dv >= NamedSince) { if (name != null) c.SetString(named, name); }
            else c.SetInt(numbered, Number(name));
        }
    }

    static readonly (string Named, string Numbered)[] BeaconKeys = [("primary_effect", "Primary"), ("secondary_effect", "Secondary")];
}
