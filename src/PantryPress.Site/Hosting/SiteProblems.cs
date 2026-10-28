using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using PantryPress.Domain.Managers.Results;
using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.Site.Hosting;

/// <summary>
/// RFC 9457 problem details for every failure the public tier answers (api-contract.md): a stable <c>code</c> and a
/// plain-language <c>title</c>: from the status for the framework's failures and host resolution's neutral 404, and from
/// the facade's error for a site that is not open to readers (PP2.13) and, later, an endpoint's own refusals (PP12).
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

    private const string ProblemContentType = "application/problem+json";

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
    public static Task WriteAsync(HttpContext context, int status) =>
        WriteAsync(context, status, CodeFor(status), TitleFor(status));

    /// <summary>
    /// A facade's refusal as an MVC result: the same problem document, written the same way, with <c>Retry-After</c> when
    /// the error says how long (SEO-007).
    /// </summary>
    public static IActionResult FromError(Error error) => new ErrorResult(error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>The status for each kind of facade error, as ApiService answers it.</summary>
    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation or ErrorKind.Rejected => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.TooManyAttempts => StatusCodes.Status429TooManyRequests,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorKind.Gone => StatusCodes.Status410Gone,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "An error kind with no status."),
    };

    public static string CodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "bad-request",
        StatusCodes.Status404NotFound => NotFoundCode,
        StatusCodes.Status405MethodNotAllowed => "method-not-allowed",
        StatusCodes.Status413PayloadTooLarge => RequestLimits.TooLargeCode,
        StatusCodes.Status415UnsupportedMediaType => "unsupported-media-type",
        StatusCodes.Status429TooManyRequests => "too-many-requests",
        >= 500 => "unexpected-error",
        _ => "request-failed",
    };

    public static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "We couldn't read that request.",
        StatusCodes.Status404NotFound => NotFoundTitle,
        StatusCodes.Status405MethodNotAllowed => "That isn't something you can do here.",
        StatusCodes.Status413PayloadTooLarge => RequestLimits.TooLargeTitle,
        StatusCodes.Status415UnsupportedMediaType => "We can't read that kind of content.",
        StatusCodes.Status429TooManyRequests => "That's a lot of requests. Please wait a moment and try again.",
        >= 500 => "Something went wrong on our side. Please try again in a moment.",
        _ => "We couldn't handle that request.",
    };

    // Written here as JSON rather than through the problem details service (PP2.13): for a controller's answer to a reader
    // who does not ask for JSON, that service hands the problem to MVC's writer, which rebuilds it without its code and
    // title. The problem's own defaults - its type above all - are the framework's, as before.
    private static Task WriteAsync(HttpContext context, int status, string code, string title)
    {
        context.Response.Headers.CacheControl = "no-store";
        var problem = TypedResults.Problem(
                statusCode: status,
                title: title,
                extensions: new Dictionary<string, object?>
                {
                    [CodeKey] = code,
                    ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
                })
            .ProblemDetails;
        return Results.Json(problem, contentType: ProblemContentType, statusCode: status).ExecuteAsync(context);
    }

    private sealed class ErrorResult(Error error) : IActionResult
    {
        public Task ExecuteResultAsync(ActionContext context)
        {
            var http = context.HttpContext;
            if (error.RetryAfter is { } wait)
            {
                http.Response.Headers.RetryAfter =
                    Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            return WriteAsync(http, StatusFor(error.Kind), error.Code, error.Title);
        }
    }
}
