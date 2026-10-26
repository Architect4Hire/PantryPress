using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.ApiService.Http;
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

// PP2.4: an admin request's site comes from its slug and the caller's membership, and from nothing else (tenancy.md rules
// 4 and 7), through two look-alike sites against the migrated database. One account reaches each of its sites as that
// site; a site id sent in the query, a header, or the body changes nothing; a stranger is refused before the body is read;
// and a refusal leaves the request with no site at all.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class SiteContextDatabaseTests(MigratedDatabase database) : IAsyncLifetime
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
            services => services.AddProbes(new IdempotencyProbeLedger(), typeof(SiteContextProbeController)));
        _ = _api.Server;

        var services = new ServiceCollection();
        services.AddAccountsIdentitySchema();
        services.AddPantryPressDbContext((_, options) => options.UseSqlServer(
            connectionString,
            MigrationServiceExtensions.UseMigrationsAssembly));
        services.AddSites();
        _services = services.BuildServiceProvider(validateScopes: true);
    }

    public async Task DisposeAsync()
    {
        await _api.DisposeAsync();
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task One_account_reaches_each_look_alike_site_as_that_site()
    {
        var account = NewAccount();
        var a = await NewSiteAsync("supper", (account, SiteRole.Owner));
        var b = await NewSiteAsync("supper", (account, SiteRole.Author), (NewAccount(), SiteRole.Owner));
        using var client = _api.CreateClientFor(account.Value);

        Assert.Equal(a.Id, await SiteOfAsync(await client.GetAsync(SiteContextProbeController.PathFor(a.Slug))));
        Assert.Equal(b.Id, await SiteOfAsync(await client.GetAsync(SiteContextProbeController.PathFor(b.Slug))));
        Assert.Equal(a.Id, await SiteOfAsync(await client.GetAsync(SiteContextProbeController.PathFor(a.Slug.ToUpperInvariant()))));
    }

    // The account can reach site B, so naming B anywhere but the slug is the one way it could be tempted there.
    [Fact]
    public async Task A_site_id_sent_anywhere_else_changes_nothing()
    {
        var account = NewAccount();
        var a = await NewSiteAsync("supper", (account, SiteRole.Owner));
        var b = await NewSiteAsync("supper", (account, SiteRole.Owner));
        using var client = _api.CreateClientFor(account.Value);
        var other = b.Id.ToString();

        using var read = new HttpRequestMessage(
            HttpMethod.Get,
            $"{SiteContextProbeController.PathFor(a.Slug)}?siteId={other}&site_id={other}&siteSlug={b.Slug}");
        read.Headers.Add("X-PP-Site", other);
        read.Headers.Add("X-PP-Site-Id", other);
        read.Headers.Add("X-Site-Id", other);

        using var write = new HttpRequestMessage(HttpMethod.Post, SiteContextProbeController.PathFor(a.Slug))
        {
            Content = JsonContent.Create(new Dictionary<string, string>
            {
                ["name"] = "lemon-tart",
                ["siteId"] = other,
                ["site_id"] = other,
                ["siteSlug"] = b.Slug,
            }),
        };
        write.Headers.Add("X-PP-Site", other);

        Assert.Equal(a.Id, await SiteOfAsync(await client.SendAsync(read)));
        Assert.Equal(a.Id, await SiteOfAsync(await client.SendAsync(write)));
    }

    // Authorization decides before the body is bound, so a stranger's malformed body cannot tell a real site from none.
    [Fact]
    public async Task A_stranger_is_refused_before_the_body_is_read()
    {
        var owner = NewAccount();
        var stranger = NewAccount();
        var site = await NewSiteAsync("theirs", (owner, SiteRole.Owner));
        using var strangerClient = _api.CreateClientFor(stranger.Value);
        using var ownerClient = _api.CreateClientFor(owner.Value);

        using var real = await strangerClient.SendAsync(MalformedPost(site.Slug));
        using var none = await strangerClient.SendAsync(MalformedPost($"none-{Guid.NewGuid():N}"));
        using var asOwner = await ownerClient.SendAsync(MalformedPost(site.Slug));

        Assert.Equal(HttpStatusCode.NotFound, real.StatusCode);
        Assert.Equal(SiteErrors.SiteNotFound.Code, await real.CodeAsync());
        Assert.Equal(await WithoutTraceAsync(none), await WithoutTraceAsync(real));
        Assert.Equal(HttpStatusCode.BadRequest, asOwner.StatusCode);

        static HttpRequestMessage MalformedPost(string slug) => new(HttpMethod.Post, SiteContextProbeController.PathFor(slug))
        {
            Content = new StringContent("{\"name\":", Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task Authorizing_enters_the_members_site()
    {
        var account = NewAccount();
        var a = await NewSiteAsync("supper", (account, SiteRole.Editor));
        await NewSiteAsync("supper", (account, SiteRole.Owner));
        await using var scope = _services.CreateAsyncScope();

        var decision = await Memberships(scope).AuthorizeAsync(account, a.Slug, SiteRole.Author, Timeout);

        Assert.True(decision.IsSuccess);
        Assert.Equal(a.Id, scope.ServiceProvider.GetRequiredService<ISiteContext>().Current);
    }

    [Fact]
    public async Task A_refusal_leaves_the_request_with_no_site()
    {
        var account = NewAccount();
        var editorOn = await NewSiteAsync("supper", (account, SiteRole.Editor));
        var notOn = await NewSiteAsync("supper", (NewAccount(), SiteRole.Owner));
        await using var scope = _services.CreateAsyncScope();
        var memberships = Memberships(scope);

        var tooLow = await memberships.AuthorizeAsync(account, editorOn.Slug, SiteRole.Owner, Timeout);
        var stranger = await memberships.AuthorizeAsync(account, notOn.Slug, SiteRole.Author, Timeout);
        var missing = await memberships.AuthorizeAsync(account, $"none-{Guid.NewGuid():N}", SiteRole.Author, Timeout);

        Assert.Equal(SiteErrors.SiteRoleRequired, tooLow.Error);
        Assert.Equal(SiteErrors.SiteNotFound, stranger.Error);
        Assert.Equal(SiteErrors.SiteNotFound, missing.Error);
        Assert.Null(scope.ServiceProvider.GetRequiredService<ISiteContext>().Current);
    }

    // Finding a membership is a question, not a resolution: only a decision that admits the caller enters the site.
    // The site policies stacked on one route are one decision: a role that passes one and fails another enters nothing.
    [Fact]
    public async Task A_role_that_meets_only_some_of_a_routes_policies_enters_nothing()
    {
        var account = NewAccount();
        var site = await NewSiteAsync("supper", (account, SiteRole.Editor));
        await using var scope = _services.CreateAsyncScope();

        var decision = await Memberships(scope).AuthorizeAsync(account, site.Slug, [SiteRole.Author, SiteRole.Owner], Timeout);

        Assert.Equal(SiteErrors.SiteRoleRequired, decision.Error);
        Assert.Null(scope.ServiceProvider.GetRequiredService<ISiteContext>().Current);
    }

    // Over HTTP: an account that owns A and is an author on look-alike B is refused B's owner route, and reaches A's.
    [Fact]
    public async Task A_stacked_owner_route_admits_the_owner_and_refuses_the_author_on_the_other_site()
    {
        var account = NewAccount();
        var a = await NewSiteAsync("supper", (account, SiteRole.Owner));
        var b = await NewSiteAsync("supper", (account, SiteRole.Author), (NewAccount(), SiteRole.Owner));
        using var client = _api.CreateClientFor(account.Value);

        using var onB = await client.GetAsync($"{SiteContextProbeController.PathFor(b.Slug)}/owner");

        Assert.Equal(HttpStatusCode.Forbidden, onB.StatusCode);
        Assert.Equal(SiteErrors.SiteRoleRequired.Code, await onB.CodeAsync());
        Assert.Equal(a.Id, await SiteOfAsync(await client.GetAsync($"{SiteContextProbeController.PathFor(a.Slug)}/owner")));
    }

    [Fact]
    public async Task Finding_a_membership_enters_nothing()
    {
        var account = NewAccount();
        var site = await NewSiteAsync("supper", (account, SiteRole.Owner));
        await using var scope = _services.CreateAsyncScope();

        Assert.NotNull(await Memberships(scope).FindAsync(account, site.Slug, Timeout));
        Assert.Null(scope.ServiceProvider.GetRequiredService<ISiteContext>().Current);
    }

    [Fact]
    public async Task One_request_cannot_be_authorized_onto_a_second_site()
    {
        var account = NewAccount();
        var a = await NewSiteAsync("supper", (account, SiteRole.Owner));
        var b = await NewSiteAsync("supper", (account, SiteRole.Owner));
        await using var scope = _services.CreateAsyncScope();
        var memberships = Memberships(scope);

        Assert.True((await memberships.AuthorizeAsync(account, a.Slug, SiteRole.Owner, Timeout)).IsSuccess);
        Assert.True((await memberships.AuthorizeAsync(account, a.Slug.ToUpperInvariant(), SiteRole.Author, Timeout)).IsSuccess);
        await Assert.ThrowsAsync<InvalidOperationException>(() => memberships.AuthorizeAsync(account, b.Slug, SiteRole.Owner, Timeout));

        Assert.Equal(a.Id, scope.ServiceProvider.GetRequiredService<ISiteContext>().Current);
    }

    private static ISiteMembershipFacade Memberships(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ISiteMembershipFacade>();

    private static AccountId NewAccount() => new(Guid.NewGuid());

    private static async Task<SiteId> SiteOfAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var answer = await response.Content.ReadFromJsonAsync<SiteContextProbeAnswer>();
            return new SiteId(answer!.Site);
        }
    }

    private static async Task<string> WithoutTraceAsync(HttpResponseMessage response)
    {
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(problem.Remove("traceId"));
        return problem.ToJsonString();
    }

    // Look-alike sites: the same name and tagline, slugs that share a prefix.
    private async Task<(SiteId Id, string Slug)> NewSiteAsync(string prefix, params (AccountId Account, SiteRole Role)[] members)
    {
        var site = new Site
        {
            Id = new SiteId(Guid.NewGuid()),
            Slug = $"{prefix}-{Guid.NewGuid():N}",
            Name = "Sunday Supper",
            Tagline = "Weeknight dinners",
            Status = SiteStatus.Live,
            TimeZone = "America/Chicago",
            Locale = "en-US",
            CreatedAt = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
        };

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        db.Add(site);
        db.AddRange(members.Select(m => new SiteMember
        {
            SiteId = site.Id,
            AccountId = m.Account,
            Role = m.Role,
            Status = SiteMemberStatus.Active,
            CreatedAt = site.CreatedAt,
        }));
        await db.SaveChangesAsync(Timeout);
        return (site.Id, site.Slug);
    }
}

public sealed record SiteContextProbeAnswer(Guid Site);

/// <summary>A site route that answers with the site its request resolved, read the one way a site route may read it.</summary>
[Route(ApiSurfaces.AdminV1Route + "/sites/{siteSlug}/context-probe")]
[Authorize(Policy = SitePolicies.SiteAuthor)]
public sealed class SiteContextProbeController(ISiteContext site) : ApiControllerBase
{
    public static string PathFor(string slug) => $"/{ApiSurfaces.AdminV1Route}/sites/{slug}/context-probe";

    // The context as an action's service as well as the controller's: a service binds nothing from the request.
    [HttpGet]
    public IActionResult Get([FromServices] ISiteContext context) => Ok(new SiteContextProbeAnswer(context.Required.Value));

    [HttpGet("owner")]
    [Authorize(Policy = SitePolicies.SiteOwner)]
    public IActionResult Owner() => Ok(new SiteContextProbeAnswer(site.Required.Value));

    [HttpPost]
    public IActionResult Post(ProbeItem item) => Ok(new SiteContextProbeAnswer(site.Required.Value));
}
