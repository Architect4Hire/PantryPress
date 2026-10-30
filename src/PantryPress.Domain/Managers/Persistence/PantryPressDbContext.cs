using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts;

namespace PantryPress.Domain.Managers.Persistence;

/// <summary>
/// The one EF Core context for PantryPress (backend.md), including the ASP.NET Core Identity stores (ACCT-001).
/// Migrations live in PantryPress.MigrationService and are applied only there (OPS-003).
/// </summary>
/// <remarks>
/// One per scope, never pooled (PP2.5): it reads its scope's <see cref="ISiteContext"/> for the global site filter and
/// the <see cref="ISiteOwned.SiteId"/> stamp (tenancy.md rules 2 and 3). A pooled context outlives its request, and EF
/// Core would hand it a site context from the root provider - one site shared by every request - so it refuses to be
/// built from pooled options, and it takes only the kernel's own site context, the one a resolver enters. Register it
/// with <see cref="PersistenceServiceCollectionExtensions.AddPantryPressDbContext"/>.
/// The save guards differ in kind. The audit log's, reference data's, and append-only rows' sit in the save overrides
/// because they are about this context's own tables (the bulk halves are interceptors beside them); the site's is an
/// interceptor in <c>Managers/Tenancy</c>, beside the filter it completes, so the kernel's tenancy rules live in one place
/// and never name this context. The model EF Core caches keeps the first context it was
/// built with as the filter's target, and every query swaps in the context running it.
/// </remarks>
public sealed class PantryPressDbContext(DbContextOptions<PantryPressDbContext> options, ISiteContext siteContext)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), ISiteScopedDbContext
{
    // EF Core marks the options of a pooled context, or of a pooled factory's, with the pool's size.
    private readonly ISiteContext _siteContext = options.FindExtension<CoreOptionsExtension>()?.MaxPoolSize is null
        ? ResolvedSiteContext.Require(siteContext)
        : throw new InvalidOperationException(
            "PantryPressDbContext is never pooled: it reads its own scope's site. Register it with AddPantryPressDbContext.");

    ISiteContext ISiteScopedDbContext.Site => _siteContext;

    // The site filter's value. EF Core reads it from the context running each query, never the one the model was built
    // with, and it throws when there is no site.
    private SiteId FilterSite => _siteContext.Required;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(
            SiteStampInterceptor.Instance,
            SiteIdUpdateGuard.Instance,
            ReferenceDataGuard.Instance,
            AppendOnlyGuard.Instance);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<SiteId>().HaveConversion<SiteIdConverter>();
        configurationBuilder.Conventions.Add(_ => new SiteQueryFilterConvention(() => FilterSite));
        configurationBuilder.Conventions.Replace<ForeignKeyIndexConvention>(
            services => new SiteForeignKeyIndexConvention(services.GetRequiredService<ProviderConventionSetBuilderDependencies>()));
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        EnsureIdentitySchemaIsPinned();
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(PantryPressDbContext).Assembly);
    }

    // Every other save overload ends in one of these two. The audit log is append-only (PP1.11), platform reference data
    // is read-only (PP4.1), and so is every other append-only row once written (PP4.4).
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AuditLog.EnsureAppendOnly(ChangeTracker);
        ReferenceDataGuard.EnsureUnchanged(ChangeTracker);
        AppendOnlyGuard.EnsureUnchanged(ChangeTracker);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AuditLog.EnsureAppendOnly(ChangeTracker);
        ReferenceDataGuard.EnsureUnchanged(ChangeTracker);
        AppendOnlyGuard.EnsureUnchanged(ChangeTracker);
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
