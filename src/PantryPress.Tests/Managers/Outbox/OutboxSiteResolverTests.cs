using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Tests.Managers.Outbox;

// PP2.11: the Worker's resolver. A message staged under a site is delivered in a scope that works on that site, so its
// handler reads site-owned rows through the filter as the request that staged it did; account work enters none.
public sealed class OutboxSiteResolverTests
{
    [Fact]
    public async Task A_delivery_works_on_the_site_its_message_was_staged_under_and_account_work_on_none()
    {
        using var services = Services();
        var site = new SiteId(Guid.NewGuid());

        Assert.Equal(site, await DeliverAsync(services, site));
        Assert.Null(await DeliverAsync(services, null));
    }

    // Each delivery has its own scope, so one message's site never carries into the next.
    [Fact]
    public async Task Every_delivery_starts_on_no_site()
    {
        using var services = Services();
        var first = new SiteId(Guid.NewGuid());
        var second = new SiteId(Guid.NewGuid());

        Assert.Equal(first, await DeliverAsync(services, first));
        Assert.Equal(second, await DeliverAsync(services, second));
        Assert.Null(await DeliverAsync(services, null));
    }

    private static ServiceProvider Services() =>
        new ServiceCollection()
            .AddSingleton<SiteSeen>()
            .AddOutboxHandler<SiteProbeWork, SiteProbeWorkHandler>()
            .BuildServiceProvider(validateScopes: true);

    private static async Task<SiteId?> DeliverAsync(ServiceProvider services, SiteId? site)
    {
        var registration = services.GetServices<OutboxHandlerRegistration>().Single();
        await using var scope = services.CreateAsyncScope();
        await registration.InvokeAsync(
            scope.ServiceProvider,
            JsonSerializer.Serialize(new SiteProbeWork("probe"), OutboxRules.Json),
            new OutboxDelivery(Guid.NewGuid(), site, 1),
            CancellationToken.None);
        return services.GetRequiredService<SiteSeen>().Site;
    }

    private sealed record SiteProbeWork(string Name) : IOutboxMessage
    {
        public static string OutboxType => "test.site-probe-work";
    }

    private sealed class SiteSeen
    {
        public SiteId? Site { get; set; }
    }

    private sealed class SiteProbeWorkHandler(ISiteContext site, SiteSeen seen) : IOutboxHandler<SiteProbeWork>
    {
        public Task HandleAsync(SiteProbeWork message, OutboxDelivery delivery, CancellationToken cancellationToken)
        {
            seen.Site = site.Current;
            return Task.CompletedTask;
        }
    }
}
