namespace PantryPress.Domain.Managers.Time;

/// <summary>
/// <see cref="IClock"/> over a <see cref="TimeProvider"/>: the system provider in hosts, a fake one in tests.
/// Timers and delays in jobs take the same <see cref="TimeProvider"/>, so a test controls both.
/// </summary>
public sealed class TimeProviderClock(TimeProvider timeProvider) : IClock
{
#pragma warning disable RS0030 // The one reader of TimeProvider's time; everything else reads IClock.
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow().ToUniversalTime();
#pragma warning restore RS0030
}
