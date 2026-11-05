// McTests/PaintEngineTests.cs: "paint" — the shared painting engine of the skin, texture and mob editors.
// Owns: PaintEngineTests — every tool (brush sizes, shapes drawn live and redrawn from the start, fills with tolerance and
// over the whole picture, dither, the selection as a limit, mirroring), floating pastes and moves (flip, turn, resize
// without loss, delete, put down, cancel, undo), colour changes, scaling, palettes and picture import.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.Core.Skins;

static class PaintEngineTests
{
    const uint Red = 0xFFFF0000, Blue = 0xFF0000FF, White = 0xFFFFFFFF, Black = 0xFF000000;

    public static void Run(string[] args)
    {
        Console.WriteLine("== Tools");
        Tools();
        Console.WriteLine("== Shapes");
        Shapes();
        Console.WriteLine("== Fill");
        Fills();
        Console.WriteLine("== Selection and floating pixels");
        Floating();
        Console.WriteLine("== Skin editor: live changes and undo");
        SkinSession();
        Console.WriteLine("== Texture editor: live changes, frames and the pack");
        TextureSession();
        Console.WriteLine("== Colour changes");
        Colours();
        Console.WriteLine("== Scaling, palettes and pictures");
        Pictures();
        Console.WriteLine("== Blank pictures and My tiles");
        BlanksAndTiles();
    }

    static void BlanksAndTiles()
    {
        // A blank skin: the base layer white, the second layer and unused pixels see-through.
        var skin = new SkinEditor(new Skin(Solid(64, 64, Red), SkinModel.Classic));
        var blank = skin.Blank(white: true)!;
        Program.Check(blank[8, 8] == White && blank[40, 8] == 0 && blank[0, 0] == 0, "blank skin: base layer white (face), hat layer and unused pixels see-through");
        Program.Check(skin.Blank(white: false)!.Pixels.All(p => p == 0), "blank see-through skin");
        // A blank texture: the frame in view, white.
        var game = new TexturePack();
        game.Textures["block/stone"] = new PackTexture { Key = "block/stone", Image = Solid(16, 16, Red) };
        var tex = new TextureEditor(new TexturePack(), game);
        tex.Select("block/stone");
        var tile = tex.Blank(white: true)!;
        Program.Check(tile.Width == 16 && tile.Pixels.All(p => p == White), "blank texture: every pixel white");
        tex.Session.Apply("Blank (white)", _ => tile);
        Program.Check(tex.Current!.Image.Pixels.All(p => p == White) && game.Textures["block/stone"].Image[0, 0] == Red && tex.CanUndo, "made blank as one step; the game's texture untouched");

        // My tiles: save, list, read back, a taken name gets a number, rename, too large refused.
        string folder = Path.Combine(TestPaths.Output, "tiles");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        var lib = new TileLibrary(folder);
        Program.Check(lib.List().Count == 0, "an empty library lists nothing (and does not need its folder)");
        var a = lib.Save("Brick wall", Pattern(16, 16));
        var b = lib.Save("Brick wall", Solid(32, 32, Blue));
        var list = lib.List();
        Program.Check(list.Count == 2 && a.Name == "Brick wall" && b.Name == "Brick wall (2)" && list.Any(t => t.Width == 32 && t.Height == 32), "saved twice under one name: the second gets a number");
        Program.Check(lib.Load(a).SamePixels(Pattern(16, 16)), "a tile reads back pixel for pixel");
        var renamed = lib.Rename(a, "My bricks");
        Program.Check(renamed.Name == "My bricks" && File.Exists(renamed.Path) && !File.Exists(a.Path) && lib.Load(renamed).SamePixels(Pattern(16, 16)), "rename keeps the picture");
        var weird = lib.Save(@"..\..\evil:name?", Solid(4, 4, Red));
        Program.Check(Path.GetDirectoryName(weird.Path) == folder, $"a name with path characters stays inside the library ({weird.Name})");
        bool refused = false;
        try { lib.Save("huge", new RgbaImage(TileLibrary.MaxSide + 1, 1)); }
        catch (InvalidDataException) { refused = true; }
        Program.Check(refused && !Directory.EnumerateFiles(folder, "*.saving").Any(), "a picture too large for a tile is refused, nothing half-written left");
        Program.Check(Png.Size(Pattern(7, 5).ToPng()) == (7, 5), "a PNG's size is read from its header");
    }

