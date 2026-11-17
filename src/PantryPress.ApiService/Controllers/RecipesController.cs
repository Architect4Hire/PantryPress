using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.ApiService.Controllers;

/// <summary>
/// A site's recipes (RCP-001, RCP-007; PP4.11): the working copy each member edits. Every route names its site, and the
/// Author policy decides and enters it (tenancy.md rule 4); a recipe is named by its id within that site alone, so another
/// site's recipe is no recipe here. Who may change a recipe is the facade's to decide: an Owner or Editor any, an Author
/// their own.
/// </summary>
[Route(ApiSurfaces.AdminV1Route)]
[Authorize]
public sealed class RecipesController(IRecipesFacade recipes) : ApiControllerBase
{
    private const string List = "sites/{" + SiteAuthorization.SlugRouteValue + "}/recipes";

    // The id is a GUID, so no word of this surface's own beside it (recipes/paste) is ever read as one.
    private const string One = List + "/{recipeId:guid}";

    private const string RecipeIdDescription = "The recipe's id, as id in the recipe or the list gives it.";

    private const string RecipeMissing =
        "recipe-not-found: no recipe on this site has that id - unknown, or another site's - one answer.";

    private const string NotYours =
        "recipe-not-yours: the caller is an author, and someone else made this recipe; only an editor or owner may change it.";

    private const string KeyInvalid = "idempotency-key-invalid: the Idempotency-Key is not 1 to 128 visible characters.";

    private const string KeyInUse = "idempotency-key-in-use: the first request with this key is still running.";

    private const string Invalid = "validation-failed: " + Fields + " " + KeyInvalid;

    private const string Fields =
        "errors names each field at its JSON path (ingredientGroups[0].lines[2].originalText, or under content. when saving): "
        + "a missing title; text that is too long, only white space, on more than one line where one is meant, or that holds "
        + "a control character or half of a surrogate pair; a yield that is not an amount or a rising range of fractions "
        + "with terms from 1 to 1,000,000; a time outside 0 to 43,200; a video not on YouTube or Vimeo; a nutrition amount "
        + "outside 0 to 9,999,999.99 or with a third decimal place; more than 20 groups, 200 ingredient lines, 100 steps, "
        + "30 pieces of equipment, 20 notes, 20 keywords, or 5 courses or cuisines; a vocabulary code not in the list or "
        + "chosen twice; or a part id that is not one of this recipe's parts, or is sent twice - on create, any part id.";

