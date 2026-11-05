// McTests/BlockModelTests.cs: "blockmodels" — Java block and item models read, resolved, drawn and written back.
// Owns: BlockModelTests — a cube_all chain (stone ← cube_all ← cube) resolves its textures and elements; a full cube gets
// the game's default UVs; drawn with a half-red half-blue texture, the south face shows red on the west (left seen from
// the south) and the north face red on the east (left seen from the north), not mirrored; a face turned 90° shows the
// texture's bottom-left at its top-left; a wall torch's turned stick leans away from its wall; a written model reads back
// the same and keeps what isn't edited (display, ambientocclusion); a broken file is refused, not thrown; and every model
// of the newest Java installed on this PC resolves (none looping or missing a parent) and draws.
using System.Numerics;
using System.Text;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;

static class BlockModelTests
{
    public static void Run(string[] args)
    {
        var files = new Dictionary<string, byte[]>
        {
            ["assets/minecraft/models/block/cube.json"] = Encoding.UTF8.GetBytes("""
                { "elements": [ { "from": [0,0,0], "to": [16,16,16], "faces": {
                  "down": { "texture": "#down", "cullface": "down" }, "up": { "texture": "#up", "cullface": "up" },
                  "north": { "texture": "#north", "cullface": "north" }, "south": { "texture": "#south", "cullface": "south" },
                  "west": { "texture": "#west", "cullface": "west" }, "east": { "texture": "#east", "cullface": "east" } } } ] }
                """),
            ["assets/minecraft/models/block/cube_all.json"] = Encoding.UTF8.GetBytes("""
                { "parent": "block/cube", "textures": { "particle": "#all", "down": "#all", "up": "#all", "north": "#all", "east": "#all", "south": "#all", "west": "#all" } }
                """),
            ["assets/minecraft/models/block/stone.json"] = Encoding.UTF8.GetBytes("""
                { "parent": "minecraft:block/cube_all", "textures": { "all": "minecraft:block/stone" }, "display": { "gui": { "scale": [0.6, 0.6, 0.6] } }, "ambientocclusion": false }
                """),
            ["assets/minecraft/models/block/turned.json"] = Encoding.UTF8.GetBytes("""
                { "textures": { "t": "block/stone" }, "elements": [ { "from": [0,0,0], "to": [16,16,16], "faces": { "south": { "texture": "#t", "rotation": 90 } } } ] }
                """),
            ["assets/minecraft/models/block/wall_torch.json"] = Encoding.UTF8.GetBytes("""
                { "textures": { "torch": "block/stone" }, "elements": [ { "from": [-1, 3.5, 7], "to": [1, 13.5, 9],
                  "rotation": { "origin": [0, 3.5, 8], "axis": "z", "angle": -22.5 }, "faces": { "up": { "uv": [7, 6, 9, 8], "texture": "#torch" } } } ] }
                """),
            ["assets/minecraft/models/block/broken.json"] = Encoding.UTF8.GetBytes("""{ "parent": 5 }"""),
        };
        var lib = new JavaModelLibrary(files, null, null);
        var stone = lib.Resolve("block/stone")!;
        Program.Check(stone.Chain.Count == 3 && stone.Elements.Count == 1 && stone.Texture("#north") == "minecraft:block/stone" && stone.ElementsFrom?.Id == "block/cube",
            $"stone ← cube_all ← cube: the textures and the elements found ({stone.Chain.Count} models, {stone.Texture("#north")})");
        Program.Check(JavaModelElement.Sides.All(s => JavaModelMesh.DefaultUv(s, Vector3.Zero, new Vector3(16)).SequenceEqual([0, 0, 16, 16])), "a full cube's faces get the whole texture");

        // Half red (left), half blue (right).
        var tex = new RgbaImage(16, 16);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) tex.Pixels[y * 16 + x] = x < 8 ? 0xFFFF0000 : 0xFF0000FF;
        RgbaImage? Texture(string id) => id == "block/stone" ? tex : null;
        var (mesh, atlas, missing) = JavaModelMesh.Build(stone, Texture);
        Program.Check(mesh.Quads.Count == 6 && missing.Count == 0, $"the cube draws as 6 faces, every texture found ({mesh.Quads.Count}, missing {missing.Count})");
        var r = new ModelRenderer();
        string Seen(float yaw, Vector3 at)
        {
            r.Render(mesh, atlas, new OrbitCamera(yaw, 0, 60, new Vector3(8, 8, 8)), new ModelViewOptions { ShowGrid = false }, 200, 200);
            if (r.Project(at) is not var (px, py)) return "none";
            uint c = unchecked((uint)r.Frame[(int)py * 200 + (int)px]);
            return RgbaImage.R(c) > RgbaImage.B(c) ? "red" : "blue";
        }
        // From the south (yaw 0) the west (x 2) is on the left: red. From the north (yaw 180) the east (x 14) is on the left: red.
        string south = Seen(0, new Vector3(2, 8, 16)) + "/" + Seen(0, new Vector3(14, 8, 16));
        string north = Seen(180, new Vector3(14, 8, 0)) + "/" + Seen(180, new Vector3(2, 8, 0));
        string east = Seen(90, new Vector3(16, 8, 14)) + "/" + Seen(90, new Vector3(16, 8, 2));
        Program.Check(south == "red/blue" && north == "red/blue" && east == "red/blue",
            $"the texture reads left to right on every side, as in the game (south {south}, north {north}, east {east})");

        var turned = lib.Resolve("block/turned")!;
        var tq = JavaModelMesh.Build(turned, Texture).Mesh.Quads.Single();
        var corner = tq.TexAt(0, 0);
        Program.Check(tq.Turns == 1 && corner.X < 0.5f && corner.Y > 15.5f, $"a face turned 90° shows the texture's bottom-left at its top-left (texture {corner.X:0.#}, {corner.Y:0.#})");
        var torch = JavaModelMesh.Build(lib.Resolve("block/wall_torch")!, Texture).Mesh.Quads.Single();
        Program.Check(torch.Origin.X > 2.5f && torch.Origin.Y < 13.5f, $"a wall torch's stick leans away from its wall (top at x {torch.Origin.X:0.0}, y {torch.Origin.Y:0.0})");

        // Written back: the same model, and what isn't edited kept.
        var model = lib.Get("block/stone")!;
        model.Textures["all"] = "block/granite";
        var again = JavaBlockModel.Read("block/stone", model.Write(), true);
        var cube = lib.Get("block/cube")!;
        cube.Elements![0].From = [1, 0, 1];
        cube.Elements[0].Faces["up"].Uv = [0, 0, 8, 8];
        var cubeAgain = JavaBlockModel.Read("block/cube", cube.Write(), true);
        Program.Check(again.Textures["all"] == "block/granite" && again.Json["display"] != null && again.Json["ambientocclusion"]?.GetValue<bool>() == false
            && cubeAgain.Elements![0].From.SequenceEqual([1, 0, 1]) && cubeAgain.Elements[0].Faces["up"].Uv!.SequenceEqual([0, 0, 8, 8]) && cubeAgain.Elements[0].Faces["down"].CullFace == "down",
            "a written model reads back the same, keeping display and ambientocclusion");
        Program.Check(lib.Get("block/broken") == null && lib.Resolve("block/broken") == null, "a broken model file is refused, not thrown");
        var wrong = new JavaModelElement { From = [0, 0, 0], To = [40, 16, 16] };
        var wrongTurn = new JavaModelElement { Axis = "y", Angle = 30 };
        Program.Check(wrong.Problem() != null && wrongTurn.Problem() != null && new JavaModelElement().Problem() == null, $"boxes the game refuses are named ({wrong.Problem()}; {wrongTurn.Problem()})");

        Game();
    }

