using PantryPress.Domain.Modules.Platform.Contracts;

namespace PantryPress.Domain.Modules.Platform.Facades;

/// <summary>
/// The seed on a host that is not a Development host (PP3.9a): it does nothing, reads nothing, and logs nothing. This is
/// the second of the three guards. The first is the AppHost, which declares the password parameter in local Development
/// alone, so it reaches no manifest and no deployment; the third is
/// <see cref="DevelopmentSeedOptions.IsAllowedHere"/>, checked when the migration job starts - the only host the setting
/// is given to, and so the only one that could act on it - so it refuses to start rather than ignore it.
/// <para>
/// Nothing the seed needs is registered on such a host either - no Sites module, no account creation, no cache
/// connection - so what a deployed migration job runs is exactly what it ran before PP3.9a.
/// </para>
/// </summary>
internal sealed class DisabledDevelopmentSeeder : IDevelopmentSeeder
{
    public Task<DevelopmentSeedReport> RunAsync(CancellationToken cancellationToken) =>
        Task.FromResult(DevelopmentSeedReport.Disabled);
}
