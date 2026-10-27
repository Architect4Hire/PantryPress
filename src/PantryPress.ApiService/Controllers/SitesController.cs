using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.ApiService.Controllers;

/// <summary>
/// Creating a site, the caller's sites, one of them, and whether an address is free (SITE-002, SITE-003; PP2.10). Create,
/// list, and availability are account-level - no site exists, or none is named - so they take no site policy; reading one
/// names its site, and its policy decides and enters it (tenancy.md rule 4). Each is limited per account, with its own
/// allowance.
/// </summary>
[Route(ApiSurfaces.AdminV1Route)]
[Authorize]
public sealed class SitesController(ISitesFacade sites) : ApiControllerBase
{
    /// <summary>
    /// Creates a site at <c>{slug}.pantrypress.app</c> with the caller as its Owner. The answer is the new site, and
    /// <c>Location</c> its address in this API.
    /// </summary>
    [HttpPost("sites")]
    [Idempotent]
    [EnableRateLimiting(ApiRateLimits.SiteCreate)]
    [EndpointDescription(
        "If this answer is lost, a retry with the same Idempotency-Key gets it again; a retry without one is answered 409 "
        + "slug-unavailable, because the first request took the address. List the caller's sites to find it.")]
    [ProducesResponseType<SiteDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status403Forbidden,
        ApiProblems.ContentType,
        Description = "email-not-verified: the caller's email address is not confirmed and they already own as many sites "
            + "as an unconfirmed account may. Confirming the address lifts it.")]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status409Conflict,
        ApiProblems.ContentType,
        Description = "slug-unavailable: the address is taken, reserved, or was used by a site before - one answer, which "
            + "never says which. idempotency-key-in-use: the first request with this key is still running.")]
    public Task<IActionResult> Create(CreateSiteRequest request, CancellationToken cancellationToken) =>
        ForAccount(
            account => sites.CreateAsync(account, request, cancellationToken),
            result => CreatedOr(result, site => $"/{ApiSurfaces.AdminV1Route}/sites/{site.Slug}"));

    /// <summary>The caller's sites, ordered by address, a page at a time.</summary>
    [HttpGet("sites")]
    [EnableRateLimiting(ApiRateLimits.Sites)]
    [ProducesResponseType<SitePage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    public Task<IActionResult> List(
        [FromQuery, Description("How many sites at most, 1 to 100. Missing means 50.")] int? limit,
        [FromQuery, Description("The nextCursor of the page before, as given; missing for the first page.")] string? cursor,
        CancellationToken cancellationToken) =>
        ForAccount(account => sites.ListAsync(account, new ListSitesQuery(limit, cursor), cancellationToken), OkOr);

    /// <summary>One of the caller's sites, with the caller's role there.</summary>
    [HttpGet("sites/{" + SiteAuthorization.SlugRouteValue + "}")]
    [Authorize(Policy = SitePolicies.SiteAuthor)]
    [EnableRateLimiting(ApiRateLimits.Sites)]
    [ProducesResponseType<SiteDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> Get(string siteSlug, CancellationToken cancellationToken) =>
        ForAccount(account => sites.GetAsync(account, siteSlug, cancellationToken), OkOr);

    /// <summary>
    /// Whether a site could take this address now. "Not available" never says why: reserved, taken, and once used all
    /// answer alike.
    /// </summary>
    [HttpGet("slug-availability")]
    [EnableRateLimiting(ApiRateLimits.SlugCheck)]
    [ProducesResponseType<SlugAvailability>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType)]
    public Task<IActionResult> CheckAvailability(
        [FromQuery, Required, Description("The address to ask about, as the blogger typed it.")] string slug,
        CancellationToken cancellationToken) =>
        ForAccount(_ => sites.CheckAvailabilityAsync(new SlugAvailabilityQuery(slug), cancellationToken), OkOr);

    private async Task<IActionResult> ForAccount<TResult>(
        Func<AccountId, Task<TResult>> call,
        Func<TResult, IActionResult> map) =>
        User.TryGetAccountId(out var account) ? map(await call(account)) : Challenge();
}
