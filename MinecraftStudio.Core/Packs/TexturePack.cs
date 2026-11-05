// MinecraftStudio.Core/Packs/TexturePack.cs: How a texture moves through its frames: the Java .png.mcmeta
// "animation" section, which can say everything a Bedrock flipbook can (ticks per frame, frame order,
// blending).
// Owns: TextureAnimation, PackTexture, PackEdition, TexturePack, VersionOrder.
using MinecraftStudio.Core.Imaging;

namespace MinecraftStudio.Core.Packs;

/// <summary>
/// How a texture moves through its frames: the Java .png.mcmeta "animation" section, which can say everything a Bedrock
/// flipbook can (ticks per frame, frame order, blending). Frames are stacked top to bottom in the image.
/// </summary>
public sealed class TextureAnimation
{
    /// <summary>Game ticks each frame shows (20 per second).</summary>
    public int FrameTime { get; set; } = 1;
    /// <summary>Blend smoothly from one frame to the next.</summary>
    public bool Interpolate { get; set; }
    /// <summary>The order of frames, each with its own time (null: the default time); null means every frame in order.</summary>
    public List<(int Index, int? Time)>? Frames { get; set; }
    /// <summary>Frame size when frames are not square (newer Java versions); null means square frames as wide as the image.</summary>
    public int? FrameWidth { get; set; }
    public int? FrameHeight { get; set; }

    public TextureAnimation Clone() => new()
    {
        FrameTime = FrameTime, Interpolate = Interpolate, FrameWidth = FrameWidth, FrameHeight = FrameHeight,
        Frames = Frames?.ToList(),
    };

    /// <summary>The size of one frame of an image with this animation.</summary>
    public (int W, int H) FrameSize(RgbaImage image)
    {
        int w = FrameWidth ?? image.Width, h = FrameHeight ?? (FrameWidth ?? image.Width);
        return (Math.Clamp(w, 1, image.Width), Math.Clamp(h, 1, image.Height));
    }

    public int FrameCount(RgbaImage image)
    {
        var (w, h) = FrameSize(image);
        return Math.Max(1, image.Width / w * (image.Height / h));
    }
}

/// <summary>One texture of a pack: its pixels (all frames, for animated ones), under the name every edition shares.</summary>
public sealed class PackTexture
{
    /// <summary>The shared name (<see cref="TextureNames"/>): the newest Java name, or "bedrock:…" for Bedrock-only textures.</summary>
    public required string Key { get; init; }
    public required RgbaImage Image { get; set; }
    public TextureAnimation? Animation { get; set; }
    /// <summary>Where it was in the pack it was read from.</summary>
    public string SourcePath { get; init; } = "";
    /// <summary>
    /// The texture's own .png.mcmeta as read (Java), kept so settings this program does not edit (blur, clamp …)
    /// survive; its animation section is replaced by <see cref="Animation"/> when written.
    /// </summary>
    public string? Mcmeta { get; set; }

    /// <summary>The first frame (the whole image when not animated).</summary>
    public RgbaImage FirstFrame()
    {
        if (Animation == null) return Image;
        var (w, h) = Animation.FrameSize(Image);
        return w == Image.Width && h == Image.Height ? Image : Image.Crop(0, 0, w, h);
    }

    public PackTexture Clone() => new() { Key = Key, Image = Image.Clone(), Animation = Animation?.Clone(), SourcePath = SourcePath, Mcmeta = Mcmeta };
}

/// <summary>Where a pack came from (and how it is saved back unless converted).</summary>
public enum PackEdition { Java, Bedrock }

/// <summary>A texture pack in memory: its textures by shared name, plus what it was and what was left aside when it was read.</summary>
public sealed class TexturePack
{
    public string Name { get; set; } = "My Pack";
    public string Description { get; set; } = "";
    public RgbaImage? Icon { get; set; }
    public PackEdition Edition { get; set; } = PackEdition.Java;
    /// <summary>For Java packs: the release line the pack was made for (from its pack format), if known.</summary>
    public JavaRelease? JavaRelease { get; set; }
    public Dictionary<string, PackTexture> Textures { get; } = new(StringComparer.Ordinal);
    /// <summary>Files that are not block or item textures (sounds, models, texts …), by path in the pack.</summary>
    public Dictionary<string, byte[]> OtherFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>What happened while reading (unknown textures, damaged files …).</summary>
    public List<string> Notes { get; } = [];
    /// <summary>
    /// The pack's own pack.mcmeta (Java) or manifest.json (Bedrock) as read. Saving the pack for the same edition and
    /// version keeps it (overlays, languages, the pack's id …) instead of writing a new one.
    /// </summary>
    public byte[]? OriginalMetadata { get; set; }

    public TexturePack CloneShallow()
    {
        var p = new TexturePack { Name = Name, Description = Description, Icon = Icon?.Clone(), Edition = Edition, JavaRelease = JavaRelease, OriginalMetadata = OriginalMetadata };
        foreach (var (k, t) in Textures) p.Textures[k] = t.Clone();
        foreach (var (k, v) in OtherFiles) p.OtherFiles[k] = v;
        p.Notes.AddRange(Notes);
        return p;
    }
}

/// <summary>Orders game version names: Alpha ("a1.2.6"), Beta ("b1.7.3"), then releases by number ("1.21.11", "26.1").</summary>
public static class VersionOrder
{
    public static int[]? Key(string version)
    {
        string v = version.Trim();
        int era = 2;
        if (v.StartsWith("Alpha ", StringComparison.OrdinalIgnoreCase)) { era = 0; v = v[6..]; }
        else if (v.StartsWith("Beta ", StringComparison.OrdinalIgnoreCase)) { era = 1; v = v[5..]; }
        else if (v.Length > 1 && (v[0] == 'a' || v[0] == 'b') && char.IsDigit(v[1])) { era = v[0] == 'a' ? 0 : 1; v = v[1..]; }
        var parts = v.Split('.');
        var key = new List<int> { era };
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out int n)) return null;
            key.Add(n);
        }
        return key.Count >= 2 ? key.ToArray() : null;
    }

    public static int Compare(string a, string b)
    {
        var ka = Key(a) ?? throw new ArgumentException($"Not a version: {a}");
        var kb = Key(b) ?? throw new ArgumentException($"Not a version: {b}");
        for (int i = 0; i < Math.Max(ka.Length, kb.Length); i++)
        {
            int x = i < ka.Length ? ka[i] : 0, y = i < kb.Length ? kb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    /// <summary>Whether the version ranges [from1, to1] and [from2, to2] share a version (empty bounds are open).</summary>
    public static bool Overlap(string? from1, string? to1, string from2, string to2) =>
        (string.IsNullOrEmpty(from1) || Compare(from1, to2) <= 0) && (string.IsNullOrEmpty(to1) || Compare(to1, from2) >= 0);
}
