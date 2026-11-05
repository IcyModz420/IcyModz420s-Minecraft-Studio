// McTests/RenderSpeed.cs: renderspeed: how long the 3D skin view takes to draw at sizes up to a large high-DPI
// window.
// Owns: RenderSpeed.
using System.Diagnostics;
using System.Numerics;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Skins;

/// <summary>renderspeed: how long the 3D skin view takes to draw at sizes up to a large high-DPI window.</summary>
static class RenderSpeed
{
    public static void Run(string[] args)
    {
        var skin = new Skin(new RgbaImage(64, 64), SkinModel.Classic);
        var hd = new Skin(new RgbaImage(128, 128), SkinModel.Slim);
        var r = new SkinRenderer();
        foreach (var (w, h) in new[] { (600, 600), (1000, 1000), (1100, 1600), (1600, 1600) })
            foreach (var s in new[] { skin, hd })
            {
                r.Render(s, OrbitCamera.Default, new SkinViewOptions(), w, h);
                var sw = Stopwatch.StartNew();
                const int n = 20;
                for (int i = 0; i < n; i++) r.Render(s, OrbitCamera.Default with { Yaw = i * 9 }, new SkinViewOptions(), w, h);
                double ms = sw.Elapsed.TotalMilliseconds / n;
                Program.Info($"{w} x {h}, {s.Image.Width}px skin: {ms:0.0} ms a frame");
                Program.Check(ms < 80, $"{w} x {h} renders in {ms:0} ms");
            }
        // Zoomed right in: the head fills the view.
        r.Render(skin, new OrbitCamera(0, 0, 20, new Vector3(0, 28, 0)), new SkinViewOptions(), 1600, 1600);
        var z = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) r.Render(skin, new OrbitCamera(i, 0, 20, new Vector3(0, 28, 0)), new SkinViewOptions(), 1600, 1600);
        Program.Info($"zoomed in, 1600 x 1600: {z.Elapsed.TotalMilliseconds / 10:0.0} ms a frame");
    }
}
