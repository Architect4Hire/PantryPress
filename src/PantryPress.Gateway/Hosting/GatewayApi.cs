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
    /// BFF or the API answered. A test pins these against <c>ApiProblems</c>.
    /// </summary>
    public const string CodeKey = "code";

    public const string NotFoundCode = "not-found";
    public const string NotFoundTitle = "We couldn't find that.";
    public const string ServerErrorCode = "unexpected-error";
    public const string ServerErrorTitle = "Something went wrong on our side. Please try again in a moment.";

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
            context.ProblemDetails.Title = TitleFor(status);
            context.ProblemDetails.Extensions[CodeKey] = CodeFor(status);
            context.ProblemDetails.Extensions.TryAdd("traceId", TraceId(context.HttpContext));

            // Nothing about a failure is cacheable, and a cached 502 would outlive the outage.
            context.HttpContext.Response.Headers.CacheControl = "no-store";

            // Exception details never reach a client, in any environment (ApiProblems does the same).
            context.ProblemDetails.Detail = status >= 500 ? null : context.ProblemDetails.Detail;
            context.ProblemDetails.Extensions.Remove("exception");
        });

    public static WebApplication MapApiFallback(this WebApplication app)
    {
        app.Map("/api/{**rest}", (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: NotFoundTitle,
                    extensions: new Dictionary<string, object?>
                    {
                        [CodeKey] = NotFoundCode,
                        ["traceId"] = TraceId(context),
                    });
            })
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
        StatusCodes.Status404NotFound => NotFoundCode,
        >= 500 => ServerErrorCode,
        _ => "request-failed",
    };

    public static string TitleFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => NotFoundTitle,
        >= 500 => ServerErrorTitle,
        _ => "We couldn't handle that request.",
    };

    private static string TraceId(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;
}