    static RgbaImage Solid(int w, int h, uint c)
    {
        var img = new RgbaImage(w, h);
        img.Fill(0, 0, w, h, c);
        return img;
    }

    static int Count(RgbaImage img, uint c) => img.Pixels.Count(p => p == c);

    static PaintStroke Stroke(RgbaImage img, PaintTool tool, int x, int y, PaintSettings? s = null, PixelRect? area = null, PixelRect? limit = null,
        Func<int, int, IEnumerable<(int X, int Y)>>? mirror = null) =>
        new(img, area ?? new PixelRect(0, 0, img.Width, img.Height), tool, Red, false, s ?? new PaintSettings(), x, y, mirror, null, limit, new Random(1));

    static void Tools()
    {
        var img = Solid(16, 16, White);
        var st = Stroke(img, PaintTool.Pencil, 2, 2);
        st.To(2, 2, false);
        st.To(10, 2, true);
        Program.Check(Count(img, Red) == 9 && st.Changed(), "pencil: a dragged line of 9 pixels");

        img = Solid(16, 16, White);
        st = Stroke(img, PaintTool.Pencil, 5, 5, new PaintSettings { BrushSize = 3 });
        st.To(5, 5, false);
        Program.Check(Count(img, Red) == 9 && img[4, 4] == Red && img[6, 6] == Red && img[7, 7] == White, "brush 3: a 3 × 3 dot around the pixel");

        img = Solid(16, 16, White);
        st = Stroke(img, PaintTool.Pencil, 5, 5, new PaintSettings { BrushSize = 2 });
        st.To(5, 5, false);
        Program.Check(Count(img, Red) == 4 && img[5, 5] == Red && img[6, 6] == Red && img[4, 4] == White, "brush 2: a 2 × 2 dot from the pixel");

        img = Solid(16, 16, White);
        st = Stroke(img, PaintTool.Eraser, 3, 3);
        st.To(3, 3, false);
        Program.Check(img[3, 3] == 0 && Count(img, White) == 255, "eraser: see-through");

        img = Solid(16, 16, White);
        st = Stroke(img, PaintTool.Dither, 0, 0, new PaintSettings { BrushSize = 4 });
        st.To(1, 1, false);
        Program.Check(Count(img, Red) == 8 && img[0, 0] == Red && img[1, 0] == White && img[1, 1] == Red, "dither: every other pixel of the brush");

        img = Solid(16, 16, 0xFF808080);
        st = Stroke(img, PaintTool.Shade, 4, 4);
        st.To(4, 4, false);
        st.To(4, 4, true);
        st.To(4, 4, true);
        Program.Check(img[4, 4] == Paint.Shade(0xFF808080, -0.12), "shade: each pixel once per stroke, however often it is passed");

        // Painting stays inside the area (a frame of an animated texture) and inside the selection.
        img = Solid(16, 32, White);
        st = Stroke(img, PaintTool.Pencil, 0, 0, new PaintSettings { BrushSize = 5 }, new PixelRect(0, 16, 16, 16));
        st.To(0, 0, false);
        Program.Check(Count(img, Red) == 9 && img[0, 16] == Red && img[0, 15] == White, "the brush never leaves the frame (area coordinates)");

        img = Solid(16, 16, White);
        st = Stroke(img, PaintTool.Pencil, 0, 5, limit: new PixelRect(4, 0, 4, 16));
        st.To(0, 5, false);
        st.To(15, 5, true);
        Program.Check(Count(img, Red) == 4 && img[4, 5] == Red && img[7, 5] == Red && img[8, 5] == White, "the selection limits painting");

        img = Solid(16, 16, White);
        st = Stroke(img, PaintTool.Pencil, 2, 3, mirror: (x, y) => [(15 - x, y)]);
        st.To(2, 3, false);
        Program.Check(img[2, 3] == Red && img[13, 3] == Red && Count(img, Red) == 2, "mirror paints the mirrored pixel too");

        img = Solid(4, 4, Red);
        st = Stroke(img, PaintTool.Pencil, 1, 1);
        st.To(1, 1, false);
        Program.Check(!st.Changed(), "painting a pixel its own colour is no change (no undo step)");
    }

