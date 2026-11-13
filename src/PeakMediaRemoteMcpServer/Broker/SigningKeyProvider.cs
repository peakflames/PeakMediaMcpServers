namespace PeakMediaRemoteMcpServer.Broker;

/// <summary>
/// The RSA-2048 key the gateway signs its access tokens with. Generated once and persisted in
/// <c>app_meta</c>, so a restart does not invalidate tokens in flight. Loaded lazily on first use, so
/// constructing this type never touches the database.
/// </summary>
public sealed class SigningKeyProvider
{
    private const string KeyMaterialMetaKey = "signing_key_pkcs8_base64";

    private readonly Lazy<RsaSecurityKey> _key;

    public SigningKeyProvider(AppMetaStore appMeta)
    {
        _key = new Lazy<RsaSecurityKey>(() => LoadOrCreate(appMeta), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public RsaSecurityKey Key => _key.Value;

    private static RsaSecurityKey LoadOrCreate(AppMetaStore appMeta)
    {
        using var candidate = RSA.Create(2048);
        var stored = appMeta
            .GetOrAddAsync(KeyMaterialMetaKey, Convert.ToBase64String(candidate.ExportPkcs8PrivateKey()))
            .GetAwaiter()
            .GetResult();
        if (stored.IsFailed)
        {
            throw new InvalidOperationException($"The token-signing key could not be loaded: {stored.Errors[0].Message}. Check Storage:DatabasePath and restart.");
        }

        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(stored.Value), out _);
        var keyId = Base64UrlEncoder.Encode(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()))[..16];
        return new RsaSecurityKey(rsa) { KeyId = keyId };
    }
}
