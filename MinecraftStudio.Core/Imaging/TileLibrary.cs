// MinecraftStudio.Core/Imaging/TileLibrary.cs: the player's own saved tiles and pictures, to use again in any editor.
// Owns: SavedTile and TileLibrary — a folder of PNG files (Documents\Minecraft Studio\My Tiles in the app): what is in it,
// saving a picture under a safe, unused name (written to a temporary file first, then moved into place), reading one
// back (size-capped), and renaming. Removing a tile is left to the app (it goes to the Recycle Bin, never deleted).
using MinecraftStudio.Core.IO;

namespace MinecraftStudio.Core.Imaging;

/// <summary>One saved tile: its name (the file name without .png), where it is, and its size.</summary>
public sealed record SavedTile(string Name, string Path, int Width, int Height, DateTime Saved);

/// <summary>A folder of the player's own tiles and pictures.</summary>
public sealed class TileLibrary
{
    /// <summary>Pictures larger than this are not kept as tiles (a whole skin or HD texture is far smaller).</summary>
    public const int MaxSide = 2048;
    const long MaxBytes = 32L << 20;

    public TileLibrary(string folder) => Folder = folder;

    public string Folder { get; }

    /// <summary>The saved tiles, newest first; files that are not readable pictures are left out.</summary>
    public List<SavedTile> List()
    {
        var list = new List<SavedTile>();
        if (!Directory.Exists(Folder)) return list;
        foreach (var file in Directory.EnumerateFiles(Folder, "*.png"))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Length > MaxBytes) continue;
                var (w, h) = Png.Size(File.ReadAllBytes(file));
                list.Add(new SavedTile(System.IO.Path.GetFileNameWithoutExtension(file), file, w, h, info.LastWriteTimeUtc));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        return list.OrderByDescending(t => t.Saved).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Saves a picture as a tile; a name already taken gets a number. Returns the tile.</summary>
    public SavedTile Save(string name, RgbaImage image)
    {
        if (image.Width > MaxSide || image.Height > MaxSide) throw new InvalidDataException($"Tiles can be up to {MaxSide} × {MaxSide} pixels.");
        Directory.CreateDirectory(Folder);
        string safe = SafeName.For(name.Trim(), "Tile");
        string path = System.IO.Path.Combine(Folder, safe + ".png");
        for (int n = 2; File.Exists(path); n++) path = System.IO.Path.Combine(Folder, $"{safe} ({n}).png");
        // Written whole to a temporary file next to it, then moved into place: a tile is never half-written.
        string temp = path + ".saving";
        File.WriteAllBytes(temp, image.ToPng());
        File.Move(temp, path);
        return new SavedTile(System.IO.Path.GetFileNameWithoutExtension(path), path, image.Width, image.Height, File.GetLastWriteTimeUtc(path));
    }

    /// <summary>Reads a tile's picture.</summary>
    public RgbaImage Load(SavedTile tile)
    {
        var info = new FileInfo(tile.Path);
        if (info.Length > MaxBytes) throw new InvalidDataException("That tile's file is too large.");
        return Png.Decode(File.ReadAllBytes(tile.Path));
    }

    /// <summary>Gives a tile a new name (a name already taken gets a number). Returns the renamed tile.</summary>
    public SavedTile Rename(SavedTile tile, string name)
    {
        string safe = SafeName.For(name.Trim(), "Tile");
        string path = System.IO.Path.Combine(Folder, safe + ".png");
        if (string.Equals(path, tile.Path, StringComparison.OrdinalIgnoreCase)) return tile;
        for (int n = 2; File.Exists(path); n++) path = System.IO.Path.Combine(Folder, $"{safe} ({n}).png");
        File.Move(tile.Path, path);
        return tile with { Name = System.IO.Path.GetFileNameWithoutExtension(path), Path = path };
    }
}
