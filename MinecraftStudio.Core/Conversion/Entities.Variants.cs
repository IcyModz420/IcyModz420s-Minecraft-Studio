// MinecraftStudio.Core/Conversion/Entities.Variants.cs: old kinds and villager jobs across Java versions, for Entities.
// Part of Entities (main part in Entities.cs). Before 1.11 several kinds were one kind with a type number (donkeys, mules
// and the undead horses were horses, wither skeletons and strays skeletons, zombie villagers and husks zombies, elder
// guardians guardians); before 1.14 a villager's job was numbers (Profession, Career, CareerLevel); and a release before
// 1.13 lacks the kinds added after it (the game drops what it doesn't know without a word).
// Owns: VillagerJobFromJava (old numbers → canonical VillagerData), OldJob and OldKind (canonical → the release's own
// form), Added and OldRelease (the kinds a release before 1.13 has).
using MinecraftStudio.Core.Nbt;

namespace MinecraftStudio.Core.Conversion;

public static partial class Entities
{
    /// <summary>The first data version (1.11) with donkeys, wither skeletons, zombie villagers … as kinds of their own.</summary>
    public const int KindsSplit = 819;
    /// <summary>The first data version (1.14) keeping a villager's job as VillagerData.</summary>
    public const int VillagerDataSince = 1952;

    /// <summary>
    /// The release whose old forms a console save is written in (the console writer's chunks are the old, TU17-era kind,
    /// which every later console upgrades): zombie villagers as IsVillager, no strays or husks of their own.
    /// </summary>
    const double ConsoleForms = 1.09;

    // 1.8–1.13 jobs by Profession and Career, as the game upgrades them in 1.14.
    static string JobName(int profession, int career) => profession switch
    {
        0 => career switch { 2 => "fisherman", 3 => "shepherd", 4 => "fletcher", _ => "farmer" },
        1 => career == 2 ? "cartographer" : "librarian",
        2 => "cleric",
        3 => career switch { 2 => "weaponsmith", 3 => "toolsmith", _ => "armorer" },
        4 => career == 2 ? "leatherworker" : "butcher",
        5 => "nitwit",
        _ => "none",
    };

    // And back for a release before 1.14: cartographers and nitwits came in 1.11; masons and the jobless have no old
    // counterpart (the closest job it has).
    static (int Profession, int Career) JobNumbers(string job, double release) => job switch
    {
        "fisherman" => (0, 2), "shepherd" => (0, 3), "fletcher" => (0, 4),
        "librarian" => (1, 1), "cartographer" => release >= 1.11 ? (1, 2) : (1, 1),
        "cleric" => (2, 1), "armorer" => (3, 1), "weaponsmith" => (3, 2), "toolsmith" or "mason" => (3, 3),
        "butcher" => (4, 1), "leatherworker" => (4, 2),
        "nitwit" => release >= 1.11 ? (5, 1) : (0, 1),
        _ => (0, 1),
    };

    /// <summary>
    /// A villager's or zombie villager's job from the old numbers (before 1.14: Profession and Career; a zombie's
    /// Profession from 1.11, ZombieType in 1.10, VillagerProfession in 1.9) as canonical VillagerData; the old keys go.
    /// </summary>
    static void VillagerJobFromJava(NbtCompound c, NbtCompound e, string id)
    {
        if (id is not ("minecraft:villager" or "minecraft:zombie_villager") || e.ContainsKey("VillagerData")) return;
        int? profession = e.GetLongOrNull("Profession") is long p ? (int)p
            : e.GetInt("ZombieType") is int zt and >= 1 and <= 5 ? zt - 1
            : e.GetLongOrNull("VillagerProfession") is long vp ? (int)vp : null;
        string job = profession is int pr ? JobName(pr, e.GetInt("Career")) : "none";
        c["VillagerData"] = new NbtCompound
        {
            ["profession"] = new NbtString("minecraft:" + job), ["level"] = new NbtInt(Math.Clamp(e.GetInt("CareerLevel", 1), 1, 5)),
            ["type"] = new NbtString("minecraft:plains"),
        };
        foreach (var key in (string[])["Profession", "Career", "CareerLevel", "ProfessionName", "ZombieType", "IsVillager", "VillagerProfession"]) c.Remove(key);
    }

    /// <summary>For a version before 1.14: the canonical job as the old numbers (a zombie villager's in its release's form).</summary>
    static void OldJob(NbtCompound c, string id, int dv, double release)
    {
        if (id is not ("minecraft:villager" or "minecraft:zombie_villager") || c.GetCompound("VillagerData") is not NbtCompound data) return;
        c.Remove("VillagerData");
        string job = Bare(data.GetString("profession") ?? "minecraft:none");
        var (profession, career) = JobNumbers(job, release);
        if (JobName(profession, career) is var became && became != job && job != "none")
            ConversionLog.Note(LossKind.Entity, ConversionLog.Friendly(id), $"a {job} became a {became}: {TargetName(dv)} has no {job}s");
        if (id == "minecraft:villager")
        {
            c.SetInt("Profession", profession);
            c.SetInt("Career", career);
            c.SetInt("CareerLevel", Math.Clamp(data.GetInt("level", 1), 1, 5));
        }
        else if (release >= 1.11) c.SetInt("Profession", profession);
        else if (release >= 1.10) c.SetInt("ZombieType", profession + 1);
        else
        {
            c.SetByte("IsVillager", 1);
            if (release >= 1.09) c.SetInt("VillagerProfession", profession);
        }
    }

