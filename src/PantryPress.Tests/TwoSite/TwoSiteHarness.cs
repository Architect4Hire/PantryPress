using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.MigrationService;
using PantryPress.Tests.Api;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.TwoSite;

// Inside the namespace, so the types win over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

/// <summary>One of the two look-alike sites and the members it was seeded with.</summary>
internal sealed record SeededSite(
    SiteId Id,
    string Slug,
    AccountId Owner,
    AccountId Editor,
    AccountId Author,
    AccountId Suspended);

/// <summary>
/// Two sites that look alike (tenancy.md, "Two-site test"; PP2.7): the same name and tagline, the same members by role,
/// and slugs one letter apart, A's a prefix of B's, so a match on a prefix or a near miss shows. Three accounts cross the
/// sites: <see cref="Shared"/> is an Author on A and an Owner on B, carrying a different role into each;
/// <see cref="SuspendedOnA"/> is suspended on A and an Editor on B, carrying a different standing into each; and
/// <see cref="Stranger"/> belongs to neither.
/// </summary>
internal sealed record TwoSiteWorld(SeededSite A, SeededSite B, AccountId Shared, AccountId SuspendedOnA, AccountId Stranger);

/// <summary>
/// The two-site harness (PP2.7; ARCH-015, PSEC-007): the services a host registers over the migrated database, two
/// look-alike sites seeded per test with fresh ids so tests share the database safely, scopes that work on one site or
/// as one account, and - when asked for - ApiService signed in as any account. The checks a change owes live in
/// <see cref="TwoSiteChecks"/>.
/// </summary>
internal sealed class TwoSiteHarness : IAsyncDisposable
{
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services;
    private readonly ApiServiceFactory? _api;

    private TwoSiteHarness(ServiceProvider services, ApiServiceFactory? api)
    {
        _services = services;
        _api = api;
    }

    public IServiceProvider Services => _services;

    /// <param name="database">The shared migrated database.</param>
    /// <param name="withApi">Also start ApiService over that database, with the site policy probe routes.</param>
    /// <param name="configure">More context configuration, such as a planted test model.</param>
    public static async Task<TwoSiteHarness> CreateAsync(
        MigratedDatabase database,
        bool withApi = false,
        Action<DbContextOptionsBuilder>? configure = null)
    {
        var connectionString = await database.App.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName, CancellationToken.None)
            ?? throw new InvalidOperationException("The test AppHost has no pantrypress connection string.");

        ApiServiceFactory? api = null;
        if (withApi)
        {
            api = new ApiServiceFactory(
                "Testing",
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:pantrypress"] = connectionString,
                    ["Gateway:SharedSecret"] = TestSecrets.GatewaySecret,
                    ["RateLimits:SignedInAccountPerMinute"] = "100000",
                },
                services => services.AddProbes(new IdempotencyProbeLedger(), typeof(SitePolicyProbeController)));
            _ = api.Server;
        }

        var services = new ServiceCollection();
        services.AddAccountsIdentitySchema();
        services.AddPantryPressDbContext((_, options) =>
        {
            options.UseSqlServer(connectionString, MigrationServiceExtensions.UseMigrationsAssembly);
            configure?.Invoke(options);
        });
        services.AddSites();
        return new TwoSiteHarness(services.BuildServiceProvider(validateScopes: true), api);
    }

    public static AccountId NewAccount() => new(Guid.NewGuid());

    /// <summary>
    /// Seeds two look-alike sites and their members, with ids and slugs no other test shares. The
    /// <paramref name="prefix"/> is a slug's start - lowercase letters, digits, and hyphens, at most 20 - so enough of a
    /// fresh id follows it to keep every test's slugs apart.
    /// </summary>
    public async Task<TwoSiteWorld> SeedAsync(string prefix = "supper")
    {
        if (prefix.Length is 0 or > 20 || !prefix.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-') || !char.IsAsciiLetterLower(prefix[0]))
        {
            throw new ArgumentException("A seed prefix is 1 to 20 lowercase letters, digits, or hyphens, starting with a letter.", nameof(prefix));
        }

        // As alike as two slugs can be: B's is A's with one more letter.
        var stem = $"{prefix}-{Guid.NewGuid():N}";
        stem = stem[..Math.Min(stem.Length, SiteLimits.MaxSlugLength - 1)];
        var (a, aMembers) = NewSite(stem);
        var (b, bMembers) = NewSite(stem + "a");
        var world = new TwoSiteWorld(a, b, NewAccount(), NewAccount(), NewAccount());

        await ChangeAsync(db =>
        {
            db.AddRange(SiteRow(a), SiteRow(b));
            db.AddRange(aMembers.Concat(bMembers));
            db.Add(Member(a.Id, world.Shared, SiteRole.Author));
            db.Add(Member(b.Id, world.Shared, SiteRole.Owner));
            db.Add(Member(a.Id, world.SuspendedOnA, SiteRole.Editor, SiteMemberStatus.Suspended));
            db.Add(Member(b.Id, world.SuspendedOnA, SiteRole.Editor));
        });
        return world;
    }

    /// <summary>A scope working on no site, as an account route or a refused request does.</summary>
    public AsyncServiceScope Scope() => _services.CreateAsyncScope();

    /// <summary>A scope working on <paramref name="site"/>, as a resolver would have entered it.</summary>
    public AsyncServiceScope In(SiteId site) => TwoSiteChecks.In(_services, site);

    /// <summary>ApiService, signed in as <paramref name="account"/>.</summary>
    public HttpClient ClientFor(AccountId account) =>
        (_api ?? throw new InvalidOperationException("This harness was made without ApiService.")).CreateClientFor(account.Value);

    /// <summary>Changes rows directly, past every facade, as a test's setup does: the tenant root, working on no site.</summary>
    public async Task ChangeAsync(Action<PantryPressDbContext> change)
    {
        await using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        change(db);
        await db.SaveChangesAsync();
    }

    /// <summary>Changes rows directly, working on <paramref name="site"/>: site-owned rows are read and stamped as its.</summary>
    public async Task ChangeAsync(SiteId site, Action<PantryPressDbContext> change)
    {
        await using var scope = In(site);
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        change(db);
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        await _services.DisposeAsync();
    }

    private static (SeededSite Site, SiteMember[] Members) NewSite(string slug)
    {
        var site = new SeededSite(new SiteId(Guid.NewGuid()), slug, NewAccount(), NewAccount(), NewAccount(), NewAccount());
        SiteMember[] members =
        [
            Member(site.Id, site.Owner, SiteRole.Owner),
            Member(site.Id, site.Editor, SiteRole.Editor),
            Member(site.Id, site.Author, SiteRole.Author),
            Member(site.Id, site.Suspended, SiteRole.Editor, SiteMemberStatus.Suspended),
        ];
        return (site, members);
    }

    private static Site SiteRow(SeededSite site) => new()
    {
        Id = site.Id,
        Slug = site.Slug,
        Name = "Sunday Supper",
        Tagline = "Weeknight dinners",
        Status = SiteStatus.Live,
        TimeZone = "America/Chicago",
        Locale = "en-US",
        CreatedAt = SeededAt,
    };

    private static SiteMember Member(SiteId site, AccountId account, SiteRole role, SiteMemberStatus status = SiteMemberStatus.Active) => new()
    {
        SiteId = site,
        AccountId = account,
        Role = role,
        Status = status,
        CreatedAt = SeededAt,
    };
}
