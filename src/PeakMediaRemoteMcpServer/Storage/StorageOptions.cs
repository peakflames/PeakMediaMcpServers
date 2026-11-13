namespace PeakMediaRemoteMcpServer.Storage;

/// <summary>The <c>Storage</c> section.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>SQLite file path; relative paths resolve against the content root (next to the binary).</summary>
    public string DatabasePath { get; set; } = "data/peakmedia.db";
}
