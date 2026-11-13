namespace PeakMediaMcpTools.Narration;

/// <summary>
/// A clip's name in the shared cache: the lowercase hex SHA-256 of the UTF-8 bytes of
/// <c>model\0voice\0style\0text</c>, followed by <c>.wav</c>. Any difference in the four inputs is a
/// different clip.
/// </summary>
public static class ClipCacheKey
{
    public static string For(string model, string voice, string style, string text)
    {
        var joined = string.Join('\0', model, voice, style, text);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(joined))) + ".wav";
    }

    /// <summary>True when <paramref name="value"/> has the shape of a cache key (safe as a file name).</summary>
    public static bool IsWellFormed(string value)
    {
        return value.Length == 68
            && value.EndsWith(".wav", StringComparison.Ordinal)
            && value[..64].All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }
}
