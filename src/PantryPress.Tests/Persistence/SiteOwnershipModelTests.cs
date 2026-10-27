using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Tests.Api;

namespace PantryPress.Tests.Persistence;

// PP2.5: the model side of the global site filter (tenancy.md rules 1 to 3), and the registration it depends on. No
// database is needed: these read the model and the service registrations only.
public sealed class SiteOwnershipModelTests
{
    private const string Unused = "Server=unused;Database=unused";

    // The written exceptions that carry a site without being site-owned (tenancy.md, "Operator tables" and "Tenant root").
    private static readonly string[] SiteBearingExceptions = ["Site", "SiteMember", "SiteHost", "OutboxMessage", "AuditEntry"];

    [Fact]
    public void Every_site_owned_type_is_filtered_by_site_and_guards_its_site_on_write()
    {
        using var services = Services(o => o.ReplaceService<IModelCustomizer, SiteOwnedProbeCustomizer>());
        using var scope = services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Model;

        var siteOwned = SiteOwnedTypes(model);
        Assert.Equal(["ProbeRecipe", "ProbeStep"], siteOwned.Select(t => t.ClrType.Name).Order());
        Assert.All(siteOwned, AssertSiteGuarded);
    }

    // Holds of the product model as soon as its first site-owned type arrives; until then it guards the exceptions.
    [Fact]
    public void The_product_model_filters_every_site_owned_type_and_nothing_else_that_carries_a_site()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Model;

        Assert.All(SiteOwnedTypes(model), AssertSiteGuarded);

