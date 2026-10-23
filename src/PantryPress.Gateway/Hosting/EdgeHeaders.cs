using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace PantryPress.Gateway.Hosting;

/// <summary>
/// What the gateway tells ApiService about the hops in front of it (ACCT-002; PP1.7, PP1.8, gateway.md).
/// </summary>
/// <remarks>
/// The inbound half lives in <c>TrustedProxies</c> (ServiceDefaults), because ApiService and the public site owe the
/// same rule: read a forwarding header only from a configured proxy, then strip the lot. This is the outbound half,
/// where the gateway writes its own from the request as it stands after that step - which, behind Front Door, is the
/// browser's address, scheme, and host rather than the last hop's.
/// </remarks>
internal static class EdgeHeaders
{
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