    /// <summary>
    /// Creates a recipe from the whole of it, made by the caller. Each ingredient line is read again on the server: send its
    /// words alone. Send no part ids: every part is new.
    /// </summary>
    [HttpPost(List)]
    [Authorize(Policy = SitePolicies.SiteAuthor)]
    [Idempotent]
    [EnableRateLimiting(ApiRateLimits.RecipeCreate)]
    [EndpointDescription(
        "If this answer is lost, a retry with the same Idempotency-Key gets it again; a retry without one creates a second "
        + "recipe. List the site's recipes to find the first.")]
    [ProducesResponseType<RecipeDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType, Description = Invalid)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType, Description = KeyInUse)]
    public Task<IActionResult> Create(string siteSlug, RecipeContent content, CancellationToken cancellationToken) =>
        ForAccount(
            account => recipes.CreateAsync(account, siteSlug, content, cancellationToken),
            result => CreatedOr(result, recipe => $"/{ApiSurfaces.AdminV1Route}/sites/{siteSlug}/recipes/{recipe.Id:D}"));

    /// <summary>
    /// The site's recipes, most recently edited first, a page at a time: those in the library, or the archived ones, narrowed
    /// by words in the title and by a course, a cuisine, and a diet, all at once.
    /// </summary>
    [HttpGet(List)]
    [Authorize(Policy = SitePolicies.SiteAuthor)]
    [EnableRateLimiting(ApiRateLimits.RecipeRead)]
    [EndpointDescription(
        "A recipe edited while the list is being paged moves to the top of it, so it may be missed or shown twice; "
        + "nothing else moves. A cursor continues only the list it came from: send the same search, filters, and archived "
        + "with it.")]
    [ProducesResponseType<RecipeSummaryPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest,
        ApiProblems.ContentType,
        Description = "validation-failed: limit is not a whole number from 1 to 100; cursor is not one this list gave under "
            + "the same search, filters, and archived; archived is not true or false; search is longer than 100 characters "
            + "once trimmed, or holds a control character or half of a surrogate pair; or course, cuisine, or diet is not a "
            + "code in the vocabulary. errors names which.")]
    public Task<IActionResult> ListRecipes(
        string siteSlug,
        [FromQuery, Description("How many recipes at most, 1 to 100. Missing means 50.")] int? limit,
        [FromQuery, Description("The nextCursor of the page before, as given, with the same search and filters; missing for the first page.")] string? cursor,
        [FromQuery, Description("True for the archived recipes; missing or false for the library.")] bool? archived,
        [FromQuery, Description("Text the title contains, as typed, ignoring case and accents; up to 100 characters. Missing or blank for any title.")] string? search,
        [FromQuery, Description("Only recipes filed under this course code, from the vocabulary; missing for any.")] string? course,
        [FromQuery, Description("Only recipes filed under this cuisine code, from the vocabulary; missing for any.")] string? cuisine,
        [FromQuery, Description("Only recipes labeled with this diet code, from the vocabulary; missing for any.")] string? diet,
        CancellationToken cancellationToken) =>
        ForAccount(
            account => recipes.ListAsync(
                account,
                siteSlug,
                new ListRecipesQuery(limit, cursor, archived, search, course, cuisine, diet),
                cancellationToken),
            OkOr);

    /// <summary>
    /// The words a recipe is filed under: every course, cuisine, diet, and allergen, by code and name, in order. The same for
    /// every site; nothing of the site's is in it.
    /// </summary>
    [HttpGet(List + "/vocabulary")]
    [Authorize(Policy = SitePolicies.SiteAuthor)]
    [EnableRateLimiting(ApiRateLimits.RecipeRead)]
    [ProducesResponseType<RecipeVocabulary>(StatusCodes.Status200OK)]
    public IActionResult Vocabulary() => Ok(recipes.GetVocabulary());

    /// <summary>The recipe's working copy, whole, archived or not, with the version to send back with the next save.</summary>
    [HttpGet(One)]
    [Authorize(Policy = SitePolicies.SiteAuthor)]
    [EnableRateLimiting(ApiRateLimits.RecipeRead)]
    [ProducesResponseType<RecipeDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType, Description = RecipeMissing)]
    public Task<IActionResult> Get(
        string siteSlug,
        [Description(RecipeIdDescription)] Guid recipeId,
        CancellationToken cancellationToken) =>
        ForAccount(account => recipes.GetAsync(account, siteSlug, new RecipeId(recipeId), cancellationToken), OkOr);

    /// <summary>
    /// Replaces the recipe's working copy with the whole recipe sent, if it is still the version the caller read. A part
    /// sent with its id keeps it, a line or step moving to another group included; a part sent without one is new; a part
    /// left out is removed. What readers see changes only when the recipe is published.
    /// </summary>
    [HttpPut(One)]
    [Authorize(Policy = SitePolicies.SiteAuthor)]
    [Idempotent]
    [EnableRateLimiting(ApiRateLimits.RecipeSave)]
    [EndpointDescription(
        "Send an Idempotency-Key with every save: a retry with the same key gets the first answer back. Without one, sending "
        + "exactly what the recipe already holds saves nothing and answers 200 with its current version, whatever version "
        + "was sent - but a save that added a part sends that part again with no id, so its retry is a different save and "
        + "is answered 409 recipe-changed once the first has landed. Send the version from the answer with the next save.")]
    [ProducesResponseType<RecipeDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest,
        ApiProblems.ContentType,
        Description = "validation-failed: version is missing or not one a recipe gave, or content is missing; otherwise "
            + Fields + " " + KeyInvalid)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ApiProblems.ContentType, Description = NotYours)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType, Description = RecipeMissing)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status409Conflict,
        ApiProblems.ContentType,
        Description = "recipe-changed: the recipe was changed since this version was read - by someone else, or by this save's "
            + "own first attempt when it added a part and was retried without its key; nothing was saved - read it again and "
            + "try again with a new Idempotency-Key, since this answer is replayed for the old one. "
            + "recipe-archived: the recipe is archived, whatever the save holds; restore it first. " + KeyInUse)]
    public Task<IActionResult> Update(
        string siteSlug,
        [Description(RecipeIdDescription)] Guid recipeId,
        UpdateRecipeRequest request,
        CancellationToken cancellationToken) =>
        ForAccount(account => recipes.UpdateAsync(account, siteSlug, new RecipeId(recipeId), request, cancellationToken), OkOr);

    /// <summary>Takes the recipe out of the library. What readers see is unchanged. Archiving it again changes nothing.</summary>
    [HttpPost(One + "/archive")]
    [Authorize(Policy = SitePolicies.SiteAuthor)]
    [Idempotent]
    [EnableRateLimiting(ApiRateLimits.RecipeSave)]
    [EndpointDescription("Safe to retry: archiving an archived recipe changes nothing and answers 204 again.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType, Description = KeyInvalid)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ApiProblems.ContentType, Description = NotYours)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType, Description = RecipeMissing)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType, Description = KeyInUse)]
    public Task<IActionResult> Archive(
        string siteSlug,
        [Description(RecipeIdDescription)] Guid recipeId,
        CancellationToken cancellationToken) =>
        ForAccount(account => recipes.ArchiveAsync(account, siteSlug, new RecipeId(recipeId), cancellationToken), NoContentOr);

    /// <summary>Puts an archived recipe back in the library. Restoring one already there changes nothing.</summary>
    [HttpPost(One + "/restore")]
    [Authorize(Policy = SitePolicies.SiteAuthor)]
    [Idempotent]
    [EnableRateLimiting(ApiRateLimits.RecipeSave)]
    [EndpointDescription("Safe to retry: restoring a recipe in the library changes nothing and answers 204 again.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType, Description = KeyInvalid)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ApiProblems.ContentType, Description = NotYours)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType, Description = RecipeMissing)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType, Description = KeyInUse)]
    public Task<IActionResult> Restore(
        string siteSlug,
        [Description(RecipeIdDescription)] Guid recipeId,
        CancellationToken cancellationToken) =>
        ForAccount(account => recipes.RestoreAsync(account, siteSlug, new RecipeId(recipeId), cancellationToken), NoContentOr);

    /// <summary>
    /// A new recipe with this one's working copy, made by the caller and in the library, titled "Copy of …" where that fits.
    /// Its parts get new ids; nothing published is copied.
    /// </summary>
    [HttpPost(One + "/duplicate")]
    [Authorize(Policy = SitePolicies.SiteAuthor)]
    [Idempotent]
    [EnableRateLimiting(ApiRateLimits.RecipeCreate)]
    [EndpointDescription(
        "If this answer is lost, a retry with the same Idempotency-Key gets it again; a retry without one makes a second "
        + "copy. List the site's recipes to find the first.")]
    [ProducesResponseType<RecipeDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ApiProblems.ContentType, Description = KeyInvalid)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ApiProblems.ContentType, Description = RecipeMissing)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ApiProblems.ContentType, Description = KeyInUse)]
    public Task<IActionResult> Duplicate(
        string siteSlug,
        [Description(RecipeIdDescription)] Guid recipeId,
        CancellationToken cancellationToken) =>
        ForAccount(
            account => recipes.DuplicateAsync(account, siteSlug, new RecipeId(recipeId), cancellationToken),
            result => CreatedOr(result, recipe => $"/{ApiSurfaces.AdminV1Route}/sites/{siteSlug}/recipes/{recipe.Id:D}"));

    private async Task<IActionResult> ForAccount<TResult>(
        Func<AccountId, Task<TResult>> call,
        Func<TResult, IActionResult> map) =>
        User.TryGetAccountId(out var account) ? map(await call(account)) : Challenge();
}
