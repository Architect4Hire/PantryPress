namespace PantryPress.Domain.Managers.Time;

/// <summary>
/// The application clock. Domain code, jobs, scheduling, and audit read the current time only from here
/// (backend.md); the system clock APIs are banned everywhere else by <c>BannedSymbols.txt</c>.
/// </summary>
public interface IClock
{
    /// <summary>The current instant, always with a zero offset.</summary>
    DateTimeOffset UtcNow { get; }
}
