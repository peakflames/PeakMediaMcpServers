namespace PeakMediaRemoteMcpServer.Broker;

/// <summary>
/// Redirect-URI rules. At registration (DCR is open) a URI must be a loopback http(s) URI on any port
/// (RFC 8252 §7.3) or listed in <c>Broker:AllowedRedirectUris</c> — this closes the open-DCR phishing
/// hole where an attacker registers their own redirect URI and mails a victim an <c>/authorize</c>
/// link. At <c>/authorize</c> the URI must match a registered one exactly, ignoring only the port of
/// a loopback URI. Comparison is on parsed <see cref="Uri"/> parts, so
/// <c>http://localhost@evil.com/</c> is host <c>evil.com</c>.
/// </summary>
public static class RedirectUriValidator
{
    public static bool IsAllowedForRegistration(string candidate, IReadOnlyList<string> allowedRedirectUris)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (IsLoopback(uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        return allowedRedirectUris.Any(allowed => Uri.TryCreate(allowed, UriKind.Absolute, out var allowedUri) && ExactMatch(uri, allowedUri));
    }

    public static bool MatchesRegistered(string candidate, IReadOnlyList<string> registeredRedirectUris)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var candidateUri))
        {
            return false;
        }

        foreach (var raw in registeredRedirectUris)
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var registered))
            {
                continue;
            }

            if (ExactMatch(candidateUri, registered))
            {
                return true;
            }

            if (IsLoopback(registered) && IsLoopback(candidateUri) && MatchesIgnoringPort(candidateUri, registered))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExactMatch(Uri a, Uri b)
    {
        return a.Port == b.Port && MatchesIgnoringPort(a, b);
    }

    private static bool MatchesIgnoringPort(Uri a, Uri b)
    {
        return string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.AbsolutePath, b.AbsolutePath, StringComparison.Ordinal)
            && string.Equals(a.Query, b.Query, StringComparison.Ordinal)
            && string.IsNullOrEmpty(a.UserInfo);
    }

    private static bool IsLoopback(Uri uri)
    {
        return string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(uri.Host, "[::1]", StringComparison.Ordinal)
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }
}