        var siteBearing = model.GetEntityTypes()
            .Where(t => t.GetProperties().Any(p => p.Name == nameof(ISiteOwned.SiteId) || p.ClrType == typeof(SiteId)))
            .ToList();
        Assert.Superset(SiteBearingExceptions.ToHashSet(), siteBearing.Select(t => t.ClrType.Name).Where(n => !IsSiteOwned(model, n)).ToHashSet());
        Assert.All(
            model.GetEntityTypes().Where(t => !typeof(ISiteOwned).IsAssignableFrom(t.ClrType)),
            t => Assert.Empty(t.GetDeclaredQueryFilters()));
    }

    [Fact]
    public void An_owned_site_owned_type_is_refused() =>
        AssertRefused(o => o.ReplaceService<IModelCustomizer, OwnedSiteOwnedCustomizer>(), "owned by another entity");

    [Fact]
    public void A_site_owned_type_under_a_base_that_is_not_is_refused() =>
        AssertRefused(o => o.ReplaceService<IModelCustomizer, DerivedSiteOwnedCustomizer>(), "base type is not");

    // A reference keyed by id alone would let one site's row point at another's.
    [Fact]
    public void A_reference_between_site_owned_types_without_the_site_in_its_key_is_refused() =>
        AssertRefused(o => o.ReplaceService<IModelCustomizer, PlainReferenceCustomizer>(), "without SiteId in the key");

    // Nothing would filter or stamp a dependent that is not site-owned, an implicit join table included.
    [Fact]
    public void A_type_that_depends_on_a_site_owned_one_but_is_not_is_refused() =>
        AssertRefused(o => o.ReplaceService<IModelCustomizer, UnownedDependentCustomizer>(), "is not site-owned itself");

    // A configuration cannot weaken the site rules: a filter of its own under the site filter's name is replaced by the
    // convention's, and turning the concurrency token off is refused.
    [Fact]
    public void A_site_owned_type_cannot_configure_its_own_site_rules()
    {
        using var services = Services(o => o.ReplaceService<IModelCustomizer, OwnSiteFilterCustomizer>());
        using var scope = services.CreateScope();
        AssertSiteGuarded(scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Model.FindEntityType(typeof(ProbeSiteBase))!);

        AssertRefused(o => o.ReplaceService<IModelCustomizer, NoSiteTokenCustomizer>(), "configures its own");
    }

    [Fact]
    public void A_site_owned_type_split_across_tables_is_refused() =>
        AssertRefused(o => o.ReplaceService<IModelCustomizer, TablePerTypeCustomizer>(), "split across tables");

    // One table per hierarchy is fine: the root carries the filter, and every type below it is read through it.
    [Fact]
    public void A_site_owned_hierarchy_in_one_table_is_filtered_at_its_root()
    {
        using var services = Services(o => o.ReplaceService<IModelCustomizer, OneTableHierarchyCustomizer>());
        using var scope = services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Model;

        var root = model.FindEntityType(typeof(ProbeSiteBase))!;
        var derived = model.FindEntityType(typeof(ProbeSiteDerived))!;
        AssertSiteGuarded(root);
        Assert.Same(root, derived.GetRootType());
        Assert.Empty(derived.GetDeclaredQueryFilters());
    }

    // The context reads only the site context a resolver enters: not a stand-in, and not a host's own registration ahead
    // of the kernel's (tenancy.md rule 2).
    [Fact]
    public void The_context_refuses_a_site_context_no_resolver_enters()
    {
        var collection = new ServiceCollection();
        collection.AddScoped<ISiteContext, FixedSite>();
        collection.AddAccountsIdentitySchema();
        collection.AddPantryPressDbContext((_, options) => options.UseSqlServer(Unused));
        using var services = collection.BuildServiceProvider(validateScopes: true);
        using var scope = services.CreateScope();

        var error = Assert.ThrowsAny<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<PantryPressDbContext>());
        Assert.Contains("is not that context", error.ToString(), StringComparison.Ordinal);
    }

    private static void AssertRefused(Action<DbContextOptionsBuilder> customize, string reason)
    {
        using var services = Services(customize);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

        var error = Assert.ThrowsAny<InvalidOperationException>(() => db.Model);
        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    // The context reads its scope's site, so it must never be pooled. EF Core would pool it, handing it a site context
    // from the root provider that every request shares, so the context refuses pooled options itself - from the pool and
    // from a pooled factory alike.
    [Fact]
    public void The_context_cannot_be_pooled()
    {
        var pooled = new ServiceCollection().AddSiteContext();
        pooled.AddAccountsIdentitySchema();
        pooled.AddDbContextPool<PantryPressDbContext>(options => options.UseSqlServer(Unused));
        using var services = pooled.BuildServiceProvider();
        using var scope = services.CreateScope();

        var error = Assert.ThrowsAny<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<PantryPressDbContext>());
        Assert.Contains("never pooled", error.ToString(), StringComparison.Ordinal);

        var factory = new ServiceCollection().AddSiteContext();
        factory.AddAccountsIdentitySchema();
        factory.AddPooledDbContextFactory<PantryPressDbContext>(options => options.UseSqlServer(Unused));
        using var factoryServices = factory.BuildServiceProvider();

        error = Assert.ThrowsAny<InvalidOperationException>(
            () => factoryServices.GetRequiredService<IDbContextFactory<PantryPressDbContext>>().CreateDbContext());
        Assert.Contains("never pooled", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiService_gives_each_request_a_context_bound_to_its_own_site_context()
    {
        await using var api = ApiServiceFactory.WithoutDatabase();

        await using var first = api.Services.CreateAsyncScope();
        await using var second = api.Services.CreateAsyncScope();
        var db = first.ServiceProvider.GetRequiredService<PantryPressDbContext>();

        Assert.Same(first.ServiceProvider.GetRequiredService<ISiteContext>(), ((ISiteScopedDbContext)db).Site);
        Assert.Same(first.ServiceProvider.GetRequiredService<ISiteContextWriter>(), ((ISiteScopedDbContext)db).Site);
        Assert.NotSame(db, second.ServiceProvider.GetRequiredService<PantryPressDbContext>());
        Assert.True(db.Database.CreateExecutionStrategy().RetriesOnFailure);

        // The Aspire client's readiness check survives the move off its pooled registration.
        var checks = api.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.Contains(checks, c => c.Name.Contains(nameof(PantryPressDbContext), StringComparison.Ordinal));
    }

    // The Worker has no test host of its own, so its registration - and every other host's - is read from source: the
    // Aspire client's AddSqlServerDbContext pools, and so do the pool and pooled factory, which would share a site; a
    // plain factory is a singleton that would read the root provider's site context.
    [Fact]
    public void No_host_pools_the_context()
    {
        var source = DomainSource.Project.Parent!;
        var hosts = new[] { "PantryPress.ApiService", "PantryPress.Worker", "PantryPress.MigrationService" };

        foreach (var project in source.GetDirectories("PantryPress.*").Where(d => d.Name != "PantryPress.Tests"))
        {
            var generated = new[] { Path.Combine(project.FullName, "bin"), Path.Combine(project.FullName, "obj") };
            var files = project.EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Where(f => !generated.Any(g => f.FullName.StartsWith(g + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                .Select(f => (f.FullName, Text: File.ReadAllText(f.FullName)))
                .ToList();

            Assert.DoesNotContain(files, f => Regex.IsMatch(f.Text, @"\b(AddSqlServerDbContext|AddDbContextPool|AddPooledDbContextFactory|AddDbContextFactory)\b"));
            if (hosts.Contains(project.Name))
            {
                Assert.Contains(files, f => f.Text.Contains(".AddPantryPressDbContext(", StringComparison.Ordinal));
            }
        }
    }

    private static List<IEntityType> SiteOwnedTypes(IModel model) =>
        model.GetEntityTypes().Where(t => typeof(ISiteOwned).IsAssignableFrom(t.ClrType)).ToList();

    private static bool IsSiteOwned(IModel model, string name) =>
        model.GetEntityTypes().Any(t => t.ClrType.Name == name && typeof(ISiteOwned).IsAssignableFrom(t.ClrType));

    // The filter compares the row's site with the context's, SiteId is required and a concurrency token, every index and
    // every reference to another site-owned type leads with or carries the site (tenancy.md, backend.md).
    private static void AssertSiteGuarded(IEntityType type)
    {
        var filter = type.FindDeclaredQueryFilter(SiteQueryFilterConvention.FilterName);
        Assert.NotNull(filter);
        Assert.Matches(@"^entity => \(entity\.SiteId == .*FilterSite\)$", filter.Expression!.ToString());

        var siteId = type.FindProperty(nameof(ISiteOwned.SiteId))!;
        Assert.False(siteId.IsNullable);
        Assert.True(siteId.IsConcurrencyToken);
        Assert.All(type.GetIndexes(), index => Assert.Equal(nameof(ISiteOwned.SiteId), index.Properties[0].Name));
        Assert.All(
            type.GetForeignKeys().Where(fk => typeof(ISiteOwned).IsAssignableFrom(fk.PrincipalEntityType.ClrType)),
            fk => Assert.Contains(fk.Properties, p => p.Name == nameof(ISiteOwned.SiteId)));
    }

    private static ServiceProvider Services(Action<DbContextOptionsBuilder>? customize = null)
    {
        var services = new ServiceCollection();
        services.AddAccountsIdentitySchema();
        services.AddPantryPressDbContext((_, options) =>
        {
            options.UseSqlServer(Unused);
            customize?.Invoke(options);
        });
        return services.BuildServiceProvider(validateScopes: true);
    }
}

internal sealed class ProbeHolder
{
    public Guid Id { get; set; }

    public ProbePart Part { get; set; } = new();
}

internal sealed class ProbePart : ISiteOwned
{
    public SiteId SiteId { get; set; }

    public string Note { get; set; } = string.Empty;
}

internal class ProbeBase
{
    public Guid Id { get; set; }
}

internal sealed class ProbeDerived : ProbeBase, ISiteOwned
{
    public SiteId SiteId { get; set; }
}

internal sealed class ProbeNote
{
    public Guid Id { get; set; }

    public Guid RecipeId { get; set; }
}

internal class ProbeSiteBase : ISiteOwned
{
    public Guid Id { get; set; }

    public SiteId SiteId { get; set; }
}

internal sealed class ProbeSiteDerived : ProbeSiteBase
{
    public string Extra { get; set; } = string.Empty;
}

internal sealed class FixedSite : ISiteContext
{
    public SiteId? Current { get; } = new SiteId(Guid.NewGuid());

    public SiteId Required => Current!.Value;
}

internal sealed class PlainReferenceCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<ProbeRecipe>().HasMany(r => r.Steps).WithOne().HasForeignKey(s => s.RecipeId);
    }
}

internal sealed class UnownedDependentCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<ProbeRecipe>().Ignore(r => r.Steps);
        modelBuilder.Entity<ProbeNote>().HasOne<ProbeRecipe>().WithMany().HasForeignKey(n => n.RecipeId);
    }
}

internal sealed class OwnSiteFilterCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<ProbeSiteBase>().HasQueryFilter(SiteQueryFilterConvention.FilterName, _ => true);
    }
}

internal sealed class NoSiteTokenCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<ProbeSiteBase>().Property(e => e.SiteId).IsConcurrencyToken(false);
    }
}

internal sealed class TablePerTypeCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<ProbeSiteBase>().UseTptMappingStrategy();
        modelBuilder.Entity<ProbeSiteDerived>();
    }
}

internal sealed class OneTableHierarchyCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<ProbeSiteBase>();
        modelBuilder.Entity<ProbeSiteDerived>();
    }
}

internal sealed class OwnedSiteOwnedCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<ProbeHolder>().OwnsOne(h => h.Part);
    }
}

internal sealed class DerivedSiteOwnedCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<ProbeBase>();
        modelBuilder.Entity<ProbeDerived>();
    }
}
