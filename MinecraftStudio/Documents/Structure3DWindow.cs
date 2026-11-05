// MinecraftStudio/Documents/Structure3DWindow.cs: a structure, or a piece of a world, turned in 3D.
// Owns: Structure3DWindow — the structure's mesh (StructureModel.Mesh: the faces that can be seen, in the map's colours,
// flat runs of one colour drawn as one) made in the background and drawn by the Animation Studio's 3D view in its
// "world" mode (drag to turn, middle-drag to move, wheel to zoom, right-click for the view from each side, double-click
// to frame it again). Not modal, read only; opened from the structure previews, the Structure File window and the map.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Structures;
using MinecraftStudio.Studio;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class Structure3DWindow : DarkDialog
{
    readonly AnimatedModelView _view;
    readonly Label _status;
    readonly string _what;
    ModelMesh? _mesh;
    RgbaImage? _colors;

    public Structure3DWindow(Structure s, string title) : base(title, 900)
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(this.Px(900), this.Px(640));
        MinimumSize = new Size(this.Px(420), this.Px(320));
        Padding = new Padding(this.Px(8));
        _what = $"{s.SizeX:N0} × {s.SizeY:N0} × {s.SizeZ:N0} blocks";
        _view = new AnimatedModelView(() => _mesh, () => _colors, () => null, () => _mesh == null ? "" : _what, "Making the 3D view…", world: true) { Dock = DockStyle.Fill };
        _status = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = this.Px(46), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(6), 0, 0),
            Text = "Drag to turn it, middle-drag to move it, the wheel zooms; right-click for the view from each side, double-click to see it all again.",
        };
        Controls.Add(_view);
        Controls.Add(_status);
        _ = MakeAsync(s);
    }

    /// <summary>Opens the 3D view of <paramref name="s"/> beside <paramref name="owner"/>.</summary>
    public static void Open(IWin32Window? owner, Structure s, string title)
    {
        var w = new Structure3DWindow(s, title);
        w.FormClosed += (_, _) => w.Dispose();
        w.Show(owner);
    }

    /// <summary>Makes the mesh (a big piece takes a moment); the view shows it when it is ready.</summary>
    internal async Task MakeAsync(Structure s)
    {
        try
        {
            var (mesh, colors, leftOut) = await Task.Run(() => StructureModel.Mesh(s));
            if (IsDisposed) return;
            (_mesh, _colors) = (mesh, colors);
            _view.ResetView();
            if (mesh.Quads.Count == 0) _status.Text = "There is nothing in it with a shape to show (only air, or only small things such as plants and torches).";
            else if (leftOut > 0) _status.Text += $"  ({leftOut:N0} small block{(leftOut == 1 ? "" : "s")} — plants, torches, rails, doors — are not drawn.)";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Making a 3D view failed", ex);
            if (!IsDisposed) { _status.ForeColor = C.ORANGE; _status.Text = "The 3D view could not be made: " + ex.Message; }
        }
    }

    /// <summary>The mesh once it is made (for the checks).</summary>
    internal ModelMesh? Mesh => _mesh;
}
