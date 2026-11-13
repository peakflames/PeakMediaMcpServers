namespace PeakMediaRemoteMcpServer.Broker.Endpoints;

/// <summary>
/// Binds the browser that started an authorization attempt to the one completing it at
/// <c>/oauth/callback</c> (confused-deputy defense): a second browser that learns a pending
/// transaction ID cannot finish someone else's sign-in.
/// </summary>
internal static class ConsentCookie
{
    public const string CookieName = "pm_consent";

    /// <summary>Sets the cookie and returns the secret whose hash goes on the transaction.</summary>
    public static string Issue(HttpContext httpContext, string txnId, TimeSpan lifetime, bool secure)
    {
        var secret = PkceHelper.GenerateOpaqueSecret();
        httpContext.Response.Cookies.Append(CookieName, $"{txnId}.{secret}", new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            MaxAge = lifetime,
            Path = "/oauth/callback",
        });
        return secret;
    }

    public static bool Validate(HttpContext httpContext, string txnId, string expectedHash)
    {
        if (!httpContext.Request.Cookies.TryGetValue(CookieName, out var raw) || string.IsNullOrEmpty(raw))
        {
            return false;
        }

        var dot = raw.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return false;
        }

        return string.Equals(raw[..dot], txnId, StringComparison.Ordinal) && TokenHashing.Verify(raw[(dot + 1)..], expectedHash);
    }

    public static void Clear(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/oauth/callback" });
    }
}
