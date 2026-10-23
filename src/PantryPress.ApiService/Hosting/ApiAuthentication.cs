using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.ApiService.Hosting;

internal static class ApiAuthentication
{
    /// <summary>
    /// Signed-in routes are marked <c>[Authorize]</c> now, but the session that proves who is calling arrives with the
    /// BFF in PP1.6 (the gateway holds the session and forwards the user). Until then this scheme authenticates nobody,
    /// so signed-in routes answer 401 in every real run; tests swap in their own scheme. PP1.6 replaces it.
    /// </summary>
    public const string PendingSessionScheme = "PendingSession";

    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(PendingSessionScheme)
            .AddScheme<AuthenticationSchemeOptions, PendingSessionHandler>(PendingSessionScheme, configureOptions: null);
        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// The signed-in account, from the name-identifier claim Identity issues. False for an anonymous caller or a
    /// malformed claim; a controller then challenges rather than guessing.
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

    private sealed class PendingSessionHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IProblemDetailsService problems)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
            WriteProblemAsync(StatusCodes.Status401Unauthorized);

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
            WriteProblemAsync(StatusCodes.Status403Forbidden);

        private async Task WriteProblemAsync(int status)
        {
            Response.StatusCode = status;
            await problems.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = Context,
                ProblemDetails = { Status = status },
            });
        }
    }
}
