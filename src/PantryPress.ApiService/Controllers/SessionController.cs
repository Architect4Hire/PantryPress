using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.ApiService.Controllers;

/// <summary>
/// The sign-in steps the gateway calls while it issues its session cookie (ACCT-002, PP-D13; PP1.6). This is the
/// internal surface: not a browser route, not forwarded by the BFF, and refused outright for any caller that cannot
/// present the gateway's secret. Responses name an account and the security stamp its session is bound to, which is
/// why nothing but the gateway may read them.
/// </summary>
[Route(ApiSurfaces.InternalV1Route + "/sessions")]
[GatewayOnly]
[EnableRateLimiting(ApiRateLimits.SignIn)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType)]
public sealed class SessionController(ISessionFacade sessions) : ApiControllerBase
{
    /// <summary>
    /// Checks an email address and password. One neutral failure covers an unknown address and a wrong password
    /// alike; a locked account is told to wait. Success says whether a second step is still owed.
    /// </summary>
    [HttpPost("credentials")]
    [ProducesResponseType<SessionStart>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    public async Task<IActionResult> SignIn(SignInRequest request, CancellationToken cancellationToken) =>
        OkOr(await sessions.SignInAsync(request, cancellationToken));

    /// <summary>
    /// Completes a sign-in that owes a second step, with an authenticator or recovery code. The account and the stamp
    /// come from the gateway's half-finished session, never from the person signing in, and the stamp is what proves
    /// the password step happened.
    /// </summary>
    [HttpPost("second-factor")]
    [ProducesResponseType<SessionStart>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType)]
    public async Task<IActionResult> CompleteSecondFactor(
        SecondFactorSignInRequest request,
        CancellationToken cancellationToken) =>
        OkOr(await sessions.CompleteSecondFactorAsync(
            new AccountId(request.AccountId),
            request.SecurityStamp,
            new SecondFactorRequest(request.Code),
            cancellationToken));
}
