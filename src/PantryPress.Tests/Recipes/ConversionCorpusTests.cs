using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

/// <summary>One case of the conversion table: a line or a step as written, and what a reader sees in each variant, in order.</summary>
internal sealed record ConversionCorpusCase(int LineNumber, bool IsStep, string Input, IReadOnlyList<string> Expected);

/// <summary>
/// The conversion table (PP4.8), <c>Corpus/conversion.txt</c>, read from the source tree; its header says how it is written.
/// There is no mode that rewrites it: each expectation is approved by reading it (CLAUDE.md).
/// </summary>
internal static class ConversionCorpus
{
    /// <summary>An ingredient case's variants, in the file's order.</summary>
    public static IReadOnlyList<(string Label, UnitSystem Target, ScaleFactor Factor)> LineVariants { get; } =
    [
        ("metric 1", UnitSystem.Metric, ScaleFactor.Unchanged),
        ("metric 2", UnitSystem.Metric, ScaleFactor.Doubled),
        ("us 1", UnitSystem.UsCustomary, ScaleFactor.Unchanged),
        ("us 1/2", UnitSystem.UsCustomary, ScaleFactor.Halved),
    ];

    /// <summary>A step case's variants, in the file's order.</summary>
    public static IReadOnlyList<(string Label, UnitSystem Target)> StepVariants { get; } =
    [
        ("metric", UnitSystem.Metric),
        ("us", UnitSystem.UsCustomary),
    ];

    public static string Path { get; } = System.IO.Path.Combine(ThisDirectory(), "Corpus", "conversion.txt");

    public static IReadOnlyList<ConversionCorpusCase> Load()
    {
        var lines = File.ReadAllLines(Path, Encoding.UTF8);
        var cases = new List<ConversionCorpusCase>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var isStep = line.StartsWith("step> ", StringComparison.Ordinal);
            if (!isStep && !line.StartsWith("> ", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Line {i + 1}: a case starts with '> ' or 'step> '.");
            }

            var labels = isStep ? StepVariants.Select(v => v.Label) : LineVariants.Select(v => v.Label);
            var expected = new List<string>();
            foreach (var label in labels)
            {
                var at = i + 1 + expected.Count;
                if (at >= lines.Length || !lines[at].StartsWith(label + " = ", StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Line {at + 1}: the case's next line is its {label} line, '{label} = '.");
                }

                expected.Add(lines[at][(label.Length + 3)..]);
            }

            var input = IngredientCorpus.Unescape(isStep ? line["step> ".Length..] : line[2..]);
            cases.Add(new ConversionCorpusCase(i + 1, isStep, input, expected));
            i += expected.Count;
        }

        return cases;
    }

    /// <summary>
    /// A converted line as the table writes it: <c>as written</c> or the quoted line, then <c>, converted</c>,
    /// <c>, rounded</c>, and <c>, unscaled &lt;reason&gt;</c> when they apply.
    /// </summary>
    public static string Describe(string input, ScaledIngredientLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var text = new StringBuilder(line.Text == input ? "as written" : $"\"{IngredientCorpus.Escape(line.Text)}\"");
        if (line.IsConverted)
        {
            text.Append(", converted");
        }

        if (line.IsRounded)
        {
            text.Append(", rounded");
        }

        if (line.Unscaled is { } reason)
        {
            text.Append(", unscaled ").Append(reason == UnscaledReason.Unparsed ? "unparsed" : "other-amounts");
        }

        return text.ToString();
    }

    public static string Describe(ConvertedStep step) =>
        step.IsConverted ? $"\"{IngredientCorpus.Escape(step.Text)}\"" : "as written";

    public static ScaledIngredientLine Convert(string input, ScaleFactor factor, UnitSystem target) =>
        RecipeConverter.ConvertLine(ScalableIngredientLine.From(input, IngredientLineParser.Parse(input)), factor, target);

    private static string ThisDirectory([CallerFilePath] string thisFile = "") =>
        System.IO.Path.GetDirectoryName(thisFile)!;
}

// PP4.8: the converter against the golden table, Corpus/conversion.txt - real lines in metric and US at two scales, and real
// steps, each with what a reader sees, read and approved by hand. Every mismatch is listed at once, with its line in the file.
public sealed class ConversionCorpusTests
{
    private static readonly IReadOnlyList<ConversionCorpusCase> Cases = ConversionCorpus.Load();

