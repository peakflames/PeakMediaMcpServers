namespace PeakMediaTestSupport;

/// <summary>A fresh directory per test (the test-only data reset); deleted on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "peakmedia-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name)
    {
        return System.IO.Path.Combine(Path, name);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A process may still hold a file briefly; the OS temp cleaner removes leftovers.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}
