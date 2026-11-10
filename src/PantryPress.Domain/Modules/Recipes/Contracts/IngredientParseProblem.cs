namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// Why the ingredient parser could not read a line (RCP-002; PP4.5), so the editor and paste-to-parse (PP4.6) can say what
/// to change. A line it could not read is kept exactly as written and flagged <see cref="IngredientParseStatus.Unparsed"/>.
/// The reason is never stored, so these numbers are not a stored format; but PP4.6 returns it, and enums are written as
/// kebab-case names (api-contract.md), so from then on renaming a member is a breaking change.
/// </summary>
public enum IngredientParseProblem
{
    /// <summary>The line is empty or only spaces.</summary>
    Empty,

    /// <summary>The line is longer than <see cref="RecipeLimits.MaxIngredientTextLength"/>.</summary>
    TooLong,

    /// <summary>The line holds a broken character (half of a surrogate pair) or a control character.</summary>
    IllFormedText,

    /// <summary>A parenthesis is opened and never closed, or closed and never opened.</summary>
    UnbalancedParentheses,

    /// <summary>
    /// The amount cannot be read: a zero, a fraction with a zero denominator (<c>1/0</c>) or a second slash, a mixed number
    /// whose fraction is not below one, a number followed by a period (a list's numbering), or digits other than 0 to 9.
    /// </summary>
    UnreadableQuantity,

    /// <summary>A range whose first amount is not below its second (<c>3-2</c>, <c>2-2</c>).</summary>
    ReversedRange,

    /// <summary>
    /// The amount's fraction has a numerator or denominator above <see cref="RecipeLimits.MaxQuantityTerm"/>, or more
    /// decimal places than that allows.
    /// </summary>
    QuantityOutOfRange,

    /// <summary>
    /// The amount could mean more than one thing, so the parser does not guess: two measures joined (<c>1 cup + 2 tbsp</c>,
    /// <c>1 cup to 1½ cups</c>, <c>2 x 400g</c>), a second number straight after the first, a fraction split by spaces
    /// (<c>1 / 2</c>), a comma inside a number (<c>1,5</c>), or a number joined to a word that is not a unit.
    /// </summary>
    AmbiguousQuantity,

    /// <summary>Nothing is left to name the ingredient once the amount and unit are read (<c>2 cups</c>).</summary>
    NoItem,

    /// <summary>The ingredient's name is longer than <see cref="RecipeLimits.MaxIngredientItemLength"/>.</summary>
    ItemTooLong,

    /// <summary>The line's note is longer than <see cref="RecipeLimits.MaxIngredientNoteLength"/>.</summary>
    NoteTooLong,
}