    [Fact]
    public void Every_line_and_step_converts_as_approved()
    {
        var mismatches = Mismatches();
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} converted lines differ:\n" + string.Join('\n', mismatches));
    }

    // Nothing reads the culture: a comma decimal point, other digits, or a different case rule would all show here.
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("fr-FR")]
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

    // The table shows every outcome: converted exactly and rounded, both directions of a density, every target unit, a range
    // with "to" and with a dash, the spoons kept, temperatures in a line and in steps, and every reason a text stays as written.
    [Fact]
    public void The_table_covers_every_outcome()
    {
        var lines = Cases.Where(c => !c.IsStep).ToList();
        var steps = Cases.Where(c => c.IsStep).ToList();
        Assert.True(lines.Count >= 100, $"The table has {lines.Count} lines.");
        Assert.True(steps.Count >= 25, $"The table has {steps.Count} steps.");
        Assert.Equal(Cases.Count, Cases.Select(c => (c.IsStep, c.Input)).Distinct().Count());

        var all = lines.SelectMany(c => c.Expected).ToList();
        Assert.Contains(all, e => e.EndsWith("\", converted", StringComparison.Ordinal));
        Assert.Contains(all, e => e.EndsWith("\", converted, rounded", StringComparison.Ordinal));
        Assert.Contains(all, e => e.StartsWith('"') && !e.Contains(", converted", StringComparison.Ordinal));
        Assert.Contains("as written", all);
        Assert.Contains("as written, unscaled unparsed", all);
        Assert.Contains("as written, unscaled other-amounts", all);

        var converted = lines
            .SelectMany(c => ConversionCorpus.LineVariants.Select(v => (c.Input, Result: ConversionCorpus.Convert(c.Input, v.Factor, v.Target))))
            .Where(r => r.Result.IsConverted && r.Result.Quantity is not null)
            .Select(r => (r.Input, r.Result, Unit: IngredientLineParser.ParseAnyLength(r.Result.Text).UnitCode))
            .ToList();
        foreach (var code in new[] { "milliliter", "liter", "gram", "kilogram", "teaspoon", "tablespoon", "cup", "ounce", "pound", "celsius", "fahrenheit" })
        {
            Assert.Contains(converted, r => r.Unit == code);
        }

        Assert.Contains(converted, r => r.Unit == "gram" && Units.All.Single(u => u.Code == IngredientLineParser.Parse(r.Input).UnitCode).Dimension == UnitDimension.Volume);
        Assert.Contains(converted, r => r.Unit == "cup" && Units.All.Single(u => u.Code == IngredientLineParser.Parse(r.Input).UnitCode).Dimension == UnitDimension.Weight);
        Assert.Contains(converted, r => r.Result.Quantity!.Kind == QuantityKind.Range && r.Result.Text.Contains(" to ", StringComparison.Ordinal));
        Assert.Contains(converted, r => r.Result.Quantity!.Kind == QuantityKind.Range && r.Result.Text.Contains(QuantityText.RangeSeparator, StringComparison.Ordinal));
        Assert.Contains(lines, c => IngredientLineParser.Parse(c.Input).UnitCode is "teaspoon" or "tablespoon");

        var stepResults = steps.SelectMany(c => c.Expected).ToList();
        Assert.Contains(stepResults, e => e.StartsWith('"'));
        Assert.Contains("as written", stepResults);
    }

    private static List<string> Mismatches()
    {
        var mismatches = new List<string>();
        foreach (var c in Cases)
        {
            var actual = c.IsStep
                ? ConversionCorpus.StepVariants.Select(v => (v.Label, Text: ConversionCorpus.Describe(RecipeConverter.ConvertStep(c.Input, v.Target))))
                : ConversionCorpus.LineVariants.Select(v => (v.Label, Text: ConversionCorpus.Describe(c.Input, ConversionCorpus.Convert(c.Input, v.Factor, v.Target))));
            mismatches.AddRange(actual
                .Select((a, i) => (a.Label, a.Text, Expected: c.Expected[i]))
                .Where(r => r.Text != r.Expected)
                .Select(r => $"line {c.LineNumber} {r.Label}: \"{IngredientCorpus.Escape(c.Input)}\"\n  expected {r.Expected}\n  actual   {r.Text}"));
        }

        return mismatches;
    }
}
