// MinecraftStudio/Studio/PixelClipboard.cs: pictures in and out of the pixel editors — files of every common kind, the
// clipboard and drag-and-drop.
// Owns: PixelClipboard — reading PNG and TGA (with the program's own readers) and JPEG, BMP, GIF, TIFF and ICO (through
// Windows), capped in file size and pixel count; copying pixels so other programs keep see-through parts ("PNG") or get
// a plain picture; and finding the picture in a drop or on the clipboard (a file, or a picture another program put there).
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MinecraftStudio.Core.Imaging;

namespace MinecraftStudio.Studio;

/// <summary>Pictures in and out of the pixel editors.</summary>
static class PixelClipboard
{
    /// <summary>Picture files larger than this are not opened (a texture or skin picture is far smaller).</summary>
    public const long MaxFileBytes = 64L << 20;
    static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".tga", ".tif", ".tiff", ".ico"];
    public const string OpenFilter = "Pictures (PNG, JPEG, BMP, GIF, TGA, TIFF)|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif;*.tga;*.tif;*.tiff;*.ico|All files|*.*";

    public static bool IsPictureFile(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Reads a picture file.</summary>
    public static RgbaImage Load(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes) throw new InvalidDataException($"{info.Name} is {info.Length / (1 << 20)} MB; pictures larger than {MaxFileBytes >> 20} MB are not opened.");
        return Decode(File.ReadAllBytes(path), Path.GetExtension(path).ToLowerInvariant());
    }

    /// <summary>Reads a picture from its bytes (PNG and TGA by this program; other kinds through Windows).</summary>
    public static RgbaImage Decode(byte[] data, string? extension = null)
    {
        if (Png.IsPng(data)) return Png.Decode(data);
        if (extension == ".tga" || extension == null && Tga.LooksLikeTga(data)) return Tga.Decode(data);
        try
        {
            using var ms = new MemoryStream(data);
            // The size is checked before the picture is decoded, so a damaged or hostile file cannot ask for gigabytes.
            using var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            if ((long)img.Width * img.Height > Png.MaxPixels) throw new InvalidDataException($"The picture is too large ({img.Width} × {img.Height}).");
            return FromImage(img);
        }
        catch (ArgumentException) { throw new InvalidDataException("This is not a picture this program can read."); }
        catch (ExternalException e) { throw new InvalidDataException("The picture could not be read: " + e.Message); }
        catch (OutOfMemoryException) { throw new InvalidDataException("The picture could not be read (damaged, or too large)."); }
    }

    /// <summary>A picture's pixels, see-through parts kept.</summary>
    public static RgbaImage FromImage(Image img)
    {
        using var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
        }
        var result = new RgbaImage(bmp.Width, bmp.Height);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var row = new int[bmp.Width];
        for (int y = 0; y < bmp.Height; y++)
        {
            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, bmp.Width);
            Buffer.BlockCopy(row, 0, result.Pixels, y * bmp.Width * 4, bmp.Width * 4);
        }
        bmp.UnlockBits(data);
        return result;
    }

    public static Bitmap ToBitmap(RgbaImage img)
    {
        var b = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
        var data = b.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var row = new int[img.Width];
        for (int y = 0; y < img.Height; y++)
        {
            Buffer.BlockCopy(img.Pixels, y * img.Width * 4, row, 0, img.Width * 4);
            Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, img.Width);
        }
        b.UnlockBits(data);
        return b;
    }

    // ------------------------------------------------------------------ clipboard and drops

    /// <summary>Whether a drop or the clipboard holds a picture (a picture file, or a picture from another program).</summary>
    public static bool HasPicture(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files) return files.Any(IsPictureFile);
            return data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap);
        }
        catch (ExternalException) { return false; }
    }

    /// <summary>The picture in a drop or on the clipboard, and its file name when it came from a file; null when there is none.</summary>
    public static (RgbaImage Image, string? Name)? Read(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var file = files.FirstOrDefault(IsPictureFile);
            return file == null ? null : (Load(file), Path.GetFileName(file));
        }
        // "PNG" keeps see-through parts (browsers, GIMP, Paint.NET put it there); a plain bitmap does not.
        if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png) return (Decode(png.ToArray()), null);
        if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is Image bmp) return (FromImage(bmp), null);
        return null;
    }

    public static (RgbaImage Image, string? Name)? FromClipboard()
    {
        try { return Clipboard.GetDataObject() is { } d ? Read(d) : null; }
        catch (ExternalException) { return null; }
    }

    /// <summary>Puts pixels on the clipboard: as PNG (see-through parts kept) and as a plain picture for other programs.</summary>
    public static bool Copy(RgbaImage img)
    {
        try
        {
            var data = new DataObject();
            data.SetData("PNG", false, new MemoryStream(img.ToPng()));
            using var bmp = ToBitmap(img);
            data.SetData(DataFormats.Bitmap, true, bmp);
            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (ExternalException) { return false; }
    }
}
