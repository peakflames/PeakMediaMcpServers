namespace PeakMediaRemoteMcpServer.Auth;

/// <summary>
/// The one rule that turns an ID token's groups into the <c>admin</c> role: the groups contain the
/// configured <c>Admin:Group</c> (exact, case-sensitive). An empty <c>Admin:Group</c> grants nobody.
/// Used by the broker's callback and the dashboard sign-in alike.
/// </summary>
public static class AdminGroup
{
    public static bool Grants(IEnumerable<string> groups, string adminGroup)
    {
        return !string.IsNullOrWhiteSpace(adminGroup) && groups.Contains(adminGroup, StringComparer.Ordinal);
    }
}
