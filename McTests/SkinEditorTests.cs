// McTests/SkinEditorTests.cs: Part of "skins": the 3D view's picking (every visible skin pixel is found exactly
// where the model geometry puts it, from every side, for both layers, HD and both arm widths) and the editor's
// strokes, mirror, fills and history.
// Owns: SkinEditorTests.
using System.Numerics;
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Skins;

/// <summary>
/// Part of "skins": the 3D view's picking (every visible skin pixel is found exactly where the model geometry puts it,
/// from every side, for both layers, HD and both arm widths) and the editor's strokes, mirror, fills and history.
/// </summary>
static class SkinEditorTests
{
    public static void Run()
    {
        Console.WriteLine("== 3D view: picking skin pixels");
        Picking();
        Console.WriteLine("== Editor: strokes, mirror, fill, undo and unsaved changes");
        Editing();
    }

    static Vector3 V((float X, float Y, float Z) p) => new(p.X, p.Y, p.Z);

    /// <summary>The centre of an image pixel on the model (as SkinPixels computes it), plus its face's normal.</summary>
    static (Vector3 Centre, Vector3 Normal) Where(SkinPixel px, int s)
    {
        var q = SkinLayout.TexelQuad(px.Face, px.I / s, px.J / s);
        float fx = (px.I % s + 0.5f) / s, fy = (px.J % s + 0.5f) / s;
        var c = V(q[0]) + (V(q[1]) - V(q[0])) * fx + (V(q[3]) - V(q[0])) * fy;
        return (c, V(SkinLayout.Normal(px.Face.Side)));
    }

