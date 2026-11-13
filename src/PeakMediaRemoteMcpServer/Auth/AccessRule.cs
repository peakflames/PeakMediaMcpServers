namespace PeakMediaRemoteMcpServer.Auth;

/// <summary>
/// The one access rule, owner-or-permitted-role: a caller may read or write a record they own (its
/// owner subject is theirs), and an administrator may read or write any record. Every tool, page and
/// query that touches caller-owned data decides through here — never per handler, never in the UI.
/// </summary>
public static class AccessRule
{
    public static bool CanAccess(Caller caller, string ownerSubject)
    {
        return caller.IsAdmin || string.Equals(caller.Subject, ownerSubject, StringComparison.Ordinal);
    }

    public static Result RequireAdmin(Caller caller)
    {
        return caller.IsAdmin ? Result.Ok() : Result.Fail("this needs the admin role");
    }

    public static bool IsAdmin(ClaimsPrincipal principal)
    {
        var caller = Caller.From(principal);
        return caller.IsSuccess && RequireAdmin(caller.Value).IsSuccess;
    }
}
