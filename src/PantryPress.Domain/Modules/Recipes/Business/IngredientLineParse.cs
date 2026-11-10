using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>Where a part is in an ingredient line, in UTF-16 code units, as .NET indexes a string.</summary>
public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// What the ingredient parser read from one line (RCP-002; PP4.5): exactly the parts an ingredient line stores (PP4.3), or
/// the reason it could not read the line. The line itself is never part of it and never changed: every part is either a
/// span of the line or text cut from it verbatim. A parsed line always has an item; a quantity's amount or range always
/// has its span, and a unit only comes with one; spans lie inside the line, apart. A line that could not be read has no
/// parts at all.
/// </summary>
public sealed record IngredientLineParse
{
    /// <summary>Joins the pieces of a note, each as written, in the order they appear in the line.</summary>
    public const string NoteSeparator = "; ";

    private IngredientLineParse()
    {
    }

    public IngredientParseStatus Status { get; private init; }

    /// <summary>Why the line could not be read; none for a parsed line.</summary>
    public IngredientParseProblem? Problem { get; private init; }

    /// <summary>The amount, the range, or "to taste"; none for a line with no quantity ("salt").</summary>
    public Quantity? Quantity { get; private init; }

    /// <summary>
    /// Where the quantity is written: an amount or range always has one (<c>2</c>, <c>2-3</c>, <c>1 to 1½</c>, or the
    /// <c>a</c> of <c>a pinch</c>), and "to taste" covers those two words.
    /// </summary>
    public TextSpan? QuantitySpan { get; private init; }

    /// <summary>
    /// How the blogger wrote the amount, so a scaled amount can be written the same way (PP4.7):
    /// <see cref="QuantityStyle.Decimal"/> when written with a decimal point (<c>1.5</c>, <c>.5</c>),
    /// <see cref="QuantityStyle.Fraction"/> otherwise (<c>2</c>, <c>1 1/2</c>, <c>1½</c>, <c>two</c>, <c>a</c>); none for
    /// "to taste" and a line with no quantity. <see cref="QuantityStyle"/> has no member for ASCII against character
    /// fractions (<c>1 1/2</c> against <c>1½</c>); that difference stays in the line, at <see cref="QuantitySpan"/>. It is not
    /// stored: reading the stored line again gives it again.
    /// </summary>
    public QuantityStyle? Style { get; private init; }

    /// <summary>The unit's vocabulary code, only ever beside an amount or range.</summary>
    public string? UnitCode { get; private init; }

    public TextSpan? UnitSpan { get; private init; }

    /// <summary>What the line names, cut from it as written ("all-purpose flour").</summary>
    public string? Item { get; private init; }

    /// <summary>
    /// The rest of the line's words - parenthesized text and what follows the item's comma - each piece as written, joined by
    /// <see cref="NoteSeparator"/>; none when there is nothing else.
    /// </summary>
    public string? Note { get; private init; }

    internal static IngredientLineParse Unparsed(IngredientParseProblem problem) =>
        new() { Status = IngredientParseStatus.Unparsed, Problem = problem };

    internal static IngredientLineParse Parsed(
        Quantity? quantity,
        TextSpan? quantitySpan,
        QuantityStyle? style,
        string? unitCode,
        TextSpan? unitSpan,
        string item,
        string? note) =>
        new()
        {
            Status = IngredientParseStatus.Parsed,
            Quantity = quantity,
            QuantitySpan = quantitySpan,
            Style = style,
            UnitCode = unitCode,
            UnitSpan = unitSpan,
            Item = item,
            Note = note,
        };
}
