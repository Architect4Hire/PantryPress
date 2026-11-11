using System.Globalization;
using PantryPress.Domain.Modules.Recipes.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>
/// A recipe's times as a reader sees them (RCP-001; PP-D05; PP4.9): prep, cook, and additional exactly as the blogger
/// entered them, and the total, which is never stored (PP4.3) but always their sum. Whole minutes and integer arithmetic
/// only; nothing here reads a clock, a culture, or floating point, so the same times give the same answer everywhere.
/// <list type="bullet">
/// <item>A time left empty was not entered and is absent here. A time entered as zero was entered: it is shown
/// (<c>0 min</c>) and counts toward the total (owner decision, PP4.9).</item>
/// <item>The total is the sum of the times entered, whichever they are. With none entered there is no total.</item>
/// </list>
/// What each time is called - "Prep", "Total", the blogger's label for the additional time - is the recipe card's (PP7.7).
/// </summary>
public sealed record RecipeTimes
{
    private RecipeTimes(RecipeDuration? prep, RecipeDuration? cook, RecipeDuration? additional, RecipeDuration? total)
    {
        Prep = prep;
        Cook = cook;
        Additional = additional;
        Total = total;
    }

    public RecipeDuration? Prep { get; }

    public RecipeDuration? Cook { get; }

    public RecipeDuration? Additional { get; }

    /// <summary>The sum of the times entered; none when none was. <see cref="From"/> is the only way to make one.</summary>
    public RecipeDuration? Total { get; }

    /// <summary>
    /// The times from their stored minutes. Each is from 0 to <see cref="RecipeLimits.MaxMinutes"/>, as the table's check
    /// and the recipe routes' validators hold them; anything else is a bug, and throws.
    /// </summary>
    public static RecipeTimes From(int? prepMinutes, int? cookMinutes, int? additionalMinutes)
    {
        var prep = Entered(prepMinutes, nameof(prepMinutes));
        var cook = Entered(cookMinutes, nameof(cookMinutes));
        var additional = Entered(additionalMinutes, nameof(additionalMinutes));

        RecipeDuration? total = prep is null && cook is null && additional is null
            ? null
            : new RecipeDuration((prep?.Minutes ?? 0) + (cook?.Minutes ?? 0) + (additional?.Minutes ?? 0));
        return new RecipeTimes(prep, cook, additional, total);
    }

    private static RecipeDuration? Entered(int? minutes, string name) => minutes switch
    {
        null => null,
        < 0 or > RecipeLimits.MaxMinutes => throw new ArgumentOutOfRangeException(
            name, minutes, string.Create(CultureInfo.InvariantCulture, $"A recipe time is from 0 to {RecipeLimits.MaxMinutes} minutes.")),
        { } m => new RecipeDuration(m),
    };
}

/// <summary>
/// A length of time in whole minutes (PP4.9), from zero up to <see cref="MaxTotalMinutes"/>, the longest total three stored times
/// can make. Its text is for a reader; its ISO 8601 form is for schema.org (PP4.10).
/// </summary>
public readonly record struct RecipeDuration
{
    /// <summary>The longest total: every one of the three times at <see cref="RecipeLimits.MaxMinutes"/>.</summary>
    public const int MaxTotalMinutes = 3 * RecipeLimits.MaxMinutes;

    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = 24 * MinutesPerHour;

    public RecipeDuration(int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minutes, MaxTotalMinutes);
        Minutes = minutes;
    }

    /// <summary>The whole length, in minutes.</summary>
    public int Minutes { get; }

    /// <summary>Whole days, once the length reaches 24 hours (owner decision, PP4.9).</summary>
    public int DaysPart => Minutes / MinutesPerDay;

    /// <summary>The hours left after the days: 0 to 23.</summary>
    public int HoursPart => Minutes % MinutesPerDay / MinutesPerHour;

    /// <summary>The minutes left after the hours: 0 to 59.</summary>
    public int MinutesPart => Minutes % MinutesPerHour;

    /// <summary>
    /// The length for a reader, in the recipe card's short words (<c>20 min</c>, <c>1 hr</c>, <c>2 hr 20 min</c>,
    /// <c>1 day 3 hr</c>, <c>2 days</c>): days, hours, and minutes, leaving out each part that is zero, and <c>0 min</c>
    /// for nothing. ASCII digits, no culture.
    /// </summary>
    public string Text
    {
        get
        {
            if (Minutes == 0)
            {
                return "0 min";
            }

            var parts = new List<string>(3);
            if (DaysPart > 0)
            {
                parts.Add(DaysPart == 1 ? "1 day" : $"{Number(DaysPart)} days");
            }

            if (HoursPart > 0)
            {
                parts.Add($"{Number(HoursPart)} hr");
            }

            if (MinutesPart > 0)
            {
                parts.Add($"{Number(MinutesPart)} min");
            }

            return string.Join(' ', parts);
        }
    }

    /// <summary>
    /// The length as an ISO 8601 duration of hours and minutes (<c>PT20M</c>, <c>PT2H20M</c>, <c>PT27H</c>, <c>PT0M</c>), as
    /// schema.org's <c>prepTime</c>, <c>cookTime</c>, and <c>totalTime</c> take it. Hours are never carried into days: an
    /// ISO day is a calendar day, which is not always 24 hours, and an hour always is.
    /// </summary>
    public string IsoDuration
    {
        get
        {
            var hours = Minutes / MinutesPerHour;
            var minutes = Minutes % MinutesPerHour;
            var text = "PT";
            if (hours > 0)
            {
                text += $"{Number(hours)}H";
            }

            if (minutes > 0 || hours == 0)
            {
                text += $"{Number(minutes)}M";
            }

            return text;
        }
    }

    public override string ToString() => Text;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
