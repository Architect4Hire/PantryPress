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
}
