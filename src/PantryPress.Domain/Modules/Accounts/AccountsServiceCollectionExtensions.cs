using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace PantryPress.Domain.Modules.Accounts;

public static class AccountsServiceCollectionExtensions
{
    /// <summary>
    /// Version 2 adds column lengths to Version 1; Version 3 adds passkeys, which ACCT-001 does not include.
    /// </summary>
    public static readonly Version IdentitySchemaVersion = IdentitySchemaVersions.Version2;

    /// <summary>Version 2's key length when none is set; pinned so a host setting cannot widen the key columns.</summary>
    public const int IdentityMaxLengthForKeys = 128;

    /// <summary>
    /// Pins the store options that shape Identity's tables. Identity builds its part of the model from
    /// <see cref="IdentityOptions"/> in the application's services, so every host that creates
    /// <c>PantryPressDbContext</c>, and the design-time factory, registers this or the model would drift from the
    /// migrations. PostConfigure runs after every host Configure, so a host cannot change them by accident. Personal
    /// data protection stays off: it would add a value converter whose output can outgrow the columns. The context
    /// refuses to build its model unless all three match.
    /// </summary>
    public static IServiceCollection AddAccountsIdentitySchema(this IServiceCollection services) =>
        services.PostConfigure<IdentityOptions>(options =>
        {
            options.Stores.SchemaVersion = IdentitySchemaVersion;
            options.Stores.MaxLengthForKeys = IdentityMaxLengthForKeys;
            options.Stores.ProtectPersonalData = false;
        });
}
