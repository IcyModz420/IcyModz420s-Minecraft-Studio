// MinecraftStudio.Core/Skins/Skin.cs: A player skin in the modern square layout (64×64, or 128×128 and larger
// for Bedrock HD skins) plus its arm model.
// Owns: Skin.
using MinecraftStudio.Core.Imaging;

namespace MinecraftStudio.Core.Skins;

/// <summary>
/// A player skin in the modern square layout (64×64, or 128×128 and larger for Bedrock HD skins) plus its arm model.
/// Old 64×32 skins are upgraded on load exactly as Java Edition does, so editing always happens on the full layout.
/// </summary>
public sealed class Skin
{
    public Skin(RgbaImage image, SkinModel model)
    {
        if (!IsSkinSize(image.Width, image.Height) || image.Width != image.Height)
            throw new ArgumentException("A skin is square: 64 × 64, 128 × 128, 256 × 256 or 512 × 512 pixels.");
        Image = image;
        Model = model;
    }

    public RgbaImage Image { get; private set; }
    public SkinModel Model { get; private set; }
    public string Name { get; set; } = "My Skin";
    /// <summary>Pixels per skin unit: 1 for 64×64, 2 for 128×128 …</summary>
    public int Scale => Image.Width / 64;

    /// <summary>What happened while loading (an old skin upgraded, the arm model guessed …).</summary>
    public List<string> LoadNotes { get; } = [];

    /// <summary>64, 128, 256 or 512 wide; as tall, or half as tall for the old layout.</summary>
    public static bool IsSkinSize(int width, int height) =>
        width is 64 or 128 or 256 or 512 && (height == width || height * 2 == width);

    /// <summary>Reads any skin PNG: 64×32 (old Java, Pocket and console skins), 64×64, or an HD size such as 128×128.</summary>
    public static Skin Load(byte[] png, SkinModel? model = null, string? name = null)
    {
        var img = RgbaImage.FromPng(png);
        var notes = new List<string>();
        if (!IsSkinSize(img.Width, img.Height))
            throw new InvalidDataException($"A {img.Width} × {img.Height} image is not a skin. Skins are 64 × 64 (old ones 64 × 32), and Bedrock HD skins 128 × 128.");
        if (img.Height * 2 == img.Width)
        {
            img = SkinConvert.LegacyToModern(img, out bool hatCleared);
            notes.Add("This is an old 64 × 32 skin. It was upgraded to the 64 × 64 layout the way Java Edition does it: the left arm and leg mirror the right ones.");
            if (hatCleared) notes.Add("Its hat layer had no see-through pixels, so (as the game does) it was treated as an empty layer.");
            if (model == SkinModel.Slim) notes.Add("Old 64 × 32 skins are drawn for classic arms; check the arms after switching to slim.");
        }
        var detected = DetectModel(img);
        if (model == null) notes.Add(detected == SkinModel.Slim ? "Slim (3 pixel) arms detected." : "Classic (4 pixel) arms detected.");
        var skin = new Skin(img, model ?? detected) { Name = name ?? "My Skin" };
        skin.LoadNotes.AddRange(notes);
        return skin;
    }

    /// <summary>Slim skins leave the last two columns of the right arm's back face, and the end of its top row, empty.</summary>
    public static SkinModel DetectModel(RgbaImage img)
    {
        int s = img.Width / 64;
        return img.AllTransparent(54 * s, 20 * s, 2 * s, 12 * s) && img.AllTransparent(50 * s, 16 * s, 2 * s, 4 * s) ? SkinModel.Slim : SkinModel.Classic;
    }

    public void Replace(RgbaImage image)
    {
        if (image.Width != Image.Width || image.Height != Image.Height) throw new ArgumentException("The new image must have the same size.");
        Image = image;
    }

    /// <summary>Switches the arm width, moving the arm pixels so the art stays in place.</summary>
    public void SetModel(SkinModel model)
    {
        if (model == Model) return;
        Image = model == SkinModel.Slim ? SkinConvert.ClassicToSlim(Image) : SkinConvert.SlimToClassic(Image);
        Model = model;
    }

    /// <summary>Changes the resolution (64, 128, 256 or 512): pixels are repeated going up and averaged going down.</summary>
    public void SetSize(int width)
    {
        if (!IsSkinSize(width, width)) throw new ArgumentException("Skins are 64, 128, 256 or 512 pixels wide.");
        if (width == Image.Width) return;
        var img = Image;
        while (img.Width > width) img = img.Halved();
        if (img.Width < width) img = img.Scaled(width, width);
        Image = img;
    }

    public Skin Copy()
    {
        var c = new Skin(Image.Clone(), Model) { Name = Name };
        c.LoadNotes.AddRange(LoadNotes);
        return c;
    }

    /// <summary>The number of base-layer pixels that are not fully opaque.</summary>
    public int SeeThroughBasePixels()
    {
        int s = Scale, holes = 0;
        foreach (var box in SkinLayout.Boxes(Model).Where(b => !b.Overlay))
            foreach (var f in box.Faces())
                for (int y = f.V * s; y < (f.V + f.H) * s; y++)
                    for (int x = f.U * s; x < (f.U + f.W) * s; x++)
                        if (RgbaImage.A(Image[x, y]) < 255) holes++;
        return holes;
    }

    /// <summary>Things worth knowing before the skin is used.</summary>
    public List<string> Check()
    {
        var problems = new List<string>();
        int holes = SeeThroughBasePixels();
        if (holes > 0)
            problems.Add($"{holes} pixel{(holes == 1 ? " is" : "s are")} see-through on the base layer. Java Edition draws the base layer solid, so they show in the pixel's hidden colour (usually black). Paint them, or put see-through parts on the second layer.");
        return problems;
    }
}
