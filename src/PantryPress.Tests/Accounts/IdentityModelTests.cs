using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Idempotency;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Accounts;

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

    // Tables later prompts added beside Identity, each on purpose: PP1.9's idempotency keys (Managers/Idempotency).
    private static readonly Type[] OtherEntityTypes =
    [
        typeof(IdempotencyRecord),
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

    // Accounts are platform data (PP-D06): one account belongs to many sites through membership.
    [Fact]
    public void Accounts_are_not_site_owned()
    {
        using var db = new PantryPressDesignTimeFactory().CreateDbContext([]);

        Assert.All(
            db.Model.GetEntityTypes(),
            e => Assert.DoesNotContain(e.GetProperties(), p => p.Name == "SiteId"));
    }

    // EF Core caches the model per context type, so this context gets its own internal services; otherwise it could
    // reuse a model another test built with the pin.
    [Fact]
    public void Context_refuses_a_host_that_did_not_pin_the_identity_schema()
    {
        using var db = new PantryPressDbContext(new DbContextOptionsBuilder<PantryPressDbContext>()
            .UseSqlServer(MigrationServiceExtensions.UseMigrationsAssembly)
            .EnableServiceProviderCaching(false)
            .Options);

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
        services.AddDbContext<PantryPressDbContext>(options => options
            .UseSqlServer(MigrationServiceExtensions.UseMigrationsAssembly)
            .EnableServiceProviderCaching(false));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();

        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Null(db.Model.FindEntityType(typeof(IdentityUserPasskey<Guid>)));
    }
}
