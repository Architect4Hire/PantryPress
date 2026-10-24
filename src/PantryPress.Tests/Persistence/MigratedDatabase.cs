using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Tests.Hosting;

namespace PantryPress.Tests.Persistence;

// One ephemeral AppHost whose migration service has finished, shared by the test classes that read or write the
// schema or need the running system. It is a collection fixture rather than one per class: each AppHost starts its own
// SQL Server container and five projects, and starting several at once outruns what a developer machine can do inside
// the timeout. It runs without the Worker (PP1.10): its tests move fake clocks, and the Worker sweeps on the real one.
public sealed class MigratedDatabase : IAsyncLifetime
{
    public const string CollectionName = "migrated database";

    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromMinutes(5));
    private IDistributedApplicationTestingBuilder? _appHost;

    /// <summary>
    /// The running AppHost, for tests that build their own services over its database or call its running services.
    /// </summary>
    public DistributedApplication App { get; private set; } = null!;

    /// <summary>The Identity stores as a host registers them (<see cref="TestDatabase.CreateServicesAsync"/>).</summary>
    public ServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _appHost = await EphemeralAppHost.CreateAsync(_timeout.Token, runWorker: false);
        App = await _appHost.BuildAsync(_timeout.Token);
        await App.StartAsync(_timeout.Token);

        var finished = await App.ResourceNotifications.WaitForResourceAsync(
            "migrationservice",
            e => KnownResourceStates.TerminalStates.Contains(e.Snapshot.State?.Text),
            _timeout.Token);
        Assert.Equal(0, finished.Snapshot.ExitCode);

        Services = await TestDatabase.CreateServicesAsync(App, _timeout.Token);
    }

    public async Task DisposeAsync()
    {
        if (Services is not null)
        {
            await Services.DisposeAsync();
        }

        if (App is not null)
        {
            await App.DisposeAsync();
        }

        if (_appHost is not null)
        {
            await _appHost.DisposeAsync();
        }

        _timeout.Dispose();
    }
}

[CollectionDefinition(MigratedDatabase.CollectionName)]
public sealed class MigratedDatabaseCollection : ICollectionFixture<MigratedDatabase>;
