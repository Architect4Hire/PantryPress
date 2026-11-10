using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

/// <summary>One case of the scaling table: the line as written, and what a reader sees at each factor, in order.</summary>
internal sealed record ScalingCorpusCase(int LineNumber, string Input, IReadOnlyList<string> Expected);

/// <summary>
/// The scaling table (PP4.7), <c>Corpus/scaling.txt</c>, read from the source tree; its header says how it is written. There
/// is no mode that rewrites it: each expectation is approved by reading it (CLAUDE.md).
/// </summary>
internal static class ScalingCorpus
{
    /// <summary>The factors each case is shown at, in the file's order: ½×, 2×, 3×, and 3 or 5 servings of a recipe for 4.</summary>
    public static IReadOnlyList<(string Label, ScaleFactor Factor)> Factors { get; } =
    [
        ("1/2", ScaleFactor.Halved),
        ("2", ScaleFactor.Doubled),
        ("3", ScaleFactor.Tripled),
        ("3/4", ScaleFactor.ForServings(Quantity.Amount(4), 3)!),
        ("5/4", ScaleFactor.ForServings(Quantity.Amount(4), 5)!),
    ];

    public static string Path { get; } = System.IO.Path.Combine(ThisDirectory(), "Corpus", "scaling.txt");

    public static IReadOnlyList<ScalingCorpusCase> Load()
    {
        var lines = File.ReadAllLines(Path, Encoding.UTF8);
        var cases = new List<ScalingCorpusCase>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (!line.StartsWith("> ", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Line {i + 1}: a case starts with '> '.");
            }

            var expected = new List<string>();
            foreach (var (label, _) in Factors)
            {
                var at = i + 1 + expected.Count;
                if (at >= lines.Length || !lines[at].StartsWith(label + " = ", StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Line {at + 1}: the case's next line is its {label} line, '{label} = '.");
                }

                expected.Add(lines[at][(label.Length + 3)..]);
            }

            cases.Add(new ScalingCorpusCase(i + 1, IngredientCorpus.Unescape(line[2..]), expected));
            i += Factors.Count;
        }

        return cases;
    }

    /// <summary>
    /// A scaled line as the table writes it: <c>as written</c> or the quoted line, then <c>, rounded</c> and
    /// <c>, unscaled &lt;reason&gt;</c> when they apply.
    /// </summary>
    public static string Describe(string input, ScaledIngredientLine scaled)
    {
        ArgumentNullException.ThrowIfNull(scaled);
        var text = new StringBuilder(scaled.Text == input ? "as written" : $"\"{IngredientCorpus.Escape(scaled.Text)}\"");
        if (scaled.IsRounded)
        {
            text.Append(", rounded");
        }

        if (scaled.Unscaled is { } reason)
        {
            text.Append(", unscaled ").Append(reason == UnscaledReason.Unparsed ? "unparsed" : "other-amounts");
        }

        return text.ToString();
    }

    public static ScaledIngredientLine Scale(string input, ScaleFactor factor) =>
        RecipeScaler.ScaleLine(ScalableIngredientLine.From(input, IngredientLineParser.Parse(input)), factor);

    private static string ThisDirectory([CallerFilePath] string thisFile = "") =>
        System.IO.Path.GetDirectoryName(thisFile)!;
}

// PP4.7: the scaler against the golden table, Corpus/scaling.txt - real lines at ½×, 2×, 3×, and two custom servings, each
// with what a reader sees, read and approved by hand. Every mismatch is listed at once, with its line in the file.
public sealed class ScalingCorpusTests
{
    private static readonly IReadOnlyList<ScalingCorpusCase> Cases = ScalingCorpus.Load();

    [Fact]
    public void Every_line_scales_as_approved()
    {
        var mismatches = Mismatches();
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} scaled lines differ:\n" + string.Join('\n', mismatches));
    }

