namespace PeakMediaRemoteMcpServer.Broker;

/// <summary>
/// PKCE (RFC 7636, S256) primitives, used twice per flow: the client's pair (verified at
/// <c>POST /token</c>) and the gateway's own pair on the upstream leg to the OIDC provider. Also the
/// source of opaque secrets (codes, refresh tokens, client secrets, nonces, consent cookie).
/// </summary>
public static class PkceHelper
{
    public static string GenerateCodeVerifier()
    {
        return GenerateOpaqueSecret();
    }

    public static string ComputeS256Challenge(string codeVerifier)
    {
        return Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
    }

    public static bool VerifyS256(string codeVerifier, string expectedChallenge)
    {
        var actual = Encoding.ASCII.GetBytes(ComputeS256Challenge(codeVerifier));
        var expected = Encoding.ASCII.GetBytes(expectedChallenge);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>32 random bytes, base64url — an unguessable bearer secret and a valid 43-char code_verifier.</summary>
    public static string GenerateOpaqueSecret()
    {
        return Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    }
}
