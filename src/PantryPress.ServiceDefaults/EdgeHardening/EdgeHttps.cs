using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PantryPress.ServiceDefaults.EdgeHardening;

/// <summary>
/// Sends a browser that arrived over plain HTTP to the same address over TLS (PSEC-002; PP1.8). For the gateway
/// alone: the internal APIs are called over plain HTTP inside the environment by the gateway and, later, the SSR
/// server, so a redirect there would break the system rather than harden it.
/// </summary>
public static class EdgeHttps
{
    /// <summary>
    /// Health probes are exempt. Container Apps probes them over plain HTTP on the container's own port, and a
    /// redirect would turn a healthy process into a failing one. Today they already short-circuit the moment they
    /// are routed, ahead of every middleware the host adds, so this list changes nothing; it is here so that
    /// short-circuiting is not the only thing standing between a probe and a redirect.
    /// </summary>
    public static readonly string[] Exempt = ["/health", "/alive"];

    /// <summary>
    /// 307 rather than the framework's default 308: the method and body survive, and nothing about "this address is
    /// HTTPS" is worth a browser caching permanently when HSTS says the same thing with an expiry.
    /// </summary>
    public static IServiceCollection AddEdgeHttpsRedirect(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddHttpsRedirection(options =>
            options.RedirectStatusCode = StatusCodes.Status307TemporaryRedirect);
    }

    /// <summary>
    /// Redirects everything but the probes. Runs after the trusted-proxy step, so a request that reached Front Door
    /// over TLS is not redirected on account of the plain-HTTP hop behind it; the framework middleware works out the
    /// HTTPS port from configuration or the server's own addresses.
    /// </summary>
    /// <remarks>
    /// Registered only where this host can tell what scheme the browser used: a local run, where Kestrel terminates
    /// TLS itself, or a deployment that named its proxy. The pair matters - a deployed host with an HTTPS port
    /// configured but no trusted proxy sees plain HTTP on every request, answers 307 to the address the edge just
    /// came from, and the edge fetches it again: an infinite redirect, and the admin app is simply down. So the two
    /// are coupled here rather than left to two lines of infrastructure that have to agree.
    /// </remarks>
    public static IApplicationBuilder UseEdgeHttpsRedirect(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
        var proxies = app.ApplicationServices.GetRequiredService<IOptions<TrustedProxyOptions>>().Value;
        if (!environment.IsDevelopment() && !proxies.TrustsAnyone)
        {
            return app;
        }

        return app.UseWhen(
            context => !IsExempt(context.Request.Path),
            branch => branch.UseHttpsRedirection());
    }

    private static bool IsExempt(PathString path) =>
        Exempt.Any(exempt => path.StartsWithSegments(exempt, StringComparison.OrdinalIgnoreCase));
}
