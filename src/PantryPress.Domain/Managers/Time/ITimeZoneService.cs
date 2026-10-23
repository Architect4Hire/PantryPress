namespace PantryPress.Domain.Managers.Time;

/// <summary>
/// Converts between instants and a site's local time. Time zones are IANA ids ("America/Chicago"); Windows ids are
/// not accepted. Instants are stored in UTC; local time is for display and scheduling only (api-contract.md).
/// </summary>
public interface ITimeZoneService
{
    /// <summary>The version of the time zone database in use, e.g. "2025b".</summary>
    string DatabaseVersion { get; }

    /// <summary>True when <paramref name="timeZoneId"/> is a known IANA time zone id.</summary>
    bool IsKnown(string timeZoneId);

    /// <summary>The local date in the zone at the current instant.</summary>
    DateOnly Today(string timeZoneId);

    /// <summary>The instant as local time in the zone, carrying the zone's offset at that instant.</summary>
    DateTimeOffset ToLocal(DateTimeOffset instant, string timeZoneId);

    /// <summary>
    /// The UTC instant for a local date and time in the zone, as used for scheduling. Daylight saving transitions
    /// resolve deterministically:
    /// <list type="bullet">
    /// <item>A time skipped when clocks go forward moves forward by the length of the gap (02:30 becomes 03:30).</item>
    /// <item>A time repeated when clocks go back resolves to its first occurrence.</item>
    /// </list>
    /// </summary>
    DateTimeOffset ToUtc(DateOnly date, TimeOnly time, string timeZoneId);
}
