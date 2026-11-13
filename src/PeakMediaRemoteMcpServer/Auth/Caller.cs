namespace PeakMediaRemoteMcpServer.Auth;

/// <summary>
/// The signed-in caller, built only from a validated gateway access token's <c>sub</c>, <c>email</c>
/// and <c>role</c> claims. <see cref="Subject"/> is the owner key of every caller-owned record.
/// </summary>
public sealed record Caller(string Subject, string Email, bool IsAdmin)
{
    public static Result<Caller> From(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return Result.Fail("the caller is not signed in");
        }

        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return Result.Fail("the access token carries no subject");
        }

        var email = principal.FindFirst("email")?.Value ?? string.Empty;
        var isAdmin = string.Equals(principal.FindFirst(JwtIssuer.RoleClaim)?.Value, JwtIssuer.AdminRole, StringComparison.Ordinal);
        return Result.Ok(new Caller(subject, email, isAdmin));
    }
}
