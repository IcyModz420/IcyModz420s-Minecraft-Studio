// MinecraftStudio.Core/LegacyConsole/XMemLzx.cs: Xbox 360 XMemCompress LZX: 128 KiB window, 32 KiB frames with
// big-endian frame headers, no E8 translation, and a 5-byte zero trailer.
// Owns: XMemLzx.
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.LegacyConsole;

/// <summary>
/// Xbox 360 XMemCompress LZX: 128 KiB window, 32 KiB frames with big-endian frame headers, no E8 translation,
/// and a 5-byte zero trailer. Decoder handles verbatim, aligned and uncompressed blocks; the encoder writes
/// one verbatim block per frame.
/// </summary>
public static partial class XMemLzx
{
    const int WindowBits = 17;
    const int WindowSize = 1 << WindowBits;
    const int WindowMask = WindowSize - 1;
    const int PositionSlots = 34;
    const int MainElements = 256 + PositionSlots * 8;
    const int SecondaryLengths = 249;
    const int FrameSize = 0x8000;

    static readonly int[] ExtraBits = new int[52];
    static readonly int[] PositionBase = new int[52];

    static XMemLzx()
    {
        int j = 0;
        for (int i = 0; i < 52; i += 2)
        {
            ExtraBits[i] = ExtraBits[i + 1] = j;
            if (i != 0 && j < 17) j++;
        }
        j = 0;
        for (int i = 0; i < 52; i++)
        {
            PositionBase[i] = j;
            j += 1 << ExtraBits[i];
        }
    }
}
