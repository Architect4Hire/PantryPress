using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Tests.Fakes;

namespace PantryPress.Tests.Api;

// PP1.10b: account email goes through the transactional outbox. The facade stages it with its save, the Worker sends it,
// and what sits in the outbox names an account and a kind of email - never the address, never a link token.
[Collection(AccountApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AccountEmailOutboxTests(AccountApiFixture fixture)
{
    [Fact]
    public async Task A_sign_up_stages_its_email_with_the_account_and_nothing_personal()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterAsync(email);

        var message = await RowAsync(await StagedIdAsync(account));

        Assert.Equal(AccountEmailMessage.OutboxType, message.Type);
        Assert.Null(message.SiteId);
        Assert.DoesNotContain("@", message.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("token", message.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(account.Value.ToString(), message.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Verification\"", message.Payload, StringComparison.Ordinal);

        // Delivered, the email has the link, and its id is the outbox message's - the same on every redelivery.
        var sent = await fixture.NextEmailToAsync(email, "Confirm your email");
        Assert.Equal(message.Id, sent.Id);
        Assert.Null((await RowAsync(message.Id)).Payload);
    }

    [Fact]
    public async Task A_redelivery_sends_the_same_email_under_the_same_id()
    {
        // At least once: the second delivery of one message reaches the provider with the first one's id, so the mailbox
        // can recognize it, and its fresh link works as well as the first.
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterAsync(email);
        await fixture.DrainEmailAsync();
        var first = fixture.EmailsTo(email).Single();

        await using (var scope = fixture.Api.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IOutboxHandler<AccountEmailMessage>>();
            await handler.HandleAsync(
                new AccountEmailMessage(account.Value, AccountEmailKind.Verification, fixture.Now),
                new OutboxDelivery(first.Id, null, 2),
                CancellationToken.None);
        }

        var both = fixture.EmailsTo(email);
        Assert.Equal(2, both.Count);
        Assert.All(both, m => Assert.Equal(first.Id, m.Id));
        var (linkAccount, token) = AccountApiFixture.ReadLink(both[1], "verify-email");
        using var verified = await fixture.Client().PostAsJsonAsync(
            "/api/v1/account/email/verify",
            new VerifyEmailRequest(linkAccount, token));
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_the_email_still_arrives()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterAsync(email);
        var id = await StagedIdAsync(account);
        fixture.Email.FailWith = new EmailDeliveryException(id, isTransient: true, "busy");
        try
        {
            await fixture.DrainEmailAsync();
            Assert.Empty(fixture.EmailsTo(email));
            Assert.Equal(OutboxState.Pending, (await RowAsync(id)).State);
        }
        finally
        {
            fixture.Email.FailWith = null;
        }

        // Past the backoff, the next delivery sends it.
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        var sent = await fixture.NextEmailToAsync(email, "Confirm your email");
        Assert.Equal(id, sent.Id);
    }

    [Fact]
    public async Task A_refused_recipient_poisons_the_message_instead_of_retrying_it()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterAsync(email);
        var id = await StagedIdAsync(account);
        fixture.Email.FailWith = new EmailDeliveryException(id, isTransient: false, "refused");
        try
        {
            await fixture.DrainEmailAsync();
        }
        finally
        {
            fixture.Email.FailWith = null;
        }

        var row = await RowAsync(id);
        Assert.Equal(OutboxState.Poisoned, row.State);
        Assert.DoesNotContain("@", row.LastError ?? string.Empty, StringComparison.Ordinal);
        Assert.Empty(fixture.EmailsTo(email));
    }

    [Fact]
    public async Task An_address_confirmed_before_the_email_goes_out_gets_no_confirmation_link()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterAsync(email);
        var id = await StagedIdAsync(account);
        await fixture.WithServiceAsync<PantryPressDbContext, int>(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == account.Value);
            user.EmailConfirmed = true;
            return await db.SaveChangesAsync();
        });

        await fixture.DrainEmailAsync();

        Assert.Empty(fixture.EmailsTo(email));
        Assert.Equal(OutboxState.Succeeded, (await RowAsync(id)).State);
    }

    // The account's one staged email, found by its payload before delivery clears it.
    [Fact]
    public async Task A_confirmation_email_that_waited_longer_than_its_link_lives_is_not_sent()
    {
        // The Worker makes the link when it sends (PP1.10b), so a message that waited - a Worker that was down, no
        // provider yet - must not turn a day-old request into a fresh link (PP1.12). The person can ask again.
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterAsync(email);
        var id = await StagedIdAsync(account);

        fixture.Time.Advance(TimeSpan.FromHours(25));
        await fixture.DrainEmailAsync();

        Assert.Empty(fixture.EmailsTo(email));
        Assert.Equal(OutboxState.Succeeded, (await RowAsync(id)).State);
    }

    [Fact]
    public async Task A_reset_asked_for_before_a_password_change_is_not_sent_after_it()
    {
        // A link made at the time would have been voided by the change; one made later must not outlive it either.
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        using (var forgot = await fixture.Client().PostAsJsonAsync(
            "/api/v1/account/password/forgot",
            new ForgotPasswordRequest(email)))
        {
            Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        }

        using (var changed = await fixture.Client(account).PostAsJsonAsync(
            "/api/v1/account/password/change",
            new ChangePasswordRequest(AccountApiFixture.FirstPassphrase, AccountApiFixture.SecondPassphrase)))
        {
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        }

        await fixture.DrainEmailAsync();

        Assert.DoesNotContain(fixture.EmailsTo(email), m => m.Subject.StartsWith("Reset", StringComparison.Ordinal));
        Assert.Contains(fixture.EmailsTo(email), m => m.Subject.StartsWith("Your PantryPress password was changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_email_for_an_account_that_is_gone_finishes_without_a_send()
    {
        await using var scope = fixture.Api.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IOutboxHandler<AccountEmailMessage>>();
        var before = fixture.Email.Sent.Count;

        await handler.HandleAsync(
            new AccountEmailMessage(Guid.NewGuid(), AccountEmailKind.PasswordChanged, fixture.Now),
            new OutboxDelivery(Guid.NewGuid(), null, 1),
            CancellationToken.None);

        Assert.Equal(before, fixture.Email.Sent.Count);
    }

    [Fact]
    public async Task An_address_no_email_can_be_written_to_is_poisoned_at_once()
    {
        // Retrying cannot help, so it is not retried for an hour or two first (PP1.12).
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        await fixture.WithServiceAsync<PantryPressDbContext, int>(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == account.Value);
            user.Email = "not an address";
            return await db.SaveChangesAsync();
        });
        await using var scope = fixture.Api.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IOutboxHandler<AccountEmailMessage>>();

        var failure = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => handler.HandleAsync(
            new AccountEmailMessage(account.Value, AccountEmailKind.PasswordChanged, fixture.Now),
            new OutboxDelivery(Guid.NewGuid(), null, 1),
            CancellationToken.None));
        Assert.DoesNotContain("not an address", failure.Message, StringComparison.Ordinal);
    }

    private Task<Guid> StagedIdAsync(AccountId account) =>
        fixture.WithServiceAsync<PantryPressDbContext, Guid>(db => db.Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.Type == AccountEmailMessage.OutboxType
                && m.Payload != null
                && m.Payload.Contains(account.Value.ToString()))
            .Select(m => m.Id)
            .SingleAsync());

    private Task<OutboxMessage> RowAsync(Guid id) =>
        fixture.WithServiceAsync<PantryPressDbContext, OutboxMessage>(
            db => db.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == id));
}

