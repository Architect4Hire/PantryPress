using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.Validation;
using PantryPress.Domain.Modules.Platform;
using PantryPress.Domain.Modules.Platform.Contracts;
using PantryPress.Domain.Modules.Platform.Business;
using PantryPress.Domain.Modules.Platform.Facades;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.MigrationService;

namespace PantryPress.Tests.Platform;

// PP3.9a: the three guards that keep the development seed in local Development. The first is the AppHost's
// (DevelopmentSeedWiringTests); these are the other two - what the host registers, and what it refuses to start with -
// and that no configuration value can turn the seed on. No database is touched: nothing here gets as far as a query.
public sealed class DevelopmentSeedGuardTests
{
    // Never connected to; only parsed.
    private const string UnusedConnectionString = "Server=127.0.0.1,1;Database=pantrypress;Integrated Security=false";

    private const string Password = "Not-a-real-password-PP39a";

    private static CancellationToken Timeout => CancellationToken.None;

    [Fact]
    public async Task A_host_outside_development_refuses_to_start_when_it_finds_seed_configuration()
    {
        using var host = BuildHost(Environments.Production, new() { ["DevSeed:Password"] = Password });

        var refused = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(Timeout));

        Assert.Contains("local Development only", string.Join(" ", refused.Failures), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_outside_development_starts_without_seed_configuration_and_registers_nothing_the_seed_needs()
    {
        using var host = BuildHost(Environments.Production, []);
        await host.StartAsync(Timeout);

        await using var scope = host.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IDevelopmentSeeder>();

        // What a deployed migration job runs: the seeder that does nothing, and none of what seeding would take.
        Assert.IsType<DisabledDevelopmentSeeder>(seeder);
        Assert.Equal(DevelopmentSeedReport.Disabled, await seeder.RunAsync(Timeout));
        Assert.Null(scope.ServiceProvider.GetService<IDevelopmentAccounts>());
        Assert.Null(scope.ServiceProvider.GetService<ISitesFacade>());
        await host.StopAsync(Timeout);
    }

    // The one way the seed is turned on is the host's own environment. A setting named like the flag must not reach it,
    // which is why DevelopmentSeedOptions.Enabled has no setter for configuration to bind.
    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public async Task Configuration_cannot_turn_the_seed_on_outside_development(string enabled)
    {
        using var host = BuildHost(Environments.Production, new() { ["DevSeed:Enabled"] = enabled });
        await host.StartAsync(Timeout);

        var options = host.Services.GetRequiredService<IOptions<DevelopmentSeedOptions>>().Value;

        Assert.False(options.Enabled);
        Assert.IsType<DisabledDevelopmentSeeder>(host.Services.GetRequiredService<IDevelopmentSeeder>());
        await host.StopAsync(Timeout);
    }

    [Fact]
    public async Task A_development_host_gets_the_real_seeder_and_what_it_works_through()
    {
        using var host = BuildHost(Environments.Development, new() { ["DevSeed:Password"] = Password }, withDatabase: true);
        await host.StartAsync(Timeout);

        await using var scope = host.Services.CreateAsyncScope();

        Assert.IsType<DevelopmentSeeder>(scope.ServiceProvider.GetRequiredService<IDevelopmentSeeder>());
        Assert.NotNull(scope.ServiceProvider.GetService<IDevelopmentAccounts>());
        Assert.NotNull(scope.ServiceProvider.GetService<ISitesFacade>());
        Assert.True(host.Services.GetRequiredService<IOptions<DevelopmentSeedOptions>>().Value.Enabled);
        await host.StopAsync(Timeout);
    }

    [Fact]
    public async Task A_development_host_with_no_password_says_how_to_set_one_and_does_nothing()
    {
        using var host = BuildHost(Environments.Development, [], withDatabase: true);
        await host.StartAsync(Timeout);

        await using var scope = host.Services.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<IDevelopmentSeeder>()
            .RunAsync(Timeout);

        // Nothing was read, so the unusable connection string was never dialled either.
        Assert.Equal(DevelopmentSeedReport.NotConfigured, report);
        var line = Assert.Single(
            host.Services.GetFakeLogCollector().GetSnapshot(),
            r => r.Level == LogLevel.Information && r.Message.Contains("no development seed password", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("dotnet user-secrets set DevSeed:Password", line.Message, StringComparison.Ordinal);
        await host.StopAsync(Timeout);
    }

    // The addresses the seed uses are ones sign-up itself would accept (PP3.9a's DECIDE), so a seeded account is an
    // ordinary account: the shape rules and the disposable-domain check (ACCT-003) both pass them. Neither can be a real
    // mailbox - .test resolves nowhere (RFC 2606) - so no person's address is in the seed.
    [Fact]
    public async Task The_seeded_addresses_are_ones_sign_up_would_accept()
    {
        var validator = new RegisterAccountRequestValidator();

        foreach (var email in DevelopmentSeedData.Emails)
        {
            var outcome = await validator.ValidateAsync(new RegisterAccountRequest(email, Password), Timeout);

            Assert.True(outcome.IsValid, string.Join("; ", outcome.Errors.Select(e => e.ErrorMessage)));
            Assert.EndsWith(".test", email, StringComparison.Ordinal);
        }
    }

    /// <param name="withDatabase">
    /// Whether to register the context at all. Only a test that resolves the seed's own services needs one, and each
    /// context built adds an Entity Framework internal service provider to the test process, which caps them.
    /// </param>
    private static IHost BuildHost(string environment, Dictionary<string, string?> settings, bool withDatabase = false)
    {
        settings["ConnectionStrings:redis"] = "127.0.0.1:1";
        settings["Sites:SubdomainZone"] = "localhost";

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddFakeLogging();
        builder.Services.AddApplicationClock();
        builder.Services.AddAccountsIdentitySchema();
        if (withDatabase)
        {
            builder.Services.AddPantryPressDbContext((_, options) => options.UseSqlServer(UnusedConnectionString));
        }

        // Exactly the migration job's own two calls (Program.cs), in its order.
        builder.Services.AddPlatformAdministration(builder.Configuration.GetSection("Platform"));
        builder.AddDevelopmentSeed();
        return builder.Build();
    }
}
