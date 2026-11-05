// McTests/ModelTests.cs: "model" — a structure as a 3D model (.obj + .mtl).
// Owns: ModelTests — one cube (8 corners, 6 faces), a 2×2×2 block of cubes (26 corners, 24 faces: none inside), a hollow
// 3×3×3 (the 6 faces round the hole too), two glass blocks (the face between them hidden; a see-through material), stone
// beside glass (its face toward the glass shown, the glass's toward the stone hidden), a slab half high, a flower left out;
// every face turned outwards; the .obj's faces all name real corners and every material is in the .mtl; a real building
// (the Java 1.16.5 sample's spawn area) written and read back. The 3D view's mesh (StructureModel.Mesh) covers exactly
// the faces the model has (the same area per colour and side) in fewer rectangles, and seen from the south it shows the
// east on the right and the top up (not mirrored); the picture is written to look at.
using System.Globalization;
using MinecraftStudio.Core.Conversion;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Skins;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Core.World;

static class ModelTests
{
    public static void Run(string[] args)
    {
        string dir = Path.Combine(TestPaths.Output, "model");
        Directory.CreateDirectory(dir);
        try
        {
            BlockState B(string s) => BlockState.Parse(s);
            Structure Of(int sx, int sy, int sz, Action<Structure> fill) { var s = new Structure(sx, sy, sz); fill(s); return s; }
            (ModelResult R, Obj O) Save(Structure s, string name)
            {
                string path = Path.Combine(dir, name + ".obj");
                var r = StructureModel.Save(s, path);
                return (r, Obj.Read(path));
            }

            var one = Save(Of(1, 1, 1, s => s.Set(0, 0, 0, B("minecraft:stone"))), "one");
            Program.Check(one.R is { Vertices: 8, Faces: 6, Materials: 1 } && one.O.Outward(), $"one cube: 8 corners, 6 faces, one colour, every face turned outwards ({one.R})");
            var cube = Save(Of(2, 2, 2, s => { for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++) s.Set(x, y, z, B("minecraft:stone")); }), "cube");
            Program.Check(cube.R is { Vertices: 26, Faces: 24 } && cube.O.Outward(), $"2×2×2 cubes: 26 corners and 24 outside faces, none inside ({cube.R})");
            var hollow = Save(Of(3, 3, 3, s => { for (int y = 0; y < 3; y++) for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++) if (!(x == 1 && y == 1 && z == 1)) s.Set(x, y, z, B("minecraft:stone")); }), "hollow");
            Program.Check(hollow.R.Faces == 54 + 6, $"a hollow 3×3×3: the 54 outside faces and the 6 round the hole ({hollow.R.Faces})");
            var glass = Save(Of(2, 1, 1, s => { s.Set(0, 0, 0, B("minecraft:glass")); s.Set(1, 0, 0, B("minecraft:glass")); }), "glass");
            Program.Check(glass.R.Faces == 10 && glass.O.SeeThroughMaterials == 1, $"two glass blocks: the face between them hidden, a see-through material ({glass.R.Faces} faces)");
            var mixed = Save(Of(2, 1, 1, s => { s.Set(0, 0, 0, B("minecraft:stone")); s.Set(1, 0, 0, B("minecraft:glass")); }), "mixed");
            Program.Check(mixed.R.Faces == 11 && mixed.R.Materials == 2, $"stone beside glass: the stone's face toward the glass shown, the glass's toward the stone hidden ({mixed.R.Faces} faces)");
            var slab = Save(Of(1, 1, 1, s => s.Set(0, 0, 0, B("minecraft:stone_slab[type=bottom,waterlogged=false]"))), "slab");
            Program.Check(slab.R.Faces == 6 && slab.O.MaxY is > 0.49 and < 0.51 && slab.O.Outward(), $"a bottom slab: a box half a block high ({slab.O.MaxY})");
            var flower = Save(Of(1, 1, 1, s => s.Set(0, 0, 0, B("minecraft:poppy"))), "flower");
            Program.Check(flower.R is { Faces: 0, BlocksLeftOut: 1 }, "a flower is left out (and counted)");
            var stairs = Save(Of(1, 1, 1, s => s.Set(0, 0, 0, B("minecraft:oak_stairs[facing=east,half=bottom,shape=straight,waterlogged=false]"))), "stairs");
            Program.Check(stairs.R.Faces == 12 && stairs.O.Outward(), $"stairs: two boxes, the step on the side they face ({stairs.R.Faces} faces)");