    // Every model of the newest Java on this PC: resolved (no loops, no missing parents) and drawn with the jar's textures.
    static void Game()
    {
        var jars = GameFolders.JavaJars();
        var flat = jars.FirstOrDefault(j => JavaReleases.Of(j.Version)?.FlatNames == true);
        if (flat.Jar == null) { Program.Info("(skipped the game's models: no Java 1.13+ installed)"); return; }
        var files = MinecraftStudio.Core.Packs.TexturePackReader.Files(flat.Jar, p => p.StartsWith("assets/minecraft/models/", StringComparison.Ordinal) || p.StartsWith("assets/minecraft/textures/block/", StringComparison.Ordinal) || p.StartsWith("assets/minecraft/textures/item/", StringComparison.Ordinal));
        var lib = new JavaModelLibrary(new Dictionary<string, byte[]>(), files, flat.Version);
        RgbaImage? Texture(string id) => files.TryGetValue($"assets/minecraft/textures/{id}.png", out var read) ? Png.Decode(read()) : null;
        int models = 0, unresolved = 0, drawn = 0, empty = 0, builtIn = 0, flatItems = 0;
        var lost = new List<string>();
        foreach (var id in lib.Ids.Where(i => i.StartsWith("block/") || i.StartsWith("item/")))
        {
            models++;
            var m = lib.Resolve(id);
            if (m == null || m.Chain[^1].Parent is string p && lib.Get(p) == null && !p.StartsWith("builtin/") && !p.EndsWith("item/generated")) { unresolved++; lost.Add(id); continue; }
            if (m.BuiltIn) { builtIn++; continue; }
            if (m.Generated) flatItems++;
            var (mesh, _, missing) = JavaModelMesh.Build(m, Texture);
            if (mesh.Quads.Count > 0) drawn++; else empty++;
        }
        // The colours come through as they are: stone (a grey file) drawn grey.
        var stoneImg = Texture("block/stone");
        var (stoneMesh, stoneAtlas, _) = JavaModelMesh.Build(lib.Resolve("block/stone")!, Texture);
        var r = new ModelRenderer();
        r.Render(stoneMesh, stoneAtlas, new OrbitCamera(20, 20, 60, new Vector3(8, 8, 8)), new ModelViewOptions { ShowGrid = false }, 100, 100);
        uint mid = unchecked((uint)r.Frame[50 * 100 + 50]);
        Program.Info($"stone: file pixel 0x{stoneImg?.Pixels[0]:X8}, atlas pixel 0x{stoneAtlas.Pixels[0]:X8}, drawn 0x{mid:X8}");
        Program.Check(stoneImg != null && stoneImg.Pixels.All(p => RgbaImage.R(p) == RgbaImage.G(p) && RgbaImage.G(p) == RgbaImage.B(p))
            && Math.Abs(RgbaImage.R(mid) - RgbaImage.B(mid)) < 12, $"stone (a grey file) reads and draws grey (drawn 0x{mid:X8})");
        Program.Info($"Java {flat.Version}: {models:N0} block and item models, {drawn:N0} drawn ({flatItems:N0} flat items), {builtIn:N0} by the game's code, {empty:N0} with nothing to draw, {unresolved} unresolved");
        Program.Check(models > 1000 && unresolved == 0, $"Java {flat.Version}: all {models:N0} block and item models resolve ({unresolved} not{(lost.Count > 0 ? ": " + string.Join(", ", lost.Take(5)) : "")})");
        Program.Check(drawn > models / 2, $"Java {flat.Version}: {drawn:N0} draw ({flatItems:N0} flat items), {builtIn:N0} are drawn by the game's code, {empty:N0} have nothing to draw (templates)");
    }
}
