namespace PantryPress.Domain.Modules.Platform.Contracts;

/// <summary>
/// The starting point a developer's machine gets after migrations (PP3.9a): two accounts with confirmed addresses and
/// two sites the first of them owns, so a local run can be signed into while sign-up is closed (PPL.13). Like
/// <see cref="IPlatformAdminReconciler"/> it runs in the migration job, which the developer starts, and nothing about it
/// is reachable from a browser or an API. It exists only on a Development host: anywhere else the registration binds
/// the implementation that does nothing, and seed configuration makes the migration job - the one host it is given to -
/// refuse to start.
/// </summary>
public interface IDevelopmentSeeder
{
    /// <summary>
    /// Seeds whatever is missing and leaves everything that is there exactly as it is, so a second run changes nothing.
    /// A failure it cannot explain throws, which fails the migration job.
    /// </summary>
    Task<DevelopmentSeedReport> RunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// What one run did. <see cref="AccountsCreated"/> and <see cref="SitesCreated"/> are zero on a second run, which is
/// what makes the seed idempotent.
/// </summary>
public sealed record DevelopmentSeedReport(bool Enabled, bool Configured, int AccountsCreated, int SitesCreated)
{
    /// <summary>Not a Development host: nothing was read and nothing was changed.</summary>
    public static DevelopmentSeedReport Disabled { get; } = new(false, false, 0, 0);

    /// <summary>
    /// A Development host with no usable <c>DevSeed:Password</c>: none set, or one the platform's own password rules
    /// refuse. Nothing was changed either way, and the log says what to set.
    /// </summary>
    public static DevelopmentSeedReport NotConfigured { get; } = new(true, false, 0, 0);
}