            string java = Path.Combine(TestPaths.Data, "amulet", "java", "vanilla", "1_16_5_dimension");
            if (Directory.Exists(java))
            {
                using var w = WorldLoader.Open(java);
                var info = WorldInfoReader.Read(w);
                var dim = w.Dimensions.First(d => CanonicalReader.DimensionId(d) == "minecraft:overworld");
                var s = StructureClip.Extract(w, dim, info.SpawnX - 16, 50, info.SpawnZ - 16, info.SpawnX + 15, 90, info.SpawnZ + 15);
                var real = Save(s, "spawn");
                Program.Check(real.R.Faces > 1000 && real.O.FacesNameRealCorners && real.O.MaterialsDefined && real.O.Faces == real.R.Faces,
                    $"the Java 1.16.5 sample's spawn area as a model: {real.R.Faces:N0} faces, {real.R.Vertices:N0} corners, {real.R.Materials} colours, {real.R.BlocksLeftOut:N0} small blocks left out; the files read back whole");
                Mesh(s, "the spawn area", Path.Combine(TestPaths.Output, "model-3d"));
            }
            Mesh(Of(3, 2, 3, s =>
            {
                for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++) s.Set(x, 0, z, B("minecraft:stone"));
                s.Set(2, 1, 1, B("minecraft:red_wool"));   // east
                s.Set(1, 1, 2, B("minecraft:blue_wool"));  // south
                s.Set(0, 1, 0, B("minecraft:glass"));
                s.Set(1, 1, 1, B("minecraft:oak_slab[type=bottom,waterlogged=false]"));
            }), "a little test build", Path.Combine(TestPaths.Output, "model-3d"), orientation: true);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    // The mesh against the faces: the same area per colour and side, fewer rectangles; drawn from the south-east to look at.
    static void Mesh(Structure s, string what, string dir, bool orientation = false)
    {
        Directory.CreateDirectory(dir);
        var faces = StructureModel.Faces(s, out _);
        var (mesh, colors, _) = StructureModel.Mesh(s);
        var want = faces.GroupBy(f => (f.Color, f.Dir)).ToDictionary(g => g.Key, g => g.Sum(f => Area(f)));
        var got = mesh.Quads.GroupBy(q => (colors.Pixels[(int)q.TexU], Array.IndexOf(new[] { "east", "west", "up", "down", "south", "north" }, q.Face)))
            .ToDictionary(g => g.Key, g => g.Sum(q => (double)(q.U.Length() * q.V.Length())));
        bool same = want.Count == got.Count && want.All(kv => got.TryGetValue(kv.Key, out var a) && Math.Abs(a - kv.Value) < 0.5);
        Program.Check(same && mesh.Quads.Count <= faces.Count, $"3D view of {what}: the mesh covers every face ({faces.Count:N0} faces as {mesh.Quads.Count:N0} rectangles, {colors.Width} colours)");
        var r = new ModelRenderer();
        r.Render(mesh, colors, ModelRenderer.CameraFor(mesh, 4 / 3f) with { Yaw = 35, Pitch = 28 }, new ModelViewOptions { ShowGrid = false }, 640, 480);
        var img = new RgbaImage(640, 480);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = unchecked((uint)r.Frame[i]);
        File.WriteAllBytes(Path.Combine(dir, string.Concat(what.Where(char.IsLetterOrDigit)) + ".png"), img.ToPng());
        if (!orientation) return;
        // Seen from the south (yaw 0): the red block (east) right of the middle, the blue one (south) in front, low down.
        var south = ModelRenderer.CameraFor(mesh, 4 / 3f) with { Yaw = 0, Pitch = 10 };
        r.Render(mesh, colors, south, new ModelViewOptions { ShowGrid = false }, 640, 480);
        var red = r.Project(new System.Numerics.Vector3(2.5f * 16, 1.5f * 16, 1.5f * 16));
        var blue = r.Project(new System.Numerics.Vector3(1.5f * 16, 1.5f * 16, 3f * 16));
        var top = r.Project(new System.Numerics.Vector3(1.5f * 16, 2f * 16, 1.5f * 16));
        var bottom = r.Project(new System.Numerics.Vector3(1.5f * 16, 0, 1.5f * 16));
        bool redRight = red is { } rp && rp.X > 320, blueMiddle = blue is { } bp && Math.Abs(bp.X - 320) < 40, up = top is { } tp && bottom is { } bt && tp.Y < bt.Y;
        uint seen = red is { } p ? unchecked((uint)r.Frame[(int)p.Y * 640 + (int)p.X]) : 0;
        Program.Check(redRight && blueMiddle && up && RgbaImage.R((uint)seen) > 2 * RgbaImage.B((uint)seen),
            $"3D view: from the south the east is on the right (red at x {red?.X:0}), the south block in the middle (x {blue?.X:0}), the top up — not mirrored");
    }

    static double Area(StructureModel.Face f)
    {
        var a = new System.Numerics.Vector3(f.A.X, f.A.Y, f.A.Z);
        var u = new System.Numerics.Vector3(f.B.X, f.B.Y, f.B.Z) - a;
        var v = new System.Numerics.Vector3(f.D.X, f.D.Y, f.D.Z) - a;
        return u.Length() * v.Length();
    }

    /// <summary>A written .obj read back: corners, faces (by corner numbers), the materials used and those its .mtl defines.</summary>
    sealed class Obj
    {
        public List<(double X, double Y, double Z)> V { get; } = [];
        public List<int[]> F { get; } = [];
        public HashSet<string> Used { get; } = [];
        public Dictionary<string, double> Defined { get; } = [];
        public int Faces => F.Count;
        public double MaxY => V.Count == 0 ? 0 : V.Max(v => v.Y);
        public int SeeThroughMaterials => Defined.Count(d => d.Value < 1);
        public bool FacesNameRealCorners => F.All(f => f.All(i => i >= 1 && i <= V.Count));
        public bool MaterialsDefined => Used.All(Defined.ContainsKey);

        public static Obj Read(string path)
        {
            var o = new Obj();
            var inv = CultureInfo.InvariantCulture;
            foreach (var line in File.ReadAllLines(path))
            {
                var p = line.Split(' ');
                if (p[0] == "v") o.V.Add((double.Parse(p[1], inv), double.Parse(p[2], inv), double.Parse(p[3], inv)));
                else if (p[0] == "f") o.F.Add(p.Skip(1).Select(int.Parse).ToArray());
                else if (p[0] == "usemtl") o.Used.Add(p[1]);
            }
            string mtl = Path.ChangeExtension(path, ".mtl");
            string? current = null;
            foreach (var line in File.ReadAllLines(mtl))
            {
                var p = line.Split(' ');
                if (p[0] == "newmtl") { current = p[1]; o.Defined[current] = 1; }
                else if (p[0] == "d" && current != null) o.Defined[current] = double.Parse(p[1], inv);
            }
            return o;
        }

        /// <summary>Every face's normal (from its corners' order) points away from the middle of the model's blocks it belongs to.</summary>
        public bool Outward()
        {
            foreach (var f in F)
            {
                var (a, b, c) = (V[f[0] - 1], V[f[1] - 1], V[f[2] - 1]);
                var u = (b.X - a.X, b.Y - a.Y, b.Z - a.Z);
                var w = (c.X - a.X, c.Y - a.Y, c.Z - a.Z);
                var n = (u.Item2 * w.Item3 - u.Item3 * w.Item2, u.Item3 * w.Item1 - u.Item1 * w.Item3, u.Item1 * w.Item2 - u.Item2 * w.Item1);
                var centre = (f.Average(i => V[i - 1].X), f.Average(i => V[i - 1].Y), f.Average(i => V[i - 1].Z));
                // The block the face belongs to: half a step back along the normal.
                double len = Math.Sqrt(n.Item1 * n.Item1 + n.Item2 * n.Item2 + n.Item3 * n.Item3);
                var inside = (centre.Item1 - n.Item1 / len * 0.01, centre.Item2 - n.Item2 / len * 0.01, centre.Item3 - n.Item3 / len * 0.01);
                if (!InsideAnyBox(inside)) return false;
            }
            return true;
        }

        // Inside the model's bounds (all the tests' shapes are filled boxes from the corners): enough to tell in from out.
        bool InsideAnyBox((double X, double Y, double Z) p) =>
            p.X > V.Min(v => v.X) && p.X < V.Max(v => v.X) && p.Y > V.Min(v => v.Y) && p.Y < V.Max(v => v.Y) && p.Z > V.Min(v => v.Z) && p.Z < V.Max(v => v.Z);
    }
}
