using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.ApiService.Controllers;

/// <summary>
/// Paste-to-parse (RCP-003; PP4.6): a pasted ingredient or step list read into proposed groups and lines for the recipe
/// editor to show for review. Nothing is saved. The routes name a site so that only its members may ask - its Author policy
/// decides and enters it (tenancy.md rule 4) - but the answer reads nothing of it.
/// </summary>
[Route(ApiSurfaces.AdminV1Route)]
[Authorize]
public sealed class RecipePasteController(IRecipePasteFacade paste) : ApiControllerBase
{
    private const string Paste = "sites/{" + SiteAuthorization.SlugRouteValue + "}/recipes/paste";

    private const string SavesNothing =
        "Saves nothing, so it is safe to repeat: a retry after a lost answer gets the same reading, unless it is over this "
        + "route's allowance (429) or the caller has left the site since (404). No Idempotency-Key is needed; one sent is "
        + "ignored. Each line comes back exactly as pasted in originalText, with what was read from it beside it; spans count "
        + "UTF-16 code units. Blank lines are skipped and never start a group.";

    private const string Invalid =
        "validation-failed: text is missing, holds no line, holds a broken character (half of a surrogate pair), or is over "
        + "30,000 characters or 200 lines; errors names it.";

    /// <summary>
    /// Reads a pasted ingredient list: group headings, and every other line through the ingredient parser. A line it cannot
    /// read is kept as pasted and flagged unparsed, with the reason.
    /// </summary>
    [HttpPost(Paste + "/ingredients")]
    [Authorize(Policy = SitePolicies.SiteAuthor)]
    [EnableRateLimiting(ApiRateLimits.RecipePaste)]
    [EndpointDescription(SavesNothing)]
    [ProducesResponseType<PastedIngredientList>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType, Description = Invalid)]
    public async Task<IActionResult> Ingredients(PasteListRequest request, CancellationToken cancellationToken) =>
        OkOr(await paste.ReadIngredientsAsync(request, cancellationToken));

    /// <summary>
    /// Reads a pasted step list: group headings, and every other line as one step, with a leading step number or bullet cut
    /// off. A line with nothing after its number, one too long, or one with a broken character is flagged, never dropped.
    /// </summary>
    [HttpPost(Paste + "/steps")]
    [Authorize(Policy = SitePolicies.SiteAuthor)]
    [EnableRateLimiting(ApiRateLimits.RecipePaste)]
    [EndpointDescription(SavesNothing)]
    [ProducesResponseType<PastedStepList>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType, Description = Invalid)]
    public async Task<IActionResult> Steps(PasteListRequest request, CancellationToken cancellationToken) =>
        OkOr(await paste.ReadStepsAsync(request, cancellationToken));
}
