using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.ApiService.Hosting;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.TwoSite;

// Inside the namespace, so the types win over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;
using PantryPress.Domain.Modules.Sites.Repositories;

// PP2.11b: an Owner listing a site's members, giving them other roles, and removing them, over ApiService against the
// migrated schema, in the two-site world (tenancy.md, "Two-site test"). Memberships are the tenant root, with no filter
// behind them, so every change on A is checked against B's rows - the shared account's above all, which is an Author on A
// and the Owner of B. A member is named only within the site its route's Owner policy entered: another site's member is no
// member here, alike with an unknown id. And every site keeps an active owner, whether the rule sees the change first, the
// owner trigger does, or two owners leaving at once deadlock.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class SiteMembersTwoSiteTests(MigratedDatabase database) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    private TwoSiteHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TwoSiteHarness.CreateAsync(database, withApi: true);

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task An_owner_lists_exactly_their_sites_members()
    {
        var world = await _harness.SeedAsync("mem-list");

        foreach (var (site, owner, expected) in new[]
        {
            (world.A, world.A.Owner, new[]
            {
                (world.A.Owner, SiteRole.Owner, SiteMemberStatus.Active),
                (world.A.Editor, SiteRole.Editor, SiteMemberStatus.Active),
                (world.A.Author, SiteRole.Author, SiteMemberStatus.Active),
                (world.A.Suspended, SiteRole.Editor, SiteMemberStatus.Suspended),
                (world.Shared, SiteRole.Author, SiteMemberStatus.Active),
                (world.SuspendedOnA, SiteRole.Editor, SiteMemberStatus.Suspended),
            }),
            (world.B, world.B.Owner, new[]
            {
                (world.B.Owner, SiteRole.Owner, SiteMemberStatus.Active),
                (world.B.Editor, SiteRole.Editor, SiteMemberStatus.Active),
                (world.B.Author, SiteRole.Author, SiteMemberStatus.Active),
                (world.B.Suspended, SiteRole.Editor, SiteMemberStatus.Suspended),
                (world.Shared, SiteRole.Owner, SiteMemberStatus.Active),
                (world.SuspendedOnA, SiteRole.Editor, SiteMemberStatus.Active),
            }),
        })
        {
            using var client = _harness.ClientFor(owner);
            var listed = await ListAllAsync(client, site.Slug);

            Assert.Equal(
                expected.Select(e => (e.Item1.Value, e.Item2, e.Item3)).OrderBy(e => e.Value),
                listed.Select(m => (m.Id, m.Role, m.Status)).OrderBy(m => m.Id));
            Assert.Equal(new[] { owner.Value }, listed.Where(m => m.IsYou).Select(m => m.Id));
        }

        // The shared account is listed by B's other owner too, with B's role for it, and is "you" only to itself.
        using var shared = _harness.ClientFor(world.Shared);
        var asShared = await ListAllAsync(shared, world.B.Slug);
        Assert.Equal((SiteRole.Owner, true), asShared.Where(m => m.Id == world.Shared.Value).Select(m => (m.Role, m.IsYou)).Single());
    }

    // Who each member is comes from Accounts, by the account's own address; the same account reads alike on both sites.
    [Fact]
    public async Task Each_member_is_shown_with_the_address_they_sign_in_with()
    {
        var world = await _harness.SeedAsync("mem-addr");
        var editorEmail = NewEmail("Editor");
        var sharedEmail = NewEmail("Shared");
        await AddAccountAsync(world.A.Editor, editorEmail);
        await AddAccountAsync(world.Shared, sharedEmail);
        await AddAccountAsync(world.Stranger, NewEmail("Stranger"));

        using var aOwner = _harness.ClientFor(world.A.Owner);
        using var bOwner = _harness.ClientFor(world.B.Owner);
        var onA = await ListAllAsync(aOwner, world.A.Slug);
        var onB = await ListAllAsync(bOwner, world.B.Slug);

        Assert.Equal(editorEmail, onA.Single(m => m.Id == world.A.Editor.Value).Email);
        Assert.Equal(sharedEmail, onA.Single(m => m.Id == world.Shared.Value).Email);
        Assert.Equal(sharedEmail, onB.Single(m => m.Id == world.Shared.Value).Email);

        // No address that is not a member's on that site, and none for an account with no row.
        Assert.DoesNotContain(onB, m => m.Email == editorEmail);
        Assert.DoesNotContain(onA.Concat(onB), m => m.Email is { } email && email.StartsWith("stranger", StringComparison.Ordinal));
        Assert.Null(onA.Single(m => m.Id == world.A.Author.Value).Email);
    }

    // Every seeded member joined at the same instant, so the account is what orders a page and where the next one starts.
    [Fact]
    public async Task Pages_cover_every_member_once_and_a_cursor_from_B_grants_nothing_on_A()
    {
        var world = await _harness.SeedAsync("mem-page");
        using var aOwner = _harness.ClientFor(world.A.Owner);
        using var bOwner = _harness.ClientFor(world.B.Owner);
        var everyone = (await ListAllAsync(aOwner, world.A.Slug)).Select(m => m.Id).ToList();

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await PageAsync(aOwner, world.A.Slug, $"limit=4{(cursor is null ? string.Empty : $"&cursor={cursor}")}");
            Assert.InRange(page.Items.Count, 1, 4);
            seen.AddRange(page.Items.Select(m => m.Id));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(2, pages);
        Assert.Equal(everyone.Order(), seen.Order());
        Assert.Equal(seen.Count, seen.Distinct().Count());

        // A cursor is a position, never a site: B's positions A's list among A's own members.
        var fromB = (await PageAsync(bOwner, world.B.Slug, "limit=1")).NextCursor!;
        var onA = await PageAsync(aOwner, world.A.Slug, $"cursor={fromB}");
        Assert.All(onA.Items, m => Assert.Contains(m.Id, everyone));

        // The lowest position there is passes every member of every site: still only A's, all of them.
        var lowest = SiteMemberCursor.After(new SiteMemberPosition(DateTimeOffset.MinValue, new AccountId(Guid.Empty)));
        var fromStart = await PageAsync(aOwner, world.A.Slug, $"cursor={lowest}");
        Assert.Equal(everyone.Order(), fromStart.Items.Select(m => m.Id).Order());
    }

    [Theory]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=101", "limit")]
    [InlineData("limit=many", "limit")]
    [InlineData("cursor=cGll", "cursor")]
    public async Task A_bad_page_names_its_parameter(string query, string field)
    {
        var world = await _harness.SeedAsync("mem-query");
        using var owner = _harness.ClientFor(world.A.Owner);

        using var refused = await owner.GetAsync($"{Members(world.A.Slug)}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _), problem.RootElement.GetRawText());
    }

    [Fact]
    public async Task Only_an_owner_of_the_named_site_reaches_its_members()
    {
        var world = await _harness.SeedAsync("mem-who");
        var missing = $"none-{Guid.NewGuid():N}"[..30];
        var target = world.A.Author.ToString();

        // Members whose role is too low already know the site is there.
        foreach (var account in new[] { world.A.Editor, world.A.Author, world.Shared })
        {
            using var client = _harness.ClientFor(account);
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
            {
                foreach (var send in Calls(world.A.Slug, target))
                {
                    using var refused = await send(client);
                    Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
                    Assert.Equal(SiteErrors.SiteRoleRequired.Code, await refused.CodeAsync());
                }
            });
        }

        // Suspended on A, a stranger, and B's owner: each is answered as if A were not there at all.
        foreach (var account in new[] { world.A.Suspended, world.SuspendedOnA, world.Stranger, world.B.Owner })
        {
            using var client = _harness.ClientFor(account);
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
            {
                foreach (var (real, none) in Calls(world.A.Slug, target).Zip(Calls(missing, target)))
                {
                    using var onReal = await real(client);
                    using var onNone = await none(client);
                    await TwoSiteChecks.AssertIndistinguishableAsync(onReal, onNone, HttpStatusCode.NotFound, SiteErrors.SiteNotFound.Code);
                }
            });
        }

        Assert.Equal(0, await CountAuditAsync(world.A.Id));
    }

    // A's owner naming a member of B alone, an unknown id, and ids that are no id at all: one answer, and B untouched.
    [Fact]
    public async Task A_member_of_another_site_is_no_member_here()
    {
        var world = await _harness.SeedAsync("mem-other");
        using var owner = _harness.ClientFor(world.A.Owner);
        var unknown = Guid.NewGuid().ToString();

        foreach (var other in new[] { world.B.Owner.ToString(), world.B.Editor.ToString(), Guid.Empty.ToString(), "not-an-id", world.B.Owner.Value.ToString("N"), "%20", world.A.Author.ToString().ToUpperInvariant() })
        {
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, () =>
                TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
                {
                    using (var put = await PutRoleAsync(owner, world.A.Slug, other, "author"))
                    using (var putUnknown = await PutRoleAsync(owner, world.A.Slug, unknown, "author"))
                    {
                        await TwoSiteChecks.AssertIndistinguishableAsync(put, putUnknown, HttpStatusCode.NotFound, SiteErrors.MemberNotFound.Code);
                    }

                    using var delete = await DeleteAsync(owner, world.A.Slug, other);
                    using var deleteUnknown = await DeleteAsync(owner, world.A.Slug, unknown);
                    await TwoSiteChecks.AssertIndistinguishableAsync(delete, deleteUnknown, HttpStatusCode.NotFound, SiteErrors.MemberNotFound.Code);
                }));
        }

        Assert.Equal(0, await CountAuditAsync(world.A.Id) + await CountAuditAsync(world.B.Id));
    }

    [Fact]
    public async Task A_role_change_for_the_shared_account_on_A_leaves_B_unchanged()
    {
        var world = await _harness.SeedAsync("mem-role");
        using var owner = _harness.ClientFor(world.A.Owner);

        HttpResponseMessage? response = null;
        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
            response = await PutRoleAsync(owner, world.A.Slug, world.Shared.ToString(), "editor"));

        using (response)
        {
            Assert.Equal(HttpStatusCode.NoContent, response!.StatusCode);
        }

        Assert.Equal((SiteRole.Editor, SiteMemberStatus.Active), await RoleAsync(world.A.Id, world.Shared));
        Assert.Equal((SiteRole.Owner, SiteMemberStatus.Active), await RoleAsync(world.B.Id, world.Shared));

        var entry = Assert.Single(await AuditAsync(world.A.Id));
        Assert.Equal(
            (SiteAudit.MemberRoleChanged, world.A.Owner.Value, AccountAudit.TargetKind, world.Shared.ToString(), "role:author->editor"),
            (entry.Action, entry.ActorId!.Value, entry.TargetKind, entry.TargetId, entry.Reason));
        Assert.Empty(await AuditAsync(world.B.Id));

        // The member's next request works with the new role, on A alone.
        using var shared = _harness.ClientFor(world.Shared);
        Assert.Equal(SiteRole.Editor, (await shared.GetFromJsonAsync<SiteDetail>($"/api/v1/sites/{world.A.Slug}", Json))!.Role);
        Assert.Equal(SiteRole.Owner, (await shared.GetFromJsonAsync<SiteDetail>($"/api/v1/sites/{world.B.Slug}", Json))!.Role);
    }

    [Fact]
    public async Task Removing_the_shared_account_from_A_leaves_B_unchanged()
    {
        var world = await _harness.SeedAsync("mem-remove");
        using var owner = _harness.ClientFor(world.A.Owner);

        HttpResponseMessage? response = null;
        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
            response = await DeleteAsync(owner, world.A.Slug, world.Shared.ToString()));

        using (response)
        {
            Assert.Equal(HttpStatusCode.NoContent, response!.StatusCode);
        }

        Assert.Null(await RoleAsync(world.A.Id, world.Shared));
        Assert.Equal((SiteRole.Owner, SiteMemberStatus.Active), await RoleAsync(world.B.Id, world.Shared));
        var entry = Assert.Single(await AuditAsync(world.A.Id));
        Assert.Equal(
            (SiteAudit.MemberRemoved, world.A.Owner.Value, world.Shared.ToString(), (string?)null),
            (entry.Action, entry.ActorId!.Value, entry.TargetId, entry.Reason));

        // Its next request reaches A no more, and B as before.
        using var shared = _harness.ClientFor(world.Shared);
        using var onA = await shared.GetAsync($"/api/v1/sites/{world.A.Slug}");
        using var onB = await shared.GetAsync($"/api/v1/sites/{world.B.Slug}");
        Assert.Equal(HttpStatusCode.NotFound, onA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, onB.StatusCode);
    }

    // The shared account acting as B's owner: A's members are no members of B, and its own changes on B leave A as it was.
    [Fact]
    public async Task The_shared_account_as_Bs_owner_reaches_only_Bs_members()
    {
        var world = await _harness.SeedAsync("mem-sharedb");
        using var shared = _harness.ClientFor(world.Shared);

        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
        {
            foreach (var aMember in new[] { world.A.Author, world.A.Owner })
            {
                using var put = await PutRoleAsync(shared, world.B.Slug, aMember.ToString(), "editor");
                using var putUnknown = await PutRoleAsync(shared, world.B.Slug, Guid.NewGuid().ToString(), "editor");
                await TwoSiteChecks.AssertIndistinguishableAsync(put, putUnknown, HttpStatusCode.NotFound, SiteErrors.MemberNotFound.Code);
                using var delete = await DeleteAsync(shared, world.B.Slug, aMember.ToString());
                Assert.Equal(SiteErrors.MemberNotFound.Code, await delete.CodeAsync());
            }

            // B has two owners, so the shared account may step down there and then leave; A keeps its row for it.
            using (var stepped = await PutRoleAsync(shared, world.B.Slug, world.Shared.ToString(), "author"))
            {
                Assert.Equal(HttpStatusCode.NoContent, stepped.StatusCode);
            }

            using var bOwner = _harness.ClientFor(world.B.Owner);
            using var removed = await DeleteAsync(bOwner, world.B.Slug, world.Shared.ToString());
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        });

        Assert.Null(await RoleAsync(world.B.Id, world.Shared));
        Assert.Equal((SiteRole.Author, SiteMemberStatus.Active), await RoleAsync(world.A.Id, world.Shared));
        Assert.Equal(0, await CountAuditAsync(world.A.Id));
    }

    // A suspended member keeps their standing through a role change, and can be removed like anyone else.
    [Fact]
    public async Task A_suspended_member_keeps_their_standing_and_can_be_removed()
    {
        var world = await _harness.SeedAsync("mem-susp");
        using var owner = _harness.ClientFor(world.A.Owner);

        using (var promoted = await PutRoleAsync(owner, world.A.Slug, world.SuspendedOnA.ToString(), "owner"))
        {
            Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);
        }

        Assert.Equal((SiteRole.Owner, SiteMemberStatus.Suspended), await RoleAsync(world.A.Id, world.SuspendedOnA));
        Assert.Equal((SiteRole.Editor, SiteMemberStatus.Active), await RoleAsync(world.B.Id, world.SuspendedOnA));

        // A suspended owner is no owner the site can keep: A's one active owner still cannot step down.
        using (var refused = await PutRoleAsync(owner, world.A.Slug, world.A.Owner.ToString(), "editor"))
        {
            Assert.Equal(SiteErrors.LastOwner.Code, await refused.CodeAsync());
        }

        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
        {
            using var removed = await DeleteAsync(owner, world.A.Slug, world.SuspendedOnA.ToString());
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        });
        Assert.Null(await RoleAsync(world.A.Id, world.SuspendedOnA));
    }

    [Fact]
    public async Task Giving_a_member_the_role_they_have_changes_and_records_nothing()
    {
        var world = await _harness.SeedAsync("mem-same");
        using var owner = _harness.ClientFor(world.A.Owner);

        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
        {
            foreach (var (member, role) in new[] { (world.A.Editor, "editor"), (world.A.Owner, "owner"), (world.Shared, "author") })
            {
                using var same = await PutRoleAsync(owner, world.A.Slug, member.ToString(), role);
                Assert.Equal(HttpStatusCode.NoContent, same.StatusCode);
            }
        });

        Assert.Equal(0, await CountAuditAsync(world.A.Id));
    }

    [Fact]
    public async Task The_last_active_owner_can_be_neither_demoted_nor_removed()
    {
        var world = await _harness.SeedAsync("mem-last");
        using var owner = _harness.ClientFor(world.A.Owner);

        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
        {
            foreach (var role in new[] { "editor", "author" })
            {
                using var demoted = await PutRoleAsync(owner, world.A.Slug, world.A.Owner.ToString(), role);
                Assert.Equal(HttpStatusCode.Conflict, demoted.StatusCode);
                Assert.Equal(SiteErrors.LastOwner.Code, await demoted.CodeAsync());
            }

            using var removed = await DeleteAsync(owner, world.A.Slug, world.A.Owner.ToString());
            Assert.Equal(HttpStatusCode.Conflict, removed.StatusCode);
            Assert.Equal(SiteErrors.LastOwner.Code, await removed.CodeAsync());
        });

        Assert.Equal(0, await CountAuditAsync(world.A.Id));
    }

    // With a second owner, the first may step down, and then reaches the owner routes no more; the second, now alone, may
    // not leave; and an owner who is not the last may leave the site altogether.
    [Fact]
    public async Task An_owner_who_is_not_the_last_may_step_down_or_leave()
    {
        var world = await _harness.SeedAsync("mem-step");
        using var first = _harness.ClientFor(world.A.Owner);
        using var second = _harness.ClientFor(world.A.Editor);

        using (var promoted = await PutRoleAsync(first, world.A.Slug, world.A.Editor.ToString(), "owner"))
        {
            Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);
        }

        using (var stepped = await PutRoleAsync(first, world.A.Slug, world.A.Owner.ToString(), "author"))
        {
            Assert.Equal(HttpStatusCode.NoContent, stepped.StatusCode);
        }

        using (var refused = await first.GetAsync(Members(world.A.Slug)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(SiteErrors.SiteRoleRequired.Code, await refused.CodeAsync());
        }

        Assert.Equal(SiteRole.Author, (await ListAllAsync(second, world.A.Slug)).Single(m => m.Id == world.A.Owner.Value).Role);
        using (var alone = await DeleteAsync(second, world.A.Slug, world.A.Editor.ToString()))
        {
            Assert.Equal(SiteErrors.LastOwner.Code, await alone.CodeAsync());
        }

        using (var promotedAgain = await PutRoleAsync(second, world.A.Slug, world.A.Author.ToString(), "owner"))
        {
            Assert.Equal(HttpStatusCode.NoContent, promotedAgain.StatusCode);
        }

        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
        {
            using var left = await DeleteAsync(second, world.A.Slug, world.A.Editor.ToString());
            Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);
        });

        using var gone = await second.GetAsync($"/api/v1/sites/{world.A.Slug}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(1, await ActiveOwnersAsync(world.A.Id));
    }

    // Two owners demoting each other at the same instant, round after round: the rule passes both, so the owner trigger or
    // a deadlock settles it, and either way exactly one is refused, last-owner, and one owner stays.
    [Fact]
    public async Task Owners_demoting_each_other_at_once_leave_one()
    {
        for (var round = 0; round < 6; round++)
        {
            var world = await _harness.SeedAsync("mem-duel");
            await PromoteAsync(world.A.Id, world.A.Editor);
            using var first = _harness.ClientFor(world.A.Owner);
            using var second = _harness.ClientFor(world.A.Editor);

            var answers = await Task.WhenAll(
                round % 2 == 0
                    ? PutRoleAsync(first, world.A.Slug, world.A.Editor.ToString(), "author")
                    : DeleteAsync(first, world.A.Slug, world.A.Editor.ToString()),
                round % 2 == 0
                    ? PutRoleAsync(second, world.A.Slug, world.A.Owner.ToString(), "author")
                    : DeleteAsync(second, world.A.Slug, world.A.Owner.ToString()));

            try
            {
                Assert.Equal(
                    new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict },
                    answers.Select(a => a.StatusCode).Order());
                Assert.Equal(SiteErrors.LastOwner.Code, await answers.Single(a => a.StatusCode == HttpStatusCode.Conflict).CodeAsync());
                Assert.Equal(1, await ActiveOwnersAsync(world.A.Id));
                Assert.Single(await AuditAsync(world.A.Id));
            }
            finally
            {
                foreach (var answer in answers)
                {
                    answer.Dispose();
                }
            }
        }
    }

    // Six owners each leaving at once: five go, the last is refused, and the site keeps one.
    [Fact]
    public async Task Owners_all_leaving_at_once_leave_one()
    {
        var world = await _harness.SeedAsync("mem-exodus");
        var owners = new List<AccountId> { world.A.Owner };
        for (var i = 0; i < 5; i++)
        {
            var account = TwoSiteHarness.NewAccount();
            await _harness.ChangeAsync(db => db.Add(Member(world.A.Id, account, SiteRole.Owner)));
            owners.Add(account);
        }

        var clients = owners.Select(_harness.ClientFor).ToList();
        HttpResponseMessage[] answers = [];
        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
            answers = await Task.WhenAll(clients.Zip(owners).Select(c => DeleteAsync(c.First, world.A.Slug, c.Second.ToString()))));

        Assert.Equal(5, answers.Count(a => a.StatusCode == HttpStatusCode.NoContent));
        var refused = Assert.Single(answers, a => a.StatusCode != HttpStatusCode.NoContent);
        Assert.Equal(SiteErrors.LastOwner.Code, await refused.CodeAsync());
        Assert.Equal(1, await ActiveOwnersAsync(world.A.Id));
        Assert.Equal(5, (await AuditAsync(world.A.Id)).Count);

        foreach (var disposable in answers.Cast<IDisposable>().Concat(clients))
        {
            disposable.Dispose();
        }
    }

    // The database's last word, made certain: the rule saw two owners, and the other stepped down between the rule and the
    // save. The owner trigger refuses the save (51003), and asking again answers last-owner, with nothing kept.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_change_that_got_past_the_rule_is_refused_by_the_database_as_last_owner(bool removal)
    {
        var world = await _harness.SeedAsync("mem-trigger");
        await PromoteAsync(world.A.Id, world.A.Editor);

        await using var scope = _harness.Scope();
        var repository = scope.ServiceProvider.GetRequiredService<SiteMemberRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<SitesUnitOfWork>();
        var owner = await repository.FindTrackedAsync(world.A.Id, world.A.Owner, CancellationToken.None);
        Assert.Equal(2, await repository.CountActiveOwnersAsync(world.A.Id, CancellationToken.None));
        if (removal)
        {
            repository.Remove(owner!);
        }
        else
        {
            owner!.Role = SiteRole.Editor;
        }

        await _harness.ChangeAsync(db => db.Set<SiteMember>().Single(m => m.SiteId == world.A.Id && m.AccountId == world.A.Editor).Role = SiteRole.Author);

        var outcome = await unitOfWork.SaveMemberChangeAsync(
            world.A.Id,
            world.A.Owner,
            removal ? null : SiteRole.Editor,
            CancellationToken.None);

        Assert.Equal(MemberChangeOutcome.LastOwner, outcome);
        Assert.Equal((SiteRole.Owner, SiteMemberStatus.Active), await RoleAsync(world.A.Id, world.A.Owner));
        Assert.False(scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().ChangeTracker.HasChanges());
    }

    // The same, with no lead: two owners each stepping down in their own unit of work at once. The harness's context has
    // no retries, so a deadlock's victim meets the failure itself; either way it is answered last-owner.
    [Fact]
    public async Task Owners_stepping_down_at_once_through_the_unit_of_work_leave_one()
    {
        for (var round = 0; round < 10; round++)
        {
            var world = await _harness.SeedAsync("mem-race");
            await PromoteAsync(world.A.Id, world.A.Editor);

            var outcomes = await Task.WhenAll(
                StepDownAsync(world.A.Id, world.A.Owner),
                StepDownAsync(world.A.Id, world.A.Editor));

            Assert.Equal(new[] { MemberChangeOutcome.Saved, MemberChangeOutcome.LastOwner }, outcomes.Order());
            Assert.Equal(1, await ActiveOwnersAsync(world.A.Id));
        }
    }

    // A deadlock (1205), made certain, and lost by the save under test, in a context that retries transient faults as every
    // host's does - here not at all, so this one deadlock is the last attempt and ends the save. Another transaction steps
    // the first owner down and holds that row; the second owner's step-down then waits in the owner trigger to read it;
    // the other transaction touches the second owner's row, closing the cycle, and wins on priority. It commits only a
    // while later, so an answer read from the committed snapshot would still see two owners and let the failure through;
    // the locking read waits for the commit, sees one, and answers last-owner.
    [Fact]
    public async Task A_deadlock_lost_to_an_owner_stepping_down_is_answered_last_owner()
    {
        await using var retrying = await TwoSiteHarness.CreateAsync(
            database,
            configure: options => new SqlServerDbContextOptionsBuilder(options).EnableRetryOnFailure(maxRetryCount: 0));
        var world = await _harness.SeedAsync("mem-deadlock");
        await PromoteAsync(world.A.Id, world.A.Editor);
        await using var holderScope = _harness.Scope();
        var holder = holderScope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Database;
        await holder.OpenConnectionAsync();
        await holder.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY HIGH");
        try
        {
            var spid = await holder.SqlQueryRaw<short>("SELECT @@SPID AS [Value]").SingleAsync();
            await using var transaction = await holder.BeginTransactionAsync();
            await holder.ExecuteSqlAsync(
                $"UPDATE [SiteMembers] SET [Role] = {(int)SiteRole.Editor} WHERE [SiteId] = {world.A.Id.Value} AND [AccountId] = {world.A.Owner.Value}");

            var stepDown = StepDownAsync(retrying, world.A.Id, world.A.Editor);
            await WaitUntilBlockedByAsync(spid);
            await holder.ExecuteSqlAsync(
                $"UPDATE [SiteMembers] SET [Role] = [Role] WHERE [SiteId] = {world.A.Id.Value} AND [AccountId] = {world.A.Editor.Value}");
            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.False(stepDown.IsCompleted, "The answer did not wait for the change that won the deadlock.");
            await transaction.CommitAsync();

            Assert.Equal(MemberChangeOutcome.LastOwner, await stepDown);
        }
        finally
        {
            // The connection goes back to the pool; its next user gets the usual priority.
            await holder.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY NORMAL");
        }

        Assert.Equal((SiteRole.Owner, SiteMemberStatus.Active), await RoleAsync(world.A.Id, world.A.Editor));
        Assert.Equal(1, await ActiveOwnersAsync(world.A.Id));
    }

    // Someone else changing the same member between the read and the save: the member's version refuses the save, and the
    // answer says what happened - changed, removed, or already as asked.
    [Fact]
    public async Task A_member_changed_meanwhile_is_answered_by_what_happened()
    {
        var world = await _harness.SeedAsync("mem-stale");

        // Another owner made the Editor an Owner: this request's Author is not what they are now.
        Assert.Equal(
            MemberChangeOutcome.Changed,
            await StaleChangeAsync(world.A.Id, world.A.Editor, SiteRole.Author, db => Row(db, world.A.Id, world.A.Editor).Role = SiteRole.Owner));
        Assert.Equal(SiteRole.Owner, (await RoleAsync(world.A.Id, world.A.Editor))!.Value.Role);

        // Removed meanwhile: a new role finds no one; a removal is done.
        Assert.Equal(
            MemberChangeOutcome.NotFound,
            await StaleChangeAsync(world.A.Id, world.A.Author, SiteRole.Editor, db => db.Remove(Row(db, world.A.Id, world.A.Author))));
        Assert.Equal(
            MemberChangeOutcome.Saved,
            await StaleChangeAsync(world.A.Id, world.Shared, null, db => db.Remove(Row(db, world.A.Id, world.Shared))));

        // Given the same role meanwhile: done.
        Assert.Equal(
            MemberChangeOutcome.Saved,
            await StaleChangeAsync(world.A.Id, world.SuspendedOnA, SiteRole.Author, db => Row(db, world.A.Id, world.SuspendedOnA).Role = SiteRole.Author));

        Assert.Equal((SiteRole.Editor, SiteMemberStatus.Active), await RoleAsync(world.B.Id, world.SuspendedOnA));
        Assert.Equal((SiteRole.Owner, SiteMemberStatus.Active), await RoleAsync(world.B.Id, world.Shared));
    }

    [Fact]
    public async Task A_retried_removal_with_its_key_gets_the_first_answer()
    {
        var world = await _harness.SeedAsync("mem-key");
        using var owner = _harness.ClientFor(world.A.Owner);
        var key = Guid.NewGuid().ToString("N");

        using var first = await DeleteAsync(owner, world.A.Slug, world.A.Author.ToString(), key);
        using var replay = await DeleteAsync(owner, world.A.Slug, world.A.Author.ToString(), key);
        using var unkeyed = await DeleteAsync(owner, world.A.Slug, world.A.Author.ToString());

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues(ApiIdempotency.ReplayedHeader)));
        Assert.Equal(SiteErrors.MemberNotFound.Code, await unkeyed.CodeAsync());
        Assert.Single(await AuditAsync(world.A.Id));

        // One account's key replays nothing to another: B's owner with the same key is answered for B.
        using var bOwner = _harness.ClientFor(world.B.Owner);
        using var onB = await DeleteAsync(bOwner, world.B.Slug, world.B.Author.ToString(), key);
        Assert.Equal(HttpStatusCode.NoContent, onB.StatusCode);
        Assert.False(onB.Headers.Contains(ApiIdempotency.ReplayedHeader));
        Assert.Null(await RoleAsync(world.B.Id, world.B.Author));
    }

    [Fact]
    public async Task A_retried_role_change_with_its_key_gets_the_first_answer()
    {
        var world = await _harness.SeedAsync("mem-rekey");
        using var owner = _harness.ClientFor(world.A.Owner);
        var key = Guid.NewGuid().ToString("N");

        using var first = await PutRoleAsync(owner, world.A.Slug, world.A.Author.ToString(), "editor", key);
        using var replay = await PutRoleAsync(owner, world.A.Slug, world.A.Author.ToString(), "editor", key);
        using var reused = await PutRoleAsync(owner, world.A.Slug, world.A.Author.ToString(), "owner", key);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues(ApiIdempotency.ReplayedHeader)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
        Assert.Equal((SiteRole.Editor, SiteMemberStatus.Active), await RoleAsync(world.A.Id, world.A.Author));
    }

    // An owner changing their own membership: the site policy runs ahead of the replay, and the first request took effect,
    // so a retry - keyed or not - hears what the caller now is on the site, as the routes' descriptions say.
    [Fact]
    public async Task A_retry_of_an_owners_change_to_themselves_hears_what_they_now_are()
    {
        var world = await _harness.SeedAsync("mem-self");
        await PromoteAsync(world.A.Id, world.A.Editor);
        using var first = _harness.ClientFor(world.A.Owner);
        var key = Guid.NewGuid().ToString("N");

        using (var stepped = await PutRoleAsync(first, world.A.Slug, world.A.Owner.ToString(), "editor", key))
        {
            Assert.Equal(HttpStatusCode.NoContent, stepped.StatusCode);
        }

        foreach (var retryKey in new[] { key, null })
        {
            using var retry = await PutRoleAsync(first, world.A.Slug, world.A.Owner.ToString(), "editor", retryKey);
            Assert.Equal(HttpStatusCode.Forbidden, retry.StatusCode);
            Assert.Equal(SiteErrors.SiteRoleRequired.Code, await retry.CodeAsync());
        }

        // The second owner may leave once the first is an owner again.
        using var second = _harness.ClientFor(world.A.Editor);
        await PromoteAsync(world.A.Id, world.A.Owner);
        var leaveKey = Guid.NewGuid().ToString("N");
        using (var left = await DeleteAsync(second, world.A.Slug, world.A.Editor.ToString(), leaveKey))
        {
            Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);
        }

        foreach (var retryKey in new[] { leaveKey, null })
        {
            using var retry = await DeleteAsync(second, world.A.Slug, world.A.Editor.ToString(), retryKey);
            Assert.Equal(HttpStatusCode.NotFound, retry.StatusCode);
            Assert.Equal(SiteErrors.SiteNotFound.Code, await retry.CodeAsync());
        }
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"role":null}""")]
    [InlineData("""{"role":"admin"}""")]
    public async Task A_bad_role_is_refused_and_changes_nothing(string body)
    {
        var world = await _harness.SeedAsync("mem-bad");
        using var owner = _harness.ClientFor(world.A.Owner);

        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
        {
            using var refused = await owner.PutAsync(
                $"{Members(world.A.Slug)}/{world.A.Author}/role",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        });
    }

    // Changing members has its own small allowance per account, and a refused change still spends it.
    [Fact]
    public async Task Changing_members_has_its_own_allowance()
    {
        await using var harness = await TwoSiteHarness.CreateAsync(
            database,
            withApi: true,
            apiSettings: new Dictionary<string, string?> { ["RateLimits:SiteMemberChangesPerAccountPerMinute"] = "2" });
        var world = await harness.SeedAsync("mem-limit");
        using var owner = harness.ClientFor(world.A.Owner);

        using var first = await PutRoleAsync(owner, world.A.Slug, world.A.Owner.ToString(), "editor");
        using var second = await DeleteAsync(owner, world.A.Slug, Guid.NewGuid().ToString());
        using var third = await PutRoleAsync(owner, world.A.Slug, world.A.Author.ToString(), "editor");
        using var list = await owner.GetAsync(Members(world.A.Slug));

        Assert.Equal(
            [HttpStatusCode.Conflict, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests, HttpStatusCode.OK],
            new[] { first.StatusCode, second.StatusCode, third.StatusCode, list.StatusCode });
        Assert.True(third.Headers.RetryAfter is not null);
    }

    // The directory answers for the ids it is given, and no more than a page of them.
    [Fact]
    public async Task The_account_directory_answers_only_for_the_ids_it_is_given()
    {
        var known = TwoSiteHarness.NewAccount();
        var other = TwoSiteHarness.NewAccount();
        var email = NewEmail();
        await AddAccountAsync(known, email);
        await AddAccountAsync(other, NewEmail());
        await using var scope = _harness.ApiServices.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IAccountDirectory>();

        var found = await directory.FindAddressesAsync([known, known, TwoSiteHarness.NewAccount()], CancellationToken.None);

        Assert.Equal(email, Assert.Single(found, f => f.Key == known).Value);
        Assert.Single(found);
        Assert.Empty(await directory.FindAddressesAsync([], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => directory.FindAddressesAsync(
            [.. Enumerable.Range(0, IAccountDirectory.MaxAccounts + 1).Select(_ => TwoSiteHarness.NewAccount())],
            CancellationToken.None));
    }

    private static string Members(string slug) => $"/api/v1/sites/{slug}/members";

    // The three member routes, each sent as its own request against the slug and member given.
    private static IEnumerable<Func<HttpClient, Task<HttpResponseMessage>>> Calls(string slug, string member) =>
    [
        client => client.GetAsync(Members(slug)),
        client => PutRoleAsync(client, slug, member, "editor"),
        client => DeleteAsync(client, slug, member),
    ];

    private static async Task<HttpResponseMessage> PutRoleAsync(HttpClient client, string slug, string member, string role, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Members(slug)}/{member}/role")
        {
            Content = JsonContent.Create(new { role }),
        };
        if (key is not null)
        {
            request.Headers.Add(ApiIdempotency.KeyHeader, key);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string slug, string member, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{Members(slug)}/{member}");
        if (key is not null)
        {
            request.Headers.Add(ApiIdempotency.KeyHeader, key);
        }

        return await client.SendAsync(request);
    }

    private static async Task<SiteMemberPage> PageAsync(HttpClient client, string slug, string query)
    {
        using var response = await client.GetAsync($"{Members(slug)}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SiteMemberPage>(Json))!;
    }

    private static async Task<List<SiteMemberItem>> ListAllAsync(HttpClient client, string slug) =>
        [.. (await PageAsync(client, slug, "limit=100")).Items];

    private static string NewEmail(string name = "cook") => SiteInvitationsTwoSiteTests.NewEmail(name);

    private static SiteMember Member(SiteId site, AccountId account, SiteRole role) => new()
    {
        SiteId = site,
        AccountId = account,
        Role = role,
        Status = SiteMemberStatus.Active,
        CreatedAt = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
    };

    private static SiteMember Row(PantryPressDbContext db, SiteId site, AccountId account) =>
        db.Set<SiteMember>().Single(m => m.SiteId == site && m.AccountId == account);

    private Task PromoteAsync(SiteId site, AccountId account) =>
        _harness.ChangeAsync(db => Row(db, site, account).Role = SiteRole.Owner);

    private Task AddAccountAsync(AccountId account, string email) =>
        _harness.ChangeAsync(db => db.Add(new ApplicationUser
        {
            Id = account.Value,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        }));

    // An owner stepping down to Editor through the unit of work, in a scope of its own, after the rule has passed.
    private Task<MemberChangeOutcome> StepDownAsync(SiteId site, AccountId owner) => StepDownAsync(_harness, site, owner);

    private static async Task<MemberChangeOutcome> StepDownAsync(TwoSiteHarness harness, SiteId site, AccountId owner)
    {
        await using var scope = harness.Scope();
        var member = await scope.ServiceProvider.GetRequiredService<SiteMemberRepository>()
            .FindTrackedAsync(site, owner, CancellationToken.None);
        member!.Role = SiteRole.Editor;
        return await scope.ServiceProvider.GetRequiredService<SitesUnitOfWork>()
            .SaveMemberChangeAsync(site, owner, SiteRole.Editor, CancellationToken.None);
    }

    // Reads the member, lets <paramref name="meanwhile"/> change their row, then saves this read's change: a role, or a
    // removal when <paramref name="newRole"/> is null.
    private async Task<MemberChangeOutcome> StaleChangeAsync(
        SiteId site,
        AccountId account,
        SiteRole? newRole,
        Action<PantryPressDbContext> meanwhile)
    {
        await using var scope = _harness.Scope();
        var repository = scope.ServiceProvider.GetRequiredService<SiteMemberRepository>();
        var member = await repository.FindTrackedAsync(site, account, CancellationToken.None);
        if (newRole is { } role)
        {
            member!.Role = role;
        }
        else
        {
            repository.Remove(member!);
        }

        await _harness.ChangeAsync(meanwhile);
        return await scope.ServiceProvider.GetRequiredService<SitesUnitOfWork>()
            .SaveMemberChangeAsync(site, account, newRole, CancellationToken.None);
    }

    private async Task<(SiteRole Role, SiteMemberStatus Status)?> RoleAsync(SiteId site, AccountId account)
    {
        await using var scope = _harness.Scope();
        var rows = await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<SiteMember>().AsNoTracking()
            .Where(m => m.SiteId == site && m.AccountId == account)
            .Select(m => new { m.Role, m.Status })
            .ToListAsync();
        return rows is [var row] ? (row.Role, row.Status) : null;
    }

    // Waits until some request is blocked by the session <paramref name="spid"/>.
    private async Task WaitUntilBlockedByAsync(short spid)
    {
        await using var scope = _harness.Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Database;
        var deadline = TimeSpan.FromSeconds(30);
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (await db.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.dm_exec_requests WHERE blocking_session_id = {spid}").SingleAsync() == 0)
        {
            Assert.True(waited.Elapsed < deadline, "Nothing came to wait on the holding transaction.");
            await Task.Delay(50);
        }
    }

    private async Task<int> ActiveOwnersAsync(SiteId site)
    {
        await using var scope = _harness.Scope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<SiteMember>()
            .CountAsync(m => m.SiteId == site && m.Role == SiteRole.Owner && m.Status == SiteMemberStatus.Active);
    }

    private async Task<List<AuditEntry>> AuditAsync(SiteId site)
    {
        await using var scope = _harness.Scope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<AuditEntry>().AsNoTracking()
            .Where(e => e.SiteId == site.Value)
            .ToListAsync();
    }

    private async Task<int> CountAuditAsync(SiteId site) => (await AuditAsync(site)).Count;
}
