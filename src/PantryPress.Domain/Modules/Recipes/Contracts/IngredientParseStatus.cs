namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// Whether an ingredient line's own words were read into structured parts (RCP-002; PP4.3). Stored as these numbers, so
/// they never change.
/// </summary>
public enum IngredientParseStatus
{
    /// <summary>The line has an item, and any quantity, unit, and note found in its words.</summary>
    Parsed = 0,

    /// <summary>
    /// The line could not be read: it is kept exactly as written, with no parsed parts, and flagged for the blogger. It is
    /// shown as written and never scaled or converted.
    /// </summary>
    Unparsed = 1,
}
