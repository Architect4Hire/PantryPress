namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// What a recipe revision is (RCP-007; PP4.4). Both kinds are frozen copies of the recipe as it was edited, and neither is
/// ever changed or removed once written; the numbers are stored and never change.
/// </summary>
public enum RecipeRevisionKind
{
    /// <summary>A save point of edits nobody has published: kept, never shown to a reader.</summary>
    Draft = 1,

    /// <summary>What readers see: the one kind page data, JSON-LD, and a post's recipe card read (ARCH-004, PP-D04).</summary>
    Published = 2,
}
