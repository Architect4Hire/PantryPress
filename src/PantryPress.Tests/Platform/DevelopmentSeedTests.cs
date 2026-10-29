using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Platform.Business;
using PantryPress.Domain.Modules.Platform.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Tests.Persistence;
using PantryPress.Tests.TwoSite;

namespace PantryPress.Tests.Platform;

// Inside the namespace, so the Sites module's types win over the PantryPress.Tests.Site namespace.
using PantryPress.Domain.Modules.Sites;

// PP3.9a: the local development seed against the schema the migration service applies, composed exactly as the
// migration job's Development run composes it. The shared fixture's own job seeds nothing (EphemeralAppHost decides the
// password, and leaves it empty), so these tests own the seed's accounts and sites.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class DevelopmentSeedTests(MigratedDatabase database)
{
    private const string Password = "Not-a-real-password-PP39a";

    private static CancellationToken Timeout => CancellationToken.None;

    [Fact]
    public async Task The_first_run_creates_the_accounts_and_their_sites_and_a_second_run_changes_nothing()
    {
        await using var services = await SeedServicesAsync(Password);
        var outboxBefore = await OutboxCountAsync(services);

        // Two look-alike sites of the harness's stand beside the seed's while it writes the tenant root twice - the
        // second site created while the first is already there (tenancy.md, "Two-site test").
        await using var harness = await TwoSiteHarness.CreateAsync(database);
        var world = await harness.SeedAsync("devseed");
        DevelopmentSeedReport first = default!;

        await TwoSiteChecks.AssertTenantRootUnchangedAsync(
            harness.Services,
            world.A.Id,
            () => TwoSiteChecks.AssertTenantRootUnchangedAsync(
                harness.Services,
                world.B.Id,
                async () => first = await RunAsync(services)));

        Assert.Equal(new DevelopmentSeedReport(true, true, 2, 2), first);

        // Two accounts, both with a confirmed address and the one password, and no platform role.
        var owner = await AccountAsync(services, DevelopmentSeedData.OwnerEmail);
        var editor = await AccountAsync(services, DevelopmentSeedData.EditorEmail);
        foreach (var account in new[] { owner, editor })
        {
            Assert.True(account.EmailConfirmed);
            Assert.True(await CheckPasswordAsync(services, account, Password));
            Assert.Empty(await RolesAsync(services, account));
        }

        // The sites, exactly as PP2.10 creates one: the site, its Owner membership, and its canonical subdomain.
        foreach (var request in DevelopmentSeedData.SiteRequests)
        {
            var site = await SiteAsync(services, request.Slug);
            Assert.Equal((request.Name, request.Tagline, SiteStatus.Live), (site.Name, site.Tagline, site.Status));
            Assert.Equal((request.TimeZone, request.Locale), (site.TimeZone, site.Locale));

            var member = Assert.Single(await MembersAsync(services, site.Id));
            Assert.Equal((new AccountId(owner.Id), SiteRole.Owner, SiteMemberStatus.Active), (member.AccountId, member.Role, member.Status));

            var host = Assert.Single(await HostsAsync(services, site.Id));
            Assert.Equal($"{request.Slug}.{TestDatabase.SubdomainZone}", host.Host);
            Assert.Equal((SiteHostKind.Subdomain, true, SiteHostStatus.Active), (host.Kind, host.IsCanonical, host.Status));
        }

        // The second account belongs to no site, so an invitation can be tried on it.
        Assert.Empty(await MembershipsOfAsync(services, new AccountId(editor.Id)));

        // Audited as the facades record it: the accounts by the development seed, the sites by their owner (PP1.11).
        foreach (var account in new[] { owner, editor })
        {
            var entry = Assert.Single(await AuditEntriesAsync(services, AccountAudit.Target(new AccountId(account.Id))));
            Assert.Equal(AccountAudit.DevelopmentSeeded, entry.Action);
            Assert.Equal("development-seed", entry.ActorKind);
            Assert.Null(entry.ActorId);
            Assert.Null(entry.SiteId);
        }

        foreach (var request in DevelopmentSeedData.SiteRequests)
        {
            var site = await SiteAsync(services, request.Slug);
            var entry = Assert.Single(await AuditEntriesAsync(services, SiteAudit.Target(site.Id)));
            Assert.Equal((SiteAudit.Created, "account", (Guid?)owner.Id, (Guid?)site.Id.Value), (entry.Action, entry.ActorKind, entry.ActorId, entry.SiteId));
        }

        // No email and nothing to send: the seed stages no outbox message of any kind (PP1.10b).
        Assert.Equal(outboxBefore, await OutboxCountAsync(services));

        // Nothing secret or personal in the log, as everywhere else: ids and counts only.
        AssertNothingSensitiveLogged(services);

        // A second run finds everything there and leaves it alone - including, for each seeded site, every tenant-root
        // row of the other one (tenancy.md, "Two-site test").
        var accountsBefore = await AccountRowsAsync(services);
        var auditBefore = await AuditCountAsync(services);
        var sites = await Task.WhenAll(DevelopmentSeedData.SiteRequests.Select(r => SiteAsync(services, r.Slug)));
        DevelopmentSeedReport second = default!;

        await TwoSiteChecks.AssertTenantRootUnchangedAsync(
            services,
            sites[0].Id,
            () => TwoSiteChecks.AssertTenantRootUnchangedAsync(services, sites[1].Id, async () => second = await RunAsync(services)));

        Assert.Equal(new DevelopmentSeedReport(true, true, 0, 0), second);
        Assert.Equal(accountsBefore, await AccountRowsAsync(services));
        Assert.Equal(auditBefore, await AuditCountAsync(services));
        Assert.Equal(outboxBefore, await OutboxCountAsync(services));
    }

    [Fact]
    public async Task With_no_password_the_seed_writes_nothing_and_says_how_to_set_one()
    {
        await using var services = await SeedServicesAsync(password: string.Empty);
        var accounts = await AccountRowsAsync(services);
        var sites = await SiteRowsAsync(services);
        var audit = await AuditCountAsync(services);

        var report = await RunAsync(services);

        Assert.Equal(DevelopmentSeedReport.NotConfigured, report);
        Assert.Equal(accounts, await AccountRowsAsync(services));
        Assert.Equal(sites, await SiteRowsAsync(services));
        Assert.Equal(audit, await AuditCountAsync(services));
        var line = Assert.Single(
            services.GetFakeLogCollector().GetSnapshot(),
            r => r.Level == LogLevel.Information && r.Message.Contains("no development seed password", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("dotnet user-secrets set DevSeed:Password", line.Message, StringComparison.Ordinal);
    }

    private Task<ServiceProvider> SeedServicesAsync(string password) =>
        TestDatabase.CreateDevelopmentSeedServicesAsync(database.App, password, Timeout);

    private static async Task<DevelopmentSeedReport> RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDevelopmentSeeder>().RunAsync(Timeout);
    }

    // No password, address, or account name in any log line or its exception: the seed says counts, ids, and slugs.
    private static void AssertNothingSensitiveLogged(IServiceProvider services)
    {
        var secrets = new[] { Password }.Concat(DevelopmentSeedData.Emails).ToList();
        Assert.All(services.GetFakeLogCollector().GetSnapshot(), record => Assert.All(secrets, secret =>
        {
            Assert.DoesNotContain(secret, record.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, record.Exception?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.All(record.StructuredState ?? [], pair =>
                Assert.DoesNotContain(secret, pair.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }));
    }

    private static async Task<ApplicationUser> AccountAsync(IServiceProvider services, string email)
    {
        await using var scope = services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        return Assert.IsType<ApplicationUser>(user);
    }

    private static async Task<bool> CheckPasswordAsync(IServiceProvider services, ApplicationUser account, string password)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.CheckPasswordAsync(await users.FindByIdAsync(account.Id.ToString()) ?? account, password);
    }

    private static async Task<IList<string>> RolesAsync(IServiceProvider services, ApplicationUser account)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetRolesAsync(await users.FindByIdAsync(account.Id.ToString()) ?? account);
    }

    // The seed's own rows, read working on no site: Sites, SiteMembers, and SiteHosts are the tenant root, which has no
    // query filter behind it (tenancy.md, "Tenant root").
    private static async Task<Site> SiteAsync(IServiceProvider services, string slug)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<Site>()
            .AsNoTracking()
            .SingleAsync(s => s.Slug == slug, Timeout);
    }

    private static async Task<List<SiteMember>> MembersAsync(IServiceProvider services, SiteId site)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<SiteMember>()
            .AsNoTracking()
            .Where(m => m.SiteId == site)
            .ToListAsync(Timeout);
    }

    private static async Task<List<SiteHost>> HostsAsync(IServiceProvider services, SiteId site)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<SiteHost>()
            .AsNoTracking()
            .Where(h => h.SiteId == site)
            .ToListAsync(Timeout);
    }

    private static async Task<List<SiteId>> MembershipsOfAsync(IServiceProvider services, AccountId account)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<SiteMember>()
            .AsNoTracking()
            .Where(m => m.AccountId == account)
            .Select(m => m.SiteId)
            .ToListAsync(Timeout);
    }

    private static async Task<List<AuditEntry>> AuditEntriesAsync(IServiceProvider services, AuditTarget target)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<AuditEntry>()
            .AsNoTracking()
            .Where(e => e.TargetKind == target.Kind && e.TargetId == target.Id)
            .OrderBy(e => e.Id)
            .ToListAsync(Timeout);
    }

    private static async Task<int> AuditCountAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<AuditEntry>().CountAsync(Timeout);
    }

    private static async Task<int> OutboxCountAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Set<OutboxMessage>().CountAsync(Timeout);
    }

    // Every account as stored, so a changed password hash, security stamp, or confirmation flag shows.
    private static async Task<string> AccountRowsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<PantryPressDbContext>().Users
            .AsNoTracking()
            .OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.EmailConfirmed, u.PasswordHash, u.SecurityStamp, u.LockoutEnd })
            .ToListAsync(Timeout);
        return string.Join("\n", rows.Select(r => $"{r.Id} {r.EmailConfirmed} {r.PasswordHash} {r.SecurityStamp} {r.LockoutEnd}"));
    }

    private static async Task<string> SiteRowsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PantryPressDbContext>();
        var sites = await db.Set<Site>().AsNoTracking().OrderBy(s => s.Slug).Select(s => new { s.Id, s.Slug, s.Status }).ToListAsync(Timeout);
        var hosts = await db.Set<SiteHost>().AsNoTracking().OrderBy(h => h.Host).Select(h => h.Host).ToListAsync(Timeout);
        return string.Join("\n", sites.Select(s => $"{s.Id} {s.Slug} {s.Status}").Concat(hosts));
    }
}