    static void Picking()
    {
        var views = new (string Name, float Yaw, float Pitch, FaceSide Side)[]
        {
            ("front", 0, 0, FaceSide.Front), ("back", 180, 0, FaceSide.Back), ("right side", -90, 0, FaceSide.Right),
            ("left side", 90, 0, FaceSide.Left), ("above", 0, 89, FaceSide.Top),
        };
        foreach (var model in new[] { SkinModel.Classic, SkinModel.Slim })
            foreach (var size in new[] { 64, 128 })
                foreach (bool overlay in new[] { false, true })
                {
                    var skin = new Skin(new RgbaImage(size, size), model);
                    var pixels = new SkinPixels(model, size);
                    var renderer = new SkinRenderer();
                    int checkedPixels = 0, wrong = 0;
                    foreach (var (name, yaw, pitch, side) in views)
                    {
                        var camera = new OrbitCamera(yaw, pitch, 60, new Vector3(0, 16, 0));
                        renderer.Render(skin, camera, new SkinViewOptions { PaintOverlay = overlay }, 700, 700);
                        var (c, _, _, _) = camera.Basis();
                        // Head faces seen straight on are never hidden behind anything: each of their pixels must be
                        // picked at its own centre. (The head top is seen from above, the others from their side.)
                        foreach (var face in SkinLayout.Boxes(model).Where(b => b.Part == "Head" && b.Overlay == overlay).SelectMany(b => b.Faces()).Where(f => f.Side == side))
                            for (int j = 0; j < face.H * size / 64; j++)
                                for (int i = 0; i < face.W * size / 64; i++)
                                {
                                    var px = new SkinPixel(face, i, j);
                                    var (centre, _) = Where(px, size / 64);
                                    // Seen from the side, the sleeves (second layer, a little larger) cover the
                                    // lowest row of the hat: only check what nothing can be in front of.
                                    if (centre.Y < 25) continue;
                                    if (renderer.Project(centre) is not { } sp) { wrong++; continue; }
                                    var got = renderer.PickAt((int)sp.X, (int)sp.Y);
                                    int ex = face.U * size / 64 + i, ey = face.V * size / 64 + j;
                                    checkedPixels++;
                                    if (got != (ex, ey)) { wrong++; if (wrong <= 3) Program.Info($"{model} {size} {(overlay ? "second layer" : "base")} {name}: ({ex},{ey}) picked as {got}"); }
                                }
                        // Everything picked is a pixel of the layer being painted, on a face turned towards the camera.
                        int seen = 0, bad = 0;
                        for (int y = 0; y < 700; y += 3)
                            for (int x = 0; x < 700; x += 3)
                                if (renderer.PickAt(x, y) is { } p)
                                {
                                    seen++;
                                    if (pixels.At(p.X, p.Y) is not { } sp2 || sp2.Overlay != overlay) { bad++; continue; }
                                    var (centre, normal) = Where(sp2, size / 64);
                                    if (Vector3.Dot(normal, c - centre) <= 0) bad++;
                                }
                        if (seen == 0 || bad > 0) { wrong++; Program.Info($"{model} {size} {name}: {bad} of {seen} picks on the wrong layer or a hidden face"); }
                    }
                    Program.Check(wrong == 0 && checkedPixels > 0, $"{model} {size} {(overlay ? "second layer" : "base layer")}: {wrong} wrong picks of {checkedPixels}");
                }
        // The front view shows the character's right side on the left of the screen (as when facing someone).
        {
            var skin = new Skin(new RgbaImage(64, 64), SkinModel.Classic);
            var r = new SkinRenderer();
            r.Render(skin, new OrbitCamera(0, 0, 60, new Vector3(0, 16, 0)), new SkinViewOptions(), 600, 600);
            var rightArm = r.Project(new Vector3(-6, 18, 2));
            var leftArm = r.Project(new Vector3(6, 18, 2));
            Program.Check(rightArm is { } ra && leftArm is { } la && ra.X < la.X && r.PickAt((int)ra.X, (int)ra.Y) is { } pick && pick.X >= 40 && pick.X < 56 && pick.Y >= 16 && pick.Y < 32,
                "front view: the right arm (texture 40,16) is on the left of the screen");
            // The eyedropper sees the hat where it has colour, and the face through its see-through pixels.
            var hat = new Skin(new RgbaImage(64, 64), SkinModel.Classic);
            hat.Image.Fill(8, 8, 8, 8, 0xFFFF0000);        // face (front of the head)
            hat.Image[42, 10] = 0xFF00FF00;                 // one hat pixel in front of face pixel (10, 10)
            r.Render(hat, new OrbitCamera(0, 0, 60, new Vector3(0, 16, 0)), new SkinViewOptions(), 600, 600);
            var hatSpot = r.Project(new Vector3(-4.5f + 2.5f * 9 / 8f, 32.5f - 2.5f * 9 / 8f, 4.5f));
            var faceSpot = r.Project(new Vector3(-4 + 5.5f, 32 - 5.5f, 4));
            Program.Check(hatSpot is { } hs && r.VisibleAt((int)hs.X, (int)hs.Y) == (42, 10) && r.PickAt((int)hs.X, (int)hs.Y) == (10, 10)
                          && faceSpot is { } fs && r.VisibleAt((int)fs.X, (int)fs.Y) == (13, 13),
                "the eyedropper sees the hat where it has colour, the face elsewhere; painting still reaches the face");
            // Hidden parts are neither drawn nor picked.
            r.Render(skin, new OrbitCamera(0, 0, 60, new Vector3(0, 16, 0)), new SkinViewOptions { HiddenParts = new HashSet<string> { "Head" } }, 600, 600);
            var head = r.Project(new Vector3(0, 28, 4));
            Program.Check(head is { } hp && (r.PickAt((int)hp.X, (int)hp.Y) is null || r.PickAt((int)hp.X, (int)hp.Y) is { } hp2 && hp2.Y >= 16), "a hidden head is not picked");
        }
    }

