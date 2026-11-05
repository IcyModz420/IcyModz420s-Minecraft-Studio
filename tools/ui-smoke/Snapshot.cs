// tools/ui-smoke/Snapshot.cs: a picture of a window that was never shown. A hidden form creates its child windows only when
// something needs them, so every control's window is made first, the way showing the form makes them (a window made early,
// before its parent's — a tool strip — is moved into its parent then), then the form is printed once with its children.
using System.Reflection;

static class Snapshot
{
    // Control.CreateControl(ignoreVisible): what showing a form runs; made to run for windows that are not shown too.
    static readonly MethodInfo? CreateAll = typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(bool)], null);

    /// <summary>Every control's window made and in its parent (background work reports back through them, and a picture needs them).</summary>
    public static void Create(Control c)
    {
        CreateAll?.Invoke(c, [true]);
        Handles(c);
    }

    static void Handles(Control c)
    {
        _ = c.Handle;
        foreach (Control child in c.Controls) Handles(child);
    }

    public static Bitmap Of(Form f)
    {
        Create(f);
        f.PerformLayout();
        var bmp = new Bitmap(Math.Max(1, f.Width), Math.Max(1, f.Height));
        f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        return bmp;
    }
}
