using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes;

/// <summary>
/// One of a recipe's notes - a tip, a substitution, how to store it - as plain text in the blogger's words, in order
/// (RCP-001; PP4.3). Site-owned and keyed by <c>(SiteId, RecipeId, Position)</c>: the list is written whole.
/// </summary>
public sealed class RecipeNote : ISiteOwned
{
    /// <summary>Stamped by the save from the request's site; never set in code (tenancy.md rule 3).</summary>
    public SiteId SiteId { get; private set; }

    public RecipeId RecipeId { get; init; }

    /// <summary>Where the note comes in the list, from 0.</summary>
    public int Position { get; init; }

    public string Text { get; set; } = string.Empty;
}