    static void Editing()
    {
        var skin = new Skin(new RgbaImage(64, 64), SkinModel.Classic);
        skin.Image.Fill(0, 0, 64, 64, 0xFF101010);
        var ed = new SkinEditor(skin) { Primary = 0xFFFF0000, Secondary = 0xFF00FF00 };
        int changes = 0;
        ed.ImageChanged += () => changes++;
        Program.Check(!ed.IsDirty && !ed.CanUndo, "a fresh editor has nothing to undo and no unsaved changes");

        // A 2D stroke connects its points with a line.
        ed.BeginStroke(10, 10, false);
        ed.StrokeTo(14, 10, connect: true);
        ed.EndStroke();
        Program.Check(Enumerable.Range(10, 5).All(x => skin.Image[x, 10] == 0xFFFF0000) && skin.Image[15, 10] == 0xFF101010 && ed.IsDirty && ed.CanUndo, "a stroke paints a connected line");
        ed.MarkSaved();
        Program.Check(!ed.IsDirty, "saved");
        ed.Undo();
        Program.Check(ed.Skin.Image[12, 10] == 0xFF101010 && ed.IsDirty && ed.CanRedo, "undo takes the stroke back (and that is an unsaved change)");
        ed.Redo();
        Program.Check(ed.Skin.Image[12, 10] == 0xFFFF0000 && !ed.IsDirty, "redo brings it back to the saved state");
        ed.Undo();
        ed.BeginStroke(30, 30, true);
        ed.EndStroke();
        Program.Check(ed.IsDirty && !ed.CanRedo && ed.Skin.Image[30, 30] == 0xFF00FF00, "undo then a different edit is unsaved, and redo is gone");

        // A stroke that changes nothing leaves no step.
        int before = UndoDepth(ed);
        ed.BeginStroke(30, 30, true);
        ed.EndStroke();
        Program.Check(UndoDepth(ed) == before, "painting a pixel its own colour makes no undo step");

        // The eyedropper picks without a step.
        uint picked = 0;
        bool second = false;
        ed.ColorPicked += (c, s) => { picked = c; second = s; };
        ed.Tool = PaintTool.Picker;
        ed.BeginStroke(30, 30, true);
        Program.Check(picked == 0xFF00FF00 && second && UndoDepth(ed) == before, "the eyedropper picks the colour and makes no step");

        // A see-through pixel is never picked; after picking with the eyedropper the pencil comes back.
        ed.Tool = PaintTool.Pencil;
        ed.Tool = PaintTool.Picker;
        string? said = null;
        ed.Message += m => said = m;
        picked = 0;
        ed.Skin.Image[60, 0] = 0;
        Program.Check(!ed.PickColor(60, 0, false) && picked == 0 && said != null && said.Contains("see-through") && ed.Tool == PaintTool.Picker,
            "a see-through pixel is not picked, and the player is told why");
        ed.BeginStroke(30, 30, false);
        Program.Check(picked == 0xFF00FF00 && ed.Tool == PaintTool.Pencil, "after a pick the pencil is back, ready to paint");
        ed.Tool = PaintTool.Pencil;

        // Mirror: the head's front column 0 also paints column 7.
        ed.Tool = PaintTool.Pencil;
        ed.Mirror = true;
        ed.BeginStroke(8, 9, false);
        ed.EndStroke();
        Program.Check(ed.Skin.Image[8, 9] == 0xFFFF0000 && ed.Skin.Image[15, 9] == 0xFFFF0000, "mirror paints both sides");
        ed.Mirror = false;

        // Fill stays on its face; the second button fills with the second colour.
        ed.Tool = PaintTool.Fill;
        ed.BeginStroke(20, 21, true);
        ed.StrokeTo(21, 21, connect: true);
        ed.EndStroke();
        var body = ed.Pixels.At(20, 21)!.Value.Face;
        bool inside = true, outside = true;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                bool onFace = ed.Pixels.At(x, y) is { } p && p.Face == body;
                if (onFace && ed.Skin.Image[x, y] != 0xFF00FF00) inside = false;
                if (!onFace && ed.Skin.Image[x, y] == 0xFF00FF00 && (x, y) != (30, 30)) outside = false;
            }
        Program.Check(inside && outside, "fill covers the body's front face and nothing else");

        // Shading touches each pixel once per stroke, however often the stroke passes over it.
        ed.Tool = PaintTool.Shade;
        uint was = ed.Skin.Image[40, 40];
        ed.BeginStroke(40, 40, false);
        ed.StrokeTo(41, 40, connect: true);
        ed.StrokeTo(40, 40, connect: true);
        ed.EndStroke();
        Program.Check(ed.Skin.Image[40, 40] == Paint.Shade(was, -0.12), "shade applies once per pixel per stroke");

        // Arm width and size are undoable steps.
        ed.Tool = PaintTool.Pencil;
        var image = ed.Skin.Image.Clone();
        ed.SetModel(SkinModel.Slim);
        Program.Check(ed.Skin.Model == SkinModel.Slim && ed.Pixels.Model == SkinModel.Slim, "slim arms");
        ed.SetSize(128);
        Program.Check(ed.Skin.Image.Width == 128 && ed.Pixels.Size == 128, "HD size");
        ed.Undo();
        ed.Undo();
        Program.Check(ed.Skin.Model == SkinModel.Classic && ed.Skin.Image.SamePixels(image) && ed.Pixels.Size == 64 && ed.Pixels.Model == SkinModel.Classic, "undo restores the arms, size and pixels");
        Program.Check(changes > 0, "views were told about changes");
    }

    static int UndoDepth(SkinEditor ed)
    {
        // Count by undoing everything and redoing it again (the editor exposes only names).
        int n = 0;
        while (ed.CanUndo) { ed.Undo(); n++; }
        for (int i = 0; i < n; i++) ed.Redo();
        return n;
    }
}
