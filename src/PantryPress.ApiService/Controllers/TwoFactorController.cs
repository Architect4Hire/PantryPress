using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.ApiService.Controllers;

/// <summary>
/// Optional two-step sign-in for the signed-in account (ACCT-001; PP1.3). Responses carry secrets (the shared key,
/// recovery codes) and are never cached (the API's default <c>no-store</c>). The code check at sign-in is part of the
/// PP1.6 session flow, not a route here.
/// </summary>
[Route(ApiSurfaces.AdminV1Route + "/account/two-factor")]
[Authorize]
[EnableRateLimiting(ApiRateLimits.SignedInAccount)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ApiProblems.ContentType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType)]
public sealed class TwoFactorController(ITwoFactorFacade twoFactor) : ApiControllerBase
{
    /// <summary>Whether two-step sign-in is on, and how many recovery codes are left.</summary>
    [HttpGet]
    [ProducesResponseType<TwoFactorStatus>(StatusCodes.Status200OK)]
    public Task<IActionResult> GetStatus(CancellationToken cancellationToken) =>
        ForAccount(account => twoFactor.GetStatusAsync(account, cancellationToken), OkOr);

    /// <summary>A new shared key for the authenticator app. Refused while two-step sign-in is on.</summary>
    [HttpPost("setup")]
    [RefreshesSession]
    [ProducesResponseType<AuthenticatorSetup>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType)]
    public Task<IActionResult> BeginSetup(CancellationToken cancellationToken) =>
        ForAccount(account => twoFactor.BeginSetupAsync(account, cancellationToken), OkOr);

    /// <summary>Turns two-step sign-in on with a code from the app, and returns recovery codes to save.</summary>
    [HttpPost("enable")]
    [RefreshesSession]
    [ProducesResponseType<RecoveryCodeList>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType)]
    public Task<IActionResult> Enable(EnableTwoFactorRequest request, CancellationToken cancellationToken) =>
        ForAccount(account => twoFactor.EnableAsync(account, request, cancellationToken), OkOr);

    /// <summary>Turns two-step sign-in off after the password is confirmed.</summary>
    [HttpPost("disable")]
    [RefreshesSession]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType)]
    public Task<IActionResult> Disable(ConfirmPasswordRequest request, CancellationToken cancellationToken) =>
        ForAccount(account => twoFactor.DisableAsync(account, request, cancellationToken), NoContentOr);

    /// <summary>Replaces every recovery code after the password is confirmed; the old codes stop working.</summary>
    [HttpPost("recovery-codes")]
    [ProducesResponseType<RecoveryCodeList>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType)]
    public Task<IActionResult> RegenerateRecoveryCodes(ConfirmPasswordRequest request, CancellationToken cancellationToken) =>
        ForAccount(account => twoFactor.RegenerateRecoveryCodesAsync(account, request, cancellationToken), OkOr);

    private async Task<IActionResult> ForAccount<TResult>(
        Func<AccountId, Task<TResult>> call,
        Func<TResult, IActionResult> map) =>
        User.TryGetAccountId(out var account) ? map(await call(account)) : Challenge();
}
