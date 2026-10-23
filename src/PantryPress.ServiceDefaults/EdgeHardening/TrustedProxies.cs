using System.Net;
using IPNetwork = System.Net.IPNetwork;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PantryPress.ServiceDefaults.EdgeHardening;

/// <summary>
/// Which hops in front of this service are allowed to say who the client is (PSEC-002; PP1.8). Empty by default, and
/// empty means <em>believe nobody</em>: the forwarding middleware is not even registered, so a request's own claim
/// about its address, scheme, or host changes nothing anywhere.
/// </summary>
/// <remarks>
/// Production fills this from infrastructure (PP19): behind Azure Front Door into Container Apps the trusted hop is
/// the ingress network, and <see cref="ForwardLimit"/> counts the hops that may be believed - one for a single proxy,
/// two when Front Door and the ingress controller both add a header. Front Door's own identity is checked separately
/// with <c>X-Azure-FDID</c> (PP9.3); being on the ingress network is not the same as having come through the edge.
/// </remarks>
public sealed class TrustedProxyOptions
{
    /// <summary>Individual peer addresses to believe, e.g. <c>10.0.0.4</c>.</summary>
    public IList<string> KnownProxies { get; } = [];

    /// <summary>Peer networks to believe in CIDR form, e.g. <c>10.0.0.0/8</c>.</summary>
    public IList<string> KnownNetworks { get; } = [];

    /// <summary>
    /// Hosts a trusted proxy may name in <c>X-Forwarded-Host</c>. Empty allows any host the proxy claims, which is why
    /// the gateway fills it: the same-origin check compares against <c>Request.Host</c>.
    /// </summary>
    public IList<string> AllowedHosts { get; } = [];

    /// <summary>How many forwarding hops may be believed, counting back from this connection.</summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>
    /// Whether a trusted proxy may also say what host was asked for. Off by default, and a separate decision from
    /// trusting it about an address, because on the public tier the host <em>is</em> the tenant (PSEC-004): a peer
    /// that may correct a logged IP is not thereby a peer that may choose whose site a reader sees. The gateway
    /// turns it on - it answers on one host of its own - and the public tier leaves it off until PP9.3 proves the
    /// edge with <c>X-Azure-FDID</c>.
    /// </summary>
    public bool ForwardHost { get; set; }

    /// <summary>
    /// Set deliberately to run a deployed host that trusts no proxy. Without it such a host refuses to start,
    /// because trusting nobody behind a proxy is not a safe default - it is a quiet failure. Every browser then
    /// shares the proxy's address, so a per-address limit becomes one global bucket far tighter than the ceiling it
    /// replaced; the scheme stays plain HTTP, so no HSTS goes out and every admin write is refused.
    /// </summary>
    public bool TrustNobody { get; set; }

    /// <summary>Whether any peer is trusted at all. False means the forwarding middleware is never registered.</summary>
    public bool TrustsAnyone => KnownProxies.Count > 0 || KnownNetworks.Count > 0;
}

/// <summary>
/// The two halves of one rule: read a forwarding header only from a peer that was configured as a proxy, then take
/// every forwarding header off the request so nothing downstream can read a client's invention as a proxy's word.
/// </summary>
public static class TrustedProxies
{
    /// <summary>
    /// Everything a proxy in front of us would legitimately set, and a client therefore must not. The standard
    /// forwarding set with its non-standard spellings (<c>-Scheme</c>, <c>-Port</c>), the Azure Front Door family -
    /// <c>X-Azure-FDID</c> is how PP9.3 will prove a request came through the edge, so a client that could forge one
    /// would walk straight past that check - and the rewriting proxies' originals. A header we do not strip is a
    /// header something downstream might read.
    /// </summary>
    public static readonly string[] Forwarding =
    [
        "Forwarded",
        "X-Forwarded-For",
        "X-Forwarded-Proto",
        "X-Forwarded-Scheme",
        "X-Forwarded-Host",
        "X-Forwarded-Port",
        "X-Forwarded-Prefix",
        "X-Azure-FDID",
        "X-Azure-ClientIP",
        "X-Azure-SocketIP",
        "X-Original-Host",
        "X-Original-URL",
        "X-Real-IP",
        "X-ProxyUser-Ip",
    ];