    static void Shapes()
    {
        var img = Solid(16, 16, White);
        var st = Stroke(img, PaintTool.Line, 0, 0);
        st.To(0, 0, false);
        st.To(15, 3, true);
        st.To(5, 0, true);
        Program.Check(Count(img, Red) == 6 && img[5, 0] == Red && img[15, 3] == White, "line: redrawn from its start each move (the earlier line is gone)");

        img = Solid(16, 16, White);
        st = Stroke(img, PaintTool.Line, 0, 0);
        st.Constrain = true;
        st.To(10, 1, true);
        Program.Check(img[10, 0] == Red && img[10, 1] == White && Count(img, Red) == 11, "Shift keeps a line straight");

        img = Solid(16, 16, White);
        st = Stroke(img, PaintTool.Rectangle, 2, 2);
        st.To(7, 5, true);
        Program.Check(Count(img, Red) == 2 * 6 + 2 * 2 && img[4, 3] == White, "rectangle outline 6 × 4");

        img = Solid(16, 16, White);
        st = Stroke(img, PaintTool.Rectangle, 2, 2, new PaintSettings { FilledShapes = true });
        st.To(7, 5, true);
        Program.Check(Count(img, Red) == 24, "filled rectangle 6 × 4");

        img = Solid(16, 16, White);
        st = Stroke(img, PaintTool.Rectangle, 2, 2);
        st.Constrain = true;
        st.To(5, 9, true);
        Program.Check(img[9, 9] == Red && img[9, 2] == Red && img[2, 9] == Red, "Shift keeps a rectangle square");

        var plus = Paint.Ellipse(new PixelRect(0, 0, 3, 3), filled: true).ToHashSet();
        Program.Check(plus.Count == 5 && !plus.Contains((0, 0)) && plus.Contains((1, 1)), "a 3 × 3 circle is a plus");
        var ring = Paint.Ellipse(new PixelRect(0, 0, 9, 9), filled: false).ToHashSet();
        var disc = Paint.Ellipse(new PixelRect(0, 0, 9, 9), filled: true).ToHashSet();
        Program.Check(!ring.Contains((4, 4)) && disc.Contains((4, 4)) && ring.IsSubsetOf(disc) && ring.Contains((4, 0)) && ring.Contains((0, 4)) && !disc.Contains((0, 0)),
            "ellipse: the outline touches all four sides, the middle is only in the filled one");
        Program.Check(Enumerable.Range(0, 9).All(y => Enumerable.Range(0, 9).All(x => disc.Contains((x, y)) == disc.Contains((8 - x, y)) && disc.Contains((x, y)) == disc.Contains((x, 8 - y)))),
            "ellipse is symmetric");
        Program.Check(Paint.Ellipse(new PixelRect(3, 3, 1, 1), false).SequenceEqual([(3, 3)]) && Paint.Ellipse(new PixelRect(0, 0, 1, 5), true).Count() == 5,
            "tiny ellipses: one pixel, and a line");
    }

