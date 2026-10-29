using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using PantryPress.Gateway.Contracts;

namespace PantryPress.Gateway.Hosting;

/// <summary>The two stages of a session, each its own authentication scheme and its own cookie.</summary>
internal static class SessionSchemes
{
    /// <summary>A session that may act: password accepted, and the second step passed if the account has one.</summary>
    public const string Complete = "pp-session";

    /// <summary>A sign-in that owes a second step. It can finish signing in and nothing else.</summary>
    public const string SecondFactor = "pp-session-pending";
}

/// <summary>What the gateway keeps in a session ticket. None of it is readable by a browser.</summary>
internal static class SessionClaims
{
    /// <summary>The security stamp the session is bound to; it travels to the API, never to the browser.</summary>
    public const string Stamp = "pp:stamp";

    /// <summary>Mirrors the API's <c>AccountClaims.TwoFactorVerified</c>.</summary>
    public const string SecondFactorVerified = "pp:mfa";

    public const string EmailVerified = "pp:email-verified";

    /// <summary>
    /// The session's own id: 32 random bytes, minted at sign-in and kept for the session's whole life (PP1.12). Signing
    /// out revokes it at the API, so the cookie is refused from then on, whoever presents it.
    /// </summary>
    public const string SessionId = "pp:sid";

    /// <summary>
    /// When the person signed in, in Unix seconds (PP1.12). The ticket's own issue time moves every time the sliding
    /// window renews it, so the absolute lifetime is measured from this instead.
    /// </summary>
    public const string AuthenticatedAt = "pp:auth-time";

    public const string True = "true";
}

/// <summary>
/// The BFF session cookies (ACCT-002, PSEC-003, PP-D13; PP1.6). Both are <c>__Host-</c> prefixed, host-only,
/// <c>Secure</c>, <c>HttpOnly</c>, <c>SameSite=Lax</c>, and <c>Path=/</c> with no <c>Domain</c>: tenant sites run
/// third-party ad script on sibling subdomains of <c>pantrypress.app</c>, so no cookie of ours may be scoped to the
/// parent domain, and the <c>__Host-</c> prefix is what stops a sibling from writing one of these at all. Nothing in
/// either cookie is a token: a ticket names an account and the security stamp its session was issued against, and the
/// API decides on every request what that account may do.
/// </summary>
internal static class SessionCookie
{
    public const string CompleteName = "__Host-pp-session";

    public const string SecondFactorName = "__Host-pp-pending";

    /// <summary>A working day, slid forward while the person keeps working. Not persistent: closing the browser ends it.</summary>
    public static readonly TimeSpan CompleteLifetime = TimeSpan.FromHours(8);

    /// <summary>
    /// However busy the person keeps it, a session ends this long after the password was given (PP1.12; NIST SP 800-63B's
    /// reauthentication bound for a second factor). Without it the sliding window renewed a ticket for ever, so a copied
    /// cookie that kept being used never ran out. ApiService remembers a sign-out for longer than this, and a test pins
    /// the two together.
    /// </summary>
    public static readonly TimeSpan CompleteAbsoluteLifetime = TimeSpan.FromHours(12);

    /// <summary>Long enough to read a code off a phone, short enough that a stolen password is not a standing invitation.</summary>
    public static readonly TimeSpan SecondFactorLifetime = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddSessionCookies(this IServiceCollection services)
    {
        // The ticket protector. Each scheme protects with its own purpose, so a half-finished session's cookie can
        // never be read as a complete one. PP19 makes this key ring persistent: until then a gateway restart signs
        // everyone out.
        services.AddDataProtection().SetApplicationName("PantryPress.Gateway");
        services.AddGatewayClock();

        services.AddAuthentication(SessionSchemes.Complete)
            .AddCookie(SessionSchemes.Complete, options =>
            {
                Configure(options, CompleteName, CompleteLifetime, sliding: true);

                // Read on every request that carries the cookie: a session past its absolute lifetime is over, and the
                // cookie goes with it, however recently it was used (PP1.12).
                options.Events.OnValidatePrincipal = async context =>
                {
                    var now = context.HttpContext.RequestServices.GetRequiredService<GatewayClock>().UtcNow;
                    if (context.Principal is { } user && IsPastAbsoluteLifetime(user, now))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(SessionSchemes.Complete);
                    }
                };
            })
            .AddCookie(
                SessionSchemes.SecondFactor,
                options => Configure(options, SecondFactorName, SecondFactorLifetime, sliding: false));

        // Ticket lifetimes are read from the application's clock when one is registered, so a test can move time and
        // watch a half-finished sign-in run out. Left alone, each handler keeps its own default.
        foreach (var scheme in new[] { SessionSchemes.Complete, SessionSchemes.SecondFactor })
        {
            services.AddOptions<CookieAuthenticationOptions>(scheme)
                .Configure<IServiceProvider>((options, provider) =>
                    options.TimeProvider = provider.GetService<TimeProvider>() ?? options.TimeProvider);
        }

