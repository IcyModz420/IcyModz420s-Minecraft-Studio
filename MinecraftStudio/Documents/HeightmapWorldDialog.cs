// MinecraftStudio/Documents/HeightmapWorldDialog.cs: Create ▸ New World from a Heightmap — how a grey picture becomes land.
// Owns: HeightmapWorldDialog — the picture (a 16-bit PNG at full precision; any picture by its brightness), a preview
// (water and snow shown as they would be), the y of black and white (filled in from a file the map's Export ▸ Heights
// saved, which also says where it came from), water and snow lines, blocks per pixel, where it goes, name, game mode and
// what the game makes around it. "Next" hands a HeightmapWorld to the Convert window, which writes any edition and checks it.
using System.Drawing.Imaging;
using MinecraftStudio.Core.Rendering;
using MinecraftStudio.Core.World;
using MinecraftStudio.Studio;
using MinecraftStudio.UI;

namespace MinecraftStudio.Documents;

sealed class HeightmapWorldDialog : DarkDialog
{
    static readonly (string Text, double Value)[] Scales =
        [("1/4 (four pixels to a block)", 0.25), ("1/2", 0.5), ("1 block per pixel", 1), ("2 × 2 blocks", 2), ("4 × 4 blocks", 4), ("8 × 8 blocks", 8), ("16 × 16 blocks", 16)];

    readonly DarkTextField _file, _name;
    readonly Button _browse, _next;
    readonly PictureBox _preview;
    readonly NumberField _low, _high, _water, _snow;
    readonly DarkCheckBox _hasWater, _hasSnow;
    readonly DarkComboBox _scale, _place, _mode, _around;
    readonly Label _status;
    HeightPicture? _picture;
    string? _path;
    bool _filling;

    /// <summary>The land to convert, once Next is pressed.</summary>
    public HeightmapWorld? World { get; private set; }

