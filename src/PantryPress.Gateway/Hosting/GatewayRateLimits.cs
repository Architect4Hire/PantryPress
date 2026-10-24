using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace PantryPress.Gateway.Hosting;

/// <summary>Per-address limits for the sign-in routes (gateway.md; PP1.8).</summary>
public sealed class GatewayRateLimitOptions
{
    /// <summary>
    /// Sign-in attempts a minute from one address. Generous for a person and their household, hopeless for a spray:
    /// per-account lockout caps attempts against one account, and this caps attempts across many.
    /// </summary>
    public int SessionPerIpPerMinute { get; set; } = 20;

    /// <summary>
    /// Current-session reads and sign-outs a minute from one address (PP1.12). Each asks the API, so each is limited -
    /// but generously, since every admin page load reads the session once.
    /// </summary>
    public int SessionChecksPerIpPerMinute { get; set; } = 120;
}

/// <summary>
/// The limit that needed a trusted proxy first (PP1.5's reasoning, deferred through PP1.7). Partitioning by address
/// before the trusted-proxy step would have put every request behind Front Door's address in one bucket, which is
/// worse than no partition at all; now that the address is the browser's, the gateway's own sign-in routes get one.
/// </summary>
/// <remarks>
/// This is the gateway's half. ApiService keeps its global ceilings as a backstop for a caller that reaches it some
/// other way, and partitions its anonymous account routes by the address the gateway vouches for.
/// </remarks>
internal static class GatewayRateLimits
{
    public const string SessionPerIp = "session-per-ip";
    public const string SessionChecksPerIp = "session-checks-per-ip";

    /// <summary>One shared bucket for a request whose address we cannot see, so it is never a way around the limit.</summary>
    private const string UnknownAddress = "unknown";

    public static IServiceCollection AddGatewayRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<GatewayRateLimitOptions>()
            .Bind(configuration)
            .Validate(
                o => o.SessionPerIpPerMinute > 0 && o.SessionChecksPerIpPerMinute > 0,
                "Gateway:SessionPerIpPerMinute and Gateway:SessionChecksPerIpPerMinute must be positive.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(SessionPerIp, context => PerAddress(context, limits => limits.SessionPerIpPerMinute));
            options.AddPolicy(SessionChecksPerIp, context => PerAddress(context, limits => limits.SessionChecksPerIpPerMinute));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await GatewayApi.WriteProblemAsync(context.HttpContext, StatusCodes.Status429TooManyRequests);
            };
        });

        return services;
    }

    private static RateLimitPartition<string> PerAddress(HttpContext context, Func<GatewayRateLimitOptions, int> permits)
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<GatewayRateLimitOptions>>().Value;
        var address = context.Connection.RemoteIpAddress?.ToString() ?? UnknownAddress;
        return RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits(limits),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }
}