// PP1.10b: the Worker is where account email is sent from, so it is the host that refuses to start without what that
// takes - an email gateway, and the admin app's origin for the links (the refusals PP1.2 gave ApiService).
public sealed class AccountEmailDeliveryTests
{
    [Fact]
    public void Without_an_email_gateway_the_worker_refuses_to_start()
    {
        using var services = Worker(gateway: null, appBaseUrl: "https://app.localhost");

        var error = Assert.Throws<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<AccountsOptions>>().Value);

        Assert.Contains("No email gateway", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_valid_admin_app_origin_the_worker_refuses_to_start()
    {
        using var services = Worker(gateway: new RecordingEmailGateway(), appBaseUrl: "http://app.pantrypress.app");

        var error = Assert.Throws<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<AccountsOptions>>().Value);

        Assert.Contains("AppBaseUrl", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_both_the_worker_has_a_handler_for_account_email()
    {
        using var services = Worker(gateway: new RecordingEmailGateway(), appBaseUrl: "https://app.localhost");

        Assert.Equal("https://app.localhost", services.GetRequiredService<IOptions<AccountsOptions>>().Value.AppBaseUrl);
        Assert.Contains(
            services.GetServices<OutboxHandlerRegistration>(),
            r => r.Type == AccountEmailMessage.OutboxType);
    }

    [Fact]
    public void A_worker_with_no_email_provider_claims_no_account_email_and_needs_no_link_origin()
    {
        // Anywhere but local Development until PP13.1 (PP1.12): account email waits in the outbox rather than poisoning,
        // and the rest of the Worker - the dispatcher, the sweep - starts as it always did.
        using var services = Worker(gateway: null, appBaseUrl: null, sendsEmail: false);

        Assert.DoesNotContain(services.GetServices<OutboxHandlerRegistration>(), r => r.Type == AccountEmailMessage.OutboxType);
        Assert.True(services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IAccountMaintenance)));
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

        services.AddAccountsWorker(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = appBaseUrl })
                .Build(),
            sendsEmail);
        return services.BuildServiceProvider();
    }
}
