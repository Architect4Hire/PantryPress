using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using static PantryPress.Tests.Api.RegistrationApiTests;

namespace PantryPress.Tests.Api;

// PP1.3b: optional authenticator-app two-step sign-in with recovery codes (ACCT-001).
[Collection(AccountApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TwoFactorApiTests(AccountApiFixture fixture)
{
    private const string Status = "/api/v1/account/two-factor";
    private const string Setup = "/api/v1/account/two-factor/setup";
    private const string Enable = "/api/v1/account/two-factor/enable";
    private const string Disable = "/api/v1/account/two-factor/disable";
    private const string NewCodes = "/api/v1/account/two-factor/recovery-codes";

    [Fact]
    public async Task Two_factor_routes_need_a_signed_in_caller()
    {
        await AssertProblemAsync(await fixture.Client().GetAsync(Status), HttpStatusCode.Unauthorized, "sign-in-required");
        await AssertProblemAsync(await fixture.Client().PostAsync(Setup, null), HttpStatusCode.Unauthorized, "sign-in-required");
    }

    [Fact]
    public async Task Setup_returns_a_key_and_link_that_are_never_cached()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);

        using var response = await fixture.Client(account).PostAsync(Setup, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var setup = await response.Content.ReadFromJsonAsync<AuthenticatorSetup>();
        Assert.NotNull(setup);
        Assert.Matches("^([a-z2-7]{4} )+[a-z2-7]{1,4}$", setup.SharedKey);
        Assert.StartsWith($"otpauth://totp/PantryPress:{Uri.EscapeDataString(email)}?secret=", setup.AuthenticatorUri, StringComparison.Ordinal);
        Assert.Contains("&issuer=PantryPress&algorithm=SHA1&digits=6&period=30", setup.AuthenticatorUri, StringComparison.Ordinal);
        Assert.Equal(20, Base32.Decode(setup.SharedKey).Length);
    }

    [Fact]
    public async Task The_authenticator_key_and_recovery_codes_are_not_stored_in_readable_form()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var key = await BeginSetupAsync(account);
        var codes = await EnableAsync(account, key);

        var (storedKey, storedCodes, plainCodes) = await fixture.WithAccountAsync(account, async (users, user) => (
            await users.GetAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey"),
            await users.GetAuthenticationTokenAsync(user, "PantryPress", "RecoveryCodeHashes"),
            await users.GetAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes")));

        Assert.NotNull(storedKey);
        Assert.DoesNotContain(key.Replace(" ", string.Empty, StringComparison.Ordinal), storedKey, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(storedCodes);
        Assert.All(codes, code => Assert.DoesNotContain(code, storedCodes, StringComparison.OrdinalIgnoreCase));
        Assert.Null(plainCodes);
    }

    [Fact]
    public async Task Turning_it_on_needs_a_working_code_and_returns_ten_recovery_codes_once()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);
        var key = await BeginSetupAsync(account);

        await AssertProblemAsync(
            await fixture.Client(account).PostAsJsonAsync(Enable, new EnableTwoFactorRequest(WrongCode(key))),
            HttpStatusCode.BadRequest,
            "invalid-code");
        var codes = await EnableAsync(account, key);

        Assert.Equal(RecoveryCodes.Count, codes.Count);
        Assert.Equal(new TwoFactorStatus(true, RecoveryCodes.Count), await GetStatusAsync(account));
        await fixture.NextEmailToAsync(email, "Two-step sign-in is now on");
        await AssertProblemAsync(await fixture.Client(account).PostAsync(Setup, null), HttpStatusCode.Conflict, "two-factor-already-on");
    }

    [Fact]
    public async Task Turning_it_on_without_setup_is_refused()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());

        await AssertProblemAsync(
            await fixture.Client(account).PostAsJsonAsync(Enable, new EnableTwoFactorRequest("123456")),
            HttpStatusCode.Conflict,
            "two-factor-setup-required");
    }

    [Fact]
    public async Task A_new_setup_replaces_the_key_shown_before()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var firstKey = await BeginSetupAsync(account);
        var secondKey = await BeginSetupAsync(account);

        Assert.NotEqual(firstKey, secondKey);
        await AssertProblemAsync(
            await fixture.Client(account).PostAsJsonAsync(Enable, new EnableTwoFactorRequest(CodeFor(firstKey))),
            HttpStatusCode.BadRequest,
            "invalid-code");
    }

    [Fact]
    public async Task At_sign_in_each_authenticator_code_is_accepted_once()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var key = await BeginSetupAsync(account);
        await EnableAsync(account, key);

        // The code used to turn it on cannot be used again; the next one can, once.
        Assert.Equal(AccountErrors.InvalidCode, (await VerifyAsync(account, CodeFor(key))).Error);
        fixture.Time.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));
        var next = CodeFor(key);
        Assert.True((await VerifyAsync(account, next)).IsSuccess);
        Assert.Equal(AccountErrors.InvalidCode, (await VerifyAsync(account, next)).Error);
    }

    [Fact]
    public async Task At_sign_in_concurrent_uses_of_one_code_let_exactly_one_through()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var key = await BeginSetupAsync(account);
        await EnableAsync(account, key);
        fixture.Time.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));
        var code = CodeFor(key);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => VerifyAsync(account, code))));

        Assert.Single(results, r => r.IsSuccess);
    }

    [Fact]
    public async Task At_sign_in_each_recovery_code_works_once_and_its_use_is_emailed()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);
        var codes = await EnableAsync(account, await BeginSetupAsync(account));

        Assert.True((await VerifyAsync(account, codes[3].ToLowerInvariant())).IsSuccess);
        Assert.Equal(AccountErrors.InvalidCode, (await VerifyAsync(account, codes[3])).Error);

        Assert.Equal(new TwoFactorStatus(true, RecoveryCodes.Count - 1), await GetStatusAsync(account));
        var notice = await fixture.NextEmailToAsync(email, "A recovery code was used");
        Assert.Contains("You have 9 codes left.", notice.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_recovery_codes_need_the_password_and_replace_the_old_ones()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);
        var oldCodes = await EnableAsync(account, await BeginSetupAsync(account));

        await AssertProblemAsync(
            await fixture.Client(account).PostAsJsonAsync(NewCodes, new ConfirmPasswordRequest("not my password at all")),
            HttpStatusCode.BadRequest,
            "incorrect-password");
        using var response = await fixture.Client(account).PostAsJsonAsync(NewCodes, new ConfirmPasswordRequest(AccountApiFixture.FirstPassphrase));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newCodes = (await response.Content.ReadFromJsonAsync<RecoveryCodeList>())!.RecoveryCodes;
        Assert.Equal(RecoveryCodes.Count, newCodes.Count);
        Assert.Equal(AccountErrors.InvalidCode, (await VerifyAsync(account, oldCodes[0])).Error);
        Assert.True((await VerifyAsync(account, newCodes[0])).IsSuccess);
        await fixture.NextEmailToAsync(email, "You have new recovery codes");
    }

    [Fact]
    public async Task Turning_it_off_needs_the_password_and_removes_every_second_factor()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);
        var key = await BeginSetupAsync(account);
        var codes = await EnableAsync(account, key);

        await AssertProblemAsync(
            await fixture.Client(account).PostAsJsonAsync(Disable, new ConfirmPasswordRequest("not my password at all")),
            HttpStatusCode.BadRequest,
            "incorrect-password");
        using (var response = await fixture.Client(account).PostAsJsonAsync(Disable, new ConfirmPasswordRequest(AccountApiFixture.FirstPassphrase)))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal(new TwoFactorStatus(false, 0), await GetStatusAsync(account));
        Assert.Equal(0, await fixture.WithAccountAsync(account, (users, user) => users.CountRecoveryCodesAsync(user)));
        Assert.Equal(AccountErrors.TwoFactorOff, (await VerifyAsync(account, codes[0])).Error);
        await fixture.NextEmailToAsync(email, "Two-step sign-in is now off");

        // Turning it off again is not an error.
        using var again = await fixture.Client(account).PostAsJsonAsync(Disable, new ConfirmPasswordRequest(AccountApiFixture.FirstPassphrase));
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_account()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var key = await BeginSetupAsync(account);

        for (var i = 1; i <= 5; i++)
        {
            await AssertProblemAsync(
                await fixture.Client(account).PostAsJsonAsync(Enable, new EnableTwoFactorRequest(WrongCode(key))),
                i < 5 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests,
                i < 5 ? "invalid-code" : "too-many-attempts");
        }

        await AssertProblemAsync(
            await fixture.Client(account).PostAsJsonAsync(Enable, new EnableTwoFactorRequest(CodeFor(key))),
            HttpStatusCode.TooManyRequests,
            "too-many-attempts");
    }

    private async Task<string> BeginSetupAsync(AccountId account)
    {
        using var response = await fixture.Client(account).PostAsync(Setup, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticatorSetup>())!.SharedKey;
    }

    private async Task<IReadOnlyList<string>> EnableAsync(AccountId account, string key)
    {
        using var response = await fixture.Client(account).PostAsJsonAsync(Enable, new EnableTwoFactorRequest(CodeFor(key)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecoveryCodeList>())!.RecoveryCodes;
    }

    private async Task<TwoFactorStatus> GetStatusAsync(AccountId account)
    {
        using var response = await fixture.Client(account).GetAsync(Status);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TwoFactorStatus>())!;
    }

    private Task<Result> VerifyAsync(AccountId account, string code) =>
        fixture.WithServiceAsync<ITwoFactorFacade, Result>(f => f.VerifySecondFactorAsync(account, new SecondFactorRequest(code), default));

    private string CodeFor(string sharedKey) => Totp.Compute(Base32.Decode(sharedKey), Totp.StepAt(fixture.Now));

    // A code that matches no step in the accepted window.
    private string WrongCode(string sharedKey)
    {
        var bytes = Base32.Decode(sharedKey);
        var step = Totp.StepAt(fixture.Now);
        var window = new[] { step - 1, step, step + 1 }.Select(s => Totp.Compute(bytes, s)).ToHashSet();
        return Enumerable.Range(0, 1_000_000).Select(n => n.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)).First(c => !window.Contains(c));
    }
}
