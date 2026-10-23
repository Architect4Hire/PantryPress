using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Platform.Contracts;
using PantryPress.Domain.Modules.Platform.Facades;

namespace PantryPress.Domain.Modules.Platform;

public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers the operator's platform-role administration (ACCT-005; PP1.4): the audited facade, the list
    /// reconciliation, and the narrow Accounts role contract it needs. Only the migration job calls this. No
    /// request-serving host does, which is what makes the assignment path operator-only rather than merely
    /// authorized. The <paramref name="configuration"/> section binds <see cref="PlatformOptions"/>.
    /// </summary>
    public static IServiceCollection AddPlatformAdministration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<PlatformOptions>()
            .Bind(configuration)
            .Validate(o => o.IsValid(), "Platform:Admins must be email addresses separated by commas, semicolons, or new lines.")
            .ValidateOnStart();

        services.AddAccountRoles();
        services.AddScoped<IPlatformAdminFacade, PlatformAdminFacade>();
        services.AddScoped<IPlatformAdminReconciler, PlatformAdminReconciler>();
        return services;
    }
}
