using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using PantryPress.Domain.Managers.Time;

namespace PantryPress.Tests.Managers.Time;

// PP0.5: site time zones for display and scheduling, including daylight saving boundaries (POST-005).
// 2026 transitions used below:
//   America/Chicago      forward Mar 8 02:00 -> 03:00, back Nov 1 02:00 -> 01:00 (-06:00 / -05:00)
//   Europe/London        forward Mar 29 01:00 -> 02:00, back Oct 25 02:00 -> 01:00 (+00:00 / +01:00)
//   Australia/Sydney     back Apr 5 03:00 -> 02:00, forward Oct 4 02:00 -> 03:00 (+10:00 / +11:00)
//   Australia/Lord_Howe  back Apr 5 02:00 -> 01:30, forward Oct 4 02:00 -> 02:30 (+10:30 / +11:00)
public sealed class TimeZoneServiceTests
{
    private const string Chicago = "America/Chicago";

    private static TzdbTimeZoneService CreateService(DateTimeOffset now) =>
        new(new TimeProviderClock(new FakeTimeProvider(now)));

    private static readonly TzdbTimeZoneService Service = CreateService(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(Chicago, "2026-03-08", "02:30", "2026-03-08T08:30:00Z")]
    [InlineData(Chicago, "2026-03-08", "02:00", "2026-03-08T08:00:00Z")]
    [InlineData("Europe/London", "2026-03-29", "01:30", "2026-03-29T01:30:00Z")]
    [InlineData("Australia/Sydney", "2026-10-04", "02:30", "2026-10-03T16:30:00Z")]
    [InlineData("Australia/Lord_Howe", "2026-10-04", "02:15", "2026-10-03T15:45:00Z")]
    public void Skipped_local_time_moves_forward_by_the_gap(string zone, string date, string time, string expectedUtc)
    {
        Assert.Equal(Utc(expectedUtc), Service.ToUtc(Date(date), Time(time), zone));
    }

    [Theory]
    [InlineData(Chicago, "2026-11-01", "01:30", "2026-11-01T06:30:00Z")]
    [InlineData(Chicago, "2026-11-01", "01:00", "2026-11-01T06:00:00Z")]
    [InlineData("Europe/London", "2026-10-25", "01:30", "2026-10-25T00:30:00Z")]
    [InlineData("Australia/Sydney", "2026-04-05", "02:30", "2026-04-04T15:30:00Z")]
    [InlineData("Australia/Lord_Howe", "2026-04-05", "01:45", "2026-04-04T14:45:00Z")]
    public void Repeated_local_time_resolves_to_its_first_occurrence(
        string zone, string date, string time, string expectedUtc)
    {
        Assert.Equal(Utc(expectedUtc), Service.ToUtc(Date(date), Time(time), zone));
    }

    [Theory]
    [InlineData("2026-03-08", "01:59", "2026-03-08T07:59:00Z")]
    [InlineData("2026-03-08", "03:00", "2026-03-08T08:00:00Z")]
    [InlineData("2026-11-01", "00:59", "2026-11-01T05:59:00Z")]
    [InlineData("2026-11-01", "02:00", "2026-11-01T08:00:00Z")]
    [InlineData("2026-07-04", "09:00", "2026-07-04T14:00:00Z")]
    [InlineData("2026-12-25", "09:00", "2026-12-25T15:00:00Z")]
    public void Unambiguous_local_time_uses_the_offset_in_effect(string date, string time, string expectedUtc)
    {
        Assert.Equal(Utc(expectedUtc), Service.ToUtc(Date(date), Time(time), Chicago));
    }

    [Fact]
    public void ToUtc_returns_a_zero_offset()
    {
        Assert.Equal(TimeSpan.Zero, Service.ToUtc(Date("2026-07-04"), Time("09:00"), Chicago).Offset);
    }

    [Theory]
    [InlineData("2026-03-08T07:59:00Z", "2026-03-08T01:59:00-06:00")]
    [InlineData("2026-03-08T08:00:00Z", "2026-03-08T03:00:00-05:00")]
    [InlineData("2026-11-01T06:30:00Z", "2026-11-01T01:30:00-05:00")]
    [InlineData("2026-11-01T07:30:00Z", "2026-11-01T01:30:00-06:00")]
    public void ToLocal_carries_the_offset_on_each_side_of_a_transition(string instant, string expectedLocal)
    {
        var local = Service.ToLocal(Utc(instant), Chicago);

        var expected = DateTimeOffset.Parse(expectedLocal, CultureInfo.InvariantCulture);
        Assert.Equal(expected.DateTime, local.DateTime);
        Assert.Equal(expected.Offset, local.Offset);
    }

    [Fact]
    public void Every_quarter_hour_on_transition_days_round_trips_except_the_skipped_hour()
    {
        foreach (var date in new[] { Date("2026-03-08"), Date("2026-11-01") })
        {
            for (var minutes = 0; minutes < 24 * 60; minutes += 15)
            {
                var time = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes));
                var local = Service.ToLocal(Service.ToUtc(date, time, Chicago), Chicago);

                var skipped = date == Date("2026-03-08") && time.Hour == 2;
                var expected = date.ToDateTime(skipped ? time.AddHours(1) : time);
                Assert.Equal(expected, local.DateTime);
            }
        }
    }

    [Theory]
    [InlineData("Pacific/Pago_Pago", "2026-10-22")]
    [InlineData("Europe/London", "2026-10-23")]
    [InlineData("Pacific/Kiritimati", "2026-10-24")]
    public void Today_is_the_local_date_at_the_fixed_instant(string zone, string expected)
    {
        var service = CreateService(Utc("2026-10-23T10:30:00Z"));

        Assert.Equal(Date(expected), service.Today(zone));
    }

    [Fact]
    public void Today_changes_at_local_midnight_not_utc_midnight()
    {
        var time = new FakeTimeProvider(Utc("2026-03-08T05:59:00Z"));
        var service = new TzdbTimeZoneService(new TimeProviderClock(time));

        Assert.Equal(Date("2026-03-07"), service.Today(Chicago));

        time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(Date("2026-03-08"), service.Today(Chicago));
    }

    [Theory]
    [InlineData("America/Chicago", true)]
    [InlineData("Australia/Lord_Howe", true)]
    [InlineData("Etc/UTC", true)]
    [InlineData("UTC", true)]
    [InlineData("Central Standard Time", false)]
    [InlineData("america/chicago", false)]
    [InlineData("Mars/Olympus_Mons", false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    public void Only_iana_ids_matching_exactly_by_case_are_known(string id, bool known)
    {
        Assert.Equal(known, Service.IsKnown(id));
    }

    [Fact]
    public void Unknown_zone_is_a_bug_not_a_silent_fallback_to_utc()
    {
        Assert.Throws<ArgumentException>(() => Service.ToLocal(Utc("2026-03-08T08:00:00Z"), "Central Standard Time"));
        Assert.Throws<ArgumentException>(() => Service.ToUtc(Date("2026-03-08"), Time("09:00"), "Mars/Olympus_Mons"));
        Assert.Throws<ArgumentException>(() => Service.Today(""));
    }

    [Fact]
    public void Database_version_is_reported()
    {
        Assert.Matches(@"^\d{4}[a-z]$", Service.DatabaseVersion);
    }

    private static DateOnly Date(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static TimeOnly Time(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    private static DateTimeOffset Utc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
