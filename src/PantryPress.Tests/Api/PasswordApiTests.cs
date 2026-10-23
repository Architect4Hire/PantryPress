using System.Net;
using System.Net.Http.Json;
using PantryPress.Domain.Modules.Accounts.Contracts;
using static PantryPress.Tests.Api.RegistrationApiTests;

namespace PantryPress.Tests.Api;

// PP1.3a: password reset and change (ACCT-001).
[Collection(AccountApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PasswordApiTests(AccountApiFixture fixture)
{
    private const string Forgot = "/api/v1/account/password/forgot";
    private const string Reset = "/api/v1/account/password/reset";
    private const string Change = "/api/v1/account/password/change";

    [Fact]
    public async Task Forgot_password_answers_the_same_for_every_address_and_emails_only_real_accounts()
    {
        var email = AccountApiFixture.NewEmail();
        var unknown = AccountApiFixture.NewEmail();
        await fixture.RegisterAsync(email);

        using var known = await fixture.Client().PostAsJsonAsync(Forgot, new ForgotPasswordRequest(email));
        using var stranger = await fixture.Client().PostAsJsonAsync(Forgot, new ForgotPasswordRequest(unknown));

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, stranger.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await stranger.Content.ReadAsStringAsync());
        var message = await fixture.NextEmailToAsync(email, "Reset your PantryPress password");
        AccountApiFixture.ReadLink(message, "reset-password");
        Assert.Empty(fixture.EmailsTo(unknown));
    }

    [Fact]
    public async Task A_reset_link_sets_the_password_once_confirms_the_email_and_sends_a_notice()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterAsync(email);
        var (linkAccount, token) = await RequestResetAsync(email);

        using var response = await fixture.Client().PostAsJsonAsync(Reset, new ResetPasswordRequest(linkAccount, token, AccountApiFixture.SecondPassphrase));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var (passwordWorks, oldPasswordWorks, confirmed) = await fixture.WithAccountAsync(account, async (users, user) => (
            await users.CheckPasswordAsync(user, AccountApiFixture.SecondPassphrase),
            await users.CheckPasswordAsync(user, AccountApiFixture.FirstPassphrase),
            user.EmailConfirmed));
        Assert.True(passwordWorks);
        Assert.False(oldPasswordWorks);
        Assert.True(confirmed);
        await fixture.NextEmailToAsync(email, "Your PantryPress password was changed");

        await AssertProblemAsync(
            await fixture.Client().PostAsJsonAsync(Reset, new ResetPasswordRequest(linkAccount, token, "a third long kitchen phrase")),
            HttpStatusCode.BadRequest,
            "invalid-reset-link");
    }

    [Fact]
    public async Task A_reset_link_expires_after_an_hour()
    {
        var email = AccountApiFixture.NewEmail();
        await fixture.RegisterAsync(email);
        var (linkAccount, token) = await RequestResetAsync(email);

        fixture.Time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));

        await AssertProblemAsync(
            await fixture.Client().PostAsJsonAsync(Reset, new ResetPasswordRequest(linkAccount, token, AccountApiFixture.SecondPassphrase)),
            HttpStatusCode.BadRequest,
            "invalid-reset-link");
    }

    [Fact]
    public async Task A_reset_link_works_only_for_its_own_account()
    {
        var first = AccountApiFixture.NewEmail();
        var second = AccountApiFixture.NewEmail();
        await fixture.RegisterAsync(first);
        var secondAccount = await fixture.RegisterAsync(second);
        var (_, firstToken) = await RequestResetAsync(first);

        await AssertProblemAsync(
            await fixture.Client().PostAsJsonAsync(Reset, new ResetPasswordRequest(secondAccount.Value, firstToken, AccountApiFixture.SecondPassphrase)),
            HttpStatusCode.BadRequest,
            "invalid-reset-link");
        Assert.True(await fixture.WithAccountAsync(secondAccount, (users, user) => users.CheckPasswordAsync(user, AccountApiFixture.FirstPassphrase)));
    }

    [Fact]
    public async Task Changing_the_password_voids_an_outstanding_reset_link()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);
        var (linkAccount, token) = await RequestResetAsync(email);

        using (var changed = await fixture.Client(account).PostAsJsonAsync(Change, new ChangePasswordRequest(AccountApiFixture.FirstPassphrase, AccountApiFixture.SecondPassphrase)))
        {
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        }

        await AssertProblemAsync(
            await fixture.Client().PostAsJsonAsync(Reset, new ResetPasswordRequest(linkAccount, token, "a third long kitchen phrase")),
            HttpStatusCode.BadRequest,
            "invalid-reset-link");
    }

    [Fact]
    public async Task A_reset_rejects_a_short_new_password_and_leaves_the_link_usable()
    {
        var email = AccountApiFixture.NewEmail();
        await fixture.RegisterAsync(email);
        var (linkAccount, token) = await RequestResetAsync(email);

        using var problem = await AssertProblemAsync(
            await fixture.Client().PostAsJsonAsync(Reset, new ResetPasswordRequest(linkAccount, token, "short")),
            HttpStatusCode.BadRequest,
            "validation-failed");
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("newPassword", out _));

        using var retried = await fixture.Client().PostAsJsonAsync(Reset, new ResetPasswordRequest(linkAccount, token, AccountApiFixture.SecondPassphrase));
        Assert.Equal(HttpStatusCode.NoContent, retried.StatusCode);
    }

    [Fact]
    public async Task Reset_emails_are_throttled_per_account()
    {
        var email = AccountApiFixture.NewEmail();
        await fixture.RegisterAsync(email);

        for (var i = 0; i < 3; i++)
        {
            using var response = await fixture.Client().PostAsJsonAsync(Forgot, new ForgotPasswordRequest(email));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        await fixture.DrainEmailAsync();
        Assert.Single(fixture.EmailsTo(email), m => m.Subject.StartsWith("Reset", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Changing_the_password_needs_a_signed_in_caller()
    {
        await AssertProblemAsync(
            await fixture.Client().PostAsJsonAsync(Change, new ChangePasswordRequest(AccountApiFixture.FirstPassphrase, AccountApiFixture.SecondPassphrase)),
            HttpStatusCode.Unauthorized,
            "sign-in-required");
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one_and_sends_a_notice()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);

        await AssertProblemAsync(
            await fixture.Client(account).PostAsJsonAsync(Change, new ChangePasswordRequest("not my password at all", AccountApiFixture.SecondPassphrase)),
            HttpStatusCode.BadRequest,
            "incorrect-password");
        using var changed = await fixture.Client(account).PostAsJsonAsync(Change, new ChangePasswordRequest(AccountApiFixture.FirstPassphrase, AccountApiFixture.SecondPassphrase));

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.True(await fixture.WithAccountAsync(account, (users, user) => users.CheckPasswordAsync(user, AccountApiFixture.SecondPassphrase)));
        await fixture.NextEmailToAsync(email, "Your PantryPress password was changed");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_until_a_reset_link_is_used()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);

        for (var i = 1; i <= 5; i++)
        {
            await AssertProblemAsync(
                await fixture.Client(account).PostAsJsonAsync(Change, new ChangePasswordRequest($"wrong password number {i}", AccountApiFixture.SecondPassphrase)),
                i < 5 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests,
                i < 5 ? "incorrect-password" : "too-many-attempts");
        }

        // Locked: even the right password is refused without being checked.
        await AssertProblemAsync(
            await fixture.Client(account).PostAsJsonAsync(Change, new ChangePasswordRequest(AccountApiFixture.FirstPassphrase, AccountApiFixture.SecondPassphrase)),
            HttpStatusCode.TooManyRequests,
            "too-many-attempts");

        var (linkAccount, token) = await RequestResetAsync(email);
        using var reset = await fixture.Client().PostAsJsonAsync(Reset, new ResetPasswordRequest(linkAccount, token, AccountApiFixture.SecondPassphrase));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.False(await fixture.WithAccountAsync(account, (users, user) => users.IsLockedOutAsync(user)));
    }

    private async Task<(Guid Account, string Token)> RequestResetAsync(string email)
    {
        // Each request starts a minute after the last, clear of the per-account email throttle.
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        using var response = await fixture.Client().PostAsJsonAsync(Forgot, new ForgotPasswordRequest(email));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return AccountApiFixture.ReadLink(await fixture.NextEmailToAsync(email, "Reset your PantryPress password"), "reset-password");
    }
}