    static void Fills()
    {
        var img = Solid(8, 8, White);
        img.Fill(4, 0, 4, 8, 0xFFF0F0F0);
        var st = Stroke(img, PaintTool.Fill, 0, 0);
        st.To(0, 0, false);
        Program.Check(Count(img, Red) == 32, "fill: only exactly the same colour");

        img = Solid(8, 8, White);
        img.Fill(4, 0, 4, 8, 0xFFF0F0F0);
        st = Stroke(img, PaintTool.Fill, 0, 0, new PaintSettings { FillTolerance = 10 });
        st.To(0, 0, false);
        Program.Check(Count(img, Red) == 64, "fill with tolerance takes close colours too");

        img = Solid(8, 8, White);
        img.Fill(3, 0, 1, 8, Black);
        st = Stroke(img, PaintTool.Fill, 0, 0);
        st.To(0, 0, false);
        Program.Check(Count(img, Red) == 24, "fill stops at a wall");
        img = Solid(8, 8, White);
        img.Fill(3, 0, 1, 8, Black);
        st = Stroke(img, PaintTool.Fill, 0, 0, new PaintSettings { FillAll = true });
        st.To(0, 0, false);
        Program.Check(Count(img, Red) == 56, "fill all: every pixel of that colour, also beyond the wall");

        img = new RgbaImage(8, 8);
        img[2, 2] = 0x00FFFFFF;
        st = Stroke(img, PaintTool.Fill, 0, 0);
        st.To(0, 0, false);
        Program.Check(Count(img, Red) == 64, "see-through pixels count as one colour, whatever colour they hide");

        img = Solid(8, 8, White);
        st = Stroke(img, PaintTool.Fill, 1, 1, limit: new PixelRect(0, 0, 4, 4));
        st.To(1, 1, false);
        st.To(6, 6, true);
        Program.Check(Count(img, Red) == 16, "fill stays inside the selection, and happens once per stroke");
    }

    static PixelSession Session(RgbaImage img, Action<string, bool, bool>? end = null) =>
        new(() => new PixelRect(0, 0, img.Width, img.Height), _ => (img, new PixelRect(0, 0, img.Width, img.Height)), end ?? ((_, _, _) => { }), () => { }, () => { });

