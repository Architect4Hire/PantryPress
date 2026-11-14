using PantryPress.Domain.Managers.Results;

namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>The Recipes module's expected failures. Codes are stable (api-contract.md); titles are what a blogger reads.</summary>
public static class RecipeErrors
{
    /// <summary>
    /// No recipe on this site has that id: unknown, or another site's - one answer, so an id says nothing about any other
    /// site (tenancy.md).
    /// </summary>
    public static readonly Error RecipeNotFound = new(
        ErrorKind.NotFound,
        "recipe-not-found",
        "We couldn't find that recipe.");

    /// <summary>
    /// The recipe was changed since the caller read it (PP4.4): nothing was saved, so no edit is lost to the other one
    /// (Template D: no silent last-write-wins).
    /// </summary>
    public static readonly Error RecipeChanged = new(
        ErrorKind.Conflict,
        "recipe-changed",
        "This recipe was changed just now, maybe in another tab. Reload it and try again.");

    /// <summary>
    /// An Author asked to change a recipe someone else made (SITE-010; PP4.11). Answered only once the recipe is found on
    /// the caller's own site, so it says nothing another member could not already read.
    /// </summary>
    public static readonly Error RecipeNotYours = new(
        ErrorKind.Forbidden,
        "recipe-not-yours",
        "Only an editor or owner can change a recipe someone else made.");

    /// <summary>The recipe is archived: restore it before changing it (PP4.11). Nothing was saved.</summary>
    public static readonly Error RecipeArchived = new(
        ErrorKind.Conflict,
        "recipe-archived",
        "This recipe is archived. Restore it to make changes.");

    /// <summary>
    /// The message on a part id that is not one of this recipe's parts - unknown, another recipe's, or another site's, one
    /// answer (tenancy.md) - given as a field error at the part's id.
    /// </summary>
    public const string PartNotFoundMessage = "This part isn't in the recipe any more. Reload the recipe and try again.";
}
