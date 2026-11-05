// MinecraftStudio.Core/Structures/StructureModel.cs: a structure as a 3D model — a Wavefront .obj with its .mtl — for
// Blender and other 3D programs (renders, thumbnails, 3D printing).
// Owns: StructureModel and ModelResult. Each block as its shape — full cubes; slabs, snow layers, carpets, chests, beds
// and the like at their height; stairs as two boxes; fences, walls, panes and bars as a post — with only the faces that
// can be seen (none against a neighbouring solid cube, none between two blocks of the same glass or water), in the map's
// block colours (grass and leaves tinted like plains), one material per colour, glass, ice and water see-through. Plants,
// torches, rails, doors and other small things are left out (counted in the result). y is up, x east, z south. The same
// faces (Faces) make the mesh of the 3D view (Mesh: whole sides of one colour merged into rectangles).
using System.Globalization;
using System.Numerics;
using System.Text;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;

namespace MinecraftStudio.Core.Structures;

/// <summary>What was written: vertices, faces, materials, and the blocks left out (no shape here).</summary>
public sealed record ModelResult(int Vertices, int Faces, int Materials, long BlocksLeftOut);

public static class StructureModel
{
    /// <summary>A box inside a block, in sixteenths (0–16).</summary>
    readonly record struct Box(int X0, int Y0, int Z0, int X1, int Y1, int Z1)
    {
        public bool Full => X0 == 0 && Y0 == 0 && Z0 == 0 && X1 == 16 && Y1 == 16 && Z1 == 16;
    }

    static readonly Box Cube = new(0, 0, 0, 16, 16, 16);

    /// <summary>The boxes a block is drawn as (none for air and what has no shape here).</summary>
    static Box[] Shape(BlockState b, JavaBlockData.VersionBlocks blocks)
    {
        if (b.IsAir) return [];
        string n = b.BaseName;
        if (n is "water" or "lava" or "bubble_column") return n == "bubble_column" ? [Cube] : [b.GetProperty("level") is null or "0" ? Cube : new Box(0, 0, 0, 16, 14, 16)];
        if (n.EndsWith("_slab", StringComparison.Ordinal))
            return [b.GetProperty("type") switch { "top" => new Box(0, 8, 0, 16, 16, 16), "double" => Cube, _ => new Box(0, 0, 0, 16, 8, 16) }];
        if (n.EndsWith("_stairs", StringComparison.Ordinal))
        {
            bool top = b.GetProperty("half") == "top";
            var basePart = top ? new Box(0, 8, 0, 16, 16, 16) : new Box(0, 0, 0, 16, 8, 16);
            int y0 = top ? 0 : 8, y1 = top ? 8 : 16;
            // The step stands on the side the stairs face.
            var step = b.GetProperty("facing") switch
            {
                "east" => new Box(8, y0, 0, 16, y1, 16),
                "west" => new Box(0, y0, 0, 8, y1, 16),
                "south" => new Box(0, y0, 8, 16, y1, 16),
                _ => new Box(0, y0, 0, 16, y1, 8),
            };
            return [basePart, step];
        }
        if (n == "snow" && int.TryParse(b.GetProperty("layers"), out int layers)) return [new Box(0, 0, 0, 16, Math.Clamp(layers, 1, 8) * 2, 16)];
        if (n.EndsWith("_carpet", StringComparison.Ordinal) || n is "moss_carpet" or "pale_moss_carpet") return [new Box(0, 0, 0, 16, 1, 16)];
        if (n.EndsWith("_fence", StringComparison.Ordinal) || n.EndsWith("_wall", StringComparison.Ordinal) || n.EndsWith("glass_pane", StringComparison.Ordinal) || n is "iron_bars" or "glass_pane")
            return [new Box(6, 0, 6, 10, 16, 10)];
        if (n is "chest" or "trapped_chest" or "ender_chest") return [new Box(1, 0, 1, 15, 14, 15)];
        if (n.EndsWith("_bed", StringComparison.Ordinal)) return [new Box(0, 0, 0, 16, 9, 16)];
        if (n is "farmland" or "dirt_path") return [new Box(0, 0, 0, 16, 15, 16)];
        if (n is "daylight_detector") return [new Box(0, 0, 0, 16, 6, 16)];
        if (n is "enchanting_table") return [new Box(0, 0, 0, 16, 12, 16)];
        if (n is "end_portal_frame") return [new Box(0, 0, 0, 16, 13, 16)];
        if (n is "cactus") return [new Box(1, 0, 1, 15, 16, 15)];
        if (n is "cake") return [new Box(1, 0, 1, 15, 8, 15)];
        if (n.EndsWith("_trapdoor", StringComparison.Ordinal) && b.GetProperty("open") != "true")
            return [b.GetProperty("half") == "top" ? new Box(0, 13, 0, 16, 16, 16) : new Box(0, 0, 0, 16, 3, 16)];
        if (n.EndsWith("_pressure_plate", StringComparison.Ordinal)) return [new Box(1, 0, 1, 15, 1, 15)];
        // Everything else that is solid is drawn as a cube; what has no collision (plants, torches, rails …) is left out.
        if (blocks.Blocks.TryGetValue(b.Name, out var def)) return def.Solid ? [Cube] : [];
        return [Cube];
    }

