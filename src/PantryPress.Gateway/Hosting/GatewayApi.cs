using System.Diagnostics;

namespace PantryPress.Gateway.Hosting;

/// <summary>
/// The <c>/api</c> paths the gateway owns, and the problem details it writes for them. It forwards the admin surface
/// (<c>/api/v1/…</c>, declared in configuration) to ApiService and answers everything else under <c>/api</c> itself:
/// the publishing API has its own edge route and never goes through the BFF (gateway.md), the public site API answers
/// on site hosts, and a version nobody has shipped is simply not here. Without this, an unmatched <c>/api</c> path
/// would fall through to the SPA route and the caller would get the app's HTML with a 200.
/// </summary>
internal static class GatewayApi
{
    /// <summary>
    /// Mirrors ApiService's problem vocabulary (api-contract.md), so the admin app reads one error shape whether the
    /// BFF or the API answered - which matters more now that the gateway answers routes of its own. A test pins every
    /// status below against <c>ApiProblems</c>, code and title alike.
    /// </summary>
    public const string CodeKey = "code";

    public const string NotFoundCode = "not-found";
    public const string NotFoundTitle = "We couldn't find that.";
    public const string UnauthorizedCode = "sign-in-required";
    public const string UnauthorizedTitle = "Please sign in to continue.";
    public const string ForbiddenCode = "forbidden";
    public const string ForbiddenTitle = "You don't have access to this.";
    public const string BadRequestCode = "bad-request";
    public const string BadRequestTitle = "We couldn't read that request.";
    public const string MethodNotAllowedCode = "method-not-allowed";
    public const string MethodNotAllowedTitle = "That action isn't available here.";
    public const string TooLargeCode = "request-too-large";
    public const string TooLargeTitle = "That request is too large.";
    public const string UnsupportedMediaTypeCode = "unsupported-media-type";
    public const string UnsupportedMediaTypeTitle = "Send the details as JSON.";
    public const string TooManyRequestsCode = "too-many-requests";
    public const string TooManyRequestsTitle = "Too many requests. Please wait a minute and try again.";
    public const string ServerErrorCode = "unexpected-error";
    public const string ServerErrorTitle = "Something went wrong on our side. Please try again in a moment.";

    /// <summary>A session that is over (the API's <c>AccountErrors.SessionEnded</c>), as <c>GET /session</c> reports it.</summary>
    public const string SessionEndedCode = "session-ended";
    public const string SessionEndedTitle = "You've been signed out. Sign in again to pick up where you left off.";

    // After the forwarded /api/v1 route (Order 1) and far ahead of the Development SPA catch-all (Order 1000), so
    // which endpoint answers never depends on route-precedence subtleties.
    private const int FallbackOrder = 2;

    /// <summary>
    /// Problem details for the failures the gateway itself produces: an unhandled fault, and a forwarded request that
    /// never reached ApiService - YARP answers 502 or 504 with no body of its own, which would leave the admin app
    /// nothing to show exactly when the API is unreachable.
    /// </summary>
    public static IServiceCollection AddApiProblems(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;

            // A problem the gateway wrote on purpose already carries its own code and title (a half-finished sign-in
            // says so); only the ones the framework and YARP produce need one from their status.
            if (!context.ProblemDetails.Extensions.ContainsKey(CodeKey))
            {
                context.ProblemDetails.Title = TitleFor(status);
                context.ProblemDetails.Extensions[CodeKey] = CodeFor(status);
            }

            context.ProblemDetails.Extensions.TryAdd("traceId", TraceId(context.HttpContext));

            // Nothing about a failure is cacheable, and a cached 502 would outlive the outage.
            context.HttpContext.Response.Headers.CacheControl = "no-store";

            // Exception details never reach a client, in any environment (ApiProblems does the same).
            context.ProblemDetails.Detail = status >= 500 ? null : context.ProblemDetails.Detail;
            context.ProblemDetails.Extensions.Remove("exception");
        });

    /// <summary>
    /// One of this gateway's problem documents, written straight to the response: for the paths it owns and for the
    /// cookie schemes, which have no sign-in page to redirect a SPA to.
    /// </summary>
    public static Task WriteProblemAsync(HttpContext context, int status, string? code = null, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Problem(
            statusCode: status,
            title: title ?? TitleFor(status),
            extensions: new Dictionary<string, object?>
            {
                [CodeKey] = code ?? CodeFor(status),
                ["traceId"] = TraceId(context),
            })
            .ExecuteAsync(context);
    }

    public static WebApplication MapApiFallback(this WebApplication app)
    {
        app.Map("/api/{**rest}", (HttpContext context) => WriteProblemAsync(context, StatusCodes.Status404NotFound))
            .Add(endpoint =>
            {
                if (endpoint is RouteEndpointBuilder route)
                {
                    route.Order = FallbackOrder;
                }
            });

        return app;
    }

    public static string CodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => BadRequestCode,
        StatusCodes.Status401Unauthorized => UnauthorizedCode,
        StatusCodes.Status403Forbidden => ForbiddenCode,
        StatusCodes.Status404NotFound => NotFoundCode,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowedCode,
        StatusCodes.Status413PayloadTooLarge => TooLargeCode,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaTypeCode,
        StatusCodes.Status429TooManyRequests => TooManyRequestsCode,
        >= 500 => ServerErrorCode,
        _ => "request-failed",
    };

    public static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => BadRequestTitle,
        StatusCodes.Status401Unauthorized => UnauthorizedTitle,
        StatusCodes.Status403Forbidden => ForbiddenTitle,
        StatusCodes.Status404NotFound => NotFoundTitle,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowedTitle,
        StatusCodes.Status413PayloadTooLarge => TooLargeTitle,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaTypeTitle,
        StatusCodes.Status429TooManyRequests => TooManyRequestsTitle,
        >= 500 => ServerErrorTitle,
        _ => "We couldn't handle that request.",
    };

    private static string TraceId(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;
}
