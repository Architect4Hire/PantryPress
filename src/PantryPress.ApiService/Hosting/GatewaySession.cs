using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.ApiService.Hosting;

/// <summary>
/// The contract between the gateway and this service for a signed-in caller (ACCT-002, PP-D13; PP1.6). The gateway
/// holds the session in its own cookie and adds these headers to every request it forwards; this service decides
/// whether to believe them, and rebuilds the caller's identity from the database either way, so the headers carry no
/// authority of their own beyond naming an account and the stamp its session was bound to.
/// </summary>
internal static class GatewaySession
{
    /// <summary>The authentication scheme the forwarded identity authenticates with.</summary>
    public const string Scheme = "GatewaySession";

    /// <summary>Proves the request came from the gateway. Without it, no identity header is read at all.</summary>
    public const string SecretHeader = "X-PP-Gateway";

    public const string AccountHeader = "X-PP-Account";

    /// <summary>The security stamp the session was issued against; a session whose stamp moved on is over.</summary>
    public const string StampHeader = "X-PP-Stamp";

    /// <summary>Set when the session passed the second sign-in step (<see cref="AccountClaims.TwoFactorVerified"/>).</summary>
    public const string SecondFactorHeader = "X-PP-Mfa";

    /// <summary>
    /// The session's own id, minted by the gateway at sign-in (PP1.12). A signed-out session is refused by it, so a copy
    /// of the cookie stops working the moment its owner signs out.
    /// </summary>
    public const string SessionIdHeader = "X-PP-Sid";

    public const string True = "true";

    /// <summary>
    /// On a response: this session is over, so the gateway clears its cookie rather than letting the person click on
    /// into 401s. Stripped by the gateway; a browser never sees it.
    /// </summary>
    public const string EndedHeader = "X-PP-Session";

    public const string Ended = "ended";

    /// <summary>
    /// On a response: the request rotated the account's security stamp, so the gateway re-issues its cookie against
    /// the new one. That ends the account's *other* sessions without signing this one out. Stripped by the gateway.
    /// </summary>
    public const string NewStampHeader = "X-PP-New-Stamp";

    /// <summary>
    /// On a response, beside <see cref="NewStampHeader"/>: the request passed the second step itself - it turned two-step
    /// sign-in on with a valid code - so the re-issued session carries the second-step claim (PP1.12). Without it the
    /// caller's own session would end at its next request, since the account now owes a second step it never passed.
    /// Stripped by the gateway.
    /// </summary>
    public const string SecondStepPassedHeader = "X-PP-Second-Step";

    public const string SecondStepPassed = "passed";

    /// <summary>
    /// On every request the gateway's proxy forwards (PP1.12). The internal surface refuses a request carrying it, so
    /// nothing the proxy forwards can reach that surface, whatever a path or route mistake might one day let through;
    /// only the gateway's own session client, which never sets it, can.
    /// </summary>
    public const string ViaProxyHeader = "X-PP-Via";

    public const string ViaProxy = "proxy";

    /// <summary>Told apart from a plain 401 so the admin app can say "you were signed out" rather than "sign in".</summary>
    public static readonly string EndedCode = AccountErrors.SessionEnded.Code;

    public static readonly string EndedTitle = AccountErrors.SessionEnded.Title;

    /// <summary>Every header the gateway owns. The gateway strips all of them from an inbound request.</summary>
    public static readonly string[] RequestHeaders =
        [SecretHeader, AccountHeader, StampHeader, SecondFactorHeader, SessionIdHeader, ViaProxyHeader];
}

/// <summary>The secret the gateway proves itself with. Set by the AppHost (locally) or Key Vault (PP19).</summary>
public sealed class GatewayTrustOptions
{
    /// <summary>Shared with <c>PantryPress.Gateway</c> and nothing else. Never logged.</summary>
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>Long enough that guessing it is hopeless; the AppHost generates 48 characters.</summary>
    public const int MinimumSecretLength = 32;

    public bool IsValid() => SharedSecret.Length >= MinimumSecretLength;
}

/// <summary>
/// Whether a request came from the gateway. ApiService is not only reachable through the BFF - the publishing API
/// gets its own edge route at PP18 - so a forwarded identity is believed only behind this secret, compared in
/// constant time.
/// </summary>
internal sealed class GatewayTrust
{
    private readonly byte[] _secret;

    public GatewayTrust(IOptions<GatewayTrustOptions> options) =>
        _secret = Encoding.UTF8.GetBytes(options.Value.SharedSecret);

    public bool IsFromGateway(HttpRequest request)
    {
        var presented = request.Headers[GatewaySession.SecretHeader];
        if (presented.Count != 1 || presented[0] is not { Length: > 0 } value)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(_secret, Encoding.UTF8.GetBytes(value));
    }
}

internal static class GatewayTrustExtensions
{
    /// <summary>
    /// Binds and checks the secret at startup: a host with no secret could not tell the gateway from anyone else, so
    /// it refuses to start rather than serve signed-in routes to whoever asks.
    /// </summary>
    public static IServiceCollection AddGatewayTrust(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GatewayTrustOptions>()
            .Bind(configuration)
            .Validate(
                o => o.IsValid(),
                $"Gateway:SharedSecret must be at least {GatewayTrustOptions.MinimumSecretLength} characters. The "
                + "gateway proves itself with it before any forwarded identity is read.")
            .ValidateOnStart();

        services.AddSingleton<GatewayTrust>();
        return services;
    }
}

internal static class InternalSurfaceGuard
{
    /// <summary>
    /// Answers any request under the internal surface that cannot present the gateway's secret, or that the gateway's
    /// proxy forwarded rather than its session client sent, with the 404 an unknown route gets, before authentication
    /// and the rate limiter (PP1.12). <see cref="GatewayOnlyAttribute"/> refuses the
    /// same callers, but only once MVC runs - after the sign-in ceiling has counted them, so a stranger could spend the
    /// gateway's allowance and learn from a 429 that the surface exists. The status-code pages write the problem.
    /// </summary>
    public static IApplicationBuilder UseInternalSurfaceGuard(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var prefix = new PathString("/" + Http.ApiSurfaces.InternalV1Route);
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)
                && (!context.RequestServices.GetRequiredService<GatewayTrust>().IsFromGateway(context.Request)
                    || context.Request.Headers.ContainsKey(GatewaySession.ViaProxyHeader)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });
    }
}

/// <summary>
/// Refuses any caller that cannot present the gateway's secret, with the same 404 an unknown route gets: the internal
/// surface does not advertise that it is there (PP1.6). The gateway's own <c>/api</c> fallback already keeps the
/// surface away from browsers; this is what keeps it away from anything else that can reach this service.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class GatewayOnlyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var trust = context.HttpContext.RequestServices.GetRequiredService<GatewayTrust>();
        if (!trust.IsFromGateway(context.HttpContext.Request))
        {
            context.Result = new NotFoundResult();
        }
    }
}
