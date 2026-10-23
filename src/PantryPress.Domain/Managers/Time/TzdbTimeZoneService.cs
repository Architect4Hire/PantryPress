using NodaTime;
using NodaTime.TimeZones;

namespace PantryPress.Domain.Managers.Time;

/// <summary>
/// <see cref="ITimeZoneService"/> over the IANA database embedded in NodaTime, so results never depend on the host
/// operating system's time zone data or its local zone.
/// </summary>
public sealed class TzdbTimeZoneService(IClock clock) : ITimeZoneService
{
    private static readonly IDateTimeZoneProvider Zones = DateTimeZoneProviders.Tzdb;

    // Skipped local times shift forward by the gap; repeated local times take the earlier offset.
    private static readonly ZoneLocalMappingResolver SchedulingResolver = Resolvers.LenientResolver;

    public string DatabaseVersion => TzdbDateTimeZoneSource.Default.TzdbVersion;

    public bool IsKnown(string timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && Zones.GetZoneOrNull(timeZoneId) is not null;

    public DateOnly Today(string timeZoneId) =>
        Instant.FromDateTimeOffset(clock.UtcNow).InZone(GetZone(timeZoneId)).Date.ToDateOnly();

    public DateTimeOffset ToLocal(DateTimeOffset instant, string timeZoneId) =>
        Instant.FromDateTimeOffset(instant).InZone(GetZone(timeZoneId)).ToDateTimeOffset();

    public DateTimeOffset ToUtc(DateOnly date, TimeOnly time, string timeZoneId)
    {
        var local = LocalDate.FromDateOnly(date) + LocalTime.FromTimeOnly(time);
        return local.InZone(GetZone(timeZoneId), SchedulingResolver).ToInstant().ToDateTimeOffset();
    }

    // Stored zone ids are validated with IsKnown when they are saved, so an unknown id here is a bug.
    private static DateTimeZone GetZone(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        return Zones.GetZoneOrNull(timeZoneId)
            ?? throw new ArgumentException($"'{timeZoneId}' is not a known IANA time zone id.", nameof(timeZoneId));
    }
}
