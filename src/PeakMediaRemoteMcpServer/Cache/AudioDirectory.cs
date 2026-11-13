namespace PeakMediaRemoteMcpServer.Cache;

/// <summary>Where the cached WAV files live: <c>Storage:AudioDirectory</c>, or <c>audio/</c> beside the database.</summary>
public sealed class AudioDirectory
{
    public AudioDirectory(string path)
    {
        Path = path;
        Directory.CreateDirectory(path);
    }

    public string Path { get; }

    public static AudioDirectory Resolve(StorageOptions storage, string databasePath, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(storage.AudioDirectory))
        {
            var databaseDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(databasePath)) ?? contentRoot;
            return new AudioDirectory(System.IO.Path.Combine(databaseDirectory, "audio"));
        }

        var configured = storage.AudioDirectory;
        return new AudioDirectory(System.IO.Path.IsPathRooted(configured) ? configured : System.IO.Path.Combine(contentRoot, configured));
    }

    /// <summary>The file for a well-formed cache key (the key is hex + <c>.wav</c>, so it is a safe file name).</summary>
    public string FileFor(string cacheKey)
    {
        return System.IO.Path.Combine(Path, cacheKey);
    }
}
