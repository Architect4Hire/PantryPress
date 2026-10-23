using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

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

    /// <summary>Long enough to read a code off a phone, short enough that a stolen password is not a standing invitation.</summary>
    public static readonly TimeSpan SecondFactorLifetime = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddSessionCookies(this IServiceCollection services)
    {
        // The ticket protector. Each scheme protects with its own purpose, so a half-finished session's cookie can
        // never be read as a complete one. PP19 makes this key ring persistent: until then a gateway restart signs
        // everyone out.
        services.AddDataProtection().SetApplicationName("PantryPress.Gateway");

        services.AddAuthentication(SessionSchemes.Complete)
            .AddCookie(SessionSchemes.Complete, options => Configure(options, CompleteName, CompleteLifetime, sliding: true))
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

    public static ClaimsPrincipal CompletePrincipal(SessionStart session, bool secondFactorVerified)
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
            claims.Add(new Claim(SessionClaims.EmailVerified, SessionClaims.True));
        }

        if (secondFactorVerified)
        {
            claims.Add(new Claim(SessionClaims.SecondFactorVerified, SessionClaims.True));
        }

        return Principal(SessionSchemes.Complete, claims);
    }

    /// <summary>The same session against a new security stamp, after a request that rotated it (PP1.6).</summary>
    public static ClaimsPrincipal WithStamp(ClaimsPrincipal user, string stamp)
    {
        ArgumentNullException.ThrowIfNull(user);
        var claims = user.Claims
            .Where(claim => !string.Equals(claim.Type, SessionClaims.Stamp, StringComparison.Ordinal))
            .Append(new Claim(SessionClaims.Stamp, stamp));
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

    private static ClaimsPrincipal Principal(string scheme, IEnumerable<Claim> claims) =>
        new(new ClaimsIdentity(claims, scheme, ClaimTypes.Email, ClaimTypes.Role));

    private static void Configure(CookieAuthenticationOptions options, string name, TimeSpan lifetime, bool sliding)
    {
        options.Cookie.Name = name;
        options.Cookie.HttpOnly = true;

        // Always, in every environment: a cookie that is allowed over plain HTTP once is a cookie that can be taken
        // over plain HTTP. Locally the gateway is reached over HTTPS at app.localhost.
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
