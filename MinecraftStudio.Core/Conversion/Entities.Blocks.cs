// MinecraftStudio.Core/Conversion/Entities.Blocks.cs: the block an enderman carries, across Java versions, for Entities.
// Part of Entities (main part in Entities.cs). Canonical = carriedBlockState {Name, Properties} under the newest names;
// before 1.13 it was numbers (carried, carriedData). Renames between versions and blocks a version lacks go the way they
// go for chunks (JavaBlocks). A carried id stored as a name (some snapshots) is kept as it was.
// Owns: CarriedFromJava, CarriedToJava.
using MinecraftStudio.Core.Nbt;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Conversion;

public static partial class Entities
{
    /// <summary>The block a Java enderman <paramref name="e"/> carries, as canonical carriedBlockState on <paramref name="c"/>.</summary>
    static void CarriedFromJava(NbtCompound c, NbtCompound e, int dv)
    {
        BlockState? state = null;
        if (e["carriedBlockState"] is NbtTag stored) state = JavaBlocks.Canonicalize(BlockState.FromJavaNbt(stored), dv);
        else if (e["carried"] is { IsNumeric: true } number)
        {
            if (number.AsLong > 0) state = JavaBlocks.Canonicalize(JavaBlocks.FromLegacy((int)number.AsLong, e.GetInt("carriedData")));
            c.Remove("carried");
            c.Remove("carriedData");
        }
        c.Remove("carriedBlockState");
        if (state is { IsAir: false }) c["carriedBlockState"] = state.ToJavaNbt();
    }

    /// <summary>A canonical enderman's carried block in the form of data version <paramref name="dv"/>, on <paramref name="c"/>.</summary>
    static void CarriedToJava(NbtCompound c, NbtCompound canonical, int dv)
    {
        if (canonical["carriedBlockState"] is not NbtTag stored) return;
        var state = BlockState.FromJavaNbt(stored);
        c.Remove("carriedBlockState");
        if (dv >= 1451)
        {
            c["carriedBlockState"] = JavaBlocks.ToVersion(state, dv).ToJavaNbt();
            return;
        }
        var (id, data) = JavaBlocks.ToLegacy(state, OldRelease(dv) ?? LegacyLevel.Release_1_12);
        if (id <= 0) return;
        c.SetShort("carried", (short)id);
        c.SetShort("carriedData", (short)data);
    }
}
