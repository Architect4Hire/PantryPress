using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// A diet the blogger labeled a recipe with (RCP-001, RCP-006; PP4.3): only ever what the blogger set, never inferred.
/// Site-owned and keyed by <c>(SiteId, RecipeId, DietCode)</c>, so a recipe names each one once; the code is held under
/// a foreign key to the platform's vocabulary (PP4.1), with no navigation to the vocabulary's row, which the reference-
/// data guard would refuse once tracked.
/// </summary>
public sealed class RecipeDiet : ISiteOwned
{
    /// <summary>Stamped by the save from the request's site; never set in code (tenancy.md rule 3).</summary>
    public SiteId SiteId { get; private set; }

    public RecipeId RecipeId { get; init; }

    public string DietCode { get; init; } = string.Empty;

    /// <summary>Where the blogger put it among the recipe's others, from 0, so it is shown and described in that order.</summary>
    public int Position { get; set; }
}
