using Microsoft.Extensions.Options;
using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.Gateway.Hosting;

/// <summary>
/// Which host names are this gateway's, and the origin a browser reaches it on (PP1.8). Behind Front Door the
/// request's own host and scheme are the hop's rather than the browser's; the trusted-proxy step corrects both, and
/// these two settings are the belt to that braces - written down once, they make the boundary checks independent of
/// proxy configuration being right.
/// </summary>
public sealed class GatewayOriginOptions
{
    /// <summary>
    /// An absolute origin - scheme, host, and port - such as <c>https://pantrypress.app</c>. When set, the
    /// same-origin check compares against it instead of working one out from the request. Unset locally and in
    /// tests, where the request's own origin is the browser's.
    /// </summary>
    public string? PublicOrigin { get; set; }

    /// <summary>
    /// Host names this gateway answers on, without ports. Empty means any, which is the local default. A tenant site
    /// host is deliberately not among them: Front Door routes those to the public site, and one arriving here came
    /// from somewhere that is not a browser following our DNS.
    /// </summary>
    public IList<string> Hosts { get; } = [];

    public bool IsValid() =>
        PublicOrigin is null
        || (Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var origin)
            && origin.PathAndQuery == "/"
            && (origin.Scheme == Uri.UriSchemeHttps || origin.Scheme == Uri.UriSchemeHttp));
}

/// <summary>
/// Refuses a request addressed to a host that is not this gateway's.
/// </summary>
/// <remarks>
/// The framework's own host filtering would do this, but it answers a bodiless 400, which would be the one answer
/// under <c>/api</c> that is not a problem document (gateway.md). So the gateway answers its own neutral 404: an
/// unknown host is not told whether it guessed something real, here or on the public tier (PSEC-004's reasoning).
/// Health probes are exempt, for the reason the redirect exempts them: Container Apps addresses a container by
/// whatever name it likes. They short-circuit ahead of this middleware today, so the exemption changes nothing and
/// is here so that short-circuiting is not the only thing holding it up.
/// </remarks>
internal static class GatewayHosts
{
    public static IApplicationBuilder UseKnownHosts(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var hosts = app.ApplicationServices.GetRequiredService<IOptions<GatewayOriginOptions>>().Value.Hosts;
        if (hosts.Count == 0)
        {
            return app;
        }

        var known = hosts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return app.Use(async (context, next) =>
        {
            // A trailing dot is the same host to DNS, and an empty Host is no host at all.
            var host = context.Request.Host.Host.TrimEnd('.');
            if (known.Contains(host) || IsProbe(context.Request.Path))
            {
                await next(context);
                return;
            }

            await GatewayApi.WriteProblemAsync(context, StatusCodes.Status404NotFound);
        });
    }

    private static bool IsProbe(PathString path) =>
        EdgeHttps.Exempt.Any(exempt => path.StartsWithSegments(exempt, StringComparison.OrdinalIgnoreCase));
}
