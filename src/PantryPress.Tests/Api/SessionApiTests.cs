using System.Net;
using System.Net.Http.Json;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using static PantryPress.Tests.Api.RegistrationApiTests;

namespace PantryPress.Tests.Api;

// PP1.6: the sign-in checks the gateway calls on the internal surface (ACCT-002). The surface is the gateway's alone:
// the BFF never forwards /api/internal/v1, and the API refuses anyone who cannot present the shared secret.
[Collection(AccountApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SessionApiTests(AccountApiFixture fixture)
{
    private const string Credentials = "/api/internal/v1/sessions/credentials";
    private const string SecondFactor = "/api/internal/v1/sessions/second-factor";
    private const string Current = "/api/internal/v1/sessions/current";
    private const string End = "/api/internal/v1/sessions/end";
    private const string SignedInRoute = "/api/v1/account/two-factor";

    [Fact]
    public async Task A_locked_account_is_told_how_long_to_wait_in_words_and_in_seconds()
    {
        // PP1.12: the lockout's 429 carries Retry-After, as the rate limiter's always has, and the gateway relays it.
        var email = AccountApiFixture.NewEmail();
        await fixture.RegisterConfirmedAsync(email);
        for (var i = 0; i < 5; i++)
        {
            using var wrong = await Gateway().PostAsJsonAsync(Credentials, new SignInRequest(email, $"wrong passphrase number {i}"));
        }

        using var locked = await Gateway().PostAsJsonAsync(Credentials, new SignInRequest(email, AccountApiFixture.FirstPassphrase));

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        var wait = Assert.NotNull(locked.Headers.RetryAfter?.Delta);
        Assert.InRange(wait, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15));
        using var problem = await AssertProblemAsync(locked, HttpStatusCode.TooManyRequests, "too-many-attempts");
        Assert.Matches("^Too many tries\\. Wait (a minute|\\d+ minutes), then try again\\.$", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_signed_out_session_is_refused_wherever_it_is_presented()
    {
        // PP1.12: signing out records the session's id, so a copy of the cookie stops working too - while the same
        // account's other session, with the same stamp, carries on.
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var stamp = await fixture.StampAsync(account);
        var signedOut = AccountApiFixture.NewSessionId();
        var other = AccountApiFixture.NewSessionId();
        using (var before = await fixture.ForwardedClient(account, stamp, sessionId: signedOut).GetAsync(SignedInRoute))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        using var ended = await Gateway().PostAsJsonAsync(End, new EndSessionRequest(account.Value, signedOut));
        using var again = await Gateway().PostAsJsonAsync(End, new EndSessionRequest(account.Value, signedOut));

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        var refused = await fixture.ForwardedClient(account, stamp, sessionId: signedOut).GetAsync(SignedInRoute);
        Assert.Equal("ended", Assert.Single(refused.Headers.GetValues("X-PP-Session")));
        await AssertProblemAsync(refused, HttpStatusCode.Unauthorized, "session-ended");
        using var stillOn = await fixture.ForwardedClient(account, stamp, sessionId: other).GetAsync(SignedInRoute);
        Assert.Equal(HttpStatusCode.OK, stillOn.StatusCode);
    }

    [Fact]
    public async Task A_request_without_a_session_id_names_nobody()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());

        var noId = await fixture.ForwardedClient(account, await fixture.StampAsync(account), sessionId: "not a session id!")
            .GetAsync(SignedInRoute);

        await AssertProblemAsync(noId, HttpStatusCode.Unauthorized, "sign-in-required");
    }

    [Fact]
    public async Task The_current_session_is_read_from_the_database_now()
    {
        // Signed in before confirming the address: the gateway's cookie still says "unconfirmed", the database does not.
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterAsync(email);
        var stamp = await fixture.StampAsync(account);
        var sessionId = AccountApiFixture.NewSessionId();
        var (linkAccount, token) = AccountApiFixture.ReadLink(await fixture.NextEmailToAsync(email, "Confirm your email"), "verify-email");
        using var verified = await fixture.Client().PostAsJsonAsync("/api/v1/account/email/verify", new VerifyEmailRequest(linkAccount, token));
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);

        using var response = await Gateway().PostAsJsonAsync(
            Current,
            new CurrentSessionRequest(account.Value, stamp, sessionId, SecondFactorVerified: false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var current = await response.Content.ReadFromJsonAsync<CurrentSession>();
        Assert.Equal(new CurrentSession(account.Value, email, EmailVerified: true, SecondFactorVerified: false), current);
        Assert.DoesNotContain(stamp, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("signed-out")]
    [InlineData("stamp-moved")]
    [InlineData("account-gone")]
    public async Task A_session_that_is_over_is_reported_as_ended(string why)
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var stamp = await fixture.StampAsync(account);
        var sessionId = AccountApiFixture.NewSessionId();
        switch (why)
        {
            case "signed-out":
                using (var ended = await Gateway().PostAsJsonAsync(End, new EndSessionRequest(account.Value, sessionId)))
                {
                    Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
                }

                break;
            case "stamp-moved":
                stamp = "A-STAMP-FROM-BEFORE";
                break;
            default:
                account = new AccountId(Guid.NewGuid());
                break;
        }

        var response = await Gateway().PostAsJsonAsync(
            Current,
            new CurrentSessionRequest(account.Value, stamp, sessionId, SecondFactorVerified: false));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "session-ended");
    }

    [Fact]
    public async Task The_current_session_and_sign_out_are_the_gateways_alone()
    {
        var account = new AccountId(Guid.NewGuid());
        using var stranger = fixture.Client();

        using var current = await stranger.PostAsJsonAsync(
            Current,
            new CurrentSessionRequest(account.Value, "STAMP", AccountApiFixture.NewSessionId(), SecondFactorVerified: false));
        using var end = await stranger.PostAsJsonAsync(End, new EndSessionRequest(account.Value, AccountApiFixture.NewSessionId()));

        Assert.Equal(HttpStatusCode.NotFound, current.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, end.StatusCode);
    }

    [Fact]
    public async Task The_right_password_names_the_account_its_stamp_and_whether_a_step_is_owed()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);

        using var response = await Gateway().PostAsJsonAsync(
            Credentials,
            new SignInRequest(email, AccountApiFixture.FirstPassphrase));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<SessionStart>();
        Assert.NotNull(session);
        Assert.Equal(account.Value, session.AccountId);
        Assert.Equal(email, session.Email);
        Assert.Equal(await fixture.StampAsync(account), session.SecurityStamp);
        Assert.False(session.SecondStepRequired);
        Assert.True(session.EmailVerified);

        // Nothing about a session is cacheable.
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task An_unverified_email_can_sign_in_and_is_told_it_is_unverified()
    {
        // ACCT-001 gates publishing on a verified address, not signing in.
        var email = AccountApiFixture.NewEmail();
        await fixture.RegisterAsync(email);

        using var response = await Gateway().PostAsJsonAsync(
            Credentials,
            new SignInRequest(email, AccountApiFixture.FirstPassphrase));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<SessionStart>();
        Assert.False(session!.EmailVerified);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_address_are_refused_the_same_way()
    {
        var email = AccountApiFixture.NewEmail();
        await fixture.RegisterConfirmedAsync(email);

        var wrongPassword = await Gateway().PostAsJsonAsync(
            Credentials,
            new SignInRequest(email, AccountApiFixture.SecondPassphrase));
        var unknownAddress = await Gateway().PostAsJsonAsync(
            Credentials,
            new SignInRequest(AccountApiFixture.NewEmail(), AccountApiFixture.FirstPassphrase));
        using var refusedPassword = await AssertProblemAsync(wrongPassword, HttpStatusCode.BadRequest, "invalid-credentials");
        using var refusedAddress = await AssertProblemAsync(unknownAddress, HttpStatusCode.BadRequest, "invalid-credentials");

        // Word for word, bar the trace id: the answer says nothing about which addresses have accounts.
        foreach (var field in new[] { "type", "title", "status", "code" })
        {
            Assert.Equal(
                refusedPassword.RootElement.GetProperty(field).ToString(),
                refusedAddress.RootElement.GetProperty(field).ToString());
        }
    }

    [Fact]
    public async Task Wrong_passwords_count_toward_lockout_and_the_right_one_then_waits_too()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var refused = await Gateway().PostAsJsonAsync(
                Credentials,
                new SignInRequest(email, "the wrong long phrase"));
            await AssertProblemAsync(refused, HttpStatusCode.BadRequest, "invalid-credentials");
        }

        // The fifth wrong one is the one that locks the account, and says so.
        using var fifth = await Gateway().PostAsJsonAsync(
            Credentials,
            new SignInRequest(email, "the wrong long phrase"));
        await AssertProblemAsync(fifth, HttpStatusCode.TooManyRequests, "too-many-attempts");

        using var locked = await Gateway().PostAsJsonAsync(
            Credentials,
            new SignInRequest(email, AccountApiFixture.FirstPassphrase));

        await AssertProblemAsync(locked, HttpStatusCode.TooManyRequests, "too-many-attempts");
        Assert.True(await fixture.WithAccountAsync(account, (users, user) => users.IsLockedOutAsync(user)));
    }

    [Fact]
    public async Task A_password_alone_is_not_enough_for_an_account_with_two_step_sign_in()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);
        var key = await EnableTwoFactorAsync(account);

        using var signedIn = await Gateway().PostAsJsonAsync(
            Credentials,
            new SignInRequest(email, AccountApiFixture.FirstPassphrase));
        var started = await signedIn.Content.ReadFromJsonAsync<SessionStart>();
        Assert.True(started!.SecondStepRequired);

        using var wrongCode = await Gateway().PostAsJsonAsync(
            SecondFactor,
            new SecondFactorSignInRequest(account.Value, started.SecurityStamp, "000000"));
        await AssertProblemAsync(wrongCode, HttpStatusCode.BadRequest, "invalid-code");

        // Turning two-step sign-in on used that step's code, and a code works once (PP1.3b).
        fixture.Time.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));
        using var completed = await Gateway().PostAsJsonAsync(
            SecondFactor,
            new SecondFactorSignInRequest(account.Value, started.SecurityStamp, CodeFor(key)));

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var session = await completed.Content.ReadFromJsonAsync<SessionStart>();
        Assert.False(session!.SecondStepRequired);
        Assert.Equal(await fixture.StampAsync(account), session.SecurityStamp);
    }

    [Fact]
    public async Task A_code_without_the_stamp_from_a_real_first_step_finishes_nothing_and_counts_nothing()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);
        var key = await EnableTwoFactorAsync(account);
        fixture.Time.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));

        // A right code, from something that knows the gateway's secret but never passed the password step. The stamp
        // is the only proof of that step, and it cannot be read without the password.
        using var forged = await Gateway().PostAsJsonAsync(
            SecondFactor,
            new SecondFactorSignInRequest(account.Value, "NOT-THE-REAL-STAMP", CodeFor(key)));

        await AssertProblemAsync(forged, HttpStatusCode.BadRequest, "sign-in-expired");

        // And nothing was held against the account: this is not a way to lock a named blogger out.
        var (failures, locked) = await fixture.WithAccountAsync(account, async (users, user) => (
            await users.GetAccessFailedCountAsync(user),
            await users.IsLockedOutAsync(user)));
        Assert.Equal(0, failures);
        Assert.False(locked);

        // The code is still good for the real sign-in, so the refusal did not spend it either.
        using var signedIn = await Gateway().PostAsJsonAsync(
            Credentials,
            new SignInRequest(email, AccountApiFixture.FirstPassphrase));
        var started = await signedIn.Content.ReadFromJsonAsync<SessionStart>();
        using var completed = await Gateway().PostAsJsonAsync(
            SecondFactor,
            new SecondFactorSignInRequest(account.Value, started!.SecurityStamp, CodeFor(key)));
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    [Fact]
    public async Task A_stamp_that_moved_while_the_second_step_was_owed_ends_the_sign_in()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);
        var key = await EnableTwoFactorAsync(account);
        using var signedIn = await Gateway().PostAsJsonAsync(
            Credentials,
            new SignInRequest(email, AccountApiFixture.FirstPassphrase));
        var started = await signedIn.Content.ReadFromJsonAsync<SessionStart>();

        // The password changes in another window while this sign-in is half finished.
        using var changed = await fixture.Client(account).PostAsJsonAsync(
            "/api/v1/account/password/change",
            new ChangePasswordRequest(AccountApiFixture.FirstPassphrase, AccountApiFixture.SecondPassphrase));
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        fixture.Time.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));
        using var completed = await Gateway().PostAsJsonAsync(
            SecondFactor,
            new SecondFactorSignInRequest(account.Value, started!.SecurityStamp, CodeFor(key)));

        await AssertProblemAsync(completed, HttpStatusCode.BadRequest, "sign-in-expired");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TestSecrets.WrongGatewaySecret)]
    public async Task Without_the_gateways_secret_the_surface_is_not_there(string? secret)
    {
        var email = AccountApiFixture.NewEmail();
        await fixture.RegisterConfirmedAsync(email);
        var client = fixture.Forwarded.CreateClient();
        if (secret is not null)
        {
            client.DefaultRequestHeaders.Add("X-PP-Gateway", secret);
        }

        using var response = await client.PostAsJsonAsync(
            Credentials,
            new SignInRequest(email, AccountApiFixture.FirstPassphrase));

        // The same 404 an unknown route gets, so the surface does not announce itself.
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not-found");
    }

    private HttpClient Gateway()
    {
        var client = fixture.Forwarded.CreateClient();
        client.DefaultRequestHeaders.Add("X-PP-Gateway", TestSecrets.GatewaySecret);
        return client;
    }

    private async Task<string> EnableTwoFactorAsync(AccountId account)
    {
        using var setup = await fixture.Client(account).PostAsync("/api/v1/account/two-factor/setup", null);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var key = (await setup.Content.ReadFromJsonAsync<AuthenticatorSetup>())!.SharedKey;

        using var enabled = await fixture.Client(account).PostAsJsonAsync(
            "/api/v1/account/two-factor/enable",
            new EnableTwoFactorRequest(CodeFor(key)));
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        return key;
    }

    private string CodeFor(string sharedKey) => Totp.Compute(Base32.Decode(sharedKey), Totp.StepAt(fixture.Now));
}