    static void Floating()
    {
        var img = Solid(8, 8, White);
        var ends = new List<(string Name, bool Commit, bool Changed)>();
        var s = Session(img, (n, c, ch) => ends.Add((n, c, ch)));
        var red = Solid(2, 2, Red);
        s.Paste(red, "Paste", (3, 3));
        Program.Check(img[3, 3] == Red && img[4, 4] == Red && Count(img, Red) == 4 && s.Selection == new PixelRect(3, 3, 2, 2), "paste shows at once and is selected");
        s.MoveFloating(-1, 6);
        Program.Check(Count(img, Red) == 2 && img[0, 6] == Red && img[0, 7] == Red && img[3, 3] == White, "moving redraws from how it was; pixels past the edge are not drawn");
        s.MoveFloating(6, 0);
        s.Commit();
        Program.Check(ends.Count == 1 && ends[0] is ("Paste", true, true) && Count(img, Red) == 4 && img[7, 1] == Red, "put down: one step");

        // Lift and move: the old place is see-through until something lands there.
        img = Solid(8, 8, White);
        img.Fill(0, 0, 2, 2, Blue);
        ends.Clear();
        s = Session(img, (n, c, ch) => ends.Add((n, c, ch)));
        s.Select(new PixelRect(0, 0, 2, 2));
        s.Lift();
        s.MoveFloating(4, 4);
        Program.Check(img[0, 0] == 0 && img[4, 4] == Blue && Count(img, Blue) == 4, "lift and move leaves a see-through hole");
        s.Cancel();
        Program.Check(img[0, 0] == Blue && img[4, 4] == White && ends.Count == 1 && !ends[0].Commit && s.Selection == new PixelRect(0, 0, 2, 2), "cancel puts everything back");

        // Copy and move keeps the original.
        s.Lift(keepCopy: true);
        s.MoveFloating(5, 5);
        s.Commit();
        Program.Check(img[0, 0] == Blue && img[5, 5] == Blue && Count(img, Blue) == 8, "copy and move keeps the original");

        // Flip and turn whole pictures; a picture that is not square is not turned.
        img = new RgbaImage(4, 4);
        img[0, 0] = Red;
        s = Session(img);
        s.Transform(PixelTransform.FlipX);
        s.Commit();
        Program.Check(img[3, 0] == Red && img[0, 0] == 0, "flip left to right");
        s.Select(null);
        s.Transform(PixelTransform.RotateRight);
        s.Commit();
        Program.Check(img[3, 3] == Red, "turn right");
        var tall = new RgbaImage(4, 8);
        s = Session(tall);
        Program.Check(!s.Transform(PixelTransform.RotateRight), "a picture that is not square is not turned as a whole");

        // Turning and flipping fold together: four turns or two flips are where they started.
        var f = new FloatingPixels(Pattern(3, 2), 0, 0);
        foreach (var t in new[] { PixelTransform.RotateRight, PixelTransform.RotateRight, PixelTransform.RotateRight, PixelTransform.RotateRight }) f.Transform(t);
        Program.Check(f.Pixels.SamePixels(Pattern(3, 2)), "four turns: as it was");
        f.Transform(PixelTransform.FlipX);
        f.Transform(PixelTransform.RotateRight);
        f.Transform(PixelTransform.FlipX);
        f.Transform(PixelTransform.RotateRight);
        Program.Check(f.Pixels.SamePixels(Pattern(3, 2)), "flip, turn, flip, turn: as it was");
        f.Transform(PixelTransform.FlipY);
        Program.Check(f.Pixels.SamePixels(Resample.Flip(Pattern(3, 2), horizontal: false)), "flip upside down");

        // Resizing starts from the pixels as they came: small and back loses nothing.
        f = new FloatingPixels(Pattern(4, 4), 0, 0);
        f.Resize(1, 1, smooth: true);
        f.Resize(4, 4, smooth: false);
        Program.Check(f.Pixels.SamePixels(Pattern(4, 4)), "resize small then back: nothing lost");
        f.Transform(PixelTransform.RotateRight);
        f.Resize(8, 4, smooth: false);
        Program.Check(f.Pixels.Width == 8 && f.Pixels.Height == 4, "resize after a turn sets the size as shown");

        // Delete.
        img = Solid(4, 4, White);
        s = Session(img);
        s.Select(new PixelRect(1, 1, 2, 2));
        s.Delete();
        Program.Check(Count(img, 0) == 4 && s.Selection == null, "delete makes the selection see-through");

        // A paste with see-through parts covers only where it has colour.
        img = Solid(4, 4, White);
        var dot = new RgbaImage(3, 3);
        dot[1, 1] = Red;
        dot[0, 0] = 0x80000000;
        s = Session(img);
        s.Paste(dot, "Paste", (0, 0));
        s.Commit();
        Program.Check(img[1, 1] == Red && img[2, 2] == White && img[0, 0] == Paint.Over(0x80000000, White) && RgbaImage.A(img[0, 0]) == 255, "a paste lays over what is under it");

        // Copy: what floats, else the selection, else everything.
        img = Pattern(4, 4);
        s = Session(img);
        Program.Check(s.Copy(img.Clone())!.SamePixels(img), "copy without a selection: everything");
        s.Select(new PixelRect(1, 1, 2, 2));
        Program.Check(s.Copy(img.Clone())!.SamePixels(img.Crop(1, 1, 2, 2)), "copy the selection");

        // Preview (a colour change being tried) then cancel.
        img = Solid(4, 4, White);
        s = Session(img);
        s.Preview("Colours", c => Red);
        s.Preview("Colours", c => Blue);
        Program.Check(Count(img, Blue) == 16, "a colour change is tried on the picture");
        s.Cancel();
        Program.Check(Count(img, White) == 16, "and taken back");
        s.Select(new PixelRect(0, 0, 2, 2));
        s.Preview("Colours", c => Blue);
        s.Commit();
        Program.Check(Count(img, Blue) == 4, "a colour change on the selection only");

        // Apply: a change of the whole area at once (wrap-around shift).
        img = Pattern(4, 4);
        var expect = Resample.Wrap(img, 1, 0);
        s = Session(img);
        s.Apply("Shift", a => Resample.Wrap(a, 1, 0));
        Program.Check(img.SamePixels(expect) && Resample.Wrap(Resample.Wrap(Pattern(4, 4), 1, 0), -1, 0).SamePixels(Pattern(4, 4)), "shift with wrap-around");
    }

