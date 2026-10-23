using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.ApiService.Controllers;

/// <summary>Password reset and change (ACCT-001; PP1.3).</summary>
[Route(ApiSurfaces.AdminV1Route + "/account/password")]
public sealed class PasswordController(IPasswordFacade passwords) : ApiControllerBase
{
    /// <summary>
    /// Emails a reset link that works for one hour, if the address has an account. Always 202, so the response never
    /// reveals which addresses are registered.
    /// </summary>
    [HttpPost("forgot")]
    [EnableRateLimiting(ApiRateLimits.AnonymousAccount)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    public async Task<IActionResult> Forgot(ForgotPasswordRequest request, CancellationToken cancellationToken) =>
        AcceptedOr(await passwords.ForgotAsync(request, cancellationToken));

    /// <summary>Sets a new password with the account id and token from the reset link. A link works once.</summary>
    [HttpPost("reset")]
    [EnableRateLimiting(ApiRateLimits.AnonymousAccount)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType)]
    public async Task<IActionResult> Reset(ResetPasswordRequest request, CancellationToken cancellationToken) =>
        NoContentOr(await passwords.ResetAsync(request, cancellationToken));

    /// <summary>Changes the password of the signed-in account. Wrong current passwords count toward lockout.</summary>
    [HttpPost("change")]
    [Authorize]
    [EnableRateLimiting(ApiRateLimits.SignedInAccount)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType)]
    public async Task<IActionResult> Change(ChangePasswordRequest request, CancellationToken cancellationToken) =>
        User.TryGetAccountId(out var account)
            ? NoContentOr(await passwords.ChangeAsync(account, request, cancellationToken))
            : Challenge();
}
