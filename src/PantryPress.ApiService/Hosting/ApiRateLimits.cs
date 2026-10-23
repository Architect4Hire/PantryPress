using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace PantryPress.ApiService.Hosting;

/// <summary>Origin rate limits for the account routes, from the <c>RateLimits</c> configuration section.</summary>
public sealed class ApiRateLimitOptions
{
    /// <summary>
    /// Requests per minute across every caller for the anonymous account routes (sign-up, confirm, resend, forgot,
    /// reset). A global ceiling only: per-IP limits belong to the gateway and the edge (PP1.5, PP1.8, PP9), and
    /// per-address email limits to the Accounts facades.
    /// </summary>
    public int AnonymousAccountPerMinute { get; set; } = 120;

    /// <summary>Requests per minute for one signed-in account on its own account routes.</summary>
    public int SignedInAccountPerMinute { get; set; } = 30;

    /// <summary>
    /// Requests per minute across every caller for the gateway's sign-in routes. Its own bucket, so attempts against
    /// sign-in cannot exhaust sign-up's ceiling. Lockout caps attempts per account; per-IP limits are PP1.8's.
    /// </summary>
    public int SignInPerMinute { get; set; } = 300;
}

internal static class ApiRateLimits
{
    public const string AnonymousAccount = "account-anonymous";
    public const string SignedInAccount = "account-signed-in";
    public const string SignIn = "account-sign-in";

    public static IServiceCollection AddApiRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiRateLimitOptions>()
            .Bind(configuration)
            .Validate(
                o => o.AnonymousAccountPerMinute > 0 && o.SignedInAccountPerMinute > 0 && o.SignInPerMinute > 0,
                "Rate limits must be positive.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(AnonymousAccount, context =>
            {
                var limits = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(AnonymousAccount, _ => PerMinute(limits.AnonymousAccountPerMinute));
            });

            options.AddPolicy(SignIn, context =>
            {
                var limits = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(SignIn, _ => PerMinute(limits.SignInPerMinute));
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
