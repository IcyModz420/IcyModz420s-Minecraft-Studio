// MinecraftStudio.Core/Skins/SkinRenderer.cs: A camera turning around a point: yaw and pitch in degrees,
// distance in skin pixels.
// Owns: OrbitCamera, SkinViewOptions, SkinRenderer.
using System.Numerics;
using MinecraftStudio.Core.Imaging;

namespace MinecraftStudio.Core.Skins;

/// <summary>A camera turning around a point: yaw and pitch in degrees, distance in skin pixels.</summary>
public readonly record struct OrbitCamera(float Yaw, float Pitch, float Distance, Vector3 Target)
{
    public static OrbitCamera Default => new(28, 10, 66, new Vector3(0, 16, 0));

    /// <summary>Position, forward, right and up of the camera.</summary>
    public (Vector3 Position, Vector3 Forward, Vector3 Right, Vector3 Up) Basis()
    {
        float yaw = Yaw * MathF.PI / 180, pitch = Pitch * MathF.PI / 180;
        var dir = new Vector3(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw));
        var c = Target + dir * Distance;
        var f = Vector3.Normalize(Target - c);
        // Straight up or down: keep a sensible right-hand direction.
        var r = MathF.Abs(f.Y) > 0.9999f ? new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw)) : Vector3.Normalize(Vector3.Cross(f, Vector3.UnitY));
        var u = Vector3.Cross(r, f);
        return (c, f, r, u);
    }
}

/// <summary>What the 3D view shows and which layer it paints.</summary>
public sealed class SkinViewOptions
{
    /// <summary>Picking finds second-layer pixels (also see-through ones, so they can be painted); otherwise base pixels.</summary>
    public bool PaintOverlay { get; set; }
    public bool ShowOverlay { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    public IReadOnlySet<string> HiddenParts { get; set; } = new HashSet<string>();
    /// <summary>Image pixel indexes (y * size + x) drawn highlighted, or -1.</summary>
    public int Hover { get; set; } = -1;
    public int HoverMirror { get; set; } = -1;
}

/// <summary>
/// Draws a skin on the player model in software. Each face is ray-cast for every screen pixel it covers, which gives
/// both the picture and, per screen pixel, the skin pixel there (what painting on the model needs).
/// </summary>
public sealed class SkinRenderer
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    /// <summary>ARGB per screen pixel.</summary>
    public int[] Frame { get; private set; } = [];
    /// <summary>The image pixel index (y * size + x) of the paintable skin pixel per screen pixel, or -1.</summary>
    public int[] Pick { get; private set; } = [];
    /// <summary>The image pixel index of the skin pixel seen in front per screen pixel (either layer), or -1.</summary>
    public int[] Visible { get; private set; } = [];
    float[] _depth = [], _pickDepth = [], _visibleDepth = [];
    int _skinSize = 64;
    OrbitCamera _camera = OrbitCamera.Default;

    /// <summary>The focal length in screen pixels (a vertical field of view of 35°).</summary>
    public float Focal => Height / 2f / MathF.Tan(17.5f * MathF.PI / 180);

