namespace PeakMediaRemoteMcpServer.Dashboard;

/// <summary>
/// The dashboard's non-page endpoints. <c>POST /signout</c> (antiforgery-checked) ends only the browser
/// session — it clears the dashboard cookie and does not sign the person out of the provider — and
/// lands on Signed Out (S-06).
/// </summary>
public static class DashboardEndpoints
{
    public const string SignOutPath = "/signout";

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(SignOutPath, SignOutAsync).AllowAnonymous().DisableAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> SignOutAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.Problem(
                title: "Bad Request",
                statusCode: StatusCodes.Status400BadRequest,
                detail: "The sign-out form expired. Reload the page and use Sign out again.");
        }

        await context.SignOutAsync(DashboardAuthSetup.CookieScheme);
        return Results.Redirect(DashboardAuthSetup.SignedOutPath);
    }
}
