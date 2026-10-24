using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace PantryPress.ApiService.Hosting;

/// <summary>Origin rate limits for the account routes, from the <c>RateLimits</c> configuration section.</summary>
public sealed class ApiRateLimitOptions
{
    /// <summary>
    /// Requests per minute from one address for the anonymous account routes (sign-up, confirm, resend, forgot,
    /// reset). Per-address since PP1.8 put a trusted-proxy step in front of it: these routes arrive through the
    /// gateway's proxy, which vouches for the browser's address, so one caller can no longer spend everyone's
    /// allowance. Per-address email limits still belong to the Accounts facades, and aggregate volume to the edge
    /// (PP9).
    /// </summary>
    public int AnonymousAccountPerIpPerMinute { get; set; } = 30;

    /// <summary>Requests per minute for one signed-in account on its own account routes.</summary>
    public int SignedInAccountPerMinute { get; set; } = 30;

    /// <summary>
    /// Requests per minute across every caller for the internal sign-in routes. Deliberately still a global ceiling:
    /// only the gateway can reach this surface, it calls with a client of its own rather than through its proxy, and
    /// it already limits sign-in per address at the browser's end (PP1.8). This is the backstop behind that.
    /// </summary>
    public int SignInPerMinute { get; set; } = 300;

    /// <summary>
    /// Requests per minute across every caller for the internal session checks - whether a session still stands, and
    /// signing one out (PP1.12). Its own ceiling, never the sign-in one: every admin page load asks, and a busy platform
    /// must neither crowd out sign-in nor have a sign-out refused. The gateway limits both per address in front of it.
    /// </summary>
    public int SessionChecksPerMinute { get; set; } = 6_000;
}

internal static class ApiRateLimits
{
    public const string AnonymousAccount = "account-anonymous";
    public const string SignedInAccount = "account-signed-in";
    public const string SignIn = "account-sign-in";
    public const string SessionCheck = "account-session-check";

    /// <summary>One shared bucket for a request whose address this service cannot see.</summary>
    private const string UnknownAddress = "unknown";

    public static IServiceCollection AddApiRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiRateLimitOptions>()
            .Bind(configuration)
            .Validate(
                o => o.AnonymousAccountPerIpPerMinute > 0 && o.SignedInAccountPerMinute > 0 && o.SignInPerMinute > 0
                    && o.SessionChecksPerMinute > 0,
                "Rate limits must be positive.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            // Partitioned by the address the gateway vouches for (ForwardedClient). A request whose address we
            // cannot see shares one bucket, so an unknown address is never a way around the limit.
            options.AddPolicy(AnonymousAccount, context =>
            {
                var limits = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiRateLimitOptions>>().Value;
                var address = context.Connection.RemoteIpAddress?.ToString() ?? UnknownAddress;
                return RateLimitPartition.GetFixedWindowLimiter(address, _ => PerMinute(limits.AnonymousAccountPerIpPerMinute));
            });

            options.AddPolicy(SignIn, context =>
            {
                var limits = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(SignIn, _ => PerMinute(limits.SignInPerMinute));
            });

            options.AddPolicy(SessionCheck, context =>
            {
                var limits = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(SessionCheck, _ => PerMinute(limits.SessionChecksPerMinute));
            });

            // Partitioned by account; an unauthenticated caller is turned away by [Authorize] before it matters, but
            // shares one small partition in case a route forgets it.
            options.AddPolicy(SignedInAccount, context =>
            {
                var limits = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiRateLimitOptions>>().Value;
                var key = context.User.TryGetAccountId(out var account) ? account.ToString() : "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => PerMinute(limits.SignedInAccountPerMinute));
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(
                    new ProblemDetailsContext
                    {
                        HttpContext = context.HttpContext,
                        ProblemDetails = { Status = StatusCodes.Status429TooManyRequests },
                    });
            };
        });
        return services;
    }

    private static FixedWindowRateLimiterOptions PerMinute(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}
