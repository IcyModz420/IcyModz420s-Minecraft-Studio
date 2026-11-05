/// <summary>
/// Where the tests find their worlds and put their output: the TestData folder next to MinecraftStudio.sln (downloaded
/// sample worlds of every edition), and a folder in %TEMP% for converted worlds.
/// </summary>
static class TestPaths
{
    public static readonly string Repo = RepoRoot();
    public static readonly string Data = Path.Combine(Repo, "TestData");
    public static readonly string Output = Path.Combine(Path.GetTempPath(), "MinecraftStudio.Tests");

    static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "MinecraftStudio.sln")) || File.Exists(Path.Combine(d.FullName, "MinecraftSaveStudio.sln"))) return d.FullName;
        return Directory.GetCurrentDirectory();
    }
}