    static RgbaImage Pattern(int w, int h)
    {
        var img = new RgbaImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[x, y] = RgbaImage.Argb(255, x * 40, y * 40, 100 + x + y);
        return img;
    }

    static void SkinSession()
    {
        var skin = new Skin(Solid(64, 64, White), SkinModel.Classic);
        var ed = new SkinEditor(skin);
        ed.Session.Paste(Solid(4, 4, Red), "Paste", (8, 8));
        Program.Check(ed.Skin.Image[8, 8] == Red && ed.IsDirty == false && !ed.CanUndo, "skin: a paste shows but is not a step until it is put down");
        ed.Undo();
        Program.Check(ed.Skin.Image[8, 8] == White && !ed.Session.IsLive && !ed.CanUndo, "skin: Undo while pasting takes the paste back");
        ed.Session.Paste(Solid(4, 4, Red), "Paste", (8, 8));
        ed.Session.Commit();
        Program.Check(ed.CanUndo && ed.UndoName == "Paste" && ed.IsDirty, "skin: put down is one step");
        ed.Undo();
        Program.Check(ed.Skin.Image[8, 8] == White && ed.CanRedo, "skin: undo the paste");
        ed.Redo();
        Program.Check(ed.Skin.Image[8, 8] == Red, "skin: redo the paste");
        // Painting puts a paste down first, and paints inside the selection only.
        ed.Session.Paste(Solid(2, 2, Blue), "Paste", (20, 20));
        ed.Tool = PaintTool.Pencil;
        ed.Primary = Black;
        ed.BeginStroke(0, 0, false);
        ed.EndStroke();
        Program.Check(ed.Skin.Image[20, 20] == Blue && ed.Skin.Image[0, 0] == White && ed.UndoName == "Paste", "skin: painting puts the paste down; outside the selection nothing is painted");
        ed.Session.Select(null);
        ed.Tool = PaintTool.Rectangle;
        ed.BeginStroke(40, 40, false);
        ed.StrokeTo(43, 43, connect: true);
        ed.EndStroke();
        Program.Check(ed.UndoName == "Rectangle" && ed.Skin.Image[40, 43] == Black && ed.Skin.Image[41, 41] == White, "skin: rectangle tool");
        // Changing the size forgets the selection.
        ed.Session.Select(new PixelRect(0, 0, 10, 10));
        ed.SetSize(128);
        Program.Check(ed.Session.Selection == null && ed.Skin.Image.Width == 128, "skin: a new size forgets the selection");
    }

