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

    /// <summary>Told apart from a plain 401 so the admin app can say "you were signed out" rather than "sign in".</summary>
    public const string EndedCode = "session-ended";

    public const string EndedTitle = "You've been signed out. Sign in again to pick up where you left off.";

    /// <summary>Every header the gateway owns. The gateway strips all of them from an inbound request.</summary>
    public static readonly string[] RequestHeaders = [SecretHeader, AccountHeader, StampHeader, SecondFactorHeader];
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
