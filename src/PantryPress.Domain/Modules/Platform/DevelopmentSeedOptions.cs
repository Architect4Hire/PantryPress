namespace PantryPress.Domain.Modules.Platform;

/// <summary>
/// The local development seed's settings, from the <c>DevSeed</c> configuration section (PP3.9a). The one value is the
/// password the seeded accounts sign in with, which comes from the AppHost's user-secrets and is passed to the
/// migration job alone, exactly as the operator's <see cref="PlatformOptions.Admins"/> list is.
/// </summary>
public sealed class DevelopmentSeedOptions
{
    public const string SectionName = "DevSeed";

    /// <summary>
    /// The password both seeded accounts are created with. Empty or missing means the seed does nothing at all: it
    /// says in one log line how to set it, and nothing else. Never written to code, settings, logs, or telemetry.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Whether this host is a Development host. Not a configuration value, deliberately: it is set from
    /// <c>IHostEnvironment</c> where the seed is registered, so no setting can turn the seed on anywhere else.
    /// </summary>
    public bool Enabled { get; private set; }

    /// <summary>Whether a password was configured at all.</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(Password);

    /// <summary>
    /// False for seed configuration on a host that is not a Development host, which makes the host that bound these
    /// options - the migration job, the only one it is given to - refuse to start rather than ignore the setting. The
    /// third of the three guards: the AppHost declares the parameter in Development alone, and the seed itself does
    /// nothing anywhere else.
    /// </summary>
    public bool IsAllowedHere() => Enabled || !IsConfigured;

    /// <summary>Marks this host a Development host; called only from the registration, never from configuration.</summary>
    internal void MarkDevelopmentHost() => Enabled = true;
}