    public HeightmapWorldDialog() : base("New world from a heightmap", 640)
    {
        var column = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        column.Controls.Add(new Label
        {
            Text = "Land from a grey picture: bright is high ground, dark is low — the heights you saved from a world (map ▸ Export ▸ Heights), or a picture from a terrain program or real elevation data. It gets grass over dirt, sand beaches, stone cliffs, snow on the peaks and water in the low ground. Next, choose the edition and version in the Convert window: it writes the world and checks it block by block.",
            AutoSize = true, MaximumSize = new Size(this.Px(600), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(0, 0, 0, this.Px(8)),
        });
        _file = new DarkTextField { Width = this.Px(360), Height = this.Px(28), AccessibleName = "Picture" };
        _file.Box.ReadOnly = true;
        _browse = Theme.Button("Choose…");
        _browse.Click += async (_, _) => await ChoosePicture();
        column.Controls.Add(Row("Picture", _file, _browse));
        _preview = new PictureBox { Size = new Size(this.Px(420), this.Px(220)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = C.BG3, Margin = new Padding(this.Px(156), this.Px(4), 0, this.Px(8)) };
        column.Controls.Add(_preview);

        _low = Num(40, -64, 319);
        _high = Num(160, -63, 320);
        column.Controls.Add(Row("Black is y", _low, Note("and white is y"), _high));
        _hasWater = new DarkCheckBox("Water up to y") { AutoSize = true, Margin = new Padding(0, this.Px(6), this.Px(6), 0) };
        _water = Num(62, -63, 319);
        _hasSnow = new DarkCheckBox("Snow from y", false) { AutoSize = true, Margin = new Padding(this.Px(12), this.Px(6), this.Px(6), 0) };
        _snow = Num(130, -63, 320);
        column.Controls.Add(Row("Water and snow", _hasWater, _water, _hasSnow, _snow));
        _scale = new DarkComboBox { Width = this.Px(240), AccessibleName = "Blocks per pixel" };
        foreach (var (text, _) in Scales) _scale.Items.Add(text);
        _scale.SelectedIndex = 2;
        column.Controls.Add(Row("Each pixel is", _scale));
        _place = new DarkComboBox { Width = this.Px(360), AccessibleName = "Where it goes" };
        _place.Items.Add("Its middle at x 0, z 0");
        _place.SelectedIndex = 0;
        column.Controls.Add(Row("Where it goes", _place));
        _name = new DarkTextField { Width = this.Px(360), Height = this.Px(28), AccessibleName = "Name" };
        _name.Text = "Heightmap World";
        column.Controls.Add(Row("Name", _name));
        _mode = new DarkComboBox { Width = this.Px(160), AccessibleName = "Game mode" };
        _mode.Items.AddRange(["Creative", "Survival"]);
        _mode.SelectedIndex = 0;
        _around = new DarkComboBox { Width = this.Px(220), AccessibleName = "Around it" };
        _around.Items.AddRange(["the game's own land", "flat land"]);
        _around.SelectedIndex = 0;
        column.Controls.Add(Row("Game mode", _mode));
        column.Controls.Add(Row("Around it", _around));

        _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(this.Px(600), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Padding = new Padding(0, this.Px(8), 0, 0) };
        var buttons = AddButtons(("Cancel", DialogResult.Cancel, false));
        _next = Theme.Button("Next: choose the edition…", true);
        _next.MinimumSize = new Size(this.Px(200), this.Px(30));
        _next.Click += (_, _) => Next();
        buttons.Controls.Add(_next);
        Controls.Add(_status);
        Controls.Add(column);
        SizeToContent();

        foreach (var n in new[] { _low, _high, _water, _snow }) n.ValueChanged += (_, _) => Changed();
        foreach (var c in new[] { _hasWater, _hasSnow }) c.CheckedChanged += (_, _) => Changed();
        _scale.SelectedIndexChanged += (_, _) => Changed();
        _place.SelectedIndexChanged += (_, _) => Changed();
        Changed();
    }

    NumberField Num(int v, int min, int max) => new() { Width = this.Px(72), Height = this.Px(28), Minimum = min, Maximum = max, Value = v, Margin = new Padding(0, 0, this.Px(6), 0) };

    Label Note(string text) => new() { Text = text, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(this.Px(6), this.Px(7), this.Px(6), 0) };

    FlowLayoutPanel Row(string label, params Control[] fields)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, this.Px(2), 0, this.Px(2)) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = C.FG_DIM, UseMnemonic = false, MinimumSize = new Size(this.Px(150), 0), Margin = new Padding(0, this.Px(7), this.Px(6), 0) });
        foreach (var f in fields) row.Controls.Add(f);
        return row;
    }

    /// <summary>Opens the dialog with a picture already chosen (a file dropped or passed in).</summary>
    public async Task LoadPicture(string path)
    {
        _status.ForeColor = C.FG_DIM;
        _status.Text = "Reading the picture…";
        _browse.Enabled = _next.Enabled = false;
        try
        {
            var picture = await Task.Run(() =>
            {
                byte[] data = File.ReadAllBytes(path);
                return Core.Imaging.Png.IsPng(data) ? Heightmap.Read(data) : Heightmap.FromImage(PixelClipboard.Decode(data, Path.GetExtension(path).ToLowerInvariant()));
            });
            _picture = picture;
            _path = path;
            _file.Text = path;
            _filling = true;
            if (picture.HasRange)
            {
                _low.Value = picture.LowY!.Value;
                _high.Value = picture.HighY!.Value;
            }
            _place.Items.Clear();
            _place.Items.Add("Its middle at x 0, z 0");
            if (picture.WestX is int wx && picture.NorthZ is int nz)
            {
                _place.Items.Add($"Where it was saved from (north-west corner x {wx:N0}, z {nz:N0})");
                _place.SelectedIndex = 1;
                _scale.SelectedIndex = 2;
            }
            else _place.SelectedIndex = 0;
            if (_name.Text == "Heightmap World") _name.Text = Core.IO.SafeName.For(Path.GetFileNameWithoutExtension(path), "Heightmap World", 60);
            _filling = false;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _filling = false;
            _browse.Enabled = true;
            Changed();
            _status.ForeColor = C.ORANGE;
            _status.Text = "That picture can't be read: " + ex.Message;
            return;
        }
        finally { _filling = false; }
        _browse.Enabled = true;
        Changed();
    }

    async Task ChoosePicture()
    {
        using var dlg = new OpenFileDialog { Title = "A heightmap picture (bright = high)", Filter = PixelClipboard.OpenFilter };
        if (dlg.ShowDialog(this) == DialogResult.OK) await LoadPicture(dlg.FileName);
    }

    HeightmapLand? Land(out string? problem)
    {
        problem = null;
        if (_picture == null) { problem = "Choose a picture first."; return null; }
        int low = (int)_low.Value, high = (int)_high.Value;
        if (high <= low) { problem = "White must be higher than black."; return null; }
        bool saved = _place.SelectedIndex == 1;
        return new HeightmapLand
        {
            Picture = _picture, LowY = low, HighY = high,
            WaterY = _hasWater.Checked ? (int)_water.Value : null, SnowY = _hasSnow.Checked ? (int)_snow.Value : null,
            BlocksPerPixel = Scales[Math.Max(0, _scale.SelectedIndex)].Value,
            WestX = saved ? _picture.WestX : null, NorthZ = saved ? _picture.NorthZ : null,
            Name = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : "Heightmap World",
            GameType = _mode.SelectedIndex == 1 ? 0 : 1, FlatAround = _around.SelectedIndex == 1,
        };
    }

    void Changed()
    {
        if (_filling) return;
        _water.Enabled = _hasWater.Checked;
        _snow.Enabled = _hasSnow.Checked;
        var land = Land(out string? problem);
        HeightmapWorld? world = null;
        if (land != null)
        {
            try { world = new HeightmapWorld(land, _path!); }
            catch (ArgumentException ex) { problem = ex.Message; }
        }
        _next.Enabled = world != null;
        _status.ForeColor = problem == null ? C.FG_DIM : C.ORANGE;
        if (world != null)
        {
            long chunks = (long)((world.Width + 15) / 16 + 1) * ((world.Length + 15) / 16 + 1);
            _status.Text = $"{_picture!.Width:N0} × {_picture.Height:N0} pixels make land {world.Width:N0} × {world.Length:N0} blocks (up to {chunks:N0} chunks), " +
                $"x {world.WestX:N0} to {world.WestX + world.Width - 1:N0}, z {world.NorthZ:N0} to {world.NorthZ + world.Length - 1:N0}." +
                (_picture.HasRange ? $" The file was saved with heights y {_picture.LowY} to {_picture.HighY}." : "");
        }
        else _status.Text = problem ?? "";
        DrawPreview(land);
    }

    // The picture as the land would look from above: grey by height, the low ground under water blue, snow white.
    void DrawPreview(HeightmapLand? land)
    {
        var old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
        if (_picture == null || land == null) return;
        var p = _picture;
        double fit = Math.Min(1.0, Math.Min(this.Px(420) / (double)p.Width, this.Px(220) / (double)p.Height));
        int w = Math.Max(1, (int)(p.Width * fit)), h = Math.Max(1, (int)(p.Height * fit));
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var px = new int[w * h];
        int range = land.HighY - land.LowY;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float level = p.Levels[Math.Min(p.Height - 1, (int)(y / fit)) * p.Width + Math.Min(p.Width - 1, (int)(x / fit))];
                int c;
                if (float.IsNaN(level)) c = ((x >> 3) + (y >> 3)) % 2 == 0 ? 0x3A3A3A : 0x2A2A2A;
                else
                {
                    int ground = land.LowY + (int)Math.Round(level * range);
                    int g = 40 + (int)(level * 200);
                    if (land.WaterY is int water && ground < water)
                    {
                        int depth = Math.Min(40, water - ground);
                        c = (20 << 16) | ((90 - depth) << 8) | (200 - depth * 2);
                    }
                    else if (land.SnowY is int snow && ground >= snow) c = 0xF0F4FF;
                    else c = (g << 16) | (Math.Min(255, g + 10) << 8) | g;
                }
                px[y * w + x] = unchecked((int)0xFF000000) | c;
            }
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        _preview.Image = bmp;
    }

    void Next()
    {
        var land = Land(out string? problem);
        if (land == null) { _status.ForeColor = C.ORANGE; _status.Text = problem; return; }
        try { World = new HeightmapWorld(land, _path!); }
        catch (ArgumentException ex) { _status.ForeColor = C.ORANGE; _status.Text = ex.Message; return; }
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview.Image?.Dispose();
        base.Dispose(disposing);
    }
}
