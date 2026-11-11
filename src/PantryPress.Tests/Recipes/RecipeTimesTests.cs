using System.Globalization;
using PantryPress.Domain.Modules.Recipes.Business;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Tests.Recipes;

// PP4.9: a recipe's times (RCP-001). The total is never stored; it is the sum of the times the blogger entered. Every
// expectation is worked by hand.
public sealed class RecipeTimesTests
{
    [Fact]
    public void The_total_is_the_sum_of_the_three_times()
    {
        var times = RecipeTimes.From(20, 60, 60);

        Assert.Equal(20, times.Prep!.Value.Minutes);
        Assert.Equal(60, times.Cook!.Value.Minutes);
        Assert.Equal(60, times.Additional!.Value.Minutes);
        Assert.Equal(140, times.Total!.Value.Minutes);
        Assert.Equal("2 hr 20 min", times.Total.Value.Text);
        Assert.Equal("PT2H20M", times.Total.Value.IsoDuration);
    }

    [Theory]
    [InlineData(15, null, null, 15)]
    [InlineData(null, 45, null, 45)]
    [InlineData(null, null, 480, 480)]
    [InlineData(10, 25, null, 35)]
    [InlineData(10, null, 120, 130)]
    [InlineData(null, 30, 5, 35)]
    public void The_total_is_the_sum_of_whichever_times_were_entered(int? prep, int? cook, int? additional, int total)
    {
        var times = RecipeTimes.From(prep, cook, additional);

        Assert.Equal(prep, times.Prep?.Minutes);
        Assert.Equal(cook, times.Cook?.Minutes);
        Assert.Equal(additional, times.Additional?.Minutes);
        Assert.Equal(total, times.Total!.Value.Minutes);
    }

    [Fact]
    public void With_no_time_entered_there_is_no_total()
    {
        var times = RecipeTimes.From(null, null, null);

        Assert.Null(times.Prep);
        Assert.Null(times.Cook);
        Assert.Null(times.Additional);
        Assert.Null(times.Total);
    }

    // The total is only ever the sum: nothing outside the type can make one or set a time, so From is the only way in and
    // a copy cannot keep a total its times no longer add up to.
    [Fact]
    public void Times_are_made_only_from_entered_minutes()
    {
        Assert.Empty(typeof(RecipeTimes).GetConstructors());
        Assert.All(typeof(RecipeTimes).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.Equal(RecipeTimes.From(10, 20, null), RecipeTimes.From(10, 20, null));
        Assert.NotEqual(RecipeTimes.From(10, 20, null), RecipeTimes.From(10, 20, 0));
    }

    // Owner decision (PP4.9): zero is a time the blogger entered, unlike an empty one, so it is shown and counted.
    [Fact]
    public void A_time_entered_as_zero_is_entered()
    {
        var noCook = RecipeTimes.From(15, 0, null);
        Assert.Equal(0, noCook.Cook!.Value.Minutes);
        Assert.Equal("0 min", noCook.Cook.Value.Text);
        Assert.Equal("PT0M", noCook.Cook.Value.IsoDuration);
        Assert.Null(noCook.Additional);
        Assert.Equal(15, noCook.Total!.Value.Minutes);

        var allZero = RecipeTimes.From(0, null, 0);
        Assert.Equal(0, allZero.Total!.Value.Minutes);
        Assert.Equal("0 min", allZero.Total.Value.Text);
    }

    [Fact]
    public void The_longest_times_make_the_longest_total()
    {
        var times = RecipeTimes.From(RecipeLimits.MaxMinutes, RecipeLimits.MaxMinutes, RecipeLimits.MaxMinutes);

        Assert.Equal(RecipeDuration.MaxTotalMinutes, times.Total!.Value.Minutes);
        Assert.Equal(129_600, RecipeDuration.MaxTotalMinutes);
        Assert.Equal("90 days", times.Total.Value.Text);
        Assert.Equal("PT2160H", times.Total.Value.IsoDuration);
        Assert.Equal("30 days", times.Prep!.Value.Text);
    }

    [Theory]
    [InlineData(-1, null, null, "prepMinutes")]
    [InlineData(null, -1, null, "cookMinutes")]
    [InlineData(null, null, -1, "additionalMinutes")]
    [InlineData(RecipeLimits.MaxMinutes + 1, null, null, "prepMinutes")]
    [InlineData(null, RecipeLimits.MaxMinutes + 1, null, "cookMinutes")]
    [InlineData(null, null, RecipeLimits.MaxMinutes + 1, "additionalMinutes")]
    [InlineData(int.MinValue, null, null, "prepMinutes")]
    [InlineData(null, null, int.MaxValue, "additionalMinutes")]
    public void A_time_the_table_cannot_hold_is_a_bug(int? prep, int? cook, int? additional, string parameter)
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => RecipeTimes.From(prep, cook, additional));
        Assert.Equal(parameter, refused.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(RecipeDuration.MaxTotalMinutes + 1)]
    [InlineData(int.MaxValue)]
    public void A_duration_is_from_zero_to_the_longest_total(int minutes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecipeDuration(minutes));

