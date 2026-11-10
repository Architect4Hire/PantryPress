using CsCheck;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.5: what holds for every line, over many generated ones - lines put together from the pieces ingredient lines are
// made of, corpus lines with a piece pushed in anywhere, and plain noise. The parser never throws, answers the same each
// time, and every answer keeps IngredientParseInvariants: it fits the table, and every part is cut from the line.
// CsCheck prints the seed that replays a failure.
public sealed class IngredientLineParserPropertyTests
{
    private const long Iterations = 20_000;

    private static readonly string[] Pieces =
    [
        "1", "2", "12", "250", "0", "1000001", "1/2", "3/4", "1\u20443", "½", "¼", "⅓", "1½", "0.5", ".5", "1.25", "1,5", "1.",
        "one", "two", "a", "an", "to", "or", "and", "plus", "x", "×", "+", "taste", "of", "-", "–", "—", "\u2011", "/", "\u2044",
        " ", " ", " ", "  ", "\u00A0", "\u2009", "\t", "(", ")", ",", ";", ":", ".", "%", "°", "º", "F", "C", "~", "*", "•", "▢",
        "cup", "cups", "c", "c.", "T", "t", "tbsp", "tsp.", "fl oz", "fl", "oz", "g", "kg", "l", "ml", "lb", "lbs", "pinch",
        "can", "clove", "°F", "ºC", "degrees", "fahrenheit", "14-ounce", "9-inch", "flour", "salt", "eggs", "large", "sifted",
        "lemon", "T-bone", "crème", "jalapeño", "🍎", "２", "¹", "↉", "\u0300", "\u200B", "\uD83C", "\u0007",
    ];

    private static readonly Gen<string> Built =
        Gen.OneOfConst(Pieces).Array[1, 16].Select(parts => string.Concat(parts));

    private static readonly IReadOnlyList<string> CorpusLines = [.. IngredientCorpus.Load().Select(c => c.Input)];

    private static readonly Gen<string> Mutated =
        Gen.Select(Gen.OneOfConst([.. CorpusLines]), Gen.OneOfConst(Pieces), Gen.Int[0, 1_000], (line, piece, at) => line.Insert(at % (line.Length + 1), piece));

    // Any UTF-16 code units at all, lone surrogates and controls included, weighted toward the ones a line is made of.
    private static readonly Gen<string> Noise = Gen.Frequency(
            (3, Gen.Char['0', '9']),
            (3, Gen.Char['a', 'z']),
            (2, Gen.OneOfConst(' ', '/', '-', '.', ',', '(', ')', '½', '–', '\u2044', '\u00A0')),
            (2, Gen.Char))
        .Array[0, 40]
        .Select(chars => new string(chars));

    private static readonly Gen<string> Lines = Gen.Frequency((3, Built), (3, Mutated), (1, Noise));

    [Fact]
    public void Every_line_keeps_the_invariants() =>
        Lines.Sample(iter: Iterations, assert: line =>
        {
            var parse = IngredientLineParser.Parse(line);
            IngredientParseInvariants.Check(line, parse);
            Assert.Equal(parse, IngredientLineParser.Parse(line));
        });

    // Spaces before a line move every span by their length and change nothing else, so where a part is found never
    // depends on where the line starts.
    [Fact]
    public void Leading_spaces_only_move_the_spans() =>
        Gen.Select(Lines.Where(l => l.Length < 400), Gen.OneOfConst(" ", "  ", "\u00A0", "\t ")).Sample(iter: Iterations, assert: (line, space) =>
        {
            var plain = IngredientLineParser.Parse(line);
            var moved = IngredientLineParser.Parse(space + line);
            if (plain.Problem == IngredientParseProblem.Empty)
            {
                Assert.Equal(IngredientParseProblem.Empty, moved.Problem);
                return;
            }

            Assert.Equal((plain.Status, plain.Problem, plain.Quantity, plain.UnitCode, plain.Item, plain.Note),
                (moved.Status, moved.Problem, moved.Quantity, moved.UnitCode, moved.Item, moved.Note));
            Assert.Equal(Move(plain.QuantitySpan, space.Length), moved.QuantitySpan);
            Assert.Equal(Move(plain.UnitSpan, space.Length), moved.UnitSpan);
        });

    // A quantity's own words, read again, are the same quantity: the span holds the whole quantity and nothing else.
    [Fact]
    public void A_quantity_span_reads_back_as_its_quantity() =>
        Lines.Sample(iter: Iterations, assert: line =>
        {
            var parse = IngredientLineParser.Parse(line);
            if (parse.Quantity is { HasAmount: true } quantity)
            {
                var span = parse.QuantitySpan!.Value;
                Assert.Equal(quantity, IngredientLineParser.Parse(line.Substring(span.Start, span.Length) + " cups flour").Quantity);
            }
        });

    private static TextSpan? Move(TextSpan? span, int by) => span is { } s ? new TextSpan(s.Start + by, s.Length) : null;
}
