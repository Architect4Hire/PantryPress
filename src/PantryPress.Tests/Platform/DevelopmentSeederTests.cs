using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Platform;
using PantryPress.Domain.Modules.Platform.Business;
using PantryPress.Domain.Modules.Platform.Contracts;
using PantryPress.Domain.Modules.Platform.Facades;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Tests.Platform;

// PP3.9a: what the seed does with each answer it can get, without a database. The expected refusals - an address or a
// slug already taken, an owner whose address is not confirmed - leave a local run working; anything else fails the
// migration job, because a developer must see it.
public sealed class DevelopmentSeederTests
{
    private const string Password = "Not-a-real-password-PP39a";

    private static CancellationToken Timeout => CancellationToken.None;

    [Fact]
    public async Task A_slug_already_held_is_left_alone_and_the_run_still_succeeds()
    {
        var (seeder, _) = Build(SiteErrors.SlugUnavailable);

        var report = await seeder.RunAsync(Timeout);

        Assert.Equal(new DevelopmentSeedReport(true, true, 2, 0), report);
    }

    // The account was there already with its address unconfirmed, so it may own only so many sites (PP2.10). Expected:
    // the seed says so and the job carries on, rather than leaving every dependent unstarted.
    [Fact]
    public async Task An_unconfirmed_owner_stops_the_sites_without_failing_the_run()
    {
        var (seeder, logs) = Build(AccountErrors.EmailNotVerified);

        var report = await seeder.RunAsync(Timeout);

        Assert.Equal(new DevelopmentSeedReport(true, true, 2, 0), report);
        Assert.Equal(
            DevelopmentSeedData.SiteRequests.Count,
            logs.GetSnapshot().Count(r => r.Level == LogLevel.Warning && r.Message.Contains("not confirmed", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Any_other_refusal_fails_the_migration_job()
    {
        var (seeder, _) = Build(SiteErrors.SiteNotFound);

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.RunAsync(Timeout));

        Assert.Contains(SiteErrors.SiteNotFound.Code, failed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_password_the_platform_refuses_seeds_nothing()
    {
        var (seeder, _) = Build(null, DevelopmentAccountOutcome.PasswordRefused);

        var report = await seeder.RunAsync(Timeout);

        // The Accounts module says what to change about the password; the seed writes nothing, sites included.
        Assert.Equal(DevelopmentSeedReport.NotConfigured, report);
    }

    [Fact]
    public async Task Accounts_already_there_count_as_nothing_created()
    {
        var (seeder, _) = Build(null, DevelopmentAccountOutcome.AlreadyThere);

        var report = await seeder.RunAsync(Timeout);

        Assert.Equal(new DevelopmentSeedReport(true, true, 0, DevelopmentSeedData.SiteRequests.Count), report);
    }

    private static (IDevelopmentSeeder Seeder, FakeLogCollector Logs) Build(
        Error? siteRefusal,
        DevelopmentAccountOutcome accounts = DevelopmentAccountOutcome.Created)
    {
        var services = new ServiceCollection();
        services.AddFakeLogging();
        services.AddSingleton<ISitesFacade>(new FakeSitesFacade(siteRefusal));
        services.AddSingleton<IDevelopmentAccounts>(new FakeDevelopmentAccounts(accounts));
        services.AddSingleton<IOptions<DevelopmentSeedOptions>>(
            new OptionsWrapper<DevelopmentSeedOptions>(DevelopmentOptions(Password)));
        services.AddSingleton<IDevelopmentSeeder, DevelopmentSeeder>();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IDevelopmentSeeder>(), provider.GetFakeLogCollector());
    }

    // Enabled is set from the host's environment, never from configuration, so a test says so the same way.
    private static DevelopmentSeedOptions DevelopmentOptions(string password)
    {
        var options = new DevelopmentSeedOptions { Password = password };
        typeof(DevelopmentSeedOptions)
            .GetMethod("MarkDevelopmentHost", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(options, null);
        return options;
    }

    private sealed class FakeDevelopmentAccounts(DevelopmentAccountOutcome outcome) : IDevelopmentAccounts
    {
        public Task<DevelopmentAccount> EnsureAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(outcome == DevelopmentAccountOutcome.PasswordRefused
                ? new DevelopmentAccount(null, outcome)
                : new DevelopmentAccount(new AccountId(Guid.NewGuid()), outcome));
    }

    private sealed class FakeSitesFacade(Error? refusal) : ISitesFacade
    {
        public Task<Result<SiteDetail>> CreateAsync(AccountId account, CreateSiteRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(refusal is { } error
                ? Result<SiteDetail>.Failure(error)
                : Result<SiteDetail>.Success(new SiteDetail(
                    request.Slug,
                    request.Name,
                    request.Tagline ?? string.Empty,
                    SiteStatus.Live,
                    request.TimeZone,
                    request.Locale ?? "en-US",
                    DateTimeOffset.UnixEpoch,
                    SiteRole.Owner,
                    $"{request.Slug}.localhost")));

        public Task<Result<SitePage>> ListAsync(AccountId account, ListSitesQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<SiteDetail>> GetAsync(AccountId account, string siteSlug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<SlugAvailability>> CheckAvailabilityAsync(SlugAvailabilityQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
