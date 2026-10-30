using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// One step of a recipe, in the blogger's own words as plain text (RCP-001; PP-D05; PP4.3). Site-owned, under its group by
/// <c>(SiteId, GroupId)</c>. A step's optional image (RCP-001) arrives with the media library, which owns what it points at.
/// </summary>
public sealed class RecipeStep : ISiteOwned
{
    public RecipeStepId Id { get; init; }

    /// <summary>Stamped by the save from the request's site; never set in code (tenancy.md rule 3).</summary>
    public SiteId SiteId { get; private set; }

    public RecipeStepGroupId GroupId { get; init; }

    /// <summary>Where the step comes in its group, from 0.</summary>
    public int Position { get; set; }

    public string Text { get; set; } = string.Empty;
}
