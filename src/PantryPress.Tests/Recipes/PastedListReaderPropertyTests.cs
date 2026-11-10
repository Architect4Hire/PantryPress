using CsCheck;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.6: what holds for every paste, over many generated ones - lists put together from corpus lines, headings, step
// markers, blank lines, and every kind of line break, and plain noise. The reader never throws and answers the same each
// time; walking its groups gives back the paste's non-blank lines in order, each exactly as pasted (nothing lost, nothing
// rewritten); a heading and a step's text are cut from their own line. CsCheck prints the seed that replays a failure.
public sealed class PastedListReaderPropertyTests
{
    private const long Iterations = 5_000;

    private static readonly string[] Breaks = ["\n", "\r\n", "\r", ((char)0x85).ToString(), ((char)0x2028).ToString(), ((char)0x2029).ToString()];

    private static readonly string[] Pieces =
    [
        "For the crust:", "For serving", "## Filling", "# Glaze:", "Crust :", "• For the topping:", "1. Preheat.", "2) Stir",
        "Step 3: Bake", "STEP 4 - Cool", "Step 5", "3.", "• Whisk", "- Pour", "* Chill", "1.5 hours later", "Make the glaze:",
        "For best results, rest it.", "", " ", "\t", ((char)0x200B).ToString(), ((char)0xA0).ToString(), "Instructions",
        "#", "####### x", ":", "For", "1:30", ((char)0xD83C).ToString(), ((char)0x07).ToString(), "🍎 apples",
    ];

    private static readonly IReadOnlyList<string> CorpusLines = [.. IngredientCorpus.Load().Select(c => c.Input)];

    private static readonly Gen<string> Line = Gen.Frequency(
        (3, Gen.OneOfConst([.. CorpusLines])),
        (3, Gen.OneOfConst(Pieces)),
        (1, Gen.OneOfConst(Pieces).Array[2, 4].Select(parts => string.Concat(parts))));

    private static readonly Gen<string> Paste = Gen.Frequency(
        (5, Gen.Select(Line.Array[0, 30], Gen.OneOfConst(Breaks).Array[30], (lines, breaks) =>
            string.Concat(lines.Select((line, i) => line + breaks[i])))),
        (1, Gen.Char.Array[0, 200].Select(chars => new string(chars))));

    [Fact]
    public void An_ingredient_paste_keeps_every_line_in_order() =>
        Paste.Sample(iter: Iterations, assert: paste =>
        {
            var groups = PastedListReader.ReadIngredients(paste);
            Walk(paste, groups, ingredientList: true, l => l.OriginalText);
            Assert.All(groups.SelectMany(g => g.Lines), l => Assert.Equal(IngredientLineParser.Parse(l.OriginalText), l.Parse));
            Assert.Equal(groups, PastedListReader.ReadIngredients(paste), new GroupComparer<PasteIngredientLine>());
        });

    [Fact]
    public void A_step_paste_keeps_every_line_in_order_and_cuts_each_step_from_its_line() =>
        Paste.Sample(iter: Iterations, assert: paste =>
        {
            var groups = PastedListReader.ReadSteps(paste);
            Walk(paste, groups, ingredientList: false, s => s.OriginalText);
            foreach (var step in groups.SelectMany(g => g.Lines))
            {
                Assert.InRange(step.TextSpan.Start, 0, step.OriginalText.Length);
                Assert.InRange(step.TextSpan.End, step.TextSpan.Start, step.OriginalText.Length);
                Assert.Equal(step.Text, step.OriginalText.Substring(step.TextSpan.Start, step.TextSpan.Length));
                Assert.Equal(step.Text.Length == 0 && step.Problem != StepPasteProblem.IllFormedText, step.Problem == StepPasteProblem.NoText);
                if (step.Text.Length > 0)
                {
                    Assert.False(char.IsWhiteSpace(step.Text[0]) || char.IsWhiteSpace(step.Text[^1]), step.Text);
                }
            }

            Assert.Equal(groups, PastedListReader.ReadSteps(paste), new GroupComparer<PasteStepLine>());
        });

    // The groups, walked in order, are the paste's non-blank lines: each heading line is its group's, exactly as pasted, with
    // the heading cut from it at its span, and every other line is the next line of the current group, exactly as pasted.
    private static void Walk<TLine>(string paste, IReadOnlyList<PasteGroup<TLine>> groups, bool ingredientList, Func<TLine, string> original)
    {
        var expected = new List<(string? Heading, string? HeadingLine, List<string> Lines)>();
        foreach (var line in PastedListReader.Lines(paste))
        {
            Assert.DoesNotContain(line, c => c is '\n' or '\r' or (char)0x85 or (char)0x2028 or (char)0x2029);
            if (PastedListReader.ReadHeading(line, ingredientList) is { } heading)
            {
                Assert.Contains(heading, line, StringComparison.Ordinal);
                Assert.InRange(heading.Length, 1, RecipeLimits.MaxGroupHeadingLength);
                Assert.False(char.IsWhiteSpace(heading[0]) || char.IsWhiteSpace(heading[^1]), heading);
                expected.Add((heading, line, []));
            }
            else
            {
                if (expected.Count == 0)
                {
                    expected.Add((null, null, []));
                }

                expected[^1].Lines.Add(line);
            }
        }

        Assert.Equal(
            expected.Select(g => (g.Heading, g.HeadingLine, string.Join('\n', g.Lines))),
            groups.Select(g => (g.Heading, g.HeadingOriginalText, string.Join('\n', g.Lines.Select(original)))));
        Assert.All(groups, g => Assert.Equal(
            g.Heading,
            g.HeadingSpan is { } span ? g.HeadingOriginalText!.Substring(span.Start, span.Length) : null));
    }

    private sealed class GroupComparer<TLine> : IEqualityComparer<PasteGroup<TLine>>
    {
        public bool Equals(PasteGroup<TLine>? x, PasteGroup<TLine>? y) =>
            x is not null && y is not null && x.Heading == y.Heading && x.HeadingOriginalText == y.HeadingOriginalText
            && x.HeadingSpan == y.HeadingSpan && x.Lines.SequenceEqual(y.Lines);

        public int GetHashCode(PasteGroup<TLine> obj) => obj.Heading?.GetHashCode(StringComparison.Ordinal) ?? 0;
    }
}
