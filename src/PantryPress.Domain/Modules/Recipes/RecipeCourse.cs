using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// A course the blogger chose for a recipe (RCP-001; PP4.3). Site-owned and keyed by <c>(SiteId, RecipeId,
/// CourseCode)</c>, so a recipe names each one once; the code is held under a foreign key to the platform's vocabulary
/// (PP4.1), with no navigation to the vocabulary's row, which the reference-data guard would refuse once tracked.
/// </summary>
public sealed class RecipeCourse : ISiteOwned
{
    /// <summary>Stamped by the save from the request's site; never set in code (tenancy.md rule 3).</summary>
    public SiteId SiteId { get; private set; }

    public RecipeId RecipeId { get; init; }

    public string CourseCode { get; init; } = string.Empty;

    /// <summary>Where the blogger put it among the recipe's others, from 0, so it is shown and described in that order.</summary>
    public int Position { get; set; }
}
