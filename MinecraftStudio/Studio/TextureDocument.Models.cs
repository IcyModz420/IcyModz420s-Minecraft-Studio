// MinecraftStudio/Studio/TextureDocument.Models.cs: the Texture Studio's block and item models (Java) — the selected
// texture on its block in 3D while it is painted, and the model window.
// Part of TextureDocument (main part in TextureDocument.cs).
// Owns: the models of the document (JavaModelLibrary: the pack's own models, then those of the Java installed on this PC
// that fits the pack, read once in the background), each model's texture ids looked up in the pack the way the studio
// names textures, the "ON A BLOCK" preview (AnimatedModelView, redrawn as the texture changes) and opening BlockModelDialog.
using MinecraftStudio.Core.Imaging;
using MinecraftStudio.Core.Models;
using MinecraftStudio.Core.Packs;
using MinecraftStudio.UI;

namespace MinecraftStudio.Studio;

partial class TextureDocument
{
    static readonly Dictionary<string, Dictionary<string, Func<byte[]>>> GameModelFiles = new(StringComparer.OrdinalIgnoreCase);
    Task<JavaModelLibrary>? _models;
    AnimatedModelView? _blockView;
    Label? _blockNote;
    ModelMesh? _blockMesh;
    RgbaImage? _blockAtlas;
    string? _blockModel;
    BlockModelDialog? _modelWindow;
    int _blockTicket;

    /// <summary>The model the "ON A BLOCK" preview shows, and its faces (for the checks).</summary>
    internal string? BlockModelShown => _blockModel;
    internal ModelMesh? BlockMesh => _blockMesh;

    /// <summary>The models of this pack and of the game, read once (in the background) when first wanted.</summary>
    internal Task<JavaModelLibrary> Models() => _models ??= Task.Run(() =>
    {
        var release = _editor.Pack.JavaRelease ?? JavaReleases.Newest;
        // The installed release the pack is for, else the newest with today's folder names.
        var jars = GameFolders.JavaJars();
        var jar = jars.FirstOrDefault(j => JavaReleases.Of(j.Version) == release);
        if (jar.Jar == null) jar = jars.FirstOrDefault(j => JavaReleases.Of(j.Version)?.FlatNames == true);
        Dictionary<string, Func<byte[]>>? files = null;
        if (jar.Jar != null)
            lock (GameModelFiles)
                if (!GameModelFiles.TryGetValue(jar.Jar, out files))
                {
                    try { files = TexturePackReader.Files(jar.Jar, p => p.StartsWith("assets/", StringComparison.Ordinal) && p.Contains("/models/", StringComparison.Ordinal) && p.EndsWith(".json", StringComparison.Ordinal)); }
                    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { files = null; }
                    if (files != null) GameModelFiles[jar.Jar] = files;
                }
        return new JavaModelLibrary(_editor.Pack.OtherFiles, files, files == null ? null : jar.Version);
    });

    /// <summary>The release the model texture names are read for (the game's models use its own names).</summary>
    JavaRelease ModelRelease(JavaModelLibrary lib) => (lib.GameVersion is string v ? JavaReleases.Of(v) : null) ?? _editor.Pack.JavaRelease ?? JavaReleases.Newest;

    /// <summary>A model's texture id ("block/stone") as the picture the pack (or the game) has now, its first frame.</summary>
    internal RgbaImage? ModelTexture(string id, JavaModelLibrary lib)
    {
        string rel = JavaModelLibrary.Normal(id);
        string key = _names.FromJava(rel, ModelRelease(lib)) ?? "java:" + rel;
        return _editor.Texture(key)?.FirstFrame();
    }

    /// <summary>The model texture id of a studio texture ("block/stone" for the shared name), or null.</summary>
    internal string? ModelTextureId(string shared, JavaModelLibrary lib) =>
        shared.StartsWith("java:", StringComparison.Ordinal) ? shared[5..] : _names.JavaNames(shared, ModelRelease(lib)).FirstOrDefault();

    /// <summary>The "ON A BLOCK" section of the side panel: the selected texture on the first model that uses it.</summary>
    Control[] BlockPreview()
    {
        _blockView = new AnimatedModelView(() => _blockMesh, () => _blockAtlas, () => null, () => _blockModel ?? "", "Choose a block texture to see it on its block.")
        {
            Width = this.Px(210), Height = this.Px(210), Margin = new Padding(this.Px(6), this.Px(2), 0, 0),
        };
        _blockNote = new Label { AutoSize = true, MaximumSize = new Size(this.Px(214), 0), ForeColor = C.FG_DIM, UseMnemonic = false, Margin = new Padding(this.Px(6), this.Px(3), 0, 0) };
        var open = Theme.Button("Models…");
        open.Margin = new Padding(this.Px(6), this.Px(4), 0, 0);
        open.Click += async (_, _) => await ShowModels();
        _editor.SelectionChanged += () => _ = ShowBlock(true);
        _editor.ImageChanged += () => _ = ShowBlock(false);
        return [_blockView, _blockNote, open];
    }

    // The selected texture on its block (a new texture: find its model; painting: only the pictures again).
    async Task ShowBlock(bool newTexture)
    {
        if (_blockView == null || _editor.Selected is not string shared) return;
        int ticket = ++_blockTicket;
        try
        {
            var lib = await Models();
            if (newTexture || _blockModel == null)
            {
                string? id = ModelTextureId(shared, lib);
                _blockModel = id == null ? null : await Task.Run(() => lib.UsingTexture(id, 1).FirstOrDefault());
            }
            if (ticket != _blockTicket || IsDisposed) return;
            if (_blockModel == null || lib.Resolve(_blockModel) is not { } model)
            {
                (_blockMesh, _blockAtlas) = (null, null);
                _blockNote!.Text = lib.GameVersion == null ? "Minecraft Java isn't installed on this PC, so only the pack's own models can be shown." : "No block or item model uses this texture.";
            }
            else
            {
                var (mesh, atlas, _) = JavaModelMesh.Build(model, t => ModelTexture(t, lib));
                (_blockMesh, _blockAtlas) = (mesh, atlas);
                _blockNote!.Text = lib.InPack(_blockModel) ? $"{_blockModel} (the pack's own model)" : $"{_blockModel} (Java {lib.GameVersion})";
            }
            _blockView.Redraw();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (_blockNote != null) _blockNote.Text = "The game's models can't be read: " + e.Message;
        }
    }

    /// <summary>The model window: the models that use the selected texture, any model, its boxes, faces and textures.</summary>
    internal async Task ShowModels()
    {
        if (_modelWindow is { IsDisposed: false }) { _modelWindow.Activate(); return; }
        var lib = await Models();
        string? start = _blockModel;
        if (start == null && _editor.Selected is string shared && ModelTextureId(shared, lib) is string id) start = lib.UsingTexture(id, 1).FirstOrDefault();
        _modelWindow = new BlockModelDialog(this, _editor, lib, start);
        _modelWindow.FormClosed += (_, _) => { _modelWindow = null; _ = ShowBlock(true); };
        _modelWindow.Show(FindForm());
    }
}
