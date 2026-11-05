// MinecraftStudio.Core/World/LagMachines.Limits.cs: limits of LagMachines.
// Part of LagMachines (main part in LagMachines.cs). Owns: LagMachines.Limits — how much of each thing it takes to count as
// a lag machine. Each is far past what ordinary play leaves: measured on real survival worlds (farms, sorters, storage,
// villages and redstone of every size) and on the test worlds, then set well above the largest seen there.
namespace MinecraftStudio.Core.World;

public static partial class LagMachines
{
    static class Limits
    {
        // Piles in one block: the game squashes the 25th mob in one place (a farm in a survival world still packed 30 into
        // one block and the one above it), and survival play has no use for more than a few minecarts, boats or armour
        // stands in one block (at most 1 in 44 survival worlds).
        public const int MobsInOneBlock = 100;
        public const int MinecartsInOneBlock = 40;
        public const int BoatsInOneBlock = 40;
        public const int ArmourStandsInOneBlock = 40;
        // Only one item frame fits on a block face.
        public const int FramesOnOneFace = 8;
        // Sand caving in falls one block per column (100 falling blocks in one chunk of a survival world, never more than 2
        // in one block); a machine drops it in one place.
        public const int FallingBlocksInOneBlock = 20;

        // Floods in one chunk (dropped items join into stacks, and despawn after five minutes). The most seen in 44 survival
        // worlds: 159 dropped items, 9 orbs, 206 falling blocks, 8 projectiles, 72 mobs, 3 minecarts.
        public const int DroppedItemsInOneChunk = 2000;
        public const int OrbsInOneChunk = 2000;
        public const int FallingBlocksInOneChunk = 1000;
        public const int LitTntInOneChunk = 64;
        public const int ProjectilesInOneChunk = 1000;
        public const int CloudsInOneChunk = 100;
        public const int MarkersInOneChunk = 3000;
        public const int MobsInOneChunk = 800;
        public const int MinecartsInOneChunk = 400;

        // Solid masses: a cube at least this wide, nearly all one part, measured as the whole connected mass (the largest in
        // survival worlds: 2 wide; dust floors 2 wide).
        public const int CubeSide = 5;
        public const double SolidFill = 0.9;
        // Floors: a patch on one level at least this wide both ways, of at least this many parts, nearly filling its rectangle.
        public const int FloorSide = 16;
        public const int FloorParts = 256;
        // Chunks with fewer of one part than this cannot hold a mass and are not kept.
        public const int PartsWorthKeeping = 20;
        public const int MassMeasuredUpTo = 2_000_000;

        // A machine running when the world was saved: redstone parts' updates only (a survival chunk had 1,856 waiting, nearly
        // all leaves). Water and lava only back a machine up: new terrain leaves thousands (4,554 in one survival chunk).
        public const int RedstoneUpdatesInOneChunk = 1000;
        public const int FluidUpdatesInOneChunk = 4000;
        public const int MovingPistonsInOneChunk = 200;

        // Data: past what players can be sent without being disconnected (the most seen: a 25 KB chest, a 914 KB chunk).
        public const long ContainerBytes = 1L << 20;
        public const long ChunkBytes = 4L << 20;
    }
}