    [Fact]
    public void Default_is_zero_minutes()
    {
        Assert.Equal(new RecipeDuration(0), default);
        Assert.Equal("0 min", default(RecipeDuration).Text);
        Assert.Equal("PT0M", default(RecipeDuration).IsoDuration);
    }

    // Every edge where a part appears or carries: a minute, an hour, a day, and either side of each.
    [Theory]
    [InlineData(0, 0, 0, 0, "0 min", "PT0M")]
    [InlineData(1, 0, 0, 1, "1 min", "PT1M")]
    [InlineData(20, 0, 0, 20, "20 min", "PT20M")]
    [InlineData(59, 0, 0, 59, "59 min", "PT59M")]
    [InlineData(60, 0, 1, 0, "1 hr", "PT1H")]
    [InlineData(61, 0, 1, 1, "1 hr 1 min", "PT1H1M")]
    [InlineData(90, 0, 1, 30, "1 hr 30 min", "PT1H30M")]
    [InlineData(140, 0, 2, 20, "2 hr 20 min", "PT2H20M")]
    [InlineData(1_380, 0, 23, 0, "23 hr", "PT23H")]
    [InlineData(1_439, 0, 23, 59, "23 hr 59 min", "PT23H59M")]
    [InlineData(1_440, 1, 0, 0, "1 day", "PT24H")]
    [InlineData(1_441, 1, 0, 1, "1 day 1 min", "PT24H1M")]
    [InlineData(1_500, 1, 1, 0, "1 day 1 hr", "PT25H")]
    [InlineData(1_620, 1, 3, 0, "1 day 3 hr", "PT27H")]
    [InlineData(1_621, 1, 3, 1, "1 day 3 hr 1 min", "PT27H1M")]
    [InlineData(2_880, 2, 0, 0, "2 days", "PT48H")]
    [InlineData(4_319, 2, 23, 59, "2 days 23 hr 59 min", "PT71H59M")]
    [InlineData(43_200, 30, 0, 0, "30 days", "PT720H")]
    [InlineData(129_599, 89, 23, 59, "89 days 23 hr 59 min", "PT2159H59M")]
    public void A_duration_reads_in_days_hours_and_minutes(int minutes, int days, int hours, int rest, string text, string iso)
    {
        var duration = new RecipeDuration(minutes);

        Assert.Equal(minutes, duration.Minutes);
        Assert.Equal(days, duration.DaysPart);
        Assert.Equal(hours, duration.HoursPart);
        Assert.Equal(rest, duration.MinutesPart);
        Assert.Equal(text, duration.Text);
        Assert.Equal(text, duration.ToString());
        Assert.Equal(iso, duration.IsoDuration);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("fr-FR")]
    [InlineData("hi-IN")]
    [InlineData("")]
    public void A_duration_reads_alike_in_every_culture(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var duration = new RecipeDuration(129_599);
            Assert.Equal("89 days 23 hr 59 min", duration.Text);
            Assert.Equal("PT2159H59M", duration.IsoDuration);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
