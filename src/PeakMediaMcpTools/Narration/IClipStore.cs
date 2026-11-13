namespace PeakMediaMcpTools.Narration;

/// <summary>A clip found in the shared cache.</summary>
public sealed record CachedClip(string CacheKey, int DurationMs, long SizeBytes);

/// <summary>
/// The host's shared audio cache and download links. The gateway implements it over SQLite and the
/// audio directory, recording the signed-in caller as creator and as owner of each download link.
/// </summary>
public interface IClipStore
{
    /// <summary>The cached clip for <paramref name="cacheKey"/> (and marks it used), or null on a miss.</summary>
    Task<Result<CachedClip?>> FindAsync(string cacheKey, CancellationToken cancellationToken);

    /// <summary>Stores a synthesized WAV under its key so later calls find it.</summary>
    Task<Result> SaveAsync(string cacheKey, byte[] wav, int durationMs, CancellationToken cancellationToken);

    /// <summary>A fresh short-lived download URL for the clip, owned by the current caller.</summary>
    Task<Result<string>> IssueDownloadUrlAsync(string cacheKey, CancellationToken cancellationToken);
}
