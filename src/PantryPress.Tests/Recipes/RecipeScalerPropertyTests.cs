using CsCheck;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.7: the laws the scaler keeps over every real line in both corpora at many factors (RCP-004, RCP-002): it never throws
// and answers the same twice; the blogger's words around the replaced spans never change; a scaled line reads back as what
// the scaler says it shows; nothing moves more than rounding allows; and an exact result scaled back by 1/k is the original.
public sealed class RecipeScalerPropertyTests
{
    private const long Iterations = 20_000;

    private static readonly string[] Lines =
    [
        .. IngredientCorpus.Load().Select(c => c.Input)
            .Concat(ScalingCorpus.Load().Select(c => c.Input))
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal),
    ];

    private static readonly Gen<ScaleFactor> Factors = Gen.Frequency(
        (2, Gen.OneOfConst([.. ScaleFactor.Multipliers])),
        (2, Gen.Select(Gen.Long[1, 12], Gen.Long[1, 36], (yield, servings) => ScaleFactor.ForServings(Quantity.Amount(yield), servings)!)),
        (1, Gen.Select(Gen.Long[1, 64], Gen.Long[1, 64], (n, d) => ScaleFactor.Of(Rational.Create(n, d)))));

    private static readonly Gen<(string Text, ScaleFactor Factor)> Cases = Gen.Select(Gen.Int[0, Lines.Length - 1].Select(i => Lines[i]), Factors);

    [Fact]
    public void Every_line_scales_without_throwing_and_the_same_twice() =>
        Cases.Sample(iter: Iterations, assert: c => Assert.Equal(Scale(c.Text, c.Factor), Scale(c.Text, c.Factor)));

    [Fact]
    public void Only_the_quantity_and_the_units_number_are_written_anew() =>
        Cases.Sample(iter: Iterations, assert: c =>
        {
            var parse = IngredientLineParser.Parse(c.Text);
            var scaled = Scale(c.Text, c.Factor);
            if (scaled.Text == c.Text)
            {
                return;
            }

            var q = parse.QuantitySpan!.Value;
            var nq = scaled.QuantitySpan!.Value;
            Assert.Equal(q.Start, nq.Start);
            Assert.Equal(c.Text[..q.Start], scaled.Text[..nq.Start]);
            var shift = nq.Length - q.Length;
            if (parse.UnitSpan is not { } unit)
            {
                Assert.Equal(c.Text[q.End..], scaled.Text[nq.End..]);
                return;
            }

            Assert.Equal(c.Text[q.End..unit.Start], scaled.Text[nq.End..(unit.Start + shift)]);
            var written = c.Text.Substring(unit.Start, unit.Length);
            var tail = c.Text[unit.End..];
            Assert.EndsWith(tail, scaled.Text, StringComparison.Ordinal);
            var shown = scaled.Text[(unit.Start + shift)..^tail.Length];
            Assert.Contains(shown, new[] { written, UnitInflections.Agree(written, true), UnitInflections.Agree(written, false) });
        });

    // What the reader sees is a line the parser reads as exactly what the scaler says it shows, with the same unit, item,
    // and note. A result past the stored term cap (1000000 g tripled) is display only, so it is left out.
    [Fact]
    public void A_scaled_line_reads_back_as_what_it_shows() =>
        Cases.Sample(iter: Iterations, assert: c =>
        {
            var parse = IngredientLineParser.Parse(c.Text);
            var scaled = Scale(c.Text, c.Factor);
            if (scaled.Text == c.Text || !WithinCap(scaled.Quantity!))
            {
                return;
            }

            var reread = IngredientLineParser.ParseAnyLength(scaled.Text);
            Assert.Equal(IngredientParseStatus.Parsed, reread.Status);
            Assert.Equal(scaled.Quantity, reread.Quantity);
            Assert.Equal(scaled.QuantitySpan, reread.QuantitySpan);
            Assert.Equal(parse.UnitCode, reread.UnitCode);
            Assert.Equal(parse.Item, reread.Item);
            Assert.Equal(parse.Note, reread.Note);
        });

    [Fact]
    public void Only_a_line_with_a_number_scaling_would_change_is_flagged() =>
        Cases.Sample(iter: Iterations, assert: c =>
        {
            var parse = IngredientLineParser.Parse(c.Text);
            var scaled = Scale(c.Text, c.Factor);

            if (c.Factor.IsUnchanged || scaled.Unscaled is null)
            {
                return;
            }

            Assert.Equal(c.Text, scaled.Text);
            Assert.False(scaled.IsRounded);
            Assert.Equal(parse.Status == IngredientParseStatus.Unparsed ? UnscaledReason.Unparsed : UnscaledReason.OtherAmounts, scaled.Unscaled);
        });

    // Each end shown is exactly the exact end rounded on the grid the line's unit and style choose, worked out here on its
    // own (a range whose ends round alike shows that one amount).
    [Fact]
    public void Each_end_is_the_exact_end_rounded_on_the_lines_grid() =>
        Cases.Sample(iter: Iterations, assert: c =>
        {
            var parse = IngredientLineParser.Parse(c.Text);
            var scaled = Scale(c.Text, c.Factor);
            if (scaled.Text == c.Text)
            {
                return;
            }

            var exact = parse.Quantity!.MultiplyBy(c.Factor.Value);
            var grid = ExpectedGrid(c.Text, parse);
            Assert.Equal(FriendlyRounding.Round(exact.Low!.Value, grid), scaled.Quantity!.Low);
            Assert.Equal(FriendlyRounding.Round(exact.High ?? exact.Low!.Value, grid), scaled.Quantity.High ?? scaled.Quantity.Low);
            Assert.Equal(scaled.IsRounded, scaled.Quantity != exact);
        });

    // The worst failure is quiet: a line that should scale shown as written with no flag. An unflagged line left as written
    // at a factor other than 1x had nothing to scale - no amount, or a temperature - or its amount rounds back to itself.
    [Fact]
    public void An_unflagged_line_shown_as_written_had_nothing_to_scale() =>
        Cases.Sample(iter: Iterations, assert: c =>
        {
            var parse = IngredientLineParser.Parse(c.Text);
            var scaled = Scale(c.Text, c.Factor);
            if (c.Factor.IsUnchanged || scaled.Unscaled is not null || scaled.Text != c.Text)
            {
                return;
            }

            if (parse.Quantity is not { HasAmount: true } quantity
                || (parse.UnitCode is { } code && Units.All.Single(u => u.Code == code).Dimension == UnitDimension.Temperature))
            {
                return;
            }

            var exact = quantity.MultiplyBy(c.Factor.Value);
            var grid = ExpectedGrid(c.Text, parse);
            var low = FriendlyRounding.Round(exact.Low!.Value, grid);
            var high = FriendlyRounding.Round(exact.High ?? exact.Low!.Value, grid);
            Assert.Equal(quantity, low == high ? Quantity.Amount(low) : Quantity.Range(low, high));
        });

    // The largest and smallest factors there are, on every line: terms out to 10^12, never an overflow.
    [Fact]
    public void Every_line_scales_at_the_extreme_factors()
    {
        ScaleFactor[] extremes =
        [
            ScaleFactor.Of(RecipeLimits.MaxQuantityTerm),
            ScaleFactor.Of(Rational.Create(1, RecipeLimits.MaxQuantityTerm)),
            ScaleFactor.ForServings(Quantity.Amount(1), RecipeLimits.MaxQuantityTerm)!,
            ScaleFactor.Of(Rational.Create(RecipeLimits.MaxQuantityTerm - 1, RecipeLimits.MaxQuantityTerm)),
        ];
        foreach (var text in Lines)
        {
            foreach (var factor in extremes)
            {
                Assert.Equal(Scale(text, factor), Scale(text, factor));
            }
        }
    }
    // recipes.md: scaling by k then 1/k returns the original quantity. Rounding is the one inexact step, so an unrounded
    // result scaled back is exactly the blogger's amount.
    [Fact]
    public void An_unrounded_result_scaled_back_by_one_over_k_is_the_original() =>
        Cases.Sample(iter: Iterations, assert: c =>
        {
            var parse = IngredientLineParser.Parse(c.Text);
            var scaled = Scale(c.Text, c.Factor);
            if (scaled.Text == c.Text || scaled.IsRounded)
            {
                return;
            }

            Assert.Equal(parse.Quantity, scaled.Quantity!.MultiplyBy(c.Factor.Value.Reciprocal()));
        });

    private static ScaledIngredientLine Scale(string text, ScaleFactor factor) => ScalingCorpus.Scale(text, factor);

    private static bool WithinCap(Quantity quantity) =>
        new[] { quantity.Low, quantity.High }.OfType<Rational>()
            .All(v => v.Numerator <= RecipeLimits.MaxQuantityTerm && v.Denominator <= RecipeLimits.MaxQuantityTerm);

    // The grid the rules choose, worked out apart from the scaler: an inexact measure, then a metric unit, then a decimal point
    // in the quantity's span.
    private static RoundingGrid ExpectedGrid(string text, IngredientLineParse parse)
    {
        var unit = parse.UnitCode is { } code ? Units.All.Single(u => u.Code == code) : null;
        if (unit?.Code is "pinch" or "dash" or "drop" or "handful")
        {
            return RoundingGrid.Whole;
        }

        if (unit?.System == UnitSystem.Metric)
        {
            return RoundingGrid.Metric;
        }

        var span = parse.QuantitySpan!.Value;
        return text.Substring(span.Start, span.Length).Contains('.', StringComparison.Ordinal) ? RoundingGrid.KitchenDecimal : RoundingGrid.Kitchen;
    }
}