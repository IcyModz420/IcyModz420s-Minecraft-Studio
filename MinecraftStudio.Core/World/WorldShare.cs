// MinecraftStudio.Core/World/WorldShare.cs: a world packed into one file to send or keep.
// Owns: WorldShare — Bedrock as a .mcworld (level.dat at the top; opened, the game imports it on Windows, phones and
// tablets), Java as a .zip with the world's folder inside (unzipped into saves, it plays). The world is only read: the
// file is written under a temporary name, opened again and checked for level.dat where the game looks, then named.
using System.IO.Compression;
using MinecraftStudio.Core.Bedrock;
using MinecraftStudio.Core.Java;

namespace MinecraftStudio.Core.World;

public static class WorldShare
{
    /// <summary>Whether this world can be packed (a Java or Bedrock world folder).</summary>
    public static bool CanPack(SaveWorld world) => world is BedrockWorld or JavaWorld && Directory.Exists(world.Location);

    /// <summary>The file name's extension for the world: .mcworld for Bedrock, .zip for Java.</summary>
    public static string Extension(SaveWorld world) => world is BedrockWorld ? ".mcworld" : ".zip";

    /// <summary>Packs the world (as saved on disk) into <paramref name="target"/>; returns its size in bytes.</summary>
    public static long Pack(SaveWorld world, string target)
    {
        if (!CanPack(world)) throw new NotSupportedException("Only Java and Bedrock world folders can be packed into one file.");
        string folder = Path.GetFullPath(world.Location).TrimEnd('\\', '/');
        string full = Path.GetFullPath(target);
        if (full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("The file cannot go inside the world's own folder.");
        bool bedrock = world is BedrockWorld;
        string temp = full + ".partial";
        if (File.Exists(temp)) File.Delete(temp);
        try
        {
            // .mcworld: the world's files at the top; .zip: the world's folder at the top, as it goes into saves.
            ZipFile.CreateFromDirectory(folder, temp, CompressionLevel.Optimal, includeBaseDirectory: !bedrock);
            using (var zip = ZipFile.OpenRead(temp))
            {
                bool has = bedrock
                    ? zip.GetEntry("level.dat") != null
                    : zip.Entries.Any(e => e.Name == "level.dat" && e.FullName.Replace('\\', '/').Count(c => c == '/') == 1);
                if (!has) throw new IOException("The packed file does not hold the world's level.dat where the game looks for it.");
            }
            File.Move(temp, full, overwrite: true);
            return new FileInfo(full).Length;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
