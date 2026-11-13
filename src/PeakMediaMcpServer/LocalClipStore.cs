namespace PeakMediaMcpServer;

/// <summary>
/// The solo user's local audio cache: one WAV file per clip, named by its cache key, in
/// <c>PEAK_MEDIA_TTS_CACHE</c> when set, else <c>~/.cache/peak-media/tts</c> — the same directory the
/// peak-media skills download gateway clips into, so both paths share one cache. No database: a file is a
/// hit only when it is a complete 16-bit mono 24 kHz WAV; anything else is a miss and is overwritten.
/// Each clip entry carries the file's full <c>path</c> in place of a download <c>url</c>.
/// </summary>
internal sealed class LocalClipStore(string directory) : IClipStore
{
    public const string CacheVariableName = "PEAK_MEDIA_TTS_CACHE";

    public string Directory { get; } = directory;

    public string LocationKey => NarrationResultFormatter.PathKey;

    /// <summary>The cache directory: <c>PEAK_MEDIA_TTS_CACHE</c>, else <c>&lt;home&gt;/.cache/peak-media/tts</c>.</summary>
    public static string ResolveDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(CacheVariableName);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".cache", "peak-media", "tts");
    }

    public async Task<Result<CachedClip?>> FindAsync(string cacheKey, CancellationToken cancellationToken)
    {
        var path = FileFor(cacheKey);
        if (!File.Exists(path))
        {
            return Result.Ok<CachedClip?>(null);
        }

        try
        {
            var wav = await File.ReadAllBytesAsync(path, cancellationToken);
            var duration = WavAudio.ReadDurationMs(wav);
            if (duration.IsFailed)
            {
                Log.Warning("A cached clip file is not a valid WAV; it will be synthesized again");
                return Result.Ok<CachedClip?>(null);
            }

            return Result.Ok<CachedClip?>(new CachedClip(cacheKey, duration.Value, wav.LongLength));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning("A cached clip file could not be read ({Kind}); it will be synthesized again", ex.GetType().Name);
            return Result.Ok<CachedClip?>(null);
        }
    }

    public async Task<Result> SaveAsync(string cacheKey, byte[] wav, int durationMs, CancellationToken cancellationToken)
    {
        var path = FileFor(cacheKey);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            await File.WriteAllBytesAsync(temporary, wav, cancellationToken);
            File.Move(temporary, path, overwrite: true);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("A clip could not be written to the local cache directory {Directory} ({Kind})", Directory, ex.GetType().Name);
            TryDelete(temporary);
            return Result.Fail("the local cache directory is not writable");
        }
    }

    public Task<Result<string>> LocateAsync(string cacheKey, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Ok(FileFor(cacheKey)));
    }

    private string FileFor(string cacheKey)
    {
        return Path.Combine(Directory, cacheKey);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a stray .tmp file is never read as a clip.
        }
    }
}
