using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Modules.Sites;
using PantryPress.Tests.Fakes;

namespace PantryPress.Tests.Sites;

// PP2.11: what the Worker registers for the Sites module. With an email provider it sends invitation email and needs the
// admin app's origin for the link; without one it claims none, and the messages wait in the outbox, as account email does.
public sealed class SitesWorkerRegistrationTests
{
    [Fact]
    public void With_a_provider_and_an_origin_the_worker_has_a_handler_for_invitation_email()
    {
        using var services = Worker(new RecordingEmailGateway(), "https://app.localhost");

        Assert.Equal("https://app.localhost", services.GetRequiredService<IOptions<SiteInvitationEmailOptions>>().Value.AppBaseUrl);
        Assert.Contains(services.GetServices<OutboxHandlerRegistration>(), r => r.Type == SiteInvitationEmailMessage.OutboxType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("app.localhost")]
    [InlineData("http://app.pantrypress.app")]
    [InlineData("https://app.pantrypress.app/admin")]
    public void A_worker_that_sends_invitations_needs_the_admin_origin(string? origin)
    {
        using var services = Worker(new RecordingEmailGateway(), origin);

        var error = Assert.Throws<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<SiteInvitationEmailOptions>>().Value);
        Assert.Contains("Sites:AppBaseUrl", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_worker_that_sends_invitations_needs_a_provider()
    {
        using var services = Worker(gateway: null, "https://app.localhost");

        var error = Assert.Throws<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<SiteInvitationEmailOptions>>().Value);
        Assert.Contains("email gateway", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_worker_with_no_email_provider_claims_no_invitation_email_and_needs_no_origin()
    {
        using var services = Worker(gateway: null, appBaseUrl: null, sendsEmail: false);

        Assert.DoesNotContain(services.GetServices<OutboxHandlerRegistration>(), r => r.Type == SiteInvitationEmailMessage.OutboxType);
    }

    private static ServiceProvider Worker(IEmailGateway? gateway, string? appBaseUrl, bool sendsEmail = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        if (gateway is not null)
        {
            services.AddSingleton(gateway);
        }

        services.AddSitesWorker(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = appBaseUrl })
                .Build(),
            sendsEmail);
        return services.BuildServiceProvider();
    }
}
