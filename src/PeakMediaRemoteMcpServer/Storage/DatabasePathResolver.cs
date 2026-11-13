namespace PeakMediaRemoteMcpServer.Storage;

/// <summary>
/// Resolves <c>Storage:DatabasePath</c> to a writable file path. When the configured directory
/// cannot be created or written, falls back to a temp path and logs a warning — that fallback does
/// not survive a restart.
/// </summary>
public static class DatabasePathResolver
{
    public static string FallbackPath => Path.Combine(Path.GetTempPath(), "peakmedia", "peakmedia.db");

    public static string Resolve(string configuredPath, string contentRoot, Microsoft.Extensions.Logging.ILogger logger)
    {
        var path = Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(contentRoot, configuredPath);
        if (TryPrepareDirectory(path))
        {
            return path;
        }

        var fallback = FallbackPath;
        TryPrepareDirectory(fallback);
        logger.LogWarning(
            "Storage directory for {ConfiguredPath} is not writable; using the temporary database {FallbackPath}. Usage, quotas and cache in it do not survive a restart: make Storage:DatabasePath writable and restart.",
            path,
            fallback);
        return fallback;
    }

    private static bool TryPrepareDirectory(string databasePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".";
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
