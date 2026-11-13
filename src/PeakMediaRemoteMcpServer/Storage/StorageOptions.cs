namespace PeakMediaRemoteMcpServer.Storage;

/// <summary>The <c>Storage</c> section.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>SQLite file path; relative paths resolve against the content root (next to the binary).</summary>
    public string DatabasePath { get; set; } = "data/peakmedia.db";

    /// <summary>
    /// Directory holding the cached WAV files. Empty means <c>audio/</c> beside the resolved database
    /// file; relative paths resolve against the content root.
    /// </summary>
    public string AudioDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Upper bound of the audio cache in GiB (2^30 bytes). The eviction service removes
    /// least-recently-used clips until the cache fits. Must be greater than 0. Default 10.
    /// </summary>
    public double CacheMaxGb { get; set; } = 10;

    /// <summary>Minutes between eviction passes. Default 10.</summary>
    public int EvictionIntervalMinutes { get; set; } = 10;
}
