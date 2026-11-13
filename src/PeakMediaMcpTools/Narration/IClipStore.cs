namespace PeakMediaMcpTools.Narration;

/// <summary>A clip found in the shared cache.</summary>
public sealed record CachedClip(string CacheKey, int DurationMs, long SizeBytes);

/// <summary>
/// The host's audio cache and where a caller finds each clip. The gateway implements it over SQLite and
/// the audio directory, recording the signed-in caller as creator and as owner of each download link
/// (<see cref="LocationKey"/> <c>url</c>); the stdio server implements it over a local directory and
/// hands back file paths (<see cref="LocationKey"/> <c>path</c>).
/// </summary>
public interface IClipStore
{
    /// <summary>The cached clip for <paramref name="cacheKey"/> (and marks it used), or null on a miss.</summary>
    Task<Result<CachedClip?>> FindAsync(string cacheKey, CancellationToken cancellationToken);

    /// <summary>Stores a synthesized WAV under its key so later calls find it.</summary>
    Task<Result> SaveAsync(string cacheKey, byte[] wav, int durationMs, CancellationToken cancellationToken);

    /// <summary>The name of the clip-entry field that carries <see cref="LocateAsync"/>'s answer: <c>url</c> or <c>path</c>.</summary>
    string LocationKey { get; }

    /// <summary>Where the caller gets the clip: a fresh short-lived download URL (gateway) or the file's full path (stdio).</summary>
    Task<Result<string>> LocateAsync(string cacheKey, CancellationToken cancellationToken);
}
