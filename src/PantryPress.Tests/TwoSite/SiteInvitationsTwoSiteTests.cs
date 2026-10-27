using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.ApiService.Hosting;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Tests.Fakes;
using PantryPress.Tests.Persistence;

namespace PantryPress.Tests.TwoSite;

// Inside the namespace, so the types win over the PantryPress.Tests.Site namespace (the public site host's tests).
using PantryPress.Domain.Modules.Sites;
using PantryPress.Domain.Modules.Sites.DataLayer;
using PantryPress.Domain.Modules.Sites.Repositories;

// PP2.11: inviting people to a site by email and joining through an invitation, over ApiService against the migrated
// schema, in the two-site world (tenancy.md, "Two-site test"). An invitation is the first site-owned row: it is filed
// under the site its Owner's route named, read only through that site's filter, and replaced only there. Its link names its
// site sealed, so accepting grants that site and nothing else, works once, only for the invited inbox, and every link
// that cannot be used gets one answer. The invitation email goes out from the outbox in a scope that works on the site
// it was staged under - the Worker's resolver - through the same handler the Worker registers.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class SiteInvitationsTwoSiteTests(MigratedDatabase database) : IAsyncLifetime
{
    private const string Accept = "/api/v1/site-invitations/accept";
    private const string AppBaseUrl = "https://app.localhost";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    private readonly RecordingEmailGateway _email = new();
    private TwoSiteHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TwoSiteHarness.CreateAsync(
        database,
        withApi: true,
        apiServices: WorkerHalf(_email));

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // The API's own clock, so seeded times and the facade's checks agree.
    private DateTimeOffset Now => _harness.ApiServices.GetRequiredService<IClock>().UtcNow;

    [Fact]
    public async Task The_invitation_row_is_site_owned_and_isolated()
    {
        var world = await _harness.SeedAsync("inv-row");

        await TwoSiteChecks.AssertSiteOwnedIsolatedAsync<SiteInvitation>(
            _harness.Services,
            world.A.Id,
            world.B.Id,
            db => Task.FromResult(AddInvitation(db, NewEmail(), SiteRole.Editor)),
            set => set.SetProperty(i => i.Status, SiteInvitationStatus.Revoked),
            invitation => invitation.Status = SiteInvitationStatus.Revoked);
    }

    [Fact]
    public async Task The_repository_reads_only_the_entered_sites_invitations()
    {
        var world = await _harness.SeedAsync("inv-read");
        var ids = new List<SiteInvitationId>();

        await TwoSiteChecks.AssertReadPathIsolatedAsync<SiteInvitation>(
            _harness.Services,
            world.A.Id,
            world.B.Id,
            db =>
            {
                var invitation = AddInvitation(db, NewEmail(), SiteRole.Author);
                ids.Add(invitation.Id);
                return Task.FromResult(invitation);
            },
            async services =>
            {
                var repository = services.GetRequiredService<SiteInvitationRepository>();
                var found = new List<object?[]>();
                foreach (var id in ids)
                {
                    if (await repository.ReadAsync(id, CancellationToken.None) is { } read)
                    {
                        found.Add([read.SiteId, read.Id]);
                    }

                    if (await repository.FindAsync(id, CancellationToken.None) is { } tracked)
                    {
                        found.Add([tracked.SiteId, tracked.Id]);
                    }
                }

                return found;
            });
    }

    [Fact]
    public async Task An_owner_invites_by_email_and_the_invitation_is_filed_under_their_site_alone()
    {
        var world = await _harness.SeedAsync("inv-new");
        var email = NewEmail("Pat");
        using var owner = _harness.ClientFor(world.A.Owner);

        HttpResponseMessage? response = null;
        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, () =>
            TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
                response = await InviteAsync(owner, world.A.Slug, email, "editor")));

        SiteInvitationDetail detail;
        using (response)
        {
            Assert.Equal(HttpStatusCode.Accepted, response!.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("@", body, StringComparison.Ordinal);
            detail = JsonSerializer.Deserialize<SiteInvitationDetail>(body, Json)!;
            Assert.Equal(SiteRole.Editor, detail.Role);
            Assert.InRange(detail.ExpiresAt - Now, TimeSpan.FromDays(7) - TimeSpan.FromMinutes(5), TimeSpan.FromDays(7));
        }

        var id = new SiteInvitationId(detail.Id);
        var invitation = await ReadInvitationAsync(world.A.Id, id);
        Assert.NotNull(invitation);
        Assert.Equal(
            (email, email.ToUpperInvariant(), SiteRole.Editor, SiteInvitationStatus.Pending, world.A.Owner),
            (invitation.Email, invitation.NormalizedEmail, invitation.Role, invitation.Status, invitation.InvitedBy));
        Assert.Null(await ReadInvitationAsync(world.B.Id, id));

        // Its email waits in the outbox under A, naming the invitation and nothing personal; its audit entry is A's too.
        var message = await StagedAsync(id);
        Assert.Equal(world.A.Id.Value, message.SiteId);
        Assert.DoesNotContain("@", message.Payload, StringComparison.Ordinal);
        await using var scope = _harness.Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var audit = await db.Set<AuditEntry>().AsNoTracking().SingleAsync(e => e.TargetId == id.ToString());
        Assert.Equal(
            (SiteAudit.MemberInvited, world.A.Owner.Value, world.A.Id.Value, SiteAudit.InvitationTargetKind),
            (audit.Action, audit.ActorId!.Value, audit.SiteId!.Value, audit.TargetKind));
    }

    [Fact]
    public async Task Only_an_owner_of_the_named_site_may_invite()
    {
        var world = await _harness.SeedAsync("inv-who");
        var missing = $"none-{Guid.NewGuid():N}"[..30];

        foreach (var account in new[] { world.A.Editor, world.A.Author, world.Shared })
        {
            using var client = _harness.ClientFor(account);
            using var refused = await InviteAsync(client, world.A.Slug, NewEmail(), "author");
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(SiteErrors.SiteRoleRequired.Code, await refused.CodeAsync());
        }

        // Suspended on A, a stranger, and B's owner: each is answered as if A were not there at all.
        foreach (var account in new[] { world.A.Suspended, world.SuspendedOnA, world.Stranger, world.B.Owner })
        {
            using var client = _harness.ClientFor(account);
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
            {
                using var real = await InviteAsync(client, world.A.Slug, NewEmail(), "author");
                using var none = await InviteAsync(client, missing, NewEmail(), "author");
                await TwoSiteChecks.AssertIndistinguishableAsync(real, none, HttpStatusCode.NotFound, SiteErrors.SiteNotFound.Code);
            });
        }

        Assert.Equal(0, await CountInvitationsAsync(world.A.Id));

        // The shared account is an Author on A and the Owner of B: it carries a different role into each.
        using var shared = _harness.ClientFor(world.Shared);
        using var onB = await InviteAsync(shared, world.B.Slug, NewEmail(), "owner");
        Assert.Equal(HttpStatusCode.Accepted, onB.StatusCode);
        Assert.Equal(1, await CountInvitationsAsync(world.B.Id));
        Assert.Equal(0, await CountInvitationsAsync(world.A.Id));
    }

    [Theory]
    [InlineData("""{"email":"pat@example.com"}""", "role")]
    [InlineData("""{"email":"pat@example.com","role":null}""", "role")]
    [InlineData("""{"email":"not an address","role":"editor"}""", "email")]
    [InlineData("""{"email":" pat@example.com","role":"editor"}""", "email")]
    [InlineData("""{"role":"editor"}""", "email")]
    public async Task A_bad_invitation_names_its_field_and_invites_no_one(string body, string field)
    {
        var world = await _harness.SeedAsync("inv-bad");
        using var owner = _harness.ClientFor(world.A.Owner);

        using var refused = await owner.PostAsync(
            Invitations(world.A.Slug),
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _), problem.RootElement.GetRawText());
        Assert.Equal(0, await CountInvitationsAsync(world.A.Id));
    }

    // A missing role must never default to the strongest one.
    [Fact]
    public async Task An_unknown_role_is_refused()
    {
        var world = await _harness.SeedAsync("inv-role");
        using var owner = _harness.ClientFor(world.A.Owner);

        using var refused = await InviteAsync(owner, world.A.Slug, NewEmail(), "admin");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(0, await CountInvitationsAsync(world.A.Id));
    }

    [Fact]
    public async Task Accepting_joins_the_invited_site_with_the_invited_role_and_nothing_else()
    {
        var world = await _harness.SeedAsync("inv-join");
        var email = NewEmail();
        var invitee = await AccountAsync(email);
        var id = await InviteOkAsync(world.A, email, "editor");
        var sent = await DeliverAsync(id);
        Assert.Equal(email, sent.To);
        Assert.Contains("Sunday Supper", sent.TextBody, StringComparison.Ordinal);
        Assert.Contains("an editor", sent.TextBody, StringComparison.Ordinal);
        Assert.Contains("7 days", sent.TextBody, StringComparison.Ordinal);

        using var client = _harness.ClientFor(invitee);
        HttpResponseMessage? response = null;
        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
            response = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest(TokenFrom(sent))));

        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
            Assert.Equal(
                new SiteSummary(world.A.Slug, "Sunday Supper", SiteStatus.Live, SiteRole.Editor, world.A.Host),
                await response.Content.ReadFromJsonAsync<SiteSummary>(Json));
        }

        await using var scope = _harness.Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var memberships = await db.Set<SiteMember>().AsNoTracking().Where(m => m.AccountId == invitee).ToListAsync();
        var member = Assert.Single(memberships);
        Assert.Equal((world.A.Id, SiteRole.Editor, SiteMemberStatus.Active), (member.SiteId, member.Role, member.Status));

        var invitation = await ReadInvitationAsync(world.A.Id, id);
        Assert.Equal((SiteInvitationStatus.Accepted, invitee), (invitation!.Status, invitation.AcceptedBy));
        Assert.NotNull(invitation.AcceptedAt);
        var audit = await db.Set<AuditEntry>().AsNoTracking()
            .SingleAsync(e => e.Action == SiteAudit.MemberJoined && e.TargetId == id.ToString());
        Assert.Equal((invitee.Value, world.A.Id.Value), (audit.ActorId!.Value, audit.SiteId!.Value));

        // The new member reaches A by its slug, and still not B.
        using var onA = await client.GetAsync($"/api/v1/sites/{world.A.Slug}");
        using var onB = await client.GetAsync($"/api/v1/sites/{world.B.Slug}");
        Assert.Equal(HttpStatusCode.OK, onA.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, onB.StatusCode);
    }

    [Fact]
    public async Task An_invitation_works_once()
    {
        var world = await _harness.SeedAsync("inv-once");
        var email = NewEmail();
        var invitee = await AccountAsync(email);
        var id = await InviteOkAsync(world.A, email, "author");
        var token = TokenFrom(await DeliverAsync(id));
        using var client = _harness.ClientFor(invitee);

        using var first = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest(token));
        using var again = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest(token));

        // The retry of an accept whose answer was lost hears that it already worked, as the route's description says.
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(SiteErrors.AlreadyMember.Code, await again.CodeAsync());
        Assert.Equal(SiteInvitationStatus.Accepted, (await ReadInvitationAsync(world.A.Id, id))!.Status);

        // Leaving the site does not bring the link back: it was used.
        await _harness.ChangeAsync(db => db.Remove(db.Set<SiteMember>().Single(m => m.SiteId == world.A.Id && m.AccountId == invitee)));
        using var afterLeaving = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest(token));
        Assert.Equal(SiteErrors.InvitationInvalid.Code, await afterLeaving.CodeAsync());
    }

    [Fact]
    public async Task Only_the_invited_inbox_confirmed_can_accept()
    {
        var world = await _harness.SeedAsync("inv-inbox");
        var email = NewEmail();
        var unconfirmed = await AccountAsync(email, confirmed: false);
        var other = await AccountAsync(NewEmail());
        var id = await InviteOkAsync(world.A, email, "editor");
        var token = TokenFrom(await DeliverAsync(id));

        using (var client = _harness.ClientFor(other))
        {
            using var refused = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest(token));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(SiteErrors.InvitationForOtherAddress.Code, await refused.CodeAsync());
            Assert.DoesNotContain(email, await refused.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        // An account with no row at all is someone else's inbox too.
        using (var client = _harness.ClientFor(TwoSiteHarness.NewAccount()))
        {
            using var refused = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest(token));
            Assert.Equal(SiteErrors.InvitationForOtherAddress.Code, await refused.CodeAsync());
        }

        using (var client = _harness.ClientFor(unconfirmed))
        {
            using var refused = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest(token));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(AccountErrors.EmailNotVerified.Code, await refused.CodeAsync());
        }

        // Refusals leave the invitation open: confirming the address and trying again joins.
        Assert.Equal(SiteInvitationStatus.Pending, (await ReadInvitationAsync(world.A.Id, id))!.Status);
        await _harness.ChangeAsync(db => db.Set<ApplicationUser>().Single(u => u.Id == unconfirmed.Value).EmailConfirmed = true);
        using var joined = await _harness.ClientFor(unconfirmed).PostAsJsonAsync(Accept, new AcceptInvitationRequest(token));
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
    }

    // The address compares in any case, as accounts do.
    [Fact]
    public async Task The_invited_address_matches_in_any_case()
    {
        var world = await _harness.SeedAsync("inv-case");
        var email = NewEmail();
        var invitee = await AccountAsync(email);
        var id = await InviteOkAsync(world.A, email.ToUpperInvariant(), "author");

        using var joined = await _harness.ClientFor(invitee)
            .PostAsJsonAsync(Accept, new AcceptInvitationRequest(TokenFrom(await DeliverAsync(id))));

        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
    }

    // Not one of ours, altered, expired, used, replaced, for a deleted site, or sealed for B but naming A's invitation:
    // one answer for every link that cannot be used, byte for byte, so a link says nothing about the site it names.
    [Fact]
    public async Task Every_unusable_link_gets_the_same_answer()
    {
        var world = await _harness.SeedAsync("inv-dead");
        var gone = await _harness.SeedAsync("inv-gone");
        var email = NewEmail();
        var invitee = await AccountAsync(email);
        using var client = _harness.ClientFor(invitee);

        var now = Now;
        // One open invitation per address on a site, so the dead ones go to other addresses: a dead link is refused before
        // anyone's address is asked about.
        var expired = await SeedInvitationAsync(world.A.Id, NewEmail(), now - TimeSpan.FromDays(8), now - TimeSpan.FromDays(1));
        var accepted = await SeedInvitationAsync(world.A.Id, email, now, now + TimeSpan.FromDays(7), SiteInvitationStatus.Accepted);
        var revoked = await SeedInvitationAsync(world.A.Id, email, now, now + TimeSpan.FromDays(7), SiteInvitationStatus.Revoked);
        var onGone = await SeedInvitationAsync(gone.A.Id, email, now, now + TimeSpan.FromDays(7));
        await _harness.ChangeAsync(db => db.Set<Site>().Single(s => s.Id == gone.A.Id).Status = SiteStatus.Deleted);
        var open = await SeedInvitationAsync(world.A.Id, email, now, now + TimeSpan.FromDays(7));
        var good = Token(world.A.Id, open);
        var altered = good[..^4] + (good[^4] == 'A' ? 'B' : 'A') + good[^3..];

        using var baseline = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest("not-a-token"));
        foreach (var token in new[]
        {
            altered,
            Token(world.A.Id, expired),
            Token(world.A.Id, accepted),
            Token(world.A.Id, revoked),
            Token(gone.A.Id, onGone),
            Token(world.B.Id, open),
            Token(world.A.Id, new SiteInvitationId(Guid.NewGuid())),
            Token(new SiteId(Guid.NewGuid()), open),
        })
        {
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, () =>
                TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
                {
                    using var refused = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest(token));
                    await TwoSiteChecks.AssertIndistinguishableAsync(
                        refused,
                        baseline,
                        HttpStatusCode.BadRequest,
                        SiteErrors.InvitationInvalid.Code);
                }));
        }

        // The good token still works after all of that: nothing above touched it.
        using var joined = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest(good));
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
    }

    [Fact]
    public async Task Inviting_again_replaces_the_open_invitation_on_that_site_alone()
    {
        var world = await _harness.SeedAsync("inv-again");
        var email = NewEmail();
        var invitee = await AccountAsync(email);
        var first = await InviteOkAsync(world.A, email, "author");
        var onB = await InviteOkAsync(world.B, email, "author");
        var firstToken = TokenFrom(await DeliverAsync(first));

        var second = await InviteOkAsync(world.A, email.ToUpperInvariant(), "editor");

        Assert.Equal(SiteInvitationStatus.Revoked, (await ReadInvitationAsync(world.A.Id, first))!.Status);
        Assert.Equal(SiteInvitationStatus.Pending, (await ReadInvitationAsync(world.A.Id, second))!.Status);
        Assert.Equal(SiteInvitationStatus.Pending, (await ReadInvitationAsync(world.B.Id, onB))!.Status);

        // The replaced link stops working, and its email is not sent if it had not gone yet.
        using var client = _harness.ClientFor(invitee);
        using var old = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest(firstToken));
        Assert.Equal(SiteErrors.InvitationInvalid.Code, await old.CodeAsync());
        var before = _email.Sent.Count;
        await DeliverAsync(first, expectEmail: false);
        Assert.Equal(before, _email.Sent.Count);

        using var joined = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest(TokenFrom(await DeliverAsync(second))));
        Assert.Equal(SiteRole.Editor, (await joined.Content.ReadFromJsonAsync<SiteSummary>(Json))!.Role);

        // B's invitation to the same inbox is its own, and still joins B.
        using var joinedB = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest(TokenFrom(await DeliverAsync(onB))));
        Assert.Equal(world.B.Slug, (await joinedB.Content.ReadFromJsonAsync<SiteSummary>(Json))!.Slug);
    }

    // Already an Author on A, or suspended there: an invitation adds a member, never changes one.
    [Fact]
    public async Task An_invitation_never_changes_a_membership()
    {
        var world = await _harness.SeedAsync("inv-member");
        foreach (var status in new[] { SiteMemberStatus.Active, SiteMemberStatus.Suspended })
        {
            var email = NewEmail();
            var member = await AccountAsync(email);
            await _harness.ChangeAsync(db => db.Add(new SiteMember
            {
                SiteId = world.A.Id,
                AccountId = member,
                Role = SiteRole.Author,
                Status = status,
                CreatedAt = Now,
            }));
            var id = await InviteOkAsync(world.A, email, "owner");

            HttpResponseMessage? refused = null;
            await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.A.Id, async () =>
                refused = await _harness.ClientFor(member)
                    .PostAsJsonAsync(Accept, new AcceptInvitationRequest(TokenFrom(await DeliverAsync(id)))));

            using (refused)
            {
                Assert.Equal(HttpStatusCode.Conflict, refused!.StatusCode);
                Assert.Equal(SiteErrors.AlreadyMember.Code, await refused.CodeAsync());
            }

            Assert.Equal(SiteInvitationStatus.Pending, (await ReadInvitationAsync(world.A.Id, id))!.Status);
        }
    }

    // Six requests with one link at the same instant: one membership and one audit entry, whichever answers succeed.
    [Fact]
    public async Task Accepts_at_the_same_instant_add_one_member()
    {
        var world = await _harness.SeedAsync("inv-race");
        var email = NewEmail();
        var invitee = await AccountAsync(email);
        var id = await InviteOkAsync(world.A, email, "author");
        var token = TokenFrom(await DeliverAsync(id));
        var clients = Enumerable.Range(0, 6).Select(_ => _harness.ClientFor(invitee)).ToList();

        var responses = await Task.WhenAll(clients.Select(c => c.PostAsJsonAsync(Accept, new AcceptInvitationRequest(token))));

        // A loser hears either that it is a member now or that the link was used, depending on when it looked.
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            Assert.Contains(
                (refused.StatusCode, await refused.CodeAsync()),
                new[]
                {
                    (HttpStatusCode.Conflict, (string?)SiteErrors.AlreadyMember.Code),
                    (HttpStatusCode.BadRequest, (string?)SiteErrors.InvitationInvalid.Code),
                });
        }

        await using var scope = _harness.Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        Assert.Equal(1, await db.Set<SiteMember>().CountAsync(m => m.AccountId == invitee));
        Assert.Equal(1, await db.Set<AuditEntry>().CountAsync(e => e.Action == SiteAudit.MemberJoined && e.TargetId == id.ToString()));
        foreach (var disposable in responses.Cast<IDisposable>().Concat(clients))
        {
            disposable.Dispose();
        }
    }

    [Fact]
    public async Task A_retried_invite_with_its_key_sends_one_invitation()
    {
        var world = await _harness.SeedAsync("inv-key");
        var email = NewEmail();
        using var owner = _harness.ClientFor(world.A.Owner);
        var key = Guid.NewGuid().ToString("N");

        using var first = await InviteAsync(owner, world.A.Slug, email, "author", key);
        using var replay = await InviteAsync(owner, world.A.Slug, email, "author", key);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues(ApiIdempotency.ReplayedHeader)));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(1, await CountInvitationsAsync(world.A.Id));
    }

    // At least once: a second delivery of one message reaches the provider under the first one's id, and its freshly
    // made link works as well as the first.
    [Fact]
    public async Task A_redelivered_invitation_sends_the_same_email_with_a_link_that_works()
    {
        var world = await _harness.SeedAsync("inv-twice");
        var email = NewEmail();
        var invitee = await AccountAsync(email);
        var id = await InviteOkAsync(world.A, email, "author");

        var first = await DeliverAsync(id);
        var second = await DeliverAsync(id);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal((await StagedAsync(id)).Id, first.Id);
        Assert.StartsWith($"{AppBaseUrl}/join#token=", Link(second), StringComparison.Ordinal);
        using var joined = await _harness.ClientFor(invitee).PostAsJsonAsync(Accept, new AcceptInvitationRequest(TokenFrom(second)));
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
    }

    // The delivery's scope works on the site its message was staged under. Handed another site, the invitation is not
    // there to read; handed none, the message can never be sent.
    [Fact]
    public async Task The_email_is_read_only_as_the_site_it_was_staged_under()
    {
        var world = await _harness.SeedAsync("inv-scope");
        var id = await InviteOkAsync(world.A, NewEmail(), "author");
        var message = await StagedAsync(id);
        var before = _email.Sent.Count;

        await InvokeAsync(message.Payload!, new OutboxDelivery(message.Id, world.B.Id, 1));
        Assert.Equal(before, _email.Sent.Count);

        await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => InvokeAsync(message.Payload!, new OutboxDelivery(message.Id, null, 1)));
        Assert.Equal(before, _email.Sent.Count);
    }

    [Fact]
    public async Task An_expired_or_deleted_sites_invitation_sends_nothing()
    {
        var world = await _harness.SeedAsync("inv-late");
        var now = Now;
        var expired = await SeedInvitationAsync(world.A.Id, NewEmail(), now - TimeSpan.FromDays(8), now - TimeSpan.FromDays(1));
        var onB = await SeedInvitationAsync(world.B.Id, NewEmail(), now, now + TimeSpan.FromDays(7));
        await _harness.ChangeAsync(db => db.Set<Site>().Single(s => s.Id == world.B.Id).Status = SiteStatus.Deleted);
        var before = _email.Sent.Count;

        await InvokeAsync(Payload(expired), new OutboxDelivery(Guid.NewGuid(), world.A.Id, 1));
        await InvokeAsync(Payload(onB), new OutboxDelivery(Guid.NewGuid(), world.B.Id, 1));

        Assert.Equal(before, _email.Sent.Count);
    }

    // B's Owner joining A through A's invitation: A gains a member, and B's tenant root is untouched.
    [Fact]
    public async Task An_owner_elsewhere_joins_only_the_inviting_site()
    {
        var world = await _harness.SeedAsync("inv-cross");
        var email = NewEmail();
        var bOwner = await AccountAsync(email);
        await _harness.ChangeAsync(db => db.Add(new SiteMember
        {
            SiteId = world.B.Id,
            AccountId = bOwner,
            Role = SiteRole.Owner,
            Status = SiteMemberStatus.Active,
            CreatedAt = Now,
        }));
        var id = await InviteOkAsync(world.A, email, "author");
        var token = TokenFrom(await DeliverAsync(id));

        HttpResponseMessage? response = null;
        await TwoSiteChecks.AssertTenantRootUnchangedAsync(_harness.Services, world.B.Id, async () =>
            response = await _harness.ClientFor(bOwner).PostAsJsonAsync(Accept, new AcceptInvitationRequest(token)));

        using (response)
        {
            Assert.Equal(world.A.Slug, (await response!.Content.ReadFromJsonAsync<SiteSummary>(Json))!.Slug);
        }

        await using var scope = _harness.Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var roles = await db.Set<SiteMember>().AsNoTracking().Where(m => m.AccountId == bOwner)
            .Select(m => new { m.SiteId, m.Role }).ToListAsync();
        Assert.Equal(2, roles.Count);
        Assert.Contains(roles, r => r.SiteId == world.A.Id && r.Role == SiteRole.Author);
        Assert.Contains(roles, r => r.SiteId == world.B.Id && r.Role == SiteRole.Owner);
    }

    // A refused accept files nothing under either site, though accepting enters the link's site before it refuses.
    [Fact]
    public async Task A_refused_accept_records_nothing()
    {
        var world = await _harness.SeedAsync("inv-quiet");
        var other = await AccountAsync(NewEmail());
        var open = await SeedInvitationAsync(world.A.Id, NewEmail(), Now, Now + TimeSpan.FromDays(7));
        var expired = await SeedInvitationAsync(world.A.Id, NewEmail(), Now - TimeSpan.FromDays(8), Now - TimeSpan.FromDays(1));
        using var client = _harness.ClientFor(other);

        foreach (var token in new[] { Token(world.A.Id, open), Token(world.A.Id, expired), Token(world.B.Id, open) })
        {
            using var refused = await client.PostAsJsonAsync(Accept, new AcceptInvitationRequest(token));
            Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);
        }

        await using var scope = _harness.Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        Guid?[] sites = [world.A.Id.Value, world.B.Id.Value];
        Assert.False(await db.Set<AuditEntry>().AnyAsync(e => sites.Contains(e.SiteId)));
        Assert.False(await db.Set<OutboxMessage>().AnyAsync(m => sites.Contains(m.SiteId)));
        Assert.False(await db.Set<SiteMember>().AnyAsync(m => m.AccountId == other));
    }

    // Each site has its own cap on open invitations: B's never count toward A's, an expired one counts toward neither, and
    // inviting an address again replaces its invitation rather than spending another place.
    [Fact]
    public async Task Each_site_has_its_own_cap_on_open_invitations()
    {
        await using var harness = await TwoSiteHarness.CreateAsync(
            database,
            withApi: true,
            apiSettings: new Dictionary<string, string?> { ["Sites:MaxOpenInvitations"] = "2" },
            apiServices: WorkerHalf(new RecordingEmailGateway()));
        var world = await harness.SeedAsync("inv-cap");
        using var aOwner = harness.ClientFor(world.A.Owner);
        using var bOwner = harness.ClientFor(world.B.Owner);
        var now = harness.ApiServices.GetRequiredService<IClock>().UtcNow;
        await harness.ChangeAsync(world.A.Id, db => db.Add(new SiteInvitation
        {
            Id = new SiteInvitationId(Guid.NewGuid()),
            Email = "old@example.com",
            NormalizedEmail = "OLD@EXAMPLE.COM",
            Role = SiteRole.Author,
            Status = SiteInvitationStatus.Pending,
            InvitedBy = world.A.Owner,
            CreatedAt = now - TimeSpan.FromDays(8),
            ExpiresAt = now - TimeSpan.FromDays(1),
        }));

        foreach (var address in new[] { NewEmail(), NewEmail() })
        {
            using var onB = await InviteAsync(bOwner, world.B.Slug, address, "author");
            Assert.Equal(HttpStatusCode.Accepted, onB.StatusCode);
        }

        var first = NewEmail();
        foreach (var address in new[] { first, NewEmail() })
        {
            using var onA = await InviteAsync(aOwner, world.A.Slug, address, "author");
            Assert.Equal(HttpStatusCode.Accepted, onA.StatusCode);
        }

        using var full = await InviteAsync(aOwner, world.A.Slug, NewEmail(), "author");
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Equal(SiteErrors.TooManyInvitations.Code, await full.CodeAsync());

        using var again = await InviteAsync(aOwner, world.A.Slug, first, "editor");
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
    }

    // Two invitations to one address at the same instant leave one open: the later replaces the earlier, as one after the
    // other would.
    [Fact]
    public async Task Invitations_to_one_address_at_the_same_instant_leave_one_open()
    {
        var world = await _harness.SeedAsync("inv-twin");
        var email = NewEmail();
        var clients = Enumerable.Range(0, 6).Select(_ => _harness.ClientFor(world.A.Owner)).ToList();

        var responses = await Task.WhenAll(clients.Select(c => InviteAsync(c, world.A.Slug, email, "author")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        await using (var scope = _harness.In(world.A.Id))
        {
            var statuses = await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>()
                .Set<SiteInvitation>().AsNoTracking().Select(i => i.Status).ToListAsync();
            Assert.Equal(6, statuses.Count);
            Assert.Single(statuses, s => s == SiteInvitationStatus.Pending);
        }

        foreach (var disposable in responses.Cast<IDisposable>().Concat(clients))
        {
            disposable.Dispose();
        }
    }

    // The Worker's half of invitation email, in this host: the handler that sends it, through a recording gateway.
    internal static Action<IServiceCollection> WorkerHalf(RecordingEmailGateway email) => services =>
    {
        services.AddSingleton<IEmailGateway>(email);
        services.AddSiteInvitationEmailDelivery(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = AppBaseUrl })
            .Build());
    };

    internal static string Invitations(string slug) => $"/api/v1/sites/{slug}/invitations";

    internal static async Task<HttpResponseMessage> InviteAsync(
        HttpClient client,
        string slug,
        string email,
        string role,
        string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Invitations(slug))
        {
            Content = JsonContent.Create(new { email, role }),
        };
        if (key is not null)
        {
            request.Headers.Add(ApiIdempotency.KeyHeader, key);
        }

        return await client.SendAsync(request);
    }

    internal static string NewEmail(string name = "cook") => $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";

    private async Task<SiteInvitationId> InviteOkAsync(SeededSite site, string email, string role)
    {
        using var owner = _harness.ClientFor(site.Owner);
        using var response = await InviteAsync(owner, site.Slug, email, role);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return new SiteInvitationId((await response.Content.ReadFromJsonAsync<SiteInvitationDetail>(Json))!.Id);
    }

    private async Task<AccountId> AccountAsync(string email, bool confirmed = true)
    {
        var id = Guid.NewGuid();
        await _harness.ChangeAsync(db => db.Add(new ApplicationUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = confirmed,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        }));
        return new AccountId(id);
    }

    private SiteInvitation AddInvitation(PantryPressDbContext db, string email, SiteRole role)
    {
        var now = Now;
        var invitation = new SiteInvitation
        {
            Id = new SiteInvitationId(Guid.NewGuid()),
            Email = email,
            NormalizedEmail = EmailAddresses.Normalize(email),
            Role = role,
            Status = SiteInvitationStatus.Pending,
            InvitedBy = TwoSiteHarness.NewAccount(),
            CreatedAt = now,
            ExpiresAt = now + TimeSpan.FromDays(7),
        };
        db.Add(invitation);
        return invitation;
    }

    private async Task<SiteInvitationId> SeedInvitationAsync(
        SiteId site,
        string email,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        SiteInvitationStatus status = SiteInvitationStatus.Pending)
    {
        var id = new SiteInvitationId(Guid.NewGuid());
        await _harness.ChangeAsync(site, db => db.Add(new SiteInvitation
        {
            Id = id,
            Email = email,
            NormalizedEmail = EmailAddresses.Normalize(email),
            Role = SiteRole.Author,
            Status = status,
            InvitedBy = TwoSiteHarness.NewAccount(),
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            AcceptedBy = status == SiteInvitationStatus.Accepted ? TwoSiteHarness.NewAccount() : null,
            AcceptedAt = status == SiteInvitationStatus.Accepted ? createdAt : null,
        }));
        return id;
    }

    private async Task<SiteInvitation?> ReadInvitationAsync(SiteId site, SiteInvitationId id)
    {
        await using var scope = _harness.In(site);
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>()
            .Set<SiteInvitation>().AsNoTracking().SingleOrDefaultAsync(i => i.Id == id);
    }

    private async Task<int> CountInvitationsAsync(SiteId site)
    {
        await using var scope = _harness.In(site);
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<SiteInvitation>().CountAsync();
    }

    private string Token(SiteId site, SiteInvitationId invitation)
    {
        using var scope = _harness.ApiServices.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SiteInvitationTokens>().Create(site, invitation);
    }

    private async Task<OutboxMessage> StagedAsync(SiteInvitationId id)
    {
        await using var scope = _harness.Scope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var needle = id.Value.ToString();
        return await db.Set<OutboxMessage>().AsNoTracking()
            .SingleAsync(m => m.Type == SiteInvitationEmailMessage.OutboxType && m.Payload!.Contains(needle));
    }

    // Delivers the invitation's staged message as the Worker would: the registration enters the delivery's site and runs
    // the handler in a fresh scope.
    private async Task<EmailMessage> DeliverAsync(SiteInvitationId id, bool expectEmail = true)
    {
        var message = await StagedAsync(id);
        var before = _email.Sent.Count;
        await InvokeAsync(message.Payload!, new OutboxDelivery(message.Id, new SiteId(message.SiteId!.Value), 1));
        if (!expectEmail)
        {
            return null!;
        }

        Assert.Equal(before + 1, _email.Sent.Count);
        return _email.Sent[^1];
    }

    private async Task InvokeAsync(string payload, OutboxDelivery delivery)
    {
        var registration = _harness.ApiServices.GetServices<OutboxHandlerRegistration>()
            .Single(r => r.Type == SiteInvitationEmailMessage.OutboxType);
        await using var scope = _harness.ApiServices.CreateAsyncScope();
        await registration.InvokeAsync(scope.ServiceProvider, payload, delivery, CancellationToken.None);
    }

    private string Payload(SiteInvitationId id) =>
        JsonSerializer.Serialize(new SiteInvitationEmailMessage(id.Value), OutboxRules.Json);

    private static string Link(EmailMessage email) =>
        email.TextBody.Split('\n').Single(line => line.StartsWith($"{AppBaseUrl}/join#token=", StringComparison.Ordinal));

    private static string TokenFrom(EmailMessage email) =>
        Uri.UnescapeDataString(Link(email)[$"{AppBaseUrl}/join#token=".Length..]);
}
