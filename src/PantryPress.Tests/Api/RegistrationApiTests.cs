using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Tests.Api;

// PP1.2c: sign-up and email confirmation through the request seam (ACCT-001, ACCT-003).
[Collection(AccountApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RegistrationApiTests(AccountApiFixture fixture)
{
    [Fact]
    public async Task Sign_up_creates_an_unconfirmed_account_and_emails_a_confirmation_link()
    {
        var email = AccountApiFixture.NewEmail();

        var account = await fixture.RegisterAsync(email);

        var message = await fixture.NextEmailToAsync(email, "Confirm your email");
        var (linkAccount, _) = AccountApiFixture.ReadLink(message, "verify-email");
        Assert.Equal(account.Value, linkAccount);
        Assert.False(await fixture.WithAccountAsync(account, (_, user) => Task.FromResult(user.EmailConfirmed)));
        Assert.Equal(
            AccountErrors.EmailNotVerified,
            (await fixture.WithServiceAsync<IAccountPublishingEligibility, Domain.Managers.Results.Result>(
                e => e.CheckAsync(account, default))).Error);
    }

    [Fact]
    public async Task Signing_up_with_a_registered_address_looks_the_same_and_sends_nothing()
    {
        var email = AccountApiFixture.NewEmail();
        await fixture.RegisterAsync(email);
        await fixture.DrainEmailAsync();
        var sentBefore = fixture.EmailsTo(email).Count;

        using var first = await fixture.Client().PostAsJsonAsync(
            "/api/v1/account/register",
            new RegisterAccountRequest(AccountApiFixture.NewEmail(), AccountApiFixture.FirstPassphrase));
        using var again = await fixture.Client().PostAsJsonAsync(
            "/api/v1/account/register",
            new RegisterAccountRequest(email.ToUpperInvariant(), AccountApiFixture.SecondPassphrase));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        Assert.Equal(first.Content.Headers.ContentType, again.Content.Headers.ContentType);
        await fixture.DrainEmailAsync();
        Assert.Equal(sentBefore, fixture.EmailsTo(email).Count);
    }

    [Fact]
    public async Task Concurrent_sign_ups_for_one_address_all_succeed_and_create_one_account()
    {
        var email = AccountApiFixture.NewEmail();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => fixture.Client().PostAsJsonAsync(
            "/api/v1/account/register",
            new RegisterAccountRequest(email, AccountApiFixture.FirstPassphrase))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        var count = await fixture.WithServiceAsync<Domain.Managers.Persistence.PantryPressDbContext, int>(
            db => Task.FromResult(db.Users.Count(u => u.NormalizedEmail == email.ToUpperInvariant())));
        Assert.Equal(1, count);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task The_confirmation_link_confirms_the_address_and_allows_publishing()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterAsync(email);
        var (linkAccount, token) = AccountApiFixture.ReadLink(await fixture.NextEmailToAsync(email, "Confirm"), "verify-email");

        using var response = await fixture.Client().PostAsJsonAsync("/api/v1/account/email/verify", new VerifyEmailRequest(linkAccount, token));
        using var again = await fixture.Client().PostAsJsonAsync("/api/v1/account/email/verify", new VerifyEmailRequest(linkAccount, token));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.True(await fixture.WithAccountAsync(account, (_, user) => Task.FromResult(user.EmailConfirmed)));
        Assert.True((await fixture.WithServiceAsync<IAccountPublishingEligibility, Domain.Managers.Results.Result>(
            e => e.CheckAsync(account, default))).IsSuccess);
    }

    [Fact]
    public async Task A_confirmation_link_works_only_for_its_own_account_and_only_for_24_hours()
    {
        var first = AccountApiFixture.NewEmail();
        var second = AccountApiFixture.NewEmail();
        await fixture.RegisterAsync(first);
        var secondAccount = await fixture.RegisterAsync(second);
        var (_, firstToken) = AccountApiFixture.ReadLink(await fixture.NextEmailToAsync(first, "Confirm"), "verify-email");
        var (_, secondToken) = AccountApiFixture.ReadLink(await fixture.NextEmailToAsync(second, "Confirm"), "verify-email");

        await AssertProblemAsync(
            await fixture.Client().PostAsJsonAsync("/api/v1/account/email/verify", new VerifyEmailRequest(secondAccount.Value, firstToken)),
            HttpStatusCode.BadRequest,
            "invalid-verification-link");
        await AssertProblemAsync(
            await fixture.Client().PostAsJsonAsync("/api/v1/account/email/verify", new VerifyEmailRequest(Guid.NewGuid(), secondToken)),
            HttpStatusCode.BadRequest,
            "invalid-verification-link");
        await AssertProblemAsync(
            await fixture.Client().PostAsJsonAsync("/api/v1/account/email/verify", new VerifyEmailRequest(secondAccount.Value, "not-a-token")),
            HttpStatusCode.BadRequest,
            "invalid-verification-link");

        fixture.Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        await AssertProblemAsync(
            await fixture.Client().PostAsJsonAsync("/api/v1/account/email/verify", new VerifyEmailRequest(secondAccount.Value, secondToken)),
            HttpStatusCode.BadRequest,
            "invalid-verification-link");
    }

    [Fact]
    public async Task Resend_emails_unconfirmed_accounts_at_most_once_a_minute_and_answers_the_same_for_everyone()
    {
        var unconfirmed = AccountApiFixture.NewEmail();
        var confirmed = AccountApiFixture.NewEmail();
        var unknown = AccountApiFixture.NewEmail();
        await fixture.RegisterAsync(unconfirmed);
        await fixture.RegisterConfirmedAsync(confirmed);
        await fixture.DrainEmailAsync();
        var unconfirmedBefore = fixture.EmailsTo(unconfirmed).Count;
        var confirmedBefore = fixture.EmailsTo(confirmed).Count;

        // Sign-up just sent one, so an immediate resend is throttled; a minute later it goes out.
        foreach (var email in new[] { unconfirmed, confirmed, unknown })
        {
            using var response = await fixture.Client().PostAsJsonAsync("/api/v1/account/email/resend", new ResendVerificationRequest(email));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        await fixture.DrainEmailAsync();
        Assert.Equal(unconfirmedBefore, fixture.EmailsTo(unconfirmed).Count);

        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        using (var response = await fixture.Client().PostAsJsonAsync("/api/v1/account/email/resend", new ResendVerificationRequest(unconfirmed)))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        await fixture.DrainEmailAsync();
        Assert.Equal(unconfirmedBefore + 1, fixture.EmailsTo(unconfirmed).Count);
        Assert.Equal(confirmedBefore, fixture.EmailsTo(confirmed).Count);
        Assert.Empty(fixture.EmailsTo(unknown));
    }

    [Fact]
    public async Task Sign_up_explains_invalid_input_in_plain_words()
    {
        using var response = await fixture.Client().PostAsJsonAsync(
            "/api/v1/account/register",
            new RegisterAccountRequest("someone@mailinator.com", "short"));

        using var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation-failed");
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Contains("Temporary inboxes", errors.GetProperty("email")[0].GetString(), StringComparison.Ordinal);
        Assert.Contains("at least 12", errors.GetProperty("password")[0].GetString(), StringComparison.Ordinal);
    }

    internal static async Task<JsonDocument> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("title").GetString()));
            return problem;
        }
    }
}
