using System.Net;
using System.Text.Json.Nodes;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.MigrationService;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.Api;

// Inside the namespace, so the types win over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

// PP2.3: the site policies over HTTP against the real membership lookup and the migrated database, through two sites
// that look alike (tenancy.md, "Two-site test"). A stranger's answer for a real site is the answer for no site at all,
// down to the headers; a suspended member and a deleted site reach nothing; one account holds its own role on each site.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class SitePolicyDatabaseTests(MigratedDatabase database) : IAsyncLifetime
{
    private ApiServiceFactory _api = null!;
    private ServiceProvider _services = null!;

    private static CancellationToken Timeout => CancellationToken.None;

    public async Task InitializeAsync()
    {
        var connectionString = await database.App.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName, Timeout)
            ?? throw new InvalidOperationException("The test AppHost has no pantrypress connection string.");

        _api = new ApiServiceFactory(
            "Testing",
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:pantrypress"] = connectionString,
                ["Gateway:SharedSecret"] = TestSecrets.GatewaySecret,
                ["RateLimits:SignedInAccountPerMinute"] = "100000",
            },
            services => services.AddProbes(new IdempotencyProbeLedger(), typeof(SitePolicyProbeController)));
        _ = _api.Server;

        var services = new ServiceCollection();
        services.AddAccountsIdentitySchema();
        services.AddDbContext<PantryPressDbContext>(options => options.UseSqlServer(
            connectionString,
            MigrationServiceExtensions.UseMigrationsAssembly));
        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _api.DisposeAsync();
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task One_account_holds_its_own_role_on_each_look_alike_site()
    {
        var account = NewAccount();
        var a = await NewSiteAsync("supper", (account, SiteRole.Owner));
        var b = await NewSiteAsync("supper", (account, SiteRole.Author), (NewAccount(), SiteRole.Owner));
        using var client = _api.CreateClientFor(account.Value);

        using var ownerOnA = await client.GetAsync(SitePolicyProbeController.PathFor(a, "owner"));
        using var ownerOnB = await client.GetAsync(SitePolicyProbeController.PathFor(b, "owner"));
        using var authorOnB = await client.GetAsync(SitePolicyProbeController.PathFor(b, "author"));

        Assert.Equal(HttpStatusCode.NoContent, ownerOnA.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ownerOnB.StatusCode);
        Assert.Equal(SiteErrors.SiteRoleRequired.Code, await ownerOnB.CodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, authorOnB.StatusCode);
    }

    [Fact]
    public async Task A_real_site_and_no_site_answer_a_stranger_alike()
    {
        var stranger = NewAccount();
        await NewSiteAsync("mine", (stranger, SiteRole.Owner));
        var other = await NewSiteAsync("theirs", (NewAccount(), SiteRole.Owner));
        using var client = _api.CreateClientFor(stranger.Value);

        using var real = await client.GetAsync(SitePolicyProbeController.PathFor(other, "author"));
        using var none = await client.GetAsync(SitePolicyProbeController.PathFor($"none-{Guid.NewGuid():N}", "author"));

        await AssertSameNotFoundAsync(real, none);
    }

    [Fact]
    public async Task A_suspended_member_and_a_deleted_site_reach_nothing()
    {
        var owner = NewAccount();
        var suspended = NewAccount();
        var slug = await NewSiteAsync("gone", (owner, SiteRole.Owner), (suspended, SiteRole.Editor));
        await ChangeAsync(db => db.Set<SiteMember>().Single(m => m.AccountId == suspended).Status = SiteMemberStatus.Suspended);
        using var ownerClient = _api.CreateClientFor(owner.Value);
        using var suspendedClient = _api.CreateClientFor(suspended.Value);

        using (var asSuspended = await suspendedClient.GetAsync(SitePolicyProbeController.PathFor(slug, "author")))
        using (var none = await suspendedClient.GetAsync(SitePolicyProbeController.PathFor($"none-{Guid.NewGuid():N}", "author")))
        {
            await AssertSameNotFoundAsync(asSuspended, none);
        }

        await ChangeAsync(db => db.Set<Site>().Single(s => s.Slug == slug).Status = SiteStatus.Deleted);

        using var asOwner = await ownerClient.GetAsync(SitePolicyProbeController.PathFor(slug, "author"));
        Assert.Equal(HttpStatusCode.NotFound, asOwner.StatusCode);
        Assert.Equal(SiteErrors.SiteNotFound.Code, await asOwner.CodeAsync());
    }

    [Fact]
    public async Task A_slug_in_another_case_names_the_same_site()
    {
        var owner = NewAccount();
        var slug = await NewSiteAsync("case", (owner, SiteRole.Owner));
        using var client = _api.CreateClientFor(owner.Value);

        using var response = await client.GetAsync(SitePolicyProbeController.PathFor(slug.ToUpperInvariant(), "owner"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static AccountId NewAccount() => new(Guid.NewGuid());

    // Two not-found answers that differ only in their trace ids, which are each request's own.
    private static async Task AssertSameNotFoundAsync(HttpResponseMessage first, HttpResponseMessage second)
    {
        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(SiteErrors.SiteNotFound.Code, await first.CodeAsync());
        Assert.Equal(await WithoutTraceAsync(second), await WithoutTraceAsync(first));
        Assert.Equal(Headers(second), Headers(first));
    }

    private static async Task<string> WithoutTraceAsync(HttpResponseMessage response)
    {
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(problem.Remove("traceId"));
        return problem.ToJsonString();
    }

    private static string Headers(HttpResponseMessage response) => string.Join(
        "\n",
        response.Headers.Concat(response.Content.Headers)
            .Where(h => h.Key is not ("Date" or "Content-Length"))
            .OrderBy(h => h.Key, StringComparer.Ordinal)
            .Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));

    // Look-alike sites: the same name and tagline, slugs that share a prefix.
    private async Task<string> NewSiteAsync(string prefix, params (AccountId Account, SiteRole Role)[] members)
    {
        var slug = $"{prefix}-{Guid.NewGuid():N}";
        var site = new Site
        {
            Id = new SiteId(Guid.NewGuid()),
            Slug = slug,
            Name = "Sunday Supper",
            Tagline = "Weeknight dinners",
            Status = SiteStatus.Live,
            TimeZone = "America/Chicago",
            Locale = "en-US",
            CreatedAt = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
        };
        await ChangeAsync(db =>
        {
            db.Add(site);
            db.AddRange(members.Select(m => new SiteMember
            {
                SiteId = site.Id,
                AccountId = m.Account,
                Role = m.Role,
                Status = SiteMemberStatus.Active,
                CreatedAt = site.CreatedAt,
            }));
        });
        return slug;
    }

    private async Task ChangeAsync(Action<PantryPressDbContext> change)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        change(db);
        await db.SaveChangesAsync(Timeout);
    }
}
