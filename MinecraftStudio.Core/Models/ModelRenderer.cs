// MinecraftStudio.Core/Models/ModelRenderer.cs: draws a mob's model with its texture in software, and tells which
// texture pixel is under each screen pixel (what painting on the 3D model needs).
// Owns: ModelViewOptions and ModelRenderer — every face ray-cast for the screen pixels it covers; see-through texture
// pixels let the faces behind show (painting picks the nearest pixel that shows, else the nearest face at all, so empty
// parts of a face can be painted too); the hovered pixel marked wherever the model uses it; a pixel grid up close.
using System.Numerics;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Skins;

namespace MinecraftStudio.Core.Models;

/// <summary>What the 3D model view shows.</summary>
public sealed class ModelViewOptions
{
    public bool ShowGrid { get; set; } = true;
    /// <summary>Image pixel index (y * width + x) drawn highlighted, or -1.</summary>
    public int Hover { get; set; } = -1;
    /// <summary>A bone drawn tinted (the one being animated or edited), or null.</summary>
    public string? Highlight { get; set; }
}

/// <summary>Draws a model in software; see <see cref="Pick"/> for painting.</summary>
public sealed class ModelRenderer
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int[] Frame { get; private set; } = [];
    int[] _visible = [], _any = [], _quad = [];
    IReadOnlyList<ModelQuad> _quads = [];
    float[] _depth = [], _visibleDepth = [], _anyDepth = [];
    int _texWidth = 1;
    OrbitCamera _camera = OrbitCamera.Default;

    /// <summary>The focal length in screen pixels (a vertical field of view of 35°).</summary>
    public float Focal => Height / 2f / MathF.Tan(17.5f * MathF.PI / 180);

    /// <summary>
    /// The camera that shows a whole model from the front-left, a little from above, in a view of the given width ÷
    /// height (a view narrower than it is tall sees less to the sides, so the camera stands further back).
    /// </summary>
    public static OrbitCamera CameraFor(ModelMesh mesh, float aspect = 1)
    {
        var size = mesh.Max - mesh.Min;
        float radius = Math.Max(4, size.Length() / 2);
        float fit = MathF.Tan(17.5f * MathF.PI / 180) * Math.Clamp(aspect, 0.25f, 1f);
        // Mobs face north (−z): yaw 180 looks at the front.
        return new OrbitCamera(180 + 32, 16, radius / fit * 1.1f, mesh.Centre);
    }

    public void Render(ModelMesh mesh, RgbaImage texture, OrbitCamera camera, ModelViewOptions options, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width != Width || height != Height)
        {
            Width = width;
            Height = height;
            int n = width * height;
            Frame = new int[n];
            _visible = new int[n];
            _any = new int[n];
            _quad = new int[n];
            _depth = new float[n];
            _visibleDepth = new float[n];
            _anyDepth = new float[n];
        }
        _camera = camera;
        _texWidth = texture.Width;
        for (int y = 0; y < height; y++)
        {
            int k = 46 - 18 * y / height;
            Array.Fill(Frame, unchecked((int)RgbaImage.Argb(255, k, k, k + 3)), y * width, width);
        }
        Array.Fill(_depth, float.MaxValue);
        Array.Fill(_visibleDepth, float.MaxValue);
        Array.Fill(_anyDepth, float.MaxValue);
        Array.Fill(_visible, -1);
        Array.Fill(_any, -1);
        Array.Fill(_quad, -1);
        _quads = mesh.Quads;
        var basis = camera.Basis();
        float sx = texture.Width / (float)mesh.Model.TextureWidth, sy = texture.Height / (float)mesh.Model.TextureHeight;
        // Solid faces first, so faces with see-through or half-see-through pixels blend over what is behind them.
        for (int i = 0; i < mesh.Quads.Count; i++) DrawFace(mesh.Quads[i], i, texture, sx, sy, basis, options, pass: 0);
        for (int i = 0; i < mesh.Quads.Count; i++) DrawFace(mesh.Quads[i], i, texture, sx, sy, basis, options, pass: 1);
    }

    /// <summary>The texture pixel to paint at a screen pixel: the nearest one that shows there, else the nearest face's.</summary>
    public (int X, int Y)? Pick(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return null;
        int i = _visible[y * Width + x];
        if (i < 0) i = _any[y * Width + x];
        return i < 0 ? null : (i % _texWidth, i / _texWidth);
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

    /// <summary>The face that shows at a screen pixel (its bone is what a click there means), or null.</summary>
    public ModelQuad? QuadAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return null;
        int i = _quad[y * Width + x];
        return i >= 0 && i < _quads.Count ? _quads[i] : null;
    }

    /// <summary>The texture pixel that shows at a screen pixel (for the eyedropper), or null.</summary>
    public (int X, int Y)? Seen(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return null;
        int i = _visible[y * Width + x];
        return i < 0 ? null : (i % _texWidth, i / _texWidth);
    }

    void DrawFace(ModelQuad q, int qi, RgbaImage tex, float sx, float sy, (Vector3 C, Vector3 F, Vector3 R, Vector3 U) cam, ModelViewOptions options, int pass)
    {
        bool tinted = options.Highlight != null && q.Bone.Equals(options.Highlight, StringComparison.OrdinalIgnoreCase);
        var o = q.Origin;
        var ux = q.U;
        var vy = q.V;
        var n = Vector3.Cross(vy, ux);
        if (n.LengthSquared() < 1e-12f) return;
        n = Vector3.Normalize(n);
        // The face's outside is where its normal points (turned with the cube).
        if (Vector3.Dot(n, q.Normal) < 0) n = -n;
        if (Vector3.Dot(n, cam.C - o) <= 0) return;
        int w = Width, h = Height;
        float focal = Focal;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in new[] { o, o + ux, o + vy, o + ux + vy })
        {
            var d = p - cam.C;
            float z = Vector3.Dot(d, cam.F);
            if (z <= 0.1f) return;
            float px = w / 2f + Vector3.Dot(d, cam.R) / z * focal, py = h / 2f - Vector3.Dot(d, cam.U) / z * focal;
            minX = Math.Min(minX, px); maxX = Math.Max(maxX, px); minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
        }
        int x0 = Math.Max(0, (int)MathF.Floor(minX)), x1 = Math.Min(w - 1, (int)MathF.Ceiling(maxX));
        int y0 = Math.Max(0, (int)MathF.Floor(minY)), y1 = Math.Min(h - 1, (int)MathF.Ceiling(maxY));
        if (x0 > x1 || y0 > y1) return;
        float uu = ux.LengthSquared(), vv = vy.LengthSquared();
        if (uu < 1e-12f || vv < 1e-12f) return;
        float nOC = Vector3.Dot(n, o - cam.C);
        float facing = -Vector3.Dot(n, cam.F);
        float shade = Math.Clamp(0.62f + 0.38f * Math.Max(0, facing) + (n.Y > 0.5f ? 0.05f : n.Y < -0.5f ? -0.1f : 0), 0.4f, 1f);
        int tw = tex.Width, th = tex.Height;
        var pixels = tex.Pixels;
        float texelWorld = MathF.Sqrt(uu) / Math.Max(0.01f, Math.Abs(q.TexW) * sx);
        bool grid = options.ShowGrid;
        int hover = options.Hover;
        var frame = Frame;
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
                if (a < -Eps || a > 1 + Eps || b < -Eps || b > 1 + Eps) continue;
                var tc = q.TexAt(Math.Clamp(a, 0, 0.99999f), Math.Clamp(b, 0, 0.99999f));
                int px = (int)MathF.Floor(tc.X * sx), py = (int)MathF.Floor(tc.Y * sy);
                if (px < 0 || py < 0 || px >= tw || py >= th) continue;
                int index = py * tw + px, p = rowStart + x;
                uint c = pixels[index];
                int alpha = RgbaImage.A(c);
                bool hovered = index == hover;
                if (pass == 0)
                {
                    // Solid pixels, nearest first; every face is a candidate for painting.
                    if (t < _anyDepth[p]) { _anyDepth[p] = t; _any[p] = index; }
                    if (alpha > 0 && t < _visibleDepth[p]) { _visibleDepth[p] = t; _visible[p] = index; _quad[p] = qi; }
                    if (alpha < 250 || t >= _depth[p]) continue;
                }
                else
                {
                    // Half see-through pixels over what is behind them; an empty pixel only as the hover mark.
                    if (alpha >= 250 || t >= _depth[p]) continue;
                    if (alpha == 0 && !hovered) continue;
                }
                if (alpha == 0) { c = 0xFFB4B4B4u; alpha = 160; }
                float sh = shade;
                if (grid)
                {
                    float screen = texelWorld * focal / t;
                    if (screen >= 6)
                    {
                        float fa = tc.X * sx - MathF.Floor(tc.X * sx), fb = tc.Y * sy - MathF.Floor(tc.Y * sy), e = 0.7f / screen;
                        if (fa < e || fa > 1 - e || fb < e || fb > 1 - e) sh *= 0.82f;
                    }
                }
                int r = (int)(RgbaImage.R(c) * sh), g = (int)(RgbaImage.G(c) * sh), bl = (int)(RgbaImage.B(c) * sh);
                if (hovered)
                {
                    int k = r * 3 + g * 6 + bl > 1400 ? 0 : 255;
                    r = (r + k) / 2; g = (g + k) / 2; bl = (bl + k) / 2;
                }
                if (tinted) { r = (r * 3 + 110) / 4; g = (g * 3 + 200) / 4; bl = (bl * 3 + 255) / 4; }
                if (alpha >= 250)
                {
                    frame[p] = unchecked((int)RgbaImage.Argb(255, r, g, bl));
                    _depth[p] = t;
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