    /// <summary>See-through materials: glass, ice, water and the like.</summary>
    static bool SeeThrough(BlockState b) =>
        b.BaseName.Contains("glass", StringComparison.Ordinal) || b.BaseName is "ice" or "frosted_ice" or "water" or "bubble_column" or "slime_block" or "honey_block";

    /// <summary>
    /// A face that can be seen: its colour, which way it looks (0 east, 1 west, 2 up, 3 down, 4 south, 5 north), its four
    /// corners in sixteenths of a block (anticlockwise seen from outside), and whether it is a whole side of a block on
    /// the block grid (those of one colour side by side can be drawn as one).
    /// </summary>
    public readonly record struct Face(uint Color, int Dir, (int X, int Y, int Z) A, (int X, int Y, int Z) B, (int X, int Y, int Z) C, (int X, int Y, int Z) D, bool Whole);

    /// <summary>Every face of the structure that can be seen, in the map's colours; <paramref name="leftOut"/>: blocks with no shape here.</summary>
    public static List<Face> Faces(Structure s, out long leftOut)
    {
        var blocks = JavaBlockData.For(StructureJava.Newest);
        var shapes = new Dictionary<int, Box[]>();
        var colors = new Dictionary<int, uint>();
        Box[] ShapeOf(BlockState b) => shapes.TryGetValue(b.Id, out var v) ? v : shapes[b.Id] = Shape(b, blocks);
        bool OpaqueCube(int x, int y, int z)
        {
            if (!s.InRange(x, y, z)) return false;
            var b = s.Get(x, y, z);
            return !SeeThrough(b) && ShapeOf(b) is [{ Full: true }];
        }
        var faces = new List<Face>();
        leftOut = 0;
        for (int y = 0; y < s.SizeY; y++)
            for (int z = 0; z < s.SizeZ; z++)
                for (int x = 0; x < s.SizeX; x++)
                {
                    var b = s.Get(x, y, z);
                    if (b.IsAir) continue;
                    var boxes = ShapeOf(b);
                    if (boxes.Length == 0) { leftOut++; continue; }
                    uint color = Color(b, colors);
                    bool glass = SeeThrough(b);
                    // A face on the block's own side is hidden by a solid cube there, or by the same see-through block.
                    bool Hidden(int dx, int dy, int dz)
                    {
                        if (OpaqueCube(x + dx, y + dy, z + dz)) return true;
                        return glass && s.InRange(x + dx, y + dy, z + dz) && s.Get(x + dx, y + dy, z + dz) == b && boxes is [{ Full: true }];
                    }
                    foreach (var box in boxes)
                    {
                        int x0 = x * 16 + box.X0, y0 = y * 16 + box.Y0, z0 = z * 16 + box.Z0, x1 = x * 16 + box.X1, y1 = y * 16 + box.Y1, z1 = z * 16 + box.Z1;
                        bool whole = box.Full;
                        if (!(box.X1 == 16 && Hidden(1, 0, 0))) faces.Add(new(color, 0, (x1, y0, z1), (x1, y0, z0), (x1, y1, z0), (x1, y1, z1), whole));
                        if (!(box.X0 == 0 && Hidden(-1, 0, 0))) faces.Add(new(color, 1, (x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0), whole));
                        if (!(box.Y1 == 16 && Hidden(0, 1, 0))) faces.Add(new(color, 2, (x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0), whole));
                        if (!(box.Y0 == 0 && Hidden(0, -1, 0))) faces.Add(new(color, 3, (x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1), whole));
                        if (!(box.Z1 == 16 && Hidden(0, 0, 1))) faces.Add(new(color, 4, (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1), whole));
                        if (!(box.Z0 == 0 && Hidden(0, 0, -1))) faces.Add(new(color, 5, (x1, y0, z0), (x0, y0, z0), (x0, y1, z0), (x1, y1, z0), whole));
                    }
                }
        return faces;
    }