    public void Render(Skin skin, OrbitCamera camera, SkinViewOptions options, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width != Width || height != Height)
        {
            Width = width;
            Height = height;
            Frame = new int[width * height];
            Pick = new int[width * height];
            Visible = new int[width * height];
            _depth = new float[width * height];
            _pickDepth = new float[width * height];
            _visibleDepth = new float[width * height];
        }
        _camera = camera;
        _skinSize = skin.Image.Width;
        // A soft vertical gradient behind the model.
        for (int y = 0; y < height; y++)
        {
            int k = 46 - 18 * y / height;
            Array.Fill(Frame, unchecked((int)RgbaImage.Argb(255, k, k, k + 3)), y * width, width);
        }
        Array.Fill(_depth, float.MaxValue);
        Array.Fill(_pickDepth, float.MaxValue);
        Array.Fill(_visibleDepth, float.MaxValue);
        Array.Fill(Pick, -1);
        Array.Fill(Visible, -1);
        var basis = camera.Basis();
        // The base layer first, so the second layer can be blended over it.
        foreach (var box in SkinLayout.Boxes(skin.Model).OrderBy(b => b.Overlay))
        {
            if (options.HiddenParts.Contains(box.Part)) continue;
            if (box.Overlay && !options.ShowOverlay) continue;
            bool pickable = box.Overlay == options.PaintOverlay;
            // While the base is painted, the second layer is drawn faintly so the base shows through it.
            float alphaScale = box.Overlay && !options.PaintOverlay ? 0.4f : 1f;
            foreach (var face in box.Faces()) DrawFace(face, skin, basis, options, pickable, alphaScale);
        }
    }

    /// <summary>The skin pixel under a screen pixel, or null.</summary>
    public (int X, int Y)? PickAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return null;
        int index = Pick[y * Width + x];
        return index < 0 ? null : (index % _skinSize, index / _skinSize);
    }

    /// <summary>The skin pixel seen at a screen pixel (the second layer where it has colour, else the base), or null.</summary>
    public (int X, int Y)? VisibleAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return null;
        int index = Visible[y * Width + x];
        return index < 0 ? null : (index % _skinSize, index / _skinSize);
    }

    /// <summary>Where a point of the model lands on the screen with the last camera, or null when behind it.</summary>
    public (float X, float Y)? Project(Vector3 p)
    {
        var (c, f, r, u) = _camera.Basis();
        var d = p - c;
        float z = Vector3.Dot(d, f);
        if (z <= 0.1f) return null;
        return (Width / 2f + Vector3.Dot(d, r) / z * Focal, Height / 2f - Vector3.Dot(d, u) / z * Focal);
    }

    static Vector3 V((float X, float Y, float Z) p) => new(p.X, p.Y, p.Z);

    void DrawFace(SkinFace face, Skin skin, (Vector3 C, Vector3 F, Vector3 R, Vector3 U) cam, SkinViewOptions options, bool pickable, float alphaScale)
    {
        int s = skin.Scale, size = skin.Image.Width, w = Width, h = Height;
        float focal = Focal;
        var o = V(SkinLayout.TexelQuad(face, 0, 0)[0]);
        var ux = V(SkinLayout.TexelQuad(face, face.W - 1, 0)[1]) - o;
        var vy = V(SkinLayout.TexelQuad(face, 0, face.H - 1)[3]) - o;
        var n = V(SkinLayout.Normal(face.Side));
        if (Vector3.Dot(n, cam.C - o) <= 0) return;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in new[] { o, o + ux, o + vy, o + ux + vy })
        {
            var d = p - cam.C;
            float z = Vector3.Dot(d, cam.F);
            if (z <= 0.1f) return;
            float sx = w / 2f + Vector3.Dot(d, cam.R) / z * focal, sy = h / 2f - Vector3.Dot(d, cam.U) / z * focal;
            minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx); minY = Math.Min(minY, sy); maxY = Math.Max(maxY, sy);
        }
        int x0 = Math.Max(0, (int)MathF.Floor(minX)), x1 = Math.Min(w - 1, (int)MathF.Ceiling(maxX));
        int y0 = Math.Max(0, (int)MathF.Floor(minY)), y1 = Math.Min(h - 1, (int)MathF.Ceiling(maxY));
        if (x0 > x1 || y0 > y1) return;

        int wp = face.W * s, hp = face.H * s, baseX = face.U * s, baseY = face.V * s;
        float uu = ux.LengthSquared(), vv = vy.LengthSquared();
        float nOC = Vector3.Dot(n, o - cam.C);
        float facing = -Vector3.Dot(n, cam.F);
        float shade = Math.Clamp(0.62f + 0.38f * Math.Max(0, facing) + (n.Y > 0 ? 0.05f : n.Y < 0 ? -0.1f : 0), 0.4f, 1f);
        float pixelWorld = MathF.Sqrt(uu) / wp;
        bool grid = options.ShowGrid, overlay = face.Box.Overlay;
        int hover = options.Hover, hoverMirror = options.HoverMirror;
        var pixels = skin.Image.Pixels;
        var frame = Frame;
        var depth = _depth;
        var pickDepth = _pickDepth;
        var pick = Pick;
        var visibleDepth = _visibleDepth;
        var visible = Visible;
        const float Eps = 1e-4f;

        void Row(int y)
        {
            int rowStart = y * w;
            var dy = cam.F + (h / 2f - y - 0.5f) / focal * cam.U;
            for (int x = x0; x <= x1; x++)
            {
                var d = dy + (x + 0.5f - w / 2f) / focal * cam.R;
                float denom = Vector3.Dot(n, d);
                if (denom >= -1e-6f) continue;
                float t = nOC / denom;
                if (t <= 0) continue;
                var hit = cam.C + t * d - o;
                float a = Vector3.Dot(hit, ux) / uu, b = Vector3.Dot(hit, vy) / vv;
                if (a < -Eps || a >= 1 + Eps || b < -Eps || b >= 1 + Eps) continue;
                int col = Math.Clamp((int)(a * wp), 0, wp - 1), row = Math.Clamp((int)(b * hp), 0, hp - 1);
                int index = (baseY + row) * size + baseX + col;
                int p = rowStart + x;
                if (pickable && t < pickDepth[p]) { pickDepth[p] = t; pick[p] = index; }
                uint c = pixels[index];
                // What the eye sees here: the nearest pixel that has any colour (the base layer always has).
                if ((!overlay || RgbaImage.A(c) > 0) && t < visibleDepth[p]) { visibleDepth[p] = t; visible[p] = index; }
                if (t >= depth[p]) continue;
                int alpha = (int)(RgbaImage.A(c) * alphaScale);
                // The base layer is drawn solid, as Java Edition does.
                if (!overlay) alpha = 255;
                bool hovered = index == hover || index == hoverMirror;
                // An empty second-layer pixel is only drawn as the hover mark (where the next dot would go).
                if (alpha == 0 && !(hovered && pickable)) continue;
                if (alpha == 0) c = 0xFFB4B4B4u;
                float sh = shade;
                if (grid)
                {
                    float px = pixelWorld * focal / t;
                    if (px >= 5)
                    {
                        float e = 0.7f / px, fa = a * wp - col, fb = b * hp - row;
                        if (fa < e || fa > 1 - e || fb < e || fb > 1 - e) sh *= 0.8f;
                    }
                }
                int r = (int)(RgbaImage.R(c) * sh), g = (int)(RgbaImage.G(c) * sh), bl = (int)(RgbaImage.B(c) * sh);
                if (hovered)
                {
                    // Lighten dark pixels and darken light ones so the hover mark always shows.
                    int k = r * 3 + g * 6 + bl > 1400 ? 0 : 255;
                    r = (r + k) / 2; g = (g + k) / 2; bl = (bl + k) / 2;
                    alpha = Math.Max(alpha, 200);
                }
                if (alpha >= 250)
                {
                    frame[p] = unchecked((int)RgbaImage.Argb(255, r, g, bl));
                    depth[p] = t;
                }
                else
                {
                    uint under = unchecked((uint)frame[p]);
                    int ia = 255 - alpha;
                    frame[p] = unchecked((int)RgbaImage.Argb(255, (r * alpha + RgbaImage.R(under) * ia) / 255, (g * alpha + RgbaImage.G(under) * ia) / 255, (bl * alpha + RgbaImage.B(under) * ia) / 255));
                }
            }
        }

        if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 20000) Parallel.For(y0, y1 + 1, Row);
        else for (int y = y0; y <= y1; y++) Row(y);
    }
}
