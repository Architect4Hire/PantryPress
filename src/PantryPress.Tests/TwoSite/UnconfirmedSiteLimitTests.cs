using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.TwoSite;

// Inside the namespace, so the types win over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;

// PP2.10: an account whose email address is not confirmed owns at most one site here (Sites:UnconfirmedAccountSiteLimit).
// A slug is never reissued, so this is what keeps throwaway accounts from taking addresses. Over the limit the answer is
// email-not-verified, before anything about the address is asked, so such an account learns nothing more about which
// addresses are taken; and the save counts again under a lock, so creates at the same instant cannot all get through.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class UnconfirmedSiteLimitTests(MigratedDatabase database) : IAsyncLifetime
{
    private const string Sites = "/api/v1/sites";

    private TwoSiteHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TwoSiteHarness.CreateAsync(
        database,
        withApi: true,
        apiSettings: new Dictionary<string, string?> { ["Sites:UnconfirmedAccountSiteLimit"] = "1" });

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task An_unconfirmed_account_gets_one_site_and_then_is_asked_to_confirm()
    {
        var world = await _harness.SeedAsync("unconfirmed");
        using var client = _harness.ClientFor(TwoSiteHarness.NewAccount());

        using var first = await client.PostAsJsonAsync(Sites, Request(Fresh("first")));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // A free address and a taken one get the same answer: over the limit, nothing about addresses is asked.
        foreach (var slug in new[] { Fresh("second"), world.A.Slug, "app" })
        {
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
            {
                using var refused = await client.PostAsJsonAsync(Sites, Request(slug));
                Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
                Assert.Equal(AccountErrors.EmailNotVerified.Code, await refused.CodeAsync());
            });
        }
    }

    // B's seeded Owner already owns B, so the limit is already reached; a deleted site still counts, since it still holds
    // its slug.
    [Fact]
    public async Task Every_site_the_account_owns_counts_a_deleted_one_included()
    {
        var world = await _harness.SeedAsync("owned");
        using var owner = _harness.ClientFor(world.B.Owner);

        using var refused = await owner.PostAsJsonAsync(Sites, Request(Fresh("more")));
        Assert.Equal(AccountErrors.EmailNotVerified.Code, await refused.CodeAsync());

        await _harness.ChangeAsync(db => db.Set<Site>().Single(s => s.Id == world.B.Id).Status = SiteStatus.Deleted);
        using var afterDeleting = await owner.PostAsJsonAsync(Sites, Request(Fresh("more")));
        Assert.Equal(AccountErrors.EmailNotVerified.Code, await afterDeleting.CodeAsync());

        // Being an Editor or an Author somewhere is not owning it.
        using var editor = _harness.ClientFor(world.A.Editor);
        using var created = await editor.PostAsJsonAsync(Sites, Request(Fresh("editor")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task A_confirmed_account_is_not_limited()
    {
        var account = await ConfirmedAccountAsync();
        using var client = _harness.ClientFor(account);

        foreach (var prefix in new[] { "one", "two", "three" })
        {
            using var response = await client.PostAsJsonAsync(Sites, Request(Fresh(prefix)));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    // Each request passes the facade's count at the same instant; only the locked count at the save lets one through.
    [Fact]
    public async Task Creates_at_the_same_instant_make_one_site_for_an_unconfirmed_account()
    {
        var account = TwoSiteHarness.NewAccount();
        var clients = Enumerable.Range(0, 6).Select(_ => _harness.ClientFor(account)).ToList();

        var responses = await Task.WhenAll(clients.Select((c, i) => c.PostAsJsonAsync(Sites, Request(Fresh($"burst{i}")))));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(AccountErrors.EmailNotVerified.Code, await refused.CodeAsync());
        }

        await using var scope = _harness.Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        Assert.Equal(1, await db.Set<SiteMember>().CountAsync(m => m.AccountId == account));
        foreach (var disposable in responses.Cast<IDisposable>().Concat(clients))
        {
            disposable.Dispose();
        }
    }

    private async Task<AccountId> ConfirmedAccountAsync()
    {
        var id = Guid.NewGuid();
        var email = $"limit-{id:N}@example.com";
        await _harness.ChangeAsync(db => db.Add(new ApplicationUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        }));
        return new AccountId(id);
    }

    private static CreateSiteRequest Request(string slug) =>
        new(slug, "Sunday Supper", null, "America/Chicago", null);

    private static string Fresh(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 21, SiteLimits.MaxSlugLength)];
}
