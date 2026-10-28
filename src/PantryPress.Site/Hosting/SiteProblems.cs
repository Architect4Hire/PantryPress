using System.Diagnostics;

namespace PantryPress.Site.Hosting;

/// <summary>
/// RFC 9457 problem details for every failure the public tier answers (api-contract.md): a stable <c>code</c> and a
/// plain-language <c>title</c> from the status, since the only failures so far are the framework's and host resolution's
/// neutral 404. Endpoint codes arrive with the endpoints (PP2.13, PP12).
/// </summary>
/// <remarks>
/// A refusal is written here in full, whatever the request's <c>Accept</c> says: the framework's own status-code pages
/// and exception handler fall back to plain text for a client that does not ask for JSON, without the code or the
/// <c>no-store</c>, and a cached "not found" would outlive the site created a moment later.
/// </remarks>
internal static class SiteProblems
{
    public const string CodeKey = "code";
    public const string NotFoundCode = "not-found";
    public const string NotFoundTitle = "We couldn't find that.";

    public static IServiceCollection AddSiteProblems(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
            if (!context.ProblemDetails.Extensions.ContainsKey(CodeKey))
            {
                context.ProblemDetails.Title = TitleFor(status);
                context.ProblemDetails.Extensions[CodeKey] = CodeFor(status);
            }

            context.ProblemDetails.Extensions.TryAdd(
                "traceId",
                Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);

            // A refusal is never cached: a stored "not found" would outlive the site that was created a moment later.
            context.HttpContext.Response.Headers.CacheControl = "no-store";

            // Exception details never reach a reader, in any environment.
            context.ProblemDetails.Detail = status >= 500 ? null : context.ProblemDetails.Detail;
            context.ProblemDetails.Extensions.Remove("exception");
        });

    /// <summary>
    /// Every bodiless failure - an unmatched route, host resolution's refusal - and every unhandled fault, answered with
    /// this host's problem document.
    /// </summary>
    public static WebApplication UseSiteProblems(this WebApplication app)
    {
        app.UseExceptionHandler(failed => failed.Run(context => WriteAsync(context, StatusCodes.Status500InternalServerError)));
        app.UseStatusCodePages(pages => WriteAsync(pages.HttpContext, pages.HttpContext.Response.StatusCode));
        return app;
    }

    /// <summary>The problem document for <paramref name="status"/>, never cached.</summary>
    public static Task WriteAsync(HttpContext context, int status)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Problem(
                statusCode: status,
                title: TitleFor(status),
                extensions: new Dictionary<string, object?>
                {
                    [CodeKey] = CodeFor(status),
                    ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
                })
            .ExecuteAsync(context);
    }

    public static string CodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "bad-request",
        StatusCodes.Status404NotFound => NotFoundCode,
        StatusCodes.Status405MethodNotAllowed => "method-not-allowed",
        StatusCodes.Status413PayloadTooLarge => "request-too-large",
        StatusCodes.Status415UnsupportedMediaType => "unsupported-media-type",
        StatusCodes.Status429TooManyRequests => "too-many-requests",
        >= 500 => "server-error",
        _ => "request-failed",
    };

    public static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "We couldn't read that request.",
        StatusCodes.Status404NotFound => NotFoundTitle,
        StatusCodes.Status405MethodNotAllowed => "That isn't something you can do here.",
        StatusCodes.Status413PayloadTooLarge => "That's more than we can accept at once.",
        StatusCodes.Status415UnsupportedMediaType => "We can't read that kind of content.",
        StatusCodes.Status429TooManyRequests => "That's a lot of requests. Please wait a moment and try again.",
        >= 500 => "Something went wrong on our side. Please try again in a moment.",
        _ => "We couldn't handle that request.",
    };
}
