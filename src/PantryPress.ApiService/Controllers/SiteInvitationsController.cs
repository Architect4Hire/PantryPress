using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.ApiService.Controllers;

/// <summary>
/// Inviting people to a site by email, and joining one through an invitation (SITE-010; PP2.11). Inviting names its site,
/// and the Owner policy decides and enters it (tenancy.md rule 4). Accepting is account-level: the caller is no member
/// yet, and the site is the one sealed inside the invitation's link, never one the request names.
/// </summary>
[Route(ApiSurfaces.AdminV1Route)]
[Authorize]
public sealed class SiteInvitationsController(ISiteInvitationsFacade invitations) : ApiControllerBase
{
    /// <summary>
    /// Invites someone to the site by email with the role they will have, and emails them a link that works once, for
    /// seven days. The answer is the same whether or not the address has a PantryPress account. Inviting an address
    /// again replaces its open invitation - the older link stops working - and the one replaced does not count toward the
    /// site's limit on open invitations.
    /// </summary>
    [HttpPost("sites/{" + SiteAuthorization.SlugRouteValue + "}/invitations")]
    [Authorize(Policy = SitePolicies.SiteOwner)]
    [Idempotent]
    [EnableRateLimiting(ApiRateLimits.SiteInvite)]
    [EndpointDescription(
        "If this answer is lost, a retry with the same Idempotency-Key gets it again; a retry without one sends a second "
        + "invitation, and the first one's link stops working.")]
    [ProducesResponseType<SiteInvitationDetail>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest,
        ApiProblems.ContentType,
        Description = "validation-failed: the address or the role needs another look; errors names which. "
            + "idempotency-key-invalid: the Idempotency-Key is not 1 to 128 visible characters.")]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status409Conflict,
        ApiProblems.ContentType,
        Description = "too-many-invitations: the site already has as many open invitations as it may. "
            + "idempotency-key-in-use: the first request with this key is still running.")]
    public Task<IActionResult> Invite(string siteSlug, InviteMemberRequest request, CancellationToken cancellationToken) =>
        ForAccount(account => invitations.InviteAsync(account, siteSlug, request, cancellationToken), AcceptedOr);

    /// <summary>
    /// Joins the site an invitation is for, with its role, from the token in the invitation's link. The caller must be
    /// signed in with the invited address, confirmed. The answer is the site, as the caller's list of sites shows it.
    /// </summary>
    [HttpPost("site-invitations/accept")]
    [Idempotent]
    [EnableRateLimiting(ApiRateLimits.InvitationAccept)]
    [EndpointDescription(
        "If this answer is lost, a retry with the same Idempotency-Key gets it again; a retry without one is answered 409 "
        + "already-member, because the first request joined the site. List the caller's sites to find it.")]
    [ProducesResponseType<SiteSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest,
        ApiProblems.ContentType,
        Description = "invitation-invalid: the link has expired, was used or replaced, or is not one of ours - one answer, "
            + "which never says which. validation-failed: no token was sent, or it is far too long to be one. "
            + "idempotency-key-invalid: the Idempotency-Key is not 1 to 128 visible characters.")]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status403Forbidden,
        ApiProblems.ContentType,
        Description = "invitation-other-address: the caller is signed in with another address than the invited one. "
            + "email-not-verified: the caller has the invited address but has not confirmed it yet.")]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status409Conflict,
        ApiProblems.ContentType,
        Description = "already-member: the caller already belongs to the site; nothing changed. "
            + "idempotency-key-in-use: the first request with this key is still running.")]
    public Task<IActionResult> Accept(AcceptInvitationRequest request, CancellationToken cancellationToken) =>
        ForAccount(account => invitations.AcceptAsync(account, request, cancellationToken), OkOr);

    private async Task<IActionResult> ForAccount<TResult>(
        Func<AccountId, Task<TResult>> call,
        Func<TResult, IActionResult> map) =>
        User.TryGetAccountId(out var account) ? map(await call(account)) : Challenge();
}
