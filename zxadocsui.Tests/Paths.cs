namespace zxadocsui.Tests;

// Locates the zxadocsui project from the test binary, so tests can read committed assets
// (the Tailwind output, the editor JS) without depending on the working directory.
internal static class Paths
{
    private static readonly string Root = FindRepoRoot();

    internal static string Ui(params string[] parts) =>
        Path.Combine(new[] { Root, "zxadocsui" }.Concat(parts).ToArray());

    internal static string Wwwroot(params string[] parts) =>
        Ui(new[] { "wwwroot" }.Concat(parts).ToArray());

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DocsApp.sln"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate DocsApp.sln above {AppContext.BaseDirectory}.");
    }
}
