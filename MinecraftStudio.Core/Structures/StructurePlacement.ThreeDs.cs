// Structures/StructurePlacement.ThreeDs.cs: why a structure is not placed into a New Nintendo 3DS Edition world.
// Owns: ThreeDsNotWritable (the message) — placement into 3DS is refused before anything is copied. Writing changed
// chunks into a 3DS save makes the game delete the world (reported by the MC3DS save research project, whose 3DS
// converter does not write 3DS worlds for this reason), and the check behind it is not publicly understood. Cropping a
// structure OUT of a 3DS world is fully supported.
namespace MinecraftStudio.Core.Structures;

public static partial class StructurePlacement
{
    public const string ThreeDsNotWritable =
        "Structures can't be placed into New Nintendo 3DS Edition worlds. When a 3DS world's chunks are changed outside " +
        "the game, the game deletes the whole world, and how it checks them isn't publicly known yet — so writing one " +
        "would put your world at risk. You can still crop buildings OUT of a 3DS world. To use a building together with a " +
        "3DS world, convert the 3DS world to Bedrock or Java Edition first (World ▸ Convert), then place it into that.";
}
