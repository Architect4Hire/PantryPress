using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Domain.Managers.Audit;

public static class AuditServiceCollectionExtensions
{
    /// <summary>
    /// <see cref="IAuditLog"/> for recording audited actions (PP1.11), scoped to share the request's context and its site
    /// context (PP2.4). A module whose facades audit calls this itself, so no host can forget it. Needs
    /// <c>PantryPressDbContext</c> and <c>IClock</c> registered by the host.
    /// </summary>
    public static IServiceCollection AddAuditLog(this IServiceCollection services)
    {
        services.AddSiteContext();
        services.TryAddScoped<IAuditLog, AuditLog>();
        return services;
    }
}