    /// <summary>
    /// Binds the peers this host believes. <paramref name="required"/> - true for a deployed host - refuses the
    /// start when none is configured and nobody has said that is on purpose, because the consequences of the empty
    /// default are invisible locally: Kestrel terminates TLS itself in a local run, so every one of them only
    /// appears behind a real edge (PP19 fills this from infrastructure).
    /// </summary>
    public static IServiceCollection AddTrustedProxies(
        this IServiceCollection services,
        IConfiguration section,
        bool required = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<TrustedProxyOptions>()
            .Bind(section)
            .Validate(
                o => !required || o.TrustsAnyone || o.TrustNobody,
                "Edge:TrustedProxies names no peer. Behind a proxy that leaves this host no way to see the browser: "
                + "per-address limits collapse into one bucket, no HSTS is sent, and every admin write is refused. "
                + "Name the proxy's network, or set Edge:TrustedProxies:TrustNobody to say it is deliberate.")
            .Validate(o => o.ForwardLimit > 0, "Edge:TrustedProxies:ForwardLimit must be at least 1.")
            .Validate(
                o => o.KnownProxies.All(p => IPAddress.TryParse(p, out _)),
                "Every Edge:TrustedProxies:KnownProxies entry must be an IP address.")
            .Validate(
                o => o.KnownNetworks.All(n => IPNetwork.TryParse(n, out _)),
                "Every Edge:TrustedProxies:KnownNetworks entry must be a CIDR network, such as 10.0.0.0/8.")
            .ValidateOnStart();
        return services;
    }

    /// <summary>
    /// Applies a trusted proxy's <c>X-Forwarded-For</c>, <c>-Proto</c>, and <c>-Host</c> to the request, so the
    /// address, scheme, and host everything downstream reads are the browser's rather than the last hop's. Nothing is
    /// registered when no peer is trusted, which is the local and test default.
    /// </summary>
    /// <remarks>
    /// This is what makes the PP1.7 browser boundary work at the edge: TLS terminates at Front Door, so without it
    /// <c>Request.IsHttps</c> is false and <c>Request.Host</c> is the container's, and every admin write would be
    /// refused as a plain-HTTP or cross-origin request. It must run <em>ahead</em> of
    /// <see cref="UseForwardingHeaderHygiene"/>, which then removes whatever is left.
    /// </remarks>
    public static IApplicationBuilder UseTrustedProxies(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.ApplicationServices.GetRequiredService<IOptions<TrustedProxyOptions>>().Value;
        if (!options.TrustsAnyone)
        {
            return app;
        }

        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = options.ForwardHost
                ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
                : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = options.ForwardLimit,

            // A proxy that sets only the address is still a proxy; demanding all three in step would make us fall
            // back to the connection's view on a partial set, which is the view we are trying to correct.
            RequireHeaderSymmetry = false,
        };

        // The framework trusts loopback out of the box. Nothing here is trusted unless an operator wrote it down.
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
        foreach (var proxy in options.KnownProxies)
        {
            forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in options.KnownNetworks)
        {
            forwarded.KnownIPNetworks.Add(IPNetwork.Parse(network));
        }

        foreach (var host in options.AllowedHosts)
        {
            forwarded.AllowedHosts.Add(host);
        }

        return app.UseForwardedHeaders(forwarded);
    }

    /// <summary>
    /// Takes the forwarding headers off every inbound request, on every path, before routing. Whatever a trusted
    /// proxy had to say has already been read into the request by then; what is left is a client's invention, and it
    /// reaches neither this service's own code nor anything it forwards to.
    /// </summary>
    public static IApplicationBuilder UseForwardingHeaderHygiene(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (context, next) =>
        {
            foreach (var header in Forwarding)
            {
                context.Request.Headers.Remove(header);
            }

            await next(context);
        });
    }
}
