using System.Net;
using System.Net.Http.Json;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using static PantryPress.Tests.Api.RegistrationApiTests;

namespace PantryPress.Tests.Api;

// PP1.6: what the API makes of the session the gateway forwards (ACCT-002, ACCT-005). The headers name an account and
// the security stamp its session was issued against; everything the caller may do is read from the database on every
// request, so a changed password, a changed second factor, or a changed platform role ends a session at its next one.
[Collection(AccountApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ForwardedSessionTests(AccountApiFixture fixture)
{
    private const string TwoFactorStatus = "/api/v1/account/two-factor";
    private const string ChangePassword = "/api/v1/account/password/change";

    [Fact]
    public async Task A_forwarded_session_is_the_signed_in_account()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());

        using var response = await fixture
            .ForwardedClient(account, await fixture.StampAsync(account))
            .GetAsync(TwoFactorStatus);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<TwoFactorStatus>();
        Assert.False(status!.IsEnabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TestSecrets.WrongGatewaySecret)]
    public async Task Identity_headers_without_the_gateways_secret_name_nobody(string? secret)
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var stamp = await fixture.StampAsync(account);
        var client = secret is null
            ? fixture.Forwarded.CreateClient()
            : fixture.ForwardedClient(account, stamp, secret: secret);
        if (secret is null)
        {
            client.DefaultRequestHeaders.Add("X-PP-Account", account.ToString());
            client.DefaultRequestHeaders.Add("X-PP-Stamp", stamp);
        }

        using var response = await client.GetAsync(TwoFactorStatus);

        // A plain 401: not "you were signed out", because as far as the API is concerned nobody ever signed in.
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "sign-in-required");
        Assert.False(response.Headers.Contains("X-PP-Session"));
    }

    [Fact]
    public async Task A_password_change_ends_the_sessions_that_are_still_on_the_old_stamp()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var before = await fixture.StampAsync(account);

        using var changed = await fixture.Client(account).PostAsJsonAsync(
            ChangePassword,
            new ChangePasswordRequest(AccountApiFixture.FirstPassphrase, AccountApiFixture.SecondPassphrase));
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        var response = await fixture.ForwardedClient(account, before).GetAsync(TwoFactorStatus);

        // The gateway reads this header and clears its cookie, so the person is told they were signed out.
        Assert.Equal("ended", Assert.Single(response.Headers.GetValues("X-PP-Session")));
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "session-ended");

        // The new stamp still works: only the stale sessions are over.
        using var current = await fixture
            .ForwardedClient(account, await fixture.StampAsync(account))
            .GetAsync(TwoFactorStatus);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    [Fact]
    public async Task A_request_that_rotates_the_stamp_hands_the_new_one_back_to_the_gateway()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var before = await fixture.StampAsync(account);

        using var response = await fixture.ForwardedClient(account, before).PostAsJsonAsync(
            ChangePassword,
            new ChangePasswordRequest(AccountApiFixture.FirstPassphrase, AccountApiFixture.SecondPassphrase));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await fixture.StampAsync(account);
        Assert.NotEqual(before, after);
        Assert.Equal(after, Assert.Single(response.Headers.GetValues("X-PP-New-Stamp")));
    }

    [Fact]
    public async Task A_request_that_changes_nothing_hands_back_no_new_stamp()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());

        using var response = await fixture
            .ForwardedClient(account, await fixture.StampAsync(account))
            .GetAsync(TwoFactorStatus);

        Assert.False(response.Headers.Contains("X-PP-New-Stamp"));
    }

    [Fact]
    public async Task An_account_that_is_gone_is_a_session_that_ended()
    {
        var response = await fixture.ForwardedClient(new AccountId(Guid.NewGuid()), "ANY-STAMP").GetAsync(TwoFactorStatus);

        // 401, not 404: a session that cannot be resolved is over, whatever became of the account (PP1.3's question).
        Assert.Equal("ended", Assert.Single(response.Headers.GetValues("X-PP-Session")));
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "session-ended");
    }

    [Fact]
    public async Task A_session_that_did_not_pass_the_second_step_is_over_once_the_account_has_one()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        await EnableTwoFactorAsync(account);
        var stamp = await fixture.StampAsync(account);

        var without = await fixture.ForwardedClient(account, stamp).GetAsync(TwoFactorStatus);
        using var with = await fixture.ForwardedClient(account, stamp, secondFactorVerified: true).GetAsync(TwoFactorStatus);

        // Turning two-step sign-in on rotates the stamp, so this is belt and braces: it fails closed either way.
        await AssertProblemAsync(without, HttpStatusCode.Unauthorized, "session-ended");
        Assert.Equal(HttpStatusCode.OK, with.StatusCode);
    }

    [Fact]
    public async Task Platform_roles_are_read_from_the_database_for_every_request()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var before = await fixture.StampAsync(account);
        var withoutRole = await ResolveAsync(account, before);
        Assert.Empty(withoutRole!.Roles);

        await fixture.GrantPlatformAdminAsync(account);

        // The grant rotates the stamp, so the session that was running before it is over (ACCT-005) - and the next
        // session carries the role without anything being re-issued or cached.
        Assert.Null(await ResolveAsync(account, before));
        var withRole = await ResolveAsync(account, await fixture.StampAsync(account));
        Assert.Equal(AccountRoles.PlatformAdmin, Assert.Single(withRole!.Roles));
    }

    private Task<SessionIdentity?> ResolveAsync(AccountId account, string stamp) =>
        fixture.WithServiceAsync<ISessionFacade, SessionIdentity?>(
            sessions => sessions.ResolveAsync(account, stamp, secondFactorVerified: false, CancellationToken.None));

    private async Task EnableTwoFactorAsync(AccountId account)
    {
        using var setup = await fixture.Client(account).PostAsync($"{TwoFactorStatus}/setup", null);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var key = (await setup.Content.ReadFromJsonAsync<AuthenticatorSetup>())!.SharedKey;
        using var enabled = await fixture.Client(account).PostAsJsonAsync(
            $"{TwoFactorStatus}/enable",
            new EnableTwoFactorRequest(Totp.Compute(Base32.Decode(key), Totp.StepAt(fixture.Now))));
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
    }
}
