using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Tests.Api;

// PP1.11: every security change to an account, made through the real ApiService routes, leaves exactly one durable
// audit entry, saved with the change - and a refused or repeated request leaves none (ACCT-005). Entries hold ids and
// codes only: the account's email address appears nowhere in them.
[Collection(AccountApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AccountAuditTests(AccountApiFixture fixture)
{
    private const string Forgot = "/api/v1/account/password/forgot";
    private const string Reset = "/api/v1/account/password/reset";
    private const string Change = "/api/v1/account/password/change";
    private const string Setup = "/api/v1/account/two-factor/setup";
    private const string Enable = "/api/v1/account/two-factor/enable";
    private const string Disable = "/api/v1/account/two-factor/disable";
    private const string NewCodes = "/api/v1/account/two-factor/recovery-codes";

    [Fact]
    public async Task A_password_change_is_audited_as_the_account_acting_on_itself()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);

        using (var wrong = await fixture.Client(account).PostAsJsonAsync(Change, new ChangePasswordRequest("not my password at all", AccountApiFixture.SecondPassphrase)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        Assert.Empty(await EntriesAsync(account));
        var changedAt = fixture.Now;
        using (var changed = await fixture.Client(account).PostAsJsonAsync(Change, new ChangePasswordRequest(AccountApiFixture.FirstPassphrase, AccountApiFixture.SecondPassphrase)))
        {
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        }

        var entry = Assert.Single(await EntriesAsync(account));
        Assert.Equal(AccountAudit.PasswordChanged, entry.Action);
        Assert.Equal("account", entry.ActorKind);
        Assert.Equal(account.Value, entry.ActorId);
        Assert.Equal(AccountAudit.TargetKind, entry.TargetKind);
        Assert.Equal(account.ToString(), entry.TargetId);
        Assert.Null(entry.SiteId);
        Assert.Null(entry.Reason);
        Assert.Equal(changedAt, entry.OccurredAt);
        AssertNamesNoOne(entry, email);
    }

    [Fact]
    public async Task A_password_reset_is_audited_once_and_a_reused_link_adds_nothing()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);
        var (linkAccount, token) = await RequestResetAsync(email);

        using (var reset = await fixture.Client().PostAsJsonAsync(Reset, new ResetPasswordRequest(linkAccount, token, AccountApiFixture.SecondPassphrase)))
        {
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        }

        using (var reused = await fixture.Client().PostAsJsonAsync(Reset, new ResetPasswordRequest(linkAccount, token, "a third long kitchen phrase")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        }

        var entry = Assert.Single(await EntriesAsync(account));
        Assert.Equal(AccountAudit.PasswordReset, entry.Action);
        Assert.Equal(account.Value, entry.ActorId);
        AssertNamesNoOne(entry, email);
    }

    [Fact]
    public async Task Each_two_step_change_is_audited_in_order_and_a_repeat_adds_nothing()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);
        var key = await BeginSetupAsync(account);

        // Setting up shows a key but turns nothing on, so it is not a change worth an entry.
        Assert.Empty(await EntriesAsync(account));
        await EnableAsync(account, key);
        IReadOnlyList<string> codes;
        using (var replaced = await fixture.Client(account).PostAsJsonAsync(NewCodes, new ConfirmPasswordRequest(AccountApiFixture.FirstPassphrase)))
        {
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            codes = (await replaced.Content.ReadFromJsonAsync<RecoveryCodeList>())!.RecoveryCodes;
        }

        Assert.True((await VerifyAsync(account, codes[0])).IsSuccess);
        Assert.Equal(AccountErrors.InvalidCode, (await VerifyAsync(account, codes[0])).Error);
        for (var i = 0; i < 2; i++)
        {
            using var off = await fixture.Client(account).PostAsJsonAsync(Disable, new ConfirmPasswordRequest(AccountApiFixture.FirstPassphrase));
            Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        }

        var entries = await EntriesAsync(account);
        Assert.Equal(
            [AccountAudit.TwoFactorEnabled, AccountAudit.RecoveryCodesReplaced, AccountAudit.RecoveryCodeUsed, AccountAudit.TwoFactorDisabled],
            entries.Select(e => e.Action));
        Assert.All(entries, e => Assert.Equal(account.Value, e.ActorId));
        Assert.All(entries, e => AssertNamesNoOne(e, email));
    }

    [Fact]
    public async Task An_authenticator_code_at_sign_in_is_not_audited_but_a_recovery_code_is()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var key = await BeginSetupAsync(account);
        var codes = await EnableAsync(account, key);
        fixture.Time.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));

        Assert.True((await VerifyAsync(account, CodeFor(key))).IsSuccess);
        Assert.True((await VerifyAsync(account, codes[1])).IsSuccess);

        Assert.Equal(
            [AccountAudit.TwoFactorEnabled, AccountAudit.RecoveryCodeUsed],
            (await EntriesAsync(account)).Select(e => e.Action));
    }

    [Fact]
    public async Task Only_the_attempt_that_locks_the_account_is_audited_and_the_system_did_it()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);

        // Five wrong passwords lock the account; the sixth try is refused without being counted.
        for (var i = 1; i <= 6; i++)
        {
            using var wrong = await fixture.Client(account).PostAsJsonAsync(Change, new ChangePasswordRequest($"wrong password number {i}", AccountApiFixture.SecondPassphrase));
            Assert.Equal(i < 5 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests, wrong.StatusCode);
        }

        var entry = Assert.Single(await EntriesAsync(account));
        Assert.Equal(AccountAudit.LockedOut, entry.Action);
        Assert.Equal("system", entry.ActorKind);
        Assert.Null(entry.ActorId);
        Assert.Equal(account.ToString(), entry.TargetId);
        AssertNamesNoOne(entry, email);
    }

    [Fact]
    public async Task Lockout_at_sign_in_is_audited_once()
    {
        var email = AccountApiFixture.NewEmail();
        var account = await fixture.RegisterConfirmedAsync(email);

        for (var i = 1; i <= 6; i++)
        {
            var signedIn = await fixture.WithServiceAsync<ISessionFacade, Result<SessionStart>>(
                f => f.SignInAsync(new SignInRequest(email, $"wrong password number {i}"), default));
            Assert.Equal(i < 5 ? AccountErrors.InvalidCredentials : AccountErrors.TooManyAttempts, signedIn.Error);
        }

        var entry = Assert.Single(await EntriesAsync(account));
        Assert.Equal((AccountAudit.LockedOut, "system"), (entry.Action, entry.ActorKind));
    }

    [Fact]
    public async Task Lockout_from_wrong_codes_is_audited_once()
    {
        var account = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var key = await BeginSetupAsync(account);

        for (var i = 1; i <= 6; i++)
        {
            using var wrong = await fixture.Client(account).PostAsJsonAsync(Enable, new EnableTwoFactorRequest(WrongCode(key)));
            Assert.Equal(i < 5 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests, wrong.StatusCode);
        }

        Assert.Equal(AccountAudit.LockedOut, Assert.Single(await EntriesAsync(account)).Action);
    }

    [Fact]
    public async Task Another_account_s_changes_never_land_in_this_one_s_trail()
    {
        var first = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());
        var second = await fixture.RegisterConfirmedAsync(AccountApiFixture.NewEmail());

        using (var changed = await fixture.Client(second).PostAsJsonAsync(Change, new ChangePasswordRequest(AccountApiFixture.FirstPassphrase, AccountApiFixture.SecondPassphrase)))
        {
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        }

        Assert.Empty(await EntriesAsync(first));
        Assert.Equal(second.Value, Assert.Single(await EntriesAsync(second)).ActorId);
    }

    // Every text column, with the address's local part and domain checked separately.
    private static void AssertNamesNoOne(AuditEntry entry, string email)
    {
        var local = email[..email.IndexOf('@', StringComparison.Ordinal)];
        foreach (var value in new[] { entry.Action, entry.ActorKind, entry.TargetKind, entry.TargetId, entry.Reason, entry.TraceId })
        {
            Assert.DoesNotContain(local, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("example.test", value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    private Task<List<AuditEntry>> EntriesAsync(AccountId account) =>
        fixture.WithServiceAsync<PantryPressDbContext, List<AuditEntry>>(db => db.Set<AuditEntry>()
            .AsNoTracking()
            .Where(e => e.TargetKind == AccountAudit.TargetKind && e.TargetId == account.ToString())
            .OrderBy(e => e.Id)
            .ToListAsync());

    private async Task<(Guid Account, string Token)> RequestResetAsync(string email)
    {
        // A minute after the last request, clear of the per-account email throttle.
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        using var response = await fixture.Client().PostAsJsonAsync(Forgot, new ForgotPasswordRequest(email));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return AccountApiFixture.ReadLink(await fixture.NextEmailToAsync(email, "Reset your PantryPress password"), "reset-password");
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
