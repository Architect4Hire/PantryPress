using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using CsCheck;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.9: the laws a recipe's times keep over every value the table can hold (RCP-001): the total is the sum of what was
// entered and nothing else; a duration's parts, its text, and its ISO 8601 form each read back as exactly its length.
public sealed partial class RecipeTimesPropertyTests
{
    private const long Iterations = 20_000;

    private static readonly Gen<int?> Time = Gen.Frequency(
        (1, Gen.Const((int?)null)),
        (1, Gen.OneOfConst<int?>(0, 1, 59, 60, 1_439, 1_440, RecipeLimits.MaxMinutes)),
        (3, Gen.Int[0, 600].Select(m => (int?)m)),
        (2, Gen.Int[0, RecipeLimits.MaxMinutes].Select(m => (int?)m)));

    private static readonly Gen<RecipeDuration> Durations = Gen.Frequency(
        (1, Gen.Int[0, 3_000]),
        (1, Gen.Int[0, RecipeDuration.MaxTotalMinutes])).Select(m => new RecipeDuration(m));

    [Fact]
    public void The_total_is_the_sum_of_the_times_entered_and_each_time_is_as_entered() =>
        Gen.Select(Time, Time, Time).Sample(iter: Iterations, assert: t =>
        {
            var (prep, cook, additional) = t;
            var times = RecipeTimes.From(prep, cook, additional);

            Assert.Equal(prep, times.Prep?.Minutes);
            Assert.Equal(cook, times.Cook?.Minutes);
            Assert.Equal(additional, times.Additional?.Minutes);
            if (prep is null && cook is null && additional is null)
            {
                Assert.Null(times.Total);
                return;
            }

            Assert.Equal((prep ?? 0) + (cook ?? 0) + (additional ?? 0), times.Total!.Value.Minutes);
            Assert.Equal(times, RecipeTimes.From(prep, cook, additional));
        });

    [Fact]
    public void The_parts_add_back_to_the_length() =>
        Durations.Sample(iter: Iterations, assert: d =>
        {
            Assert.InRange(d.HoursPart, 0, 23);
            Assert.InRange(d.MinutesPart, 0, 59);
            Assert.Equal(d.Minutes, (d.DaysPart * 24 * 60) + (d.HoursPart * 60) + d.MinutesPart);
        });

    // The text is read back by its own grammar, independently of how it was written: number-and-word pairs split by single
    // spaces, days then hours then minutes, each at most once, no number zero but the "0 min" of nothing, hours below 24
    // and minutes below 60, and "day" singular only for one.
    [Fact]
    public void The_text_reads_back_as_the_length() =>
        Durations.Sample(iter: Iterations, assert: d =>
        {
            if (d.Minutes == 0)
            {
                Assert.Equal("0 min", d.Text);
                return;
            }

            var words = d.Text.Split(' ');
            Assert.True(words.Length is 2 or 4 or 6, d.Text);
            var total = 0;
            var lastPlace = -1;
            for (var i = 0; i < words.Length; i += 2)
            {
                Assert.Matches(WholeNumber(), words[i]);
                var number = int.Parse(words[i], NumberStyles.None, CultureInfo.InvariantCulture);
                var (place, size, limit) = words[i + 1] switch
                {
                    "day" when number == 1 => (0, 24 * 60, int.MaxValue),
                    "days" when number != 1 => (0, 24 * 60, int.MaxValue),
                    "hr" => (1, 60, 24),
                    "min" => (2, 1, 60),
                    var word => throw new Xunit.Sdk.XunitException($"'{word}' in '{d.Text}'."),
                };
                Assert.True(place > lastPlace, d.Text);
                Assert.InRange(number, 1, limit - 1);
                lastPlace = place;
                total += number * size;
            }

            Assert.Equal(d.Minutes, total);
        });

    // The ISO form is read back by the framework's own XML duration reader, and is hours and minutes only.
    [Fact]
    public void The_iso_duration_reads_back_as_the_length() =>
        Durations.Sample(iter: Iterations, assert: d =>
        {
            Assert.Matches(IsoDuration(), d.IsoDuration);
            var read = XmlConvert.ToTimeSpan(d.IsoDuration);
            Assert.Equal(d.Minutes * TimeSpan.TicksPerMinute, read.Ticks);
        });

    [GeneratedRegex("^[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex WholeNumber();

    [GeneratedRegex("^PT(?:[1-9][0-9]*H)?(?:[1-9][0-9]?M)?$|^PT0M$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDuration();
}