    /// <summary>For a release before 1.11: the kind it keeps the entity as, with its type number set.</summary>
    static string OldKind(NbtCompound c, string id, int dv, double release)
    {
        switch (id)
        {
            case "minecraft:horse": c.SetInt("Type", 0); return id;
            case "minecraft:donkey": c.SetInt("Type", 1); return "minecraft:horse";
            case "minecraft:mule": c.SetInt("Type", 2); return "minecraft:horse";
            case "minecraft:zombie_horse": c.SetInt("Type", 3); return "minecraft:horse";
            case "minecraft:skeleton_horse": c.SetInt("Type", 4); return "minecraft:horse";
            case "minecraft:elder_guardian": c.SetByte("Elder", 1); return "minecraft:guardian";
            case "minecraft:wither_skeleton": c.SetByte("SkeletonType", 1); return "minecraft:skeleton";
            case "minecraft:stray":
                if (release >= 1.10) c.SetByte("SkeletonType", 2);
                else ConversionLog.Note(LossKind.Entity, "stray", $"became a skeleton: {TargetName(dv)} has no strays");
                return "minecraft:skeleton";
            case "minecraft:husk":
                if (release >= 1.10) c.SetInt("ZombieType", 6);
                else ConversionLog.Note(LossKind.Entity, "husk", $"became a zombie: {TargetName(dv)} has no husks");
                return "minecraft:zombie";
            case "minecraft:zombie_villager":
                return "minecraft:zombie"; // its job set by OldJob
            default:
                return id;
        }
    }

    /// <summary>
    /// The release (<see cref="LegacyLevel"/>) a Java target before 1.13 is: the target's own, else from the data version
    /// (1.8 without one). Null from 1.13 on, and for consoles (which keep their own set).
    /// </summary>
    static double? OldRelease(int dv)
    {
        if (dv >= 1451) return null;
        var t = ConversionLog.Current?.Target;
        if (t?.Kind == TargetKind.LegacyConsole) return null;
        if (t?.Kind == TargetKind.Java) return t.LegacyLevel;
        return dv >= 1139 ? 1.12 : dv >= 819 ? 1.11 : dv >= 510 ? 1.10 : dv >= 169 ? 1.09 : LegacyLevel.Release_1_8;
    }

    /// <summary>
    /// The release each kind older than 1.13 came in (strays and husks are not here: before 1.10 they become skeletons and
    /// zombies). A target before that release can't have it.
    /// </summary>
    static readonly Dictionary<string, double> Added = new(StringComparer.Ordinal)
    {
        ["minecraft:parrot"] = 1.12, ["minecraft:illusioner"] = 1.12,
        ["minecraft:llama"] = 1.11, ["minecraft:llama_spit"] = 1.11, ["minecraft:vex"] = 1.11, ["minecraft:evoker"] = 1.11,
        ["minecraft:vindicator"] = 1.11, ["minecraft:evoker_fangs"] = 1.11,
        ["minecraft:polar_bear"] = 1.10,
        ["minecraft:shulker"] = 1.09, ["minecraft:shulker_bullet"] = 1.09, ["minecraft:dragon_fireball"] = 1.09,
        ["minecraft:area_effect_cloud"] = 1.09, ["minecraft:spectral_arrow"] = 1.09,
        ["minecraft:endermite"] = 1.08, ["minecraft:guardian"] = 1.08, ["minecraft:elder_guardian"] = 1.08, ["minecraft:rabbit"] = 1.08,
        ["minecraft:armor_stand"] = 1.08,
        ["minecraft:command_block_minecart"] = 1.07,
        ["minecraft:horse"] = 1.06, ["minecraft:donkey"] = 1.06, ["minecraft:mule"] = 1.06, ["minecraft:skeleton_horse"] = 1.06,
        ["minecraft:zombie_horse"] = 1.06, ["minecraft:leash_knot"] = 1.06,
        ["minecraft:hopper_minecart"] = 1.05, ["minecraft:tnt_minecart"] = 1.05,
        ["minecraft:firework_rocket"] = 1.0406,
        ["minecraft:wither"] = 1.04, ["minecraft:wither_skull"] = 1.04, ["minecraft:bat"] = 1.04, ["minecraft:witch"] = 1.04,
        ["minecraft:item_frame"] = 1.04, ["minecraft:wither_skeleton"] = 1.04, ["minecraft:zombie_villager"] = 1.04,
        ["minecraft:experience_bottle"] = 1.03,
        ["minecraft:iron_golem"] = 1.02, ["minecraft:ocelot"] = 1.02,
        ["minecraft:blaze"] = 1.0, ["minecraft:magma_cube"] = 1.0, ["minecraft:mooshroom"] = 1.0, ["minecraft:snow_golem"] = 1.0,
        ["minecraft:villager"] = 1.0, ["minecraft:ender_dragon"] = 1.0, ["minecraft:end_crystal"] = 1.0, ["minecraft:eye_of_ender"] = 1.0,
        ["minecraft:small_fireball"] = 1.0, ["minecraft:potion"] = 1.0, ["minecraft:ender_pearl"] = 1.0,
        ["minecraft:enderman"] = LegacyLevel.Beta_1_8, ["minecraft:cave_spider"] = LegacyLevel.Beta_1_8,
        ["minecraft:silverfish"] = LegacyLevel.Beta_1_8, ["minecraft:experience_orb"] = LegacyLevel.Beta_1_8,
        ["minecraft:wolf"] = 0.54, ["minecraft:squid"] = LegacyLevel.Beta_1_2,
        ["minecraft:ghast"] = LegacyLevel.Alpha_1_2, ["minecraft:zombified_piglin"] = LegacyLevel.Alpha_1_2, ["minecraft:fireball"] = LegacyLevel.Alpha_1_2,
    };
}
