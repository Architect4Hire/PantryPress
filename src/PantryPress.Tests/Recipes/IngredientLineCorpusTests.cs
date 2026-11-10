using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.5: the parser against the golden corpus, Corpus/ingredient-lines.txt - hundreds of lines in the shapes bloggers
// really write, each with the parse a person read and approved. Every mismatch is listed at once, with its line in the
// file, so a change to the parser shows everything it moves.
public sealed class IngredientLineCorpusTests
{
    private static readonly IReadOnlyList<IngredientCorpusCase> Cases = IngredientCorpus.Load();

    [Fact]
    public void Every_line_parses_as_approved()
    {
        var mismatches = Cases
            .Select(c => (Case: c, Actual: IngredientCorpus.Describe(IngredientLineParser.Parse(c.Input))))
            .Where(r => r.Actual != r.Case.Expected)
            .Select(r => $"line {r.Case.LineNumber}: \"{IngredientCorpus.Escape(r.Case.Input)}\"\n  expected {r.Case.Expected}\n  actual   {r.Actual}")
            .ToList();

        Assert.True(mismatches.Count == 0, $"{mismatches.Count} corpus lines parse differently:\n" + string.Join('\n', mismatches));
    }

    [Fact]
    public void Every_line_keeps_the_parse_invariants()
    {
        foreach (var c in Cases)
        {
            IngredientParseInvariants.Check(c.Input, IngredientLineParser.Parse(c.Input));
        }
    }

    // The corpus is large and covers what it claims: parsed lines with every quantity kind, with and without a unit and a
    // note, and every reason a line can be refused that a line of at most the stored length can show. The length reasons
    // are proved in IngredientLineParserTests, where a line can be built to size.
    [Fact]
    public void The_corpus_covers_every_shape()
    {
        Assert.True(Cases.Count >= 400, $"The corpus has {Cases.Count} lines.");
        Assert.Equal(Cases.Count, Cases.Select(c => c.Input).Distinct(StringComparer.Ordinal).Count());

        var parses = Cases.Select(c => IngredientLineParser.Parse(c.Input)).ToList();
        foreach (var kind in Enum.GetValues<QuantityKind>())
        {
            Assert.Contains(parses, p => p.Quantity?.Kind == kind);
        }

        Assert.Contains(parses, p => p.Status == IngredientParseStatus.Parsed && p.Quantity is null);
        Assert.Contains(parses, p => p.Quantity?.Kind == QuantityKind.Range && p.UnitCode is not null);
        Assert.Contains(parses, p => p.Note?.Contains(IngredientLineParse.NoteSeparator, StringComparison.Ordinal) == true);

        IngredientParseProblem[] proved = [IngredientParseProblem.TooLong, IngredientParseProblem.ItemTooLong, IngredientParseProblem.NoteTooLong];
        foreach (var problem in Enum.GetValues<IngredientParseProblem>().Except(proved))
        {
            Assert.Contains(parses, p => p.Problem == problem);
        }
    }

    // Every unit in the vocabulary is read by at least one line, so a unit the parser cannot reach would show here.
    [Fact]
    public void The_corpus_reads_every_unit()
    {
        var read = Cases.Select(c => IngredientLineParser.Parse(c.Input).UnitCode).OfType<string>().ToHashSet(StringComparer.Ordinal);
        Assert.Equal([], Units.All.Select(u => u.Code).Where(code => !read.Contains(code)).ToArray());
    }
}