    /// <summary>
    /// Writes <paramref name="objPath"/> and, beside it, its .mtl. Returns what was written. The structure is only read.
    /// </summary>
    public static ModelResult Save(Structure s, string objPath)
    {
        var vertices = new Dictionary<(int, int, int), int>();
        var vertexList = new List<(int X, int Y, int Z)>();
        var faces = new Dictionary<uint, List<int[]>>();
        int V((int X, int Y, int Z) p)
        {
            if (vertices.TryGetValue(p, out int i)) return i;
            vertexList.Add(p);
            return vertices[p] = vertexList.Count;
        }
        var all = Faces(s, out long leftOut);
        foreach (var f in all)
        {
            if (!faces.TryGetValue(f.Color, out var list)) faces[f.Color] = list = [];
            list.Add([V(f.A), V(f.B), V(f.C), V(f.D)]);
        }

        string mtlName = Path.GetFileNameWithoutExtension(objPath) + ".mtl";
        var obj = new StringBuilder();
        obj.Append("# ").Append(AppInfo.Name).Append(": ").Append(s.Describe()).Append("\n");
        obj.Append("mtllib ").Append(mtlName).Append("\no structure\n");
        var inv = CultureInfo.InvariantCulture;
        foreach (var (vx, vy, vz) in vertexList)
            obj.Append("v ").Append((vx / 16.0).ToString("0.####", inv)).Append(' ').Append((vy / 16.0).ToString("0.####", inv)).Append(' ').Append((vz / 16.0).ToString("0.####", inv)).Append('\n');
        var mtl = new StringBuilder($"# {AppInfo.Name}: the block colours of {Path.GetFileName(objPath)}\n");
        int faceCount = 0;
        foreach (var (color, list) in faces.Where(f => f.Value.Count > 0))
        {
            string name = "c" + (color & 0xFFFFFF).ToString("X6", inv) + ((color >> 24) < 255 ? "t" : "");
            obj.Append("usemtl ").Append(name).Append('\n');
            foreach (var f in list) obj.Append("f ").Append(f[0]).Append(' ').Append(f[1]).Append(' ').Append(f[2]).Append(' ').Append(f[3]).Append('\n');
            faceCount += list.Count;
            double r = ((color >> 16) & 255) / 255.0, g = ((color >> 8) & 255) / 255.0, bl = (color & 255) / 255.0, d = (color >> 24) / 255.0;
            mtl.Append("newmtl ").Append(name).Append('\n')
               .Append("Kd ").Append(r.ToString("0.###", inv)).Append(' ').Append(g.ToString("0.###", inv)).Append(' ').Append(bl.ToString("0.###", inv)).Append('\n')
               .Append("d ").Append(d.ToString("0.##", inv)).Append("\nillum 1\n\n");
        }
        IO.SafeFile.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(objPath))!, mtlName), Encoding.UTF8.GetBytes(mtl.ToString()));
        IO.SafeFile.WriteAllBytes(objPath, Encoding.UTF8.GetBytes(obj.ToString()));
        return new ModelResult(vertexList.Count, faceCount, faces.Count(f => f.Value.Count > 0), leftOut);
    }

    /// <summary>
    /// The structure for the 3D view (ModelRenderer): its faces, with whole block sides of one colour side by side in one
    /// plane drawn as one rectangle, and the colours as a one-row picture the faces point into. Units are sixteenths of a
    /// block (as a mob model's), y up, x east, z south.
    /// </summary>
    public static (ModelMesh Mesh, RgbaImage Colors, long LeftOut) Mesh(Structure s)
    {
        var faces = Faces(s, out long leftOut);
        var palette = new Dictionary<uint, int>();
        int Slot(uint c) => palette.TryGetValue(c, out int i) ? i : palette[c] = palette.Count;
        var quads = new List<ModelQuad>();
        var normals = new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ };
        string[] names = ["east", "west", "up", "down", "south", "north"];
        void Add(uint color, int dir, Vector3 origin, Vector3 u, Vector3 v)
        {
            int slot = Slot(color);
            quads.Add(new ModelQuad("structure", 0, names[dir], origin, u, v, normals[dir], slot + 0.5f, 0.5f, 0, 0));
        }
        // Whole sides by plane and colour: cells (a, b) along the plane's two axes, merged into rectangles.
        var planes = new Dictionary<(int Dir, int At, uint Color), HashSet<(int A, int B)>>();
        foreach (var f in faces)
        {
            var lo = (X: Math.Min(Math.Min(f.A.X, f.B.X), Math.Min(f.C.X, f.D.X)), Y: Math.Min(Math.Min(f.A.Y, f.B.Y), Math.Min(f.C.Y, f.D.Y)), Z: Math.Min(Math.Min(f.A.Z, f.B.Z), Math.Min(f.C.Z, f.D.Z)));
            var hi = (X: Math.Max(Math.Max(f.A.X, f.B.X), Math.Max(f.C.X, f.D.X)), Y: Math.Max(Math.Max(f.A.Y, f.B.Y), Math.Max(f.C.Y, f.D.Y)), Z: Math.Max(Math.Max(f.A.Z, f.B.Z), Math.Max(f.C.Z, f.D.Z)));
            if (f.Whole)
            {
                var key = f.Dir switch { 0 or 1 => (f.Dir, lo.X, f.Color), 2 or 3 => (f.Dir, lo.Y, f.Color), _ => (f.Dir, lo.Z, f.Color) };
                var cell = f.Dir switch { 0 or 1 => (lo.Z >> 4, lo.Y >> 4), 2 or 3 => (lo.X >> 4, lo.Z >> 4), _ => (lo.X >> 4, lo.Y >> 4) };
                if (!planes.TryGetValue(key, out var set)) planes[key] = set = [];
                set.Add(cell);
                continue;
            }
            var o = new Vector3(lo.X, lo.Y, lo.Z);
            switch (f.Dir)
            {
                case 0 or 1: Add(f.Color, f.Dir, o, new Vector3(0, 0, hi.Z - lo.Z), new Vector3(0, hi.Y - lo.Y, 0)); break;
                case 2 or 3: Add(f.Color, f.Dir, o, new Vector3(hi.X - lo.X, 0, 0), new Vector3(0, 0, hi.Z - lo.Z)); break;
                default: Add(f.Color, f.Dir, o, new Vector3(hi.X - lo.X, 0, 0), new Vector3(0, hi.Y - lo.Y, 0)); break;
            }
        }
        foreach (var ((dir, at, color), cells) in planes)
            foreach (var (a, b, w, h) in Rectangles(cells))
            {
                // Back to sixteenths: a and b run along z, y (east/west faces), x, z (up/down) or x, y (south/north).
                var (origin, u, v) = dir switch
                {
                    0 or 1 => (new Vector3(at, b * 16, a * 16), new Vector3(0, 0, w * 16), new Vector3(0, h * 16, 0)),
                    2 or 3 => (new Vector3(a * 16, at, b * 16), new Vector3(w * 16, 0, 0), new Vector3(0, 0, h * 16)),
                    _ => (new Vector3(a * 16, b * 16, at), new Vector3(w * 16, 0, 0), new Vector3(0, h * 16, 0)),
                };
                Add(color, dir, origin, u, v);
            }
        var colors = new RgbaImage(Math.Max(1, palette.Count), 1);
        foreach (var (c, i) in palette) colors.Pixels[i] = c;
        var model = new EntityModel { Id = "structure", TextureWidth = colors.Width, TextureHeight = 1 };
        // Framed on what is drawn (the air above a piece of land is not part of it).
        var min = quads.Count == 0 ? Vector3.Zero : quads.Select(q => q.Origin).Aggregate(Vector3.Min);
        var max = quads.Count == 0 ? new Vector3(s.SizeX * 16, s.SizeY * 16, s.SizeZ * 16) : quads.Select(q => q.Origin + q.U + q.V).Aggregate(Vector3.Max);
        var mesh = new ModelMesh { Model = model, Quads = quads, Min = min, Max = max };
        return (mesh, colors, leftOut);
    }

    // Cells of one plane as few rectangles (x, y, width, height): each row's run, grown down while the rows below match.
    static IEnumerable<(int A, int B, int W, int H)> Rectangles(HashSet<(int A, int B)> cells)
    {
        var left = new HashSet<(int A, int B)>(cells);
        foreach (var (a0, b0) in cells.OrderBy(c => c.B).ThenBy(c => c.A))
        {
            if (!left.Contains((a0, b0))) continue;
            int w = 1;
            while (left.Contains((a0 + w, b0))) w++;
            int h = 1;
            while (Enumerable.Range(a0, w).All(a => left.Contains((a, b0 + h)))) h++;
            for (int b = b0; b < b0 + h; b++)
                for (int a = a0; a < a0 + w; a++) left.Remove((a, b));
            yield return (a0, b0, w, h);
        }
    }

    /// <summary>The block's colour (alpha 153 for see-through blocks), tinted like plains for grass, leaves and water.</summary>
    static uint Color(BlockState b, Dictionary<int, uint> cache)
    {
        if (cache.TryGetValue(b.Id, out uint c)) return c;
        var (color, tint) = BlockColors.Lookup(b);
        var t = Biomes.Default;
        c = tint switch { TintKind.Grass => t.Grass, TintKind.Foliage => t.Foliage, TintKind.Water => t.Water, _ => color } & 0xFFFFFF;
        c |= SeeThrough(b) ? 0x99000000u : 0xFF000000u;
        return cache[b.Id] = c;
    }
}
