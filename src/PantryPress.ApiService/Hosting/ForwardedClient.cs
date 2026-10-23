using System.Net;
using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.ApiService.Hosting;

/// <summary>
/// Who the client is, when the gateway is the one saying so (PP1.8). ApiService is not only reachable through the
/// BFF - the publishing API gets its own edge route at PP18 - so a forwarding header is read only from a caller that
/// can present the gateway's shared secret, and is removed from every request either way.
/// </summary>
/// <remarks>
/// The secret does the work an IP allow-list would do elsewhere, and does it better here: pod addresses are not
/// knowable in advance, while the secret is already the thing this service believes an identity on (PP1.6). The
/// forwarded host is deliberately <em>not</em> applied: no admin route reads it, and the one place a host decides
/// anything is the public site, where PP9.3 owns the rule.
/// </remarks>
internal static class ForwardedClient
{
    public static IApplicationBuilder UseGatewayForwardedClient(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (context, next) =>
        {
            if (context.RequestServices.GetRequiredService<GatewayTrust>().IsFromGateway(context.Request))
            {
                Apply(context);
            }

            // Believed or not, no forwarding header survives into the rest of the request.
            foreach (var header in TrustedProxies.Forwarding)
            {
                context.Request.Headers.Remove(header);
            }

            await next(context);
        });
    }

    private static void Apply(HttpContext context)
    {
        // The gateway writes its headers with Set, not append, so there is one value and it is the gateway's own
        // view of the browser. The last entry is the nearest hop regardless.
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
        if (forwardedFor.Length > 0)
        {
            var nearest = forwardedFor.Split(',')[^1].Trim();
            if (IPEndPoint.TryParse(nearest, out var endpoint))
            {
                context.Connection.RemoteIpAddress = endpoint.Address;
            }
        }

        var proto = context.Request.Headers["X-Forwarded-Proto"].ToString();
        if (string.Equals(proto, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.Equals(proto, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            context.Request.Scheme = proto.ToLowerInvariant();
        }
    }
}
