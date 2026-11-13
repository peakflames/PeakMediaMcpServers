namespace PeakMediaRemoteMcpServer.Broker.Storage;

/// <summary>
/// SHA-256 hashing for secrets the gateway only ever verifies — authorization codes, refresh tokens,
/// client secrets and the consent-cookie value — so a copy of the database holds nothing usable.
/// </summary>
public static class TokenHashing
{
    public static string Hash(string secret)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    /// <summary>Constant-time comparison of a candidate secret against a stored hash.</summary>
    public static bool Verify(string candidateSecret, string storedHash)
    {
        var candidate = Encoding.UTF8.GetBytes(Hash(candidateSecret));
        var expected = Encoding.UTF8.GetBytes(storedHash);
        return candidate.Length == expected.Length && CryptographicOperations.FixedTimeEquals(candidate, expected);
    }
}
