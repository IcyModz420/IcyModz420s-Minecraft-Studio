// MinecraftStudio.Core/Packs/WorldPack.cs: one pack a world has (or uses), for the world's Packs page.
// Owns: WorldPackKind (Java data pack / world resource pack, Bedrock behavior / resource pack) and WorldPack — its id,
// name, description, version, where its files are, whether the world has it turned on, and what can be done with it.
// Reading and changing a world's packs: WorldPacks (WorldPacks.cs, .Java.cs, .Bedrock.cs).
namespace MinecraftStudio.Core.Packs;

public enum WorldPackKind
{
    /// <summary>Java: a data pack in the world's datapacks folder (or one built into the game).</summary>
    DataPack,
    /// <summary>Java: the world's own resource pack (resources.zip next to level.dat), offered when the world opens.</summary>
    WorldResourcePack,
    /// <summary>Bedrock: a behavior pack the world uses (world_behavior_packs.json).</summary>
    BehaviorPack,
    /// <summary>Bedrock: a resource (texture) pack the world uses (world_resource_packs.json).</summary>
    ResourcePack,
}

/// <summary>A pack a world has or uses.</summary>
public sealed class WorldPack
{
    public required WorldPackKind Kind { get; init; }
    /// <summary>Java: "file/Name.zip" or a built-in id such as "vanilla"; Bedrock: the pack's UUID.</summary>
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Bedrock: the pack version ("1.0.0"); Java: the pack format number.</summary>
    public string Version { get; init; } = "";
    /// <summary>The pack's file or folder on this PC, or null when it cannot be found.</summary>
    public string? Location { get; init; }
    /// <summary>Turned on for this world (Java: enabled; Bedrock: in the world's pack list).</summary>
    public bool Active { get; init; }
    /// <summary>Part of the game itself (Java's "vanilla" and feature packs): can be switched, not uninstalled.</summary>
    public bool BuiltIn { get; init; }
    /// <summary>The pack's files are inside the world folder (so uninstalling takes them out of it).</summary>
    public bool InWorld { get; init; }
    /// <summary>Where the pack is, in words.</summary>
    public string Where { get; init; } = "";

    public string KindText => Kind switch
    {
        WorldPackKind.DataPack => "Data pack",
        WorldPackKind.WorldResourcePack => "World resource pack",
        WorldPackKind.BehaviorPack => "Behavior pack",
        _ => "Resource pack",
    };

    /// <summary>Whether it can be taken out of the world (its files move to the removed-packs folder, never deleted).</summary>
    public bool CanUninstall => !BuiltIn && (InWorld || Kind is WorldPackKind.BehaviorPack or WorldPackKind.ResourcePack);
    public bool Missing => Location == null && !BuiltIn;

    public override string ToString() => $"{KindText}: {Name} ({Id})";
}