        return services;
    }

    /// <summary>
    /// The half-finished session. It carries the stamp too: that is what proves to the API that the password step
    /// really happened, and it means a password or role change inside the window ends the sign-in instead of letting
    /// a code finish it.
    /// </summary>
    public static ClaimsPrincipal SecondFactorPrincipal(SessionStart session)
    {
        ArgumentNullException.ThrowIfNull(session);
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, session.AccountId.ToString("D")),
            new(ClaimTypes.Email, session.Email),
            new(SessionClaims.Stamp, session.SecurityStamp),
        ];

        if (session.EmailVerified)
        {
            // So the current-session route tells the truth at this stage too, not a hardcoded "unverified".
            claims.Add(new Claim(SessionClaims.EmailVerified, SessionClaims.True));
        }

        return Principal(SessionSchemes.SecondFactor, claims);
    }

    public static ClaimsPrincipal CompletePrincipal(
        SessionStart session,
        bool secondFactorVerified,
        string sessionId,
        DateTimeOffset authenticatedAt)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, session.AccountId.ToString("D")),
            new(ClaimTypes.Email, session.Email),
            new(SessionClaims.Stamp, session.SecurityStamp),
            new(SessionClaims.SessionId, sessionId),
            new(
                SessionClaims.AuthenticatedAt,
                authenticatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
        ];

        if (session.EmailVerified)
        {
            claims.Add(new Claim(SessionClaims.EmailVerified, SessionClaims.True));
        }

        if (secondFactorVerified)
        {
            claims.Add(new Claim(SessionClaims.SecondFactorVerified, SessionClaims.True));
        }

        return Principal(SessionSchemes.Complete, claims);
    }

    /// <summary>A new session id: 32 bytes from the system's cryptographic generator, base64url.</summary>
    public static string NewSessionId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// The same session against a new security stamp, after a request that rotated it (PP1.6). Its id and sign-in time
    /// carry over, so it is still the same session to sign out and still ends when it would have.
    /// </summary>
    /// <remarks>
    /// <paramref name="passedSecondStep"/> when the request that rotated it passed the second step itself - it turned
    /// two-step sign-in on with a valid code - so the session goes on carrying that claim (PP1.12).
    /// </remarks>
    public static ClaimsPrincipal WithStamp(ClaimsPrincipal user, string stamp, bool passedSecondStep = false)
    {
        ArgumentNullException.ThrowIfNull(user);
        var claims = user.Claims
            .Where(claim => !string.Equals(claim.Type, SessionClaims.Stamp, StringComparison.Ordinal)
                && !(passedSecondStep && string.Equals(claim.Type, SessionClaims.SecondFactorVerified, StringComparison.Ordinal)))
            .Append(new Claim(SessionClaims.Stamp, stamp));
        if (passedSecondStep)
        {
            claims = claims.Append(new Claim(SessionClaims.SecondFactorVerified, SessionClaims.True));
        }

        return Principal(SessionSchemes.Complete, [.. claims]);
    }

    /// <summary>A session's ticket properties: never persistent, so the cookie lives as long as the browser session.</summary>
    public static AuthenticationProperties Properties() => new() { IsPersistent = false, AllowRefresh = true };

    public static bool TryGetAccount(ClaimsPrincipal? user, out Guid account)
    {
        account = Guid.Empty;
        return user?.Identity?.IsAuthenticated == true
            && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out account)
            && account != Guid.Empty;
    }

    public static string? Stamp(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.FindFirstValue(SessionClaims.Stamp);
    }

    public static string? SessionId(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.FindFirstValue(SessionClaims.SessionId);
    }

    /// <summary>
    /// Whether a complete session is past its absolute lifetime, or carries no sign-in time or id - a ticket from
    /// before PP1.12, with nothing to sign it out by. Either way it is over.
    /// </summary>
    public static bool IsPastAbsoluteLifetime(ClaimsPrincipal user, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        return !long.TryParse(
                user.FindFirstValue(SessionClaims.AuthenticatedAt),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds)
            || string.IsNullOrEmpty(SessionId(user))
            || now - DateTimeOffset.FromUnixTimeSeconds(seconds) >= CompleteAbsoluteLifetime;
    }

    public static string Email(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
    }

    public static bool Has(ClaimsPrincipal user, string claim)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.HasClaim(claim, SessionClaims.True);
    }

    // Named by the account id, so nothing that logs a principal's name logs an email address.
    private static ClaimsPrincipal Principal(string scheme, IEnumerable<Claim> claims) =>
        new(new ClaimsIdentity(claims, scheme, ClaimTypes.NameIdentifier, ClaimTypes.Role));

    private static void Configure(CookieAuthenticationOptions options, string name, TimeSpan lifetime, bool sliding)
    {
        options.Cookie.Name = name;
        options.Cookie.HttpOnly = true;

        // Always, in every environment: a cookie that is allowed over plain HTTP once is a cookie that can be taken
        // over plain HTTP. Locally the gateway is reached over HTTPS at the apex, localhost.
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";

        // No Domain, ever: that is what the __Host- prefix requires and what keeps the cookie off sibling subdomains.
        options.Cookie.Domain = null;
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = lifetime;
        options.SlidingExpiration = sliding;

        // There is no sign-in page to send a browser to: the admin app is a SPA and asks over fetch, so an
        // unauthenticated answer is a problem document, like every other failure on this surface.
        options.Events.OnRedirectToLogin = context => GatewayApi.WriteProblemAsync(
            context.HttpContext,
            StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => GatewayApi.WriteProblemAsync(
            context.HttpContext,
            StatusCodes.Status403Forbidden);
    }
}
