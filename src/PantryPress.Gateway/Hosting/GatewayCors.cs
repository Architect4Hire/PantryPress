using Microsoft.Extensions.Options;

namespace PantryPress.Gateway.Hosting;

/// <summary>
/// Same origin, nothing else (ACCT-002; PP1.7, gateway.md). The admin app is served from this gateway's own origin,
/// so there is no cross-origin caller to allow: the default policy allows none, which means a preflight is answered
/// here with no <c>Access-Control-Allow-*</c> header and is never forwarded to ApiService.
/// </summary>
/// <remarks>
/// CORS only asks a browser not to read an answer; the request still happens, which is why a write also has its
/// <c>Origin</c> checked. A request with an <c>Origin</c> from somewhere else - or an opaque one, as a sandboxed frame
/// sends - is refused before it reaches a route, in front of the antiforgery check, so a cross-site write is turned
/// away on the header alone whether or not it guessed a token.
/// </remarks>
internal static class GatewayCors
{
    public const string CrossOriginCode = "cross-origin-blocked";

    public const string CrossOriginTitle = "That request came from another site.";

    /// <summary>A default policy that allows no origin at all: CORS is closed, and closed on purpose, not by omission.</summary>
    public static IServiceCollection AddClosedCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<GatewayOriginOptions>()
            .Bind(configuration)
            .Validate(o => o.IsValid(), "Gateway:PublicOrigin must be an absolute origin, such as https://app.pantrypress.app.")
            .ValidateOnStart();

        return services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins()));
    }

    /// <summary>
    /// Refuses a state-changing request that says it came from another origin. Safe methods are left alone - a
    /// cross-origin read is the browser's business and gets no CORS headers to work with - and a request without an
    /// <c>Origin</c> header passes, because a browser sends one on exactly the cross-site writes this is about.
    /// </summary>
    public static IApplicationBuilder UseSameOriginWrites(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var configured = app.ApplicationServices.GetRequiredService<IOptions<GatewayOriginOptions>>().Value.PublicOrigin;
        return app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin;
            if (HttpMethods.IsGet(context.Request.Method)
                || HttpMethods.IsHead(context.Request.Method)
                || HttpMethods.IsOptions(context.Request.Method)
                || HttpMethods.IsTrace(context.Request.Method)
                || origin.Count == 0)
            {
                await next(context);
                return;
            }

            if (origin.Count > 1 || !IsSameOrigin(context.Request, origin[0], configured))
            {
                await GatewayApi.WriteProblemAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    CrossOriginCode,
                    CrossOriginTitle);
                return;
            }

            await next(context);
        });
    }

    private static bool IsSameOrigin(HttpRequest request, string? origin, string? configured)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var sent))
        {
            // "null", a relative value, or nonsense: not this origin, and nothing to compare.
            return false;
        }

        if (configured is not null)
        {
            // An origin written down by an operator beats one worked out from a request every time - compared as
            // origins, so that writing the default port explicitly does not quietly stop matching.
            return Uri.TryCreate(configured, UriKind.Absolute, out var expected)
                && string.Equals(
                    sent.GetLeftPart(UriPartial.Authority),
                    expected.GetLeftPart(UriPartial.Authority),
                    StringComparison.OrdinalIgnoreCase);
        }

        var host = request.Host;
        var port = host.Port
            ?? (string.Equals(request.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80);
        return string.Equals(sent.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(sent.Host, host.Host, StringComparison.OrdinalIgnoreCase)
            && sent.Port == port;
    }
}