    // Nothing reads the culture: a comma decimal point, other digits, or a different case rule would all show here.
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("")]
    public void The_table_is_the_same_in_every_culture(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Empty(Mismatches());
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // At 1× every line is exactly as written, unflagged, for the whole parser corpus and the table's own lines.
    [Fact]
    public void At_one_every_corpus_line_is_exactly_as_written()
    {
        var inputs = IngredientCorpus.Load().Select(c => c.Input).Concat(Cases.Select(c => c.Input)).Where(t => t.Length > 0);
        foreach (var input in inputs)
        {
            var parse = IngredientLineParser.Parse(input);
            Assert.Equal(
                new ScaledIngredientLine(input, parse.Quantity, parse.QuantitySpan, false, null),
                ScalingCorpus.Scale(input, ScaleFactor.Unchanged));
        }
    }

    // The table is large and shows every outcome: scaled exactly and rounded, as written with and without each reason, a
    // range kept and a range collapsed, every rounding grid, and both kinds of fraction.
    [Fact]
    public void The_table_covers_every_outcome()
    {
        Assert.True(Cases.Count >= 200, $"The table has {Cases.Count} lines.");
        Assert.Equal(Cases.Count, Cases.Select(c => c.Input).Distinct(StringComparer.Ordinal).Count());

        var all = Cases.SelectMany(c => c.Expected).ToList();
        Assert.Contains(all, e => e.StartsWith('"') && !e.Contains(", rounded", StringComparison.Ordinal));
        Assert.Contains(all, e => e.StartsWith('"') && e.EndsWith(", rounded", StringComparison.Ordinal));
        Assert.Contains("as written", all);
        Assert.Contains("as written, unscaled unparsed", all);
        Assert.Contains("as written, unscaled other-amounts", all);

        var scaled = Cases
            .SelectMany(c => ScalingCorpus.Factors.Select(f => (c.Input, Result: ScalingCorpus.Scale(c.Input, f.Factor))))
            .Where(r => r.Result.Text != r.Input)
            .ToList();
        Assert.Contains(scaled, r => r.Result.Quantity!.Kind == QuantityKind.Range);
        Assert.Contains(scaled, r => r.Result.Quantity!.Kind == QuantityKind.Amount && IngredientLineParser.Parse(r.Input).Quantity!.Kind == QuantityKind.Range);
        Assert.Contains(scaled, r => r.Result.Text.Contains(" to ", StringComparison.Ordinal));
        Assert.Contains(scaled, r => r.Result.Text.Contains('½', StringComparison.Ordinal));
        Assert.Contains(scaled, r => r.Result.Text.Contains("1/2", StringComparison.Ordinal));
        Assert.Contains(scaled, r => r.Result.Text.Contains("0.", StringComparison.Ordinal));

        var units = Cases.Select(c => IngredientLineParser.Parse(c.Input).UnitCode).OfType<string>()
            .Select(code => Units.All.Single(u => u.Code == code)).ToList();
        Assert.Contains(units, u => u.System == UnitSystem.Metric);
        Assert.Contains(units, u => u.Code is "pinch" or "dash" or "drop" or "handful");
        Assert.Contains(units, u => u.Dimension == UnitDimension.Temperature);
        Assert.Contains(Cases, c => IngredientLineParser.Parse(c.Input) is { Quantity: not null, UnitCode: null });
    }

    private static List<string> Mismatches() =>
        Cases
            .SelectMany(c => ScalingCorpus.Factors.Select((f, i) => (Case: c, f.Label, Expected: c.Expected[i], Actual: ScalingCorpus.Describe(c.Input, ScalingCorpus.Scale(c.Input, f.Factor)))))
            .Where(r => r.Actual != r.Expected)
            .Select(r => $"line {r.Case.LineNumber} at {r.Label}: \"{IngredientCorpus.Escape(r.Case.Input)}\"\n  expected {r.Expected}\n  actual   {r.Actual}")
            .ToList();
}
