using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Tests.Api;

// PP2.3: the site policies (SITE-010, auth.md) in the API host, over a stand-in membership lookup; the real lookup is
// SitePolicyDatabaseTests'. No site route exists yet (PP2.10), so a probe stands in for one.
public sealed class SitePolicyTests : IAsyncLifetime
{
    private const string Slug = "sunday-supper";
    private const string OtherSlug = "sunday-supper-2";

    private static readonly SiteId Site = new(Guid.NewGuid());

    private readonly Memberships _memberships = new();
    private ApiServiceFactory _api = null!;

    public Task InitializeAsync()
    {
        _api = ApiServiceFactory.WithoutDatabase(configureServices: services =>
        {
            services.AddProbes(new IdempotencyProbeLedger(), typeof(SitePolicyProbeController));
            services.Replace(ServiceDescriptor.Singleton<ISiteMembershipFacade>(_memberships));
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    public static TheoryData<SiteRole, string, HttpStatusCode> Matrix => new()
    {
        { SiteRole.Owner, "owner", HttpStatusCode.NoContent },
        { SiteRole.Owner, "editor", HttpStatusCode.NoContent },
        { SiteRole.Owner, "author", HttpStatusCode.NoContent },
        { SiteRole.Editor, "owner", HttpStatusCode.Forbidden },
        { SiteRole.Editor, "editor", HttpStatusCode.NoContent },
        { SiteRole.Editor, "author", HttpStatusCode.NoContent },
        { SiteRole.Author, "owner", HttpStatusCode.Forbidden },
        { SiteRole.Author, "editor", HttpStatusCode.Forbidden },
        { SiteRole.Author, "author", HttpStatusCode.NoContent },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Each_policy_admits_its_role_and_those_above(SiteRole role, string route, HttpStatusCode expected)
    {
        var account = _memberships.Add(Slug, role);
        using var client = _api.CreateClientFor(account.Value);

        using var response = await client.GetAsync(SitePolicyProbeController.PathFor(Slug, route));

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Forbidden)
        {
            Assert.Equal(SiteErrors.SiteRoleRequired.Code, await response.CodeAsync());
        }
    }

    // A stranger learns nothing from a slug: a site they are not a member of answers exactly as one that does not exist.
    [Fact]
    public async Task Not_a_member_and_no_such_site_are_one_answer()
    {
        var account = _memberships.Add(Slug, SiteRole.Owner);
        using var client = _api.CreateClientFor(account.Value);

        using var notMember = await client.GetAsync(SitePolicyProbeController.PathFor(OtherSlug, "author"));
        using var missing = await client.GetAsync(SitePolicyProbeController.PathFor("no-such-site", "author"));

        Assert.Equal(HttpStatusCode.NotFound, notMember.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(await WithoutTraceAsync(missing), await WithoutTraceAsync(notMember));
        Assert.Equal(SiteErrors.SiteNotFound.Code, await missing.CodeAsync());
    }

    // The trace id is each request's own; everything else in the two answers must match.
    private static async Task<string> WithoutTraceAsync(HttpResponseMessage response)
    {
        var problem = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(problem.Remove("traceId"));
        return problem.ToJsonString();
    }

    [Fact]
    public async Task An_anonymous_caller_is_asked_to_sign_in_and_nothing_is_looked_up()
    {
        using var client = _api.CreateClientFor(null);

        using var response = await client.GetAsync(SitePolicyProbeController.PathFor(Slug, "author"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _memberships.Lookups);
    }

    /// <summary>
    /// Memberships by slug, deciding as the real facade does, counting each question. The rule itself is
    /// SiteRolesTests'; the real lookup over HTTP is SitePolicyDatabaseTests'.
    /// </summary>
    private sealed class Memberships : ISiteMembershipFacade
    {
        private readonly ConcurrentDictionary<(AccountId, string), SiteMembership> _held = new();
        private int _lookups;

        public int Lookups => _lookups;

        public AccountId Add(string slug, SiteRole role)
        {
            var account = new AccountId(Guid.NewGuid());
            _held[(account, slug)] = new SiteMembership(Site, role);
            return account;
        }

        public Task<SiteMembership?> FindAsync(AccountId account, string siteSlug, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _lookups);
            return Task.FromResult(_held.TryGetValue((account, siteSlug), out var membership) ? membership : null);
        }

        public async Task<Result<SiteMembership>> AuthorizeAsync(
            AccountId account,
            string siteSlug,
            SiteRole leastRole,
            CancellationToken cancellationToken)
        {
            if (await FindAsync(account, siteSlug, cancellationToken) is not { } membership)
            {
                return SiteErrors.SiteNotFound;
            }

            return SiteRoles.Covers(membership.Role, leastRole) ? membership : SiteErrors.SiteRoleRequired;
        }
    }
}

[Route(ApiSurfaces.AdminV1Route + "/sites/{siteSlug}/policy-probe")]
[Authorize(Policy = SitePolicies.SiteAuthor)]
public sealed class SitePolicyProbeController : ApiControllerBase
{
    public static string PathFor(string slug, string route) => $"/{ApiSurfaces.AdminV1Route}/sites/{slug}/policy-probe/{route}";

    [HttpGet("owner")]
    [Authorize(Policy = SitePolicies.SiteOwner)]
    public IActionResult Owner() => NoContent();

    [HttpGet("editor")]
    [Authorize(Policy = SitePolicies.SiteEditor)]
    public IActionResult Editor() => NoContent();

    [HttpGet("author")]
    public IActionResult Author() => NoContent();
}
