using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Modules.Accounts;

namespace PantryPress.Domain.Managers.Persistence;

/// <summary>
/// The one EF Core context for PantryPress (backend.md), including the ASP.NET Core Identity stores (ACCT-001).
/// Migrations live in PantryPress.MigrationService and are applied only there (OPS-003).
/// </summary>
public sealed class PantryPressDbContext(DbContextOptions<PantryPressDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        EnsureIdentitySchemaIsPinned();
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(PantryPressDbContext).Assembly);
    }

    // Every other save overload ends in one of these two. The audit log is append-only (PP1.11).
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AuditLog.EnsureAppendOnly(ChangeTracker);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AuditLog.EnsureAppendOnly(ChangeTracker);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Identity reads these from the host's IdentityOptions and silently falls back to Version 1 without them.
    private void EnsureIdentitySchemaIsPinned()
    {
        var stores = this.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
            ?.GetService<IOptions<IdentityOptions>>()?.Value.Stores;

        if (stores is not
            {
                ProtectPersonalData: false,
                MaxLengthForKeys: AccountsServiceCollectionExtensions.IdentityMaxLengthForKeys,
            }
            || stores.SchemaVersion != AccountsServiceCollectionExtensions.IdentitySchemaVersion)
        {
            throw new InvalidOperationException(
                "PantryPressDbContext needs the pinned Identity store options. Call " +
                $"{nameof(AccountsServiceCollectionExtensions.AddAccountsIdentitySchema)}() where the context is registered.");
        }
    }
}
