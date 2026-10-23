using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.ApiService.Controllers;

/// <summary>Sign-up and email confirmation (ACCT-001, ACCT-003; PP1.2). Anonymous; rate-limited globally.</summary>
[Route(ApiSurfaces.AdminV1Route + "/account")]
[EnableRateLimiting(ApiRateLimits.AnonymousAccount)]
public sealed class AccountController(IAccountRegistrationFacade registration) : ApiControllerBase
{
    /// <summary>
    /// Creates an account and emails a confirmation link. Answers 202 whether or not the address already has an
    /// account, so the response never reveals which addresses are registered.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    public async Task<IActionResult> Register(RegisterAccountRequest request, CancellationToken cancellationToken) =>
        AcceptedOr(await registration.RegisterAsync(request, cancellationToken));

    /// <summary>Confirms the email address with the account id and token from the emailed link.</summary>
    [HttpPost("email/verify")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType)]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken) =>
        NoContentOr(await registration.VerifyEmailAsync(request, cancellationToken));

    /// <summary>Emails a new confirmation link if the address has an unconfirmed account. Always 202.</summary>
    [HttpPost("email/resend")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    public async Task<IActionResult> ResendVerification(
        ResendVerificationRequest request,
        CancellationToken cancellationToken) =>
        AcceptedOr(await registration.ResendVerificationAsync(request, cancellationToken));
}
