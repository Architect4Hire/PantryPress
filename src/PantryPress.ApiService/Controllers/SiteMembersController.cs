using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.ApiService.Controllers;

/// <summary>
/// A site's members, as its Owner manages them (SITE-010; PP2.11b). Every route names its site, and the Owner policy
/// decides and enters it (tenancy.md rule 4); a member is named by their id within that site alone.
/// </summary>
[Route(ApiSurfaces.AdminV1Route)]
[Authorize]
public sealed class SiteMembersController(ISiteMembersFacade members) : ApiControllerBase
{
    private const string Members = "sites/{" + SiteAuthorization.SlugRouteValue + "}/members";

    private const string MemberIdDescription = "The member's id, exactly as id in the member list gives it.";

    /// <summary>
    /// The site's members, active and suspended, in the order they joined, a page at a time, with the address each signs
    /// in with.
    /// </summary>
    [HttpGet(Members)]
    [Authorize(Policy = SitePolicies.SiteOwner)]
    [EnableRateLimiting(ApiRateLimits.SiteMemberList)]
    [ProducesResponseType<SiteMemberPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest,
        ApiProblems.ContentType,
        Description = "validation-failed: limit is not 1 to 100, or cursor is not one this list gave; errors names which.")]
    public Task<IActionResult> List(
        string siteSlug,
        [FromQuery, Description("How many members at most, 1 to 100. Missing means 50.")] int? limit,
        [FromQuery, Description("The nextCursor of the page before, as given; missing for the first page.")] string? cursor,
        CancellationToken cancellationToken) =>
        ForAccount(account => members.ListAsync(account, siteSlug, new ListSiteMembersQuery(limit, cursor), cancellationToken), OkOr);

    /// <summary>
    /// Gives a member another role. Their next request works with it. Giving the role they already have changes nothing.
    /// The site always keeps an active owner, so its last one cannot be given another role.
    /// </summary>
    [HttpPut(Members + "/{memberId}/role")]
    [Authorize(Policy = SitePolicies.SiteOwner)]
    [Idempotent]
    [EnableRateLimiting(ApiRateLimits.SiteMemberChange)]
    [EndpointDescription(
        "Safe to retry: setting a role the member already has changes nothing and answers 204 again, and with the same "
        + "Idempotency-Key a retry gets the first answer back. Except for an owner who gave themselves another role: the "
        + "first request took effect, so a retry, with the key or without, is answered 403 site-role-required. Read the "
        + "caller's site to confirm their role.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest,
        ApiProblems.ContentType,
        Description = "validation-failed: the role is missing or not one of owner, editor, or author; errors names it. "
            + "idempotency-key-invalid: the Idempotency-Key is not 1 to 128 visible characters.")]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound,
        ApiProblems.ContentType,
        Description = "member-not-found: no member of this site has that id - unknown, or another site's - one answer.")]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status409Conflict,
        ApiProblems.ContentType,
        Description = "last-owner: the member is the site's last active owner. "
            + "member-changed: someone else changed this member at the same moment; nothing changed - read the member "
            + "list, and try again with a new Idempotency-Key, since this answer is replayed for the old one. "
            + "idempotency-key-in-use: the first request with this key is still running.")]
    public Task<IActionResult> ChangeRole(
        string siteSlug,
        [Description(MemberIdDescription)] string memberId,
        ChangeMemberRoleRequest request,
        CancellationToken cancellationToken) =>
        ForAccount(account => members.ChangeRoleAsync(account, siteSlug, memberId, request, cancellationToken), NoContentOr);

    /// <summary>
    /// Removes a member from the site; their next request reaches it no more. An owner removing themselves leaves the
    /// site. The site always keeps an active owner, so its last one cannot be removed.
    /// </summary>
    [HttpDelete(Members + "/{memberId}")]
    [Authorize(Policy = SitePolicies.SiteOwner)]
    [Idempotent]
    [EnableRateLimiting(ApiRateLimits.SiteMemberChange)]
    [EndpointDescription(
        "If this answer is lost, a retry with the same Idempotency-Key gets it again; a retry without one is answered 404 "
        + "member-not-found, because the first request removed the member. Except for an owner who removed themselves: "
        + "the first request took them off the site, so a retry, with the key or without, is answered 404 site-not-found. "
        + "List the caller's sites to confirm. Two owners removing one member at the same moment both hear 204; the "
        + "member is gone either way.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest,
        ApiProblems.ContentType,
        Description = "idempotency-key-invalid: the Idempotency-Key is not 1 to 128 visible characters.")]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound,
        ApiProblems.ContentType,
        Description = "member-not-found: no member of this site has that id - unknown, already removed, or another "
            + "site's - one answer.")]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status409Conflict,
        ApiProblems.ContentType,
        Description = "last-owner: the member is the site's last active owner. "
            + "member-changed: someone else changed this member at the same moment; nothing changed - read the member "
            + "list, and try again with a new Idempotency-Key, since this answer is replayed for the old one. "
            + "idempotency-key-in-use: the first request with this key is still running.")]
    public Task<IActionResult> Remove(
        string siteSlug,
        [Description(MemberIdDescription)] string memberId,
        CancellationToken cancellationToken) =>
        ForAccount(account => members.RemoveAsync(account, siteSlug, memberId, cancellationToken), NoContentOr);

    private async Task<IActionResult> ForAccount<TResult>(
        Func<AccountId, Task<TResult>> call,
        Func<TResult, IActionResult> map) =>
        User.TryGetAccountId(out var account) ? map(await call(account)) : Challenge();
}
