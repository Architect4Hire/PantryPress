using PantryPress.Domain.Modules.Recipes;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

/// <summary>
/// What every parse owes, whatever the line (RCP-002; PP4.5): it fits the ingredient table's checks (PP4.3), and nothing
/// in it is made up - the item and every piece of the note are cut from the line, each span names what it says it does,
/// and the quantity's own words read back as the same quantity.
/// </summary>
internal static class IngredientParseInvariants
{
    public static void Check(string text, IngredientLineParse parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        if (parse.Status == IngredientParseStatus.Unparsed)
        {
            Assert.NotNull(parse.Problem);
            Assert.Null(parse.Quantity);
            Assert.Null(parse.QuantitySpan);
            Assert.Null(parse.Style);
            Assert.Null(parse.UnitCode);
            Assert.Null(parse.UnitSpan);
            Assert.Null(parse.Item);
            Assert.Null(parse.Note);
            return;
        }

        Assert.Equal(IngredientParseStatus.Parsed, parse.Status);
        Assert.Null(parse.Problem);
        AssertCut(text, parse.Item, RecipeLimits.MaxIngredientItemLength, whole: true);
        if (parse.Note is { } note)
        {
            AssertCut(text, note, RecipeLimits.MaxIngredientNoteLength, whole: false);
            foreach (var piece in note.Split(IngredientLineParse.NoteSeparator))
            {
                Assert.Contains(piece, text, StringComparison.Ordinal);
            }
        }

        switch (parse.Quantity)
        {
            case null:
                Assert.Null(parse.QuantitySpan);
                Assert.Null(parse.Style);
                Assert.Null(parse.UnitCode);
                break;
            case { Kind: QuantityKind.ToTaste }:
                var words = Within(text, parse.QuantitySpan);
                // The parser separates words by whitespace and by invisible format characters alike (a zero-width space), so
                // the words are read back the same way.
                var separated = new string([.. words.Select(c => IngredientLineTokenizer.IsFormat(c) ? ' ' : c)]);
                Assert.Equal("to taste", string.Join(' ', separated.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), StringComparer.OrdinalIgnoreCase);
                Assert.Null(parse.Style);
                Assert.Null(parse.UnitCode);
                break;
            case { } quantity:
                var written = Within(text, parse.QuantitySpan);
                var reread = IngredientLineParser.Parse(written + " cup flour");
                Assert.Equal(quantity, reread.Quantity);
                Assert.Equal(written.Contains('.', StringComparison.Ordinal) ? QuantityStyle.Decimal : QuantityStyle.Fraction, parse.Style);
                _ = StoredQuantity.From(quantity);
                break;
        }

        Assert.Equal(parse.UnitCode is null, parse.UnitSpan is null);
        if (parse.UnitSpan is { } unit)
        {
            Within(text, unit);
            Assert.Equal(new UnitMatch(parse.UnitCode!, unit.Length), UnitSpellings.Match(text, unit.Start));
            Assert.True(parse.QuantitySpan!.Value.End <= unit.Start, "The unit comes after the quantity, apart from it.");
        }

        // The row the parse would be stored as, through the entity's own quantity mapping.
        _ = new RecipeIngredient { OriginalText = text, Quantity = parse.Quantity };
    }

    // An item is one cut from the line; a note is pieces joined, so each piece is checked as a cut instead.
    private static void AssertCut(string text, string? part, int maxLength, bool whole)
    {
        Assert.False(string.IsNullOrEmpty(part));
        Assert.True(part.Length <= maxLength, $"\"{part}\" is longer than {maxLength}.");
        Assert.False(Blank(part[0]) || Blank(part[^1]), $"\"{part}\" has whitespace at an edge.");
        if (whole)
        {
            Assert.Contains(part, text, StringComparison.Ordinal);
        }
    }

    private static bool Blank(char c) => char.IsWhiteSpace(c) || IngredientLineTokenizer.IsFormat(c);

    private static string Within(string text, TextSpan? span)
    {
        Assert.NotNull(span);
        var (start, length) = (span.Value.Start, span.Value.Length);
        Assert.True(start >= 0 && length > 0 && start + length <= text.Length, $"Span {start}+{length} is outside \"{text}\".");
        return text.Substring(start, length);
    }
}
