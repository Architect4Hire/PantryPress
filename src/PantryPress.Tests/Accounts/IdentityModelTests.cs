using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Idempotency;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Recipes;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Accounts;

// Inside the namespace, so the type wins over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

// PP1.1: the Identity stores in PantryPressDbContext (ACCT-001, auth.md). No database is needed.
public sealed class IdentityModelTests
{
    private static readonly Type[] IdentityEntityTypes =
    [
        typeof(ApplicationUser),
        typeof(IdentityRole<Guid>),
        typeof(IdentityRoleClaim<Guid>),
        typeof(IdentityUserClaim<Guid>),
        typeof(IdentityUserLogin<Guid>),
        typeof(IdentityUserRole<Guid>),
        typeof(IdentityUserToken<Guid>),
    ];

    // Tables later prompts added beside Identity, each on purpose: PP1.9's idempotency keys (Managers/Idempotency),
    // PP1.10's transactional outbox (Managers/Outbox), PP1.11's audit log (Managers/Audit), PP1.12's signed-out
    // sessions (Modules/Accounts), PP2.2's site, PP2.3's memberships, and PP2.9's hosts, the tenant root (Modules/Sites),
    // PP2.8's reserved slugs, platform reference data (Modules/Sites), PP2.11's invitations, the first site-owned
    // table (Modules/Sites), and PP4.1's recipe vocabulary, platform reference data (Modules/Recipes).
    private static readonly Type[] OtherEntityTypes =
    [
        typeof(IdempotencyRecord),
        typeof(OutboxMessage),
        typeof(AuditEntry),
        typeof(RevokedSession),
        typeof(Site),
        typeof(SiteMember),
        typeof(SiteHost),
        typeof(ReservedSlug),
        typeof(SiteInvitation),
        typeof(Unit),
        typeof(UnitAlias),
        typeof(Course),
        typeof(Cuisine),
        typeof(Diet),
        typeof(Allergen),
        typeof(Recipe),
        typeof(RecipeIngredientGroup),
        typeof(RecipeIngredient),
        typeof(RecipeStepGroup),
        typeof(RecipeStep),
        typeof(RecipeEquipment),
        typeof(RecipeNote),
        typeof(RecipeKeyword),
        typeof(RecipeCourse),
        typeof(RecipeCuisine),
        typeof(RecipeDiet),
        typeof(RecipeAllergen),
    ];

    [Fact]
    public void Model_holds_the_identity_stores_and_nothing_undeclared()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        Assert.Equal(
            IdentityEntityTypes.Concat(OtherEntityTypes).Select(t => t.FullName).Order(),
            db.Model.GetEntityTypes().Select(e => e.ClrType.FullName).Order());
    }

    [Fact]
    public void Schema_is_version_2_with_bounded_keys_and_no_passkeys()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        Assert.Null(db.Model.FindEntityType(typeof(IdentityUserPasskey<Guid>)));
        Assert.Equal(256, db.Model.FindEntityType(typeof(ApplicationUser))!.FindProperty("PhoneNumber")!.GetMaxLength());
        Assert.Equal(128, db.Model.FindEntityType(typeof(IdentityUserLogin<Guid>))!.FindProperty("ProviderKey")!.GetMaxLength());
        Assert.Equal(128, db.Model.FindEntityType(typeof(IdentityUserToken<Guid>))!.FindProperty("Name")!.GetMaxLength());
    }

    [Fact]
    public void Normalized_email_is_unique_in_the_database()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        var user = db.Model.FindEntityType(typeof(ApplicationUser))!;

        var index = Assert.Single(
            user.GetIndexes(),
            i => i.Properties.Select(p => p.Name).SequenceEqual(["NormalizedEmail"]));
        Assert.True(index.IsUnique);
        Assert.Equal("EmailIndex", index.GetDatabaseName());
        Assert.Equal("[NormalizedEmail] IS NOT NULL", index.GetFilter());
    }

    // Accounts are platform data (PP-D06): one account belongs to many sites through membership. Two tables carry a
    // SiteId that they do not own by, so the column is optional and nothing filters on it: the outbox (PP1.10), whose
    // dispatcher reads every site's messages and hands each its site, and the audit log (PP1.11), which the operator reads
    // across sites (OPS-006). Membership (PP2.3) and a site's hosts (PP2.9) carry a required one and no filter either:
    // one grants a site and the other names it from a Host, so both are read before any site is known (tenancy.md,
    // "Tenant root").
    [Fact]
    public void Accounts_are_not_site_owned()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);
        Type[] siteCarrying = [typeof(OutboxMessage), typeof(AuditEntry)];
        Type[] membership = [typeof(SiteMember), typeof(SiteHost)];

        // Site-owned rows (PP2.11's invitations first) carry their site by rule; SiteOwnershipModelTests holds them to it.
        Assert.All(
            db.Model.GetEntityTypes().Where(e => !siteCarrying.Contains(e.ClrType) && !membership.Contains(e.ClrType)
                && !typeof(ISiteOwned).IsAssignableFrom(e.ClrType)),
            e => Assert.DoesNotContain(e.GetProperties(), p => p.Name == "SiteId"));
        Assert.All(siteCarrying, type =>
        {
            var entity = db.Model.FindEntityType(type)!;
            Assert.True(entity.FindProperty("SiteId")!.IsNullable);
            Assert.Empty(entity.GetDeclaredQueryFilters());
        });
        Assert.All(membership, type =>
        {
            var entity = db.Model.FindEntityType(type)!;
            Assert.False(entity.FindProperty("SiteId")!.IsNullable);
            Assert.Empty(entity.GetDeclaredQueryFilters());
        });
    }

    // EF Core caches the model per context type, so this context gets its own internal services; otherwise it could
    // reuse a model another test built with the pin. Asking for its own is what EF Core counts, and it counts across the
    // whole test process - every test host builds one - so the count is ignored here: it says nothing about the pin, and
    // without this the refusal under test is replaced by a warning about how many hosts other tests happened to build.
    [Fact]
    public void Context_refuses_a_host_that_did_not_pin_the_identity_schema()
    {
        using var db = new PantryPressDbContext(new DbContextOptionsBuilder<PantryPressDbContext>()
            .UseSqlServer(MigrationServiceExtensions.UseMigrationsAssembly)
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options,
            new SiteContext());

        var error = Assert.Throws<InvalidOperationException>(() => db.Model);
        Assert.Contains(nameof(AccountsServiceCollectionExtensions.AddAccountsIdentitySchema), error.Message, StringComparison.Ordinal);
    }

    // The pin runs after every host Configure, so a host setting cannot reshape the tables.
    [Fact]
    public async Task Host_identity_settings_cannot_change_the_pinned_schema()
    {
        var services = new ServiceCollection();
        services.AddAccountsIdentitySchema();
        services.Configure<IdentityOptions>(options =>
        {
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            options.Stores.MaxLengthForKeys = 450;
            options.Stores.ProtectPersonalData = true;
        });
        services.AddPantryPressDbContext((_, options) => options
            .UseSqlServer(MigrationServiceExtensions.UseMigrationsAssembly)

            // Its own internal services, so another test's model cannot stand in - and the count of those, which EF Core
            // keeps for the whole test process, says nothing about the pin this test is about.
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Null(db.Model.FindEntityType(typeof(IdentityUserPasskey<Guid>)));
    }
}
