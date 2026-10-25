using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Platform.Contracts;

namespace PantryPress.ApiService.Hosting;

internal static partial class ApiAuthentication
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(GatewaySession.Scheme)
            .AddScheme<AuthenticationSchemeOptions, GatewaySessionHandler>(GatewaySession.Scheme, configureOptions: null);

        // The named policies (auth.md). PlatformAdmin is the platform-wide one (PP-D03): the role plus a session that
        // passed the second sign-in step, because an operator's password alone must not be enough (ACCT-005). No route
        // carries it yet; the operator console (PP18, PP19) is what will. The site policies come from membership, never
        // from Identity roles (PP2.3).
        services.AddAuthorizationBuilder()
            .AddPolicy(PlatformPolicies.PlatformAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(PlatformRoles.PlatformAdmin)
                .RequireClaim(AccountClaims.TwoFactorVerified, AccountClaims.Yes))
            .AddSitePolicies();
        return services;
    }

    /// <summary>
    /// The signed-in account, from the name-identifier claim the session scheme issues. False for an anonymous caller
    /// or a malformed claim; a controller then challenges rather than guessing.
    /// </summary>
    public static bool TryGetAccountId(this ClaimsPrincipal user, out AccountId account)
    {
        account = default;
        if (user.Identity?.IsAuthenticated != true
            || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            || id == Guid.Empty)
        {
            return false;
        }

        account = new AccountId(id);
        return true;
    }

    /// <summary>
    /// The session the gateway forwards (ACCT-002; PP1.6). The gateway's cookie names an account, the security stamp its
    /// session was issued against, and the session's own id (PP1.12); everything the caller is allowed to do is read from the database here, on
    /// every request, so a changed password, a changed second factor, or a revoked platform role ends a session at its
    /// next request rather than whenever a cached copy happens to expire (ACCT-005).
    /// </summary>
    private sealed partial class GatewaySessionHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        GatewayTrust trust,
        ISessionFacade sessions,
        IProblemDetailsService problems)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        /// <summary>A stamp is a short opaque string; anything much longer is not one of ours.</summary>
        private const int MaxStampLength = 256;

        private const string SessionEndedKey = "pp:session-ended";

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!trust.IsFromGateway(Request))
            {
                // Identity headers from a caller that cannot prove it is the gateway are the shape of an attempt to
                // forge one. They are ignored, never read, and worth hearing about.
                if (GatewaySession.RequestHeaders.Any(header => Request.Headers.ContainsKey(header)))
                {
                    LogUntrustedIdentity(Logger, Request.Path);
                }

                return AuthenticateResult.NoResult();
            }

            if (Request.Headers[GatewaySession.AccountHeader] is not [{ } accountHeader]
                || !Guid.TryParse(accountHeader, out var id)
                || id == Guid.Empty)
            {
                return AuthenticateResult.NoResult();
            }

            if (Request.Headers[GatewaySession.StampHeader] is not [{ Length: > 0 and <= MaxStampLength } stamp])
            {
                return AuthenticateResult.NoResult();
            }

            // Every complete session the gateway holds has an id (PP1.12); a request without one names no session.
            if (Request.Headers[GatewaySession.SessionIdHeader] is not [{ } sessionId]
                || !SessionLimits.IsValidSessionId(sessionId))
            {
                return AuthenticateResult.NoResult();
            }

            // Whether this session may act at all - the stamp, a sign-out, the account, and the second-step rule - is
            // the facade's to decide; this handler only turns its answer into a ticket or a 401.
            var secondFactorVerified = Request.Headers[GatewaySession.SecondFactorHeader] == GatewaySession.True;
            var identity = await sessions.ResolveAsync(
                new AccountId(id),
                stamp,
                sessionId,
                secondFactorVerified,
                Context.RequestAborted);
            if (identity is null)
            {
                return Ended("This session no longer resolves to an account that may act.");
            }

            var claims = new List<Claim>(3 + identity.Roles.Count)
            {
                new(ClaimTypes.NameIdentifier, identity.AccountId.ToString("D")),
                new(ClaimTypes.Email, identity.Email),
            };

            // What the session has proved now, as the facade judged it - not the gateway's word that it once did.
            if (identity.SecondFactorVerified)
            {
                claims.Add(new Claim(AccountClaims.TwoFactorVerified, AccountClaims.Yes));
            }

            claims.AddRange(identity.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

            // Named by the account id, never the email address, so nothing that logs a principal's name logs an email.
            var principal = new ClaimsPrincipal(
                new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.NameIdentifier, ClaimTypes.Role));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
            WriteProblemAsync(StatusCodes.Status401Unauthorized);

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
            WriteProblemAsync(StatusCodes.Status403Forbidden);

        private AuthenticateResult Ended(string reason)
        {
            Context.Items[SessionEndedKey] = true;
            return AuthenticateResult.Fail(reason);
        }

        private async Task WriteProblemAsync(int status)
        {
            Response.StatusCode = status;
            var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = status };
            if (status == StatusCodes.Status401Unauthorized && Context.Items.ContainsKey(SessionEndedKey))
            {
                // The gateway clears its cookie when it sees this, so the person is told they were signed out instead
                // of clicking on into 401s. The header never reaches a browser: the gateway strips it.
                Response.Headers[GatewaySession.EndedHeader] = GatewaySession.Ended;
                problem.Title = GatewaySession.EndedTitle;
                problem.Extensions[ApiProblems.CodeKey] = GatewaySession.EndedCode;
            }

            await problems.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = Context,
                ProblemDetails = problem,
            });
        }

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "A request to {Path} carried session headers without the gateway's secret; they were ignored.")]
        private static partial void LogUntrustedIdentity(ILogger logger, string path);
    }
}
