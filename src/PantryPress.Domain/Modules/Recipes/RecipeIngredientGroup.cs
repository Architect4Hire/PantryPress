using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// One group of a recipe's ingredients, in order, with an optional heading ("For the crust") (RCP-001; PP4.3). A recipe
/// with one plain list has one group with no heading. Site-owned, under its recipe by <c>(SiteId, RecipeId)</c>.
/// </summary>
public sealed class RecipeIngredientGroup : ISiteOwned
{
    public RecipeIngredientGroupId Id { get; init; }

    /// <summary>Stamped by the save from the request's site; never set in code (tenancy.md rule 3).</summary>
    public SiteId SiteId { get; private set; }

    public RecipeId RecipeId { get; init; }

    /// <summary>Where the group comes in the recipe, from 0.</summary>
    public int Position { get; set; }

    public string? Heading { get; set; }
}
