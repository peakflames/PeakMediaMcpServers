namespace PeakMediaTestSupport;

/// <summary>Locates the repository and the built server binaries the process-level tests launch.</summary>
public static class RepoPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>Debug or Release, taken from where the test assembly itself was built.</summary>
    public static string Configuration { get; } =
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? "Release"
            : "Debug";

    public static string BinDirectory(string project)
    {
        return Path.Combine(RepoRoot, "src", project, "bin", Configuration, "net11.0");
    }

    public static string ServerDll(string project)
    {
        var path = Path.Combine(BinDirectory(project), $"{project}.dll");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Built server not found at {path}. Build the solution first.", path);
        }

        return path;
    }

    public static string LogDirectory(string project)
    {
        return Path.Combine(BinDirectory(project), "logs");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PeakMediaMcpServers.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("PeakMediaMcpServers.sln not found above the test binaries.");
    }
}