    static void TextureSession()
    {
        var game = new TexturePack { Name = "Game" };
        game.Textures["block/stone"] = new PackTexture { Key = "block/stone", Image = Solid(16, 16, 0xFF7F7F7F) };
        var strip = Solid(16, 32, White);
        strip.Fill(0, 16, 16, 16, Black);
        game.Textures["block/water_still"] = new PackTexture { Key = "block/water_still", Image = strip, Animation = new TextureAnimation { FrameTime = 2 } };
        var pack = new TexturePack { Name = "Mine" };
        var ed = new TextureEditor(pack, game);
        int packChanges = 0;
        ed.PackChanged += () => packChanges++;
        ed.Select("block/stone");
        ed.Session.Paste(Solid(4, 4, Red), "Paste", (0, 0));
        Program.Check(pack.Textures.ContainsKey("block/stone") && ed.Current!.Image[0, 0] == Red, "texture: a paste on a game texture puts it in the pack while it floats");
        ed.Session.Cancel();
        Program.Check(!pack.Textures.ContainsKey("block/stone") && ed.Current!.Image[0, 0] == 0xFF7F7F7F && game.Textures["block/stone"].Image[0, 0] == 0xFF7F7F7F,
            "texture: cancelled, it leaves the pack again and the game's texture was never touched");
        ed.Session.Paste(Solid(4, 4, Red), "Paste", (0, 0));
        ed.Select("block/water_still");
        Program.Check(pack.Textures["block/stone"].Image[0, 0] == Red && ed.UndoName == "Paste" && ed.Session.Selection == null, "texture: choosing another texture puts the paste down");
        // The second frame: the selection and pastes are in frame coordinates.
        ed.SelectFrame(1);
        ed.Session.Paste(Solid(2, 2, Red), "Paste", (15, 15));
        ed.Session.Commit();
        var water = pack.Textures["block/water_still"].Image;
        Program.Check(water[15, 31] == Red && water[15, 15] == White && water[14, 31] == Black && Count(water, Red) == 1, "texture: paste lands in the frame in view, clipped to it");
        ed.Undo();
        Program.Check(!pack.Textures.ContainsKey("block/water_still"), "texture: undo takes the texture out of the pack again");
        ed.Undo();
        Program.Check(!pack.Textures.ContainsKey("block/stone") && ed.Selected == "block/stone", "texture: undo the first paste, the stone shows again");
        // Flip a whole frame.
        ed.Select("block/water_still");
        ed.SelectFrame(0);
        ed.Session.Select(new PixelRect(0, 0, 4, 4));
        ed.Session.Delete();
        Program.Check(pack.Textures["block/water_still"].Image[0, 0] == 0 && pack.Textures["block/water_still"].Image[0, 16] == Black, "texture: delete in frame 1 only");
        // Strokes still work as before and leave nothing behind when they change nothing.
        ed.Select("block/stone");
        ed.Tool = PaintTool.Pencil;
        ed.Primary = 0xFF7F7F7F;
        ed.BeginStroke(3, 3, false);
        ed.EndStroke();
        Program.Check(!pack.Textures.ContainsKey("block/stone"), "texture: a stroke that changes nothing leaves the game texture out of the pack");
        Program.Check(packChanges > 0, "texture: the list hears about pack changes");
    }

    static void Colours()
    {
        var none = new ColorAdjustment();
        Program.Check(none.IsNone && ColorAdjust.Apply(0xFF336699, none) == 0xFF336699, "no change changes nothing");
        Program.Check(ColorAdjust.Apply(0xFF336699, new ColorAdjustment { Invert = true }) == 0xFFCC9966, "invert");
        uint g = ColorAdjust.Apply(0xFFFF0000, new ColorAdjustment { Grayscale = true });
        Program.Check(RgbaImage.R(g) == RgbaImage.G(g) && RgbaImage.G(g) == RgbaImage.B(g) && RgbaImage.R(g) == 76, "grey keeps how bright it looks");
        uint h = ColorAdjust.Apply(0xFFFF0000, new ColorAdjustment { Hue = 120 });
        Program.Check(h == 0xFF00FF00, "hue turn: red to green");
        Program.Check(ColorAdjust.Apply(0x80FF0000, new ColorAdjustment { Hue = 120 }) == 0x8000FF00 && ColorAdjust.Apply(0x00FF0000, new ColorAdjustment { Invert = true }) == 0x00FF0000,
            "opacity is kept; see-through pixels are left alone");
        Program.Check(ColorAdjust.Apply(0xFF808080, new ColorAdjustment { Brightness = -100 }) == Black && ColorAdjust.Apply(0xFF808080, new ColorAdjustment { Brightness = 100 }) == White,
            "brightness to black and white");
        uint tint = ColorAdjust.Apply(0xFFFFFFFF, new ColorAdjustment { Tint = 0xFF00FF00 });
        Program.Check(tint == 0xFF00FF00, "colourise white toward green");
        Program.Check(ColorAdjust.Apply(0xFF808080, new ColorAdjustment { Posterize = 2 }) == White && ColorAdjust.Apply(0xFF7F7F7F, new ColorAdjustment { Posterize = 2 }) == Black, "posterize to two levels");
        Program.Check(ColorAdjust.Apply(0xFF808080, new ColorAdjustment { Saturation = 100 }) == 0xFF808080, "grey stays grey however vivid");
    }

