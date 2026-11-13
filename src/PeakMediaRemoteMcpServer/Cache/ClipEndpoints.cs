namespace PeakMediaRemoteMcpServer.Cache;

/// <summary>
/// <c>GET /clips/{token}</c>: the WAV behind a download link, with no sign-in (the unguessable token
/// is the credential). Unknown links answer 404 and expired links 410, as RFC 9457 problem details
/// naming the next action.
/// </summary>
public static class ClipEndpoints
{
    public const string PathPrefix = "/clips/";

    public const string NotFoundDetail = "No clip matches this download link. Call synthesize_narration again for a new link.";

    public const string ExpiredDetail = "This download link has expired. Call synthesize_narration again with the same request for a new link; cached clips are free.";

    public static IEndpointRouteBuilder MapClipEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/clips/{token}", DownloadAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> DownloadAsync(string token, DownloadTokens tokens, AudioDirectory audio, CancellationToken cancellationToken)
    {
        var resolved = await tokens.ResolveAsync(token, cancellationToken);
        if (resolved.IsFailed)
        {
            return Results.Problem(
                title: "Service Unavailable",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                detail: "The gateway's audio cache is unavailable. Try the download again in a moment.");
        }

        var (lookup, cacheKey) = resolved.Value;
        if (lookup == DownloadLookup.Expired)
        {
            return Results.Problem(title: "Gone", statusCode: StatusCodes.Status410Gone, detail: ExpiredDetail);
        }

        var path = cacheKey is null ? null : audio.FileFor(cacheKey);
        if (lookup == DownloadLookup.Unknown || path is null || !File.Exists(path))
        {
            return Results.Problem(title: "Not Found", statusCode: StatusCodes.Status404NotFound, detail: NotFoundDetail);
        }

        return Results.File(path, "audio/wav");
    }
}
