using PantryPress.Domain.Managers.Results;

namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>
/// Paste-to-parse (RCP-003; PP4.6): reads a pasted ingredient list or step list into proposed groups and lines for the
/// blogger to review before anything is saved. It reads and writes nothing - no recipe, no site data, no audit, no outbox -
/// so the same paste gets the same answer on every site. Every proposed line carries the pasted line exactly as it was;
/// so does every heading's line; whatever is proposed beside it is advice. The routes that save a recipe (PP4.11) read each line again on the server and
/// never take a client's parts on trust.
/// </summary>
public interface IRecipePasteFacade
{
    /// <summary>
    /// Reads a pasted ingredient list: group headings, and each other non-blank line through the ingredient parser (PP4.5),
    /// flagged with a reason when it cannot be read. Otherwise a validation error: nothing pasted, or more than
    /// <see cref="RecipeLimits.MaxPastedTextLength"/> characters or <see cref="RecipeLimits.MaxPastedLines"/> lines.
    /// </summary>
    Task<Result<PastedIngredientList>> ReadIngredientsAsync(PasteListRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a pasted step list: group headings, and each other non-blank line as one step, with a leading step number or
    /// bullet cut off. Validated as <see cref="ReadIngredientsAsync"/> is.
    /// </summary>
    Task<Result<PastedStepList>> ReadStepsAsync(PasteListRequest request, CancellationToken cancellationToken);
}

/// <summary>A pasted list to read.</summary>
/// <param name="Text">
/// Required. The list as pasted, lines separated by any line break: up to 30,000 characters and 200 non-blank lines,
/// headings included. Blank lines are skipped and never start a group.
/// </param>
public sealed record PasteListRequest(string? Text);

/// <summary>Where a part is in a pasted line, in UTF-16 code units, as both .NET and JavaScript index a string.</summary>
/// <param name="Start">The first code unit.</param>
/// <param name="Length">How many code units.</param>
public sealed record PastedSpan(int Start, int Length);

/// <summary>An exact amount, never floating point (PP-D05): a fraction in lowest terms with a positive denominator.</summary>
/// <param name="Numerator">The numerator, above zero.</param>
/// <param name="Denominator">The denominator, above zero.</param>
public sealed record PastedFraction(long Numerator, long Denominator);

/// <summary>An ingredient line's quantity as the parser read it.</summary>
/// <param name="Kind">An amount, a range, or to taste.</param>
/// <param name="Low">The amount, or a range's low end; none for to taste.</param>
/// <param name="High">A range's high end; none otherwise.</param>
public sealed record PastedQuantity(QuantityKind Kind, PastedFraction? Low, PastedFraction? High);

/// <summary>The ingredient groups read from a paste, in the order pasted.</summary>
/// <param name="Groups">
/// The groups. Lines before the first heading form a group with no heading; a paste with no heading is one such group.
/// </param>
public sealed record PastedIngredientList(IReadOnlyList<PastedIngredientGroup> Groups);

/// <summary>One group of a pasted ingredient list.</summary>
/// <param name="Heading">The group's heading cut from its line ("For the crust"); none for lines before any heading.</param>
/// <param name="HeadingOriginalText">
/// The heading's line exactly as pasted ("For the crust:"), so a line misread as a heading can be turned back into a line
/// in the blogger's own words; none when <paramref name="Heading"/> is none.
/// </param>
/// <param name="HeadingSpan">Where <paramref name="Heading"/> is in <paramref name="HeadingOriginalText"/>.</param>
/// <param name="Lines">The group's lines in the order pasted; empty when two headings follow each other.</param>
public sealed record PastedIngredientGroup(
    string? Heading,
    string? HeadingOriginalText,
    PastedSpan? HeadingSpan,
    IReadOnlyList<PastedIngredientLine> Lines);

/// <summary>One proposed ingredient line: the pasted line, and what the parser read from it (RCP-002).</summary>
/// <param name="OriginalText">The line exactly as pasted, which is what a saved line keeps.</param>
/// <param name="Status">Parsed, or unparsed: kept as written and flagged.</param>
/// <param name="Problem">Why an unparsed line could not be read; none for a parsed one.</param>
/// <param name="Quantity">The amount, range, or to taste; none when the line has none or is unparsed.</param>
/// <param name="QuantitySpan">Where the quantity is written in <paramref name="OriginalText"/>.</param>
/// <param name="Style">Whether the amount is written as a decimal or a fraction; none for to taste or no amount.</param>
/// <param name="UnitCode">The unit's vocabulary code, only beside an amount or range.</param>
/// <param name="UnitSpan">Where the unit is written in <paramref name="OriginalText"/>.</param>
/// <param name="Item">What the line names, cut from it as written.</param>
/// <param name="Note">The line's other pieces, each as written, joined by "; ".</param>
public sealed record PastedIngredientLine(
    string OriginalText,
    IngredientParseStatus Status,
    IngredientParseProblem? Problem,
    PastedQuantity? Quantity,
    PastedSpan? QuantitySpan,
    QuantityStyle? Style,
    string? UnitCode,
    PastedSpan? UnitSpan,
    string? Item,
    string? Note);

/// <summary>The step groups read from a paste, in the order pasted.</summary>
/// <param name="Groups">The groups, as <see cref="PastedIngredientList.Groups"/> are made.</param>
public sealed record PastedStepList(IReadOnlyList<PastedStepGroup> Groups);

/// <summary>One group of a pasted step list.</summary>
/// <param name="Heading">The group's heading cut from its line; none for steps before any heading.</param>
/// <param name="HeadingOriginalText">
/// The heading's line exactly as pasted, so a step misread as a heading ("Whisk the sugar and milk:") can be turned back
/// into a step in the blogger's own words; none when <paramref name="Heading"/> is none.
/// </param>
/// <param name="HeadingSpan">Where <paramref name="Heading"/> is in <paramref name="HeadingOriginalText"/>.</param>
/// <param name="Steps">The group's steps in the order pasted; empty when two headings follow each other.</param>
public sealed record PastedStepGroup(
    string? Heading,
    string? HeadingOriginalText,
    PastedSpan? HeadingSpan,
    IReadOnlyList<PastedStep> Steps);

/// <summary>One proposed step.</summary>
/// <param name="OriginalText">The line exactly as pasted.</param>
/// <param name="Text">
/// The proposed step: the line with a leading step number or bullet and the spaces at its edges cut off, every other
/// character as pasted. Empty when the line is only a number or bullet.
/// </param>
/// <param name="TextSpan">Where <paramref name="Text"/> is in <paramref name="OriginalText"/>.</param>
/// <param name="Problem">Why the step cannot be kept as it is; none when it can.</param>
public sealed record PastedStep(string OriginalText, string Text, PastedSpan TextSpan, StepPasteProblem? Problem);