    static void Pictures()
    {
        // Scaling.
        var checker = new RgbaImage(4, 4);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                checker[x, y] = (x + y) % 2 == 0 ? Black : White;
        var avg = Resample.Scale(checker, 1, 1, smooth: true);
        Program.Check(Math.Abs(RgbaImage.R(avg[0, 0]) - 128) <= 1, "smooth shrink averages");
        var half = new RgbaImage(2, 1);
        half[0, 0] = Red;
        var mixed = Resample.Scale(half, 1, 1, smooth: true);
        Program.Check(RgbaImage.R(mixed[0, 0]) == 255 && RgbaImage.G(mixed[0, 0]) == 0 && Math.Abs(RgbaImage.A(mixed[0, 0]) - 128) <= 1, "see-through pixels do not darken a smooth shrink");
        Program.Check(Resample.Scale(checker, 8, 8, smooth: false)[1, 0] == Black && Resample.Scale(checker, 8, 8, smooth: false)[2, 0] == White, "nearest-pixel enlarging stays crisp");
        var up = Resample.Scale(Solid(2, 2, Red), 7, 5, smooth: true);
        Program.Check(up.Width == 7 && up.Height == 5 && Count(up, Red) == 35, "smooth enlarging of one colour stays that colour");
        Program.Check(Resample.RotateLeft(Resample.RotateRight(Pattern(3, 5))).SamePixels(Pattern(3, 5)), "turn right then left");

        // Palettes.
        var four = new RgbaImage(4, 1, [Red, Red, Blue, 0x00FFFFFF]);
        Program.Check(Palette.Colors(four).SequenceEqual([Red, Blue]), "colours used, most used first, see-through ignored");
        var grad = new RgbaImage(64, 1);
        for (int x = 0; x < 64; x++) grad[x, 0] = RgbaImage.Argb(255, x * 4, x * 4, x * 4);
        var cut = Palette.MedianCut(grad, 4);
        Program.Check(cut.Count == 4, "median cut: four colours");
        var mapped = Palette.Map(grad, cut, dither: false);
        Program.Check(Palette.Colors(mapped, 999).Count <= 4 && mapped[0, 0] == cut.OrderBy(c => RgbaImage.R(c)).First(), "mapped onto them");
        var dithered = Palette.Map(Solid(8, 8, 0xFF808080), [Black, White], dither: true);
        Program.Check(Count(dithered, Black) > 16 && Count(dithered, White) > 16, "dithering mixes the two to stand for grey");

        // Picture import: sizes, background, colours.
        Program.Check(PictureFit.Size(200, 100, 16, 16, PictureFitMode.Fit) == (16, 8) && PictureFit.Size(200, 100, 16, 16, PictureFitMode.Fill) == (32, 16) &&
                      PictureFit.Size(200, 100, 16, 16, PictureFitMode.Stretch) == (16, 16) && PictureFit.Size(200, 100, 16, 16, PictureFitMode.Original) == (200, 100), "fit, fill, stretch, original");
        var photo = Solid(200, 100, White);
        photo.Fill(80, 30, 40, 40, Red);
        var fit = PictureFit.Prepare(photo, 16, 16, new PictureOptions { Fit = PictureFitMode.Fill, RemoveBackground = true });
        Program.Check(fit.Width == 16 && fit.Height == 16 && fit[0, 0] == 0 && RgbaImage.R(fit[8, 8]) > 200 && RgbaImage.A(fit[8, 8]) == 255, "fill crops to the area; the plain background goes see-through");
        var two = PictureFit.Prepare(Pattern(32, 32), 16, 16, new PictureOptions { Colors = 3 });
        Program.Check(Palette.Colors(two, 999).Count <= 3, "reduced to three colours");
        var own = PictureFit.Prepare(Pattern(32, 32), 16, 16, new PictureOptions { UsePalette = true, Dither = true }, [Red, Blue]);
        Program.Check(own.Pixels.All(p => p == Red || p == Blue), "only the texture's own colours");
    }
}
