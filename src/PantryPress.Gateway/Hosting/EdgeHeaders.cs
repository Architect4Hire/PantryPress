using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace PantryPress.Gateway.Hosting;

/// <summary>
/// What a client is never allowed to tell us about the hops in front of the gateway (ACCT-002; PP1.7, gateway.md).
/// Every inbound request loses its forwarding headers before anything reads one, and the proxy then writes its own
/// from the connection it can actually see, so ApiService gets a single value per header and it is the gateway's word.
/// </summary>
/// <remarks>
/// The gateway therefore believes nobody about the client's address or the original host. That is deliberate here:
/// PP1.8 adds the trusted-proxy step, which reads Front Door's forwarding headers when - and only when - the peer is a
/// known proxy, and must run ahead of this strip.
/// </remarks>
internal static class EdgeHeaders
{
    /// <summary>
    /// Everything a proxy in front of us would legitimately set, and a client therefore must not. The list covers the
    /// non-standard spellings too (<c>-Scheme</c>, <c>-Port</c>): a header we do not strip is a header something
    /// downstream might read.
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
    ];

    /// <summary>
    /// Takes the forwarding headers off every inbound request, on every path, before routing: the gateway's own
    /// routes, the forwarded admin surface, and the Development catch-all to the admin dev server alike.
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

    /// <summary>
    /// The <c>X-Forwarded-*</c> headers the gateway itself vouches for. Declared rather than inherited, and with
    /// <see cref="ForwardedTransformActions.Set"/>: appending would hand a client's claim to the API as the first
    /// value of a list it had every reason to trust.
    /// </summary>
    public static void AddForwardedTransforms(TransformBuilderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.UseDefaultForwarders = false;
        context.AddXForwarded(ForwardedTransformActions.Set);
    }
}
