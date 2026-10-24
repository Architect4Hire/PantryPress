using Microsoft.AspNetCore.Identity;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.Repositories;

namespace PantryPress.Domain.Modules.Accounts.DataLayer;

/// <summary>
/// The Accounts data layer (ARCH-002): composes Identity's user manager over <see cref="AccountUserStore"/>, the signed-out
/// sessions in <see cref="SessionRevocationRepository"/>, and the <see cref="AccountsUnitOfWork"/> for the facades.
/// Platform roles are not here: they live in <see cref="AccountRolesDataLayer"/>, which only the operator's process
/// registers (ACCT-005). Nothing here saves except <see cref="SaveChangesAsync"/>, <see cref="SaveNewAccountAsync"/>, and
/// <see cref="SaveRevocationAsync"/>, which the facades call, and <see cref="PurgeExpiredRevocationsAsync"/>, one batched
/// delete statement for the Worker's sweep; every other call only stages changes.
/// </summary>
internal sealed class AccountsDataLayer(
    AccountUserManager users,
    AccountsUnitOfWork unitOfWork,
    SessionRevocationRepository revocations)
{
    // Hashing a password for an address that already has an account makes both sign-up paths do the same slow work.
    private static readonly ApplicationUser TimingUser = new();

    public Task<ApplicationUser?> FindAsync(AccountId account) => users.FindByIdAsync(account.ToString());

    public Task<ApplicationUser?> FindByEmailAsync(string email) => users.FindByEmailAsync(email);

    public void HashPasswordForTiming(string password) => users.PasswordHasher.HashPassword(TimingUser, password);

    /// <summary>Stages a new account; <see cref="SaveNewAccountAsync"/> commits it.</summary>
    public Task<IdentityResult> CreateAsync(ApplicationUser user, string password) => users.CreateAsync(user, password);

    public Task<SaveOutcome> SaveNewAccountAsync(string email, CancellationToken cancellationToken) =>
        unitOfWork.SaveNewAccountAsync(email, cancellationToken);

    public Task<SaveOutcome> SaveChangesAsync(CancellationToken cancellationToken) => unitOfWork.SaveAsync(cancellationToken);

    /// <summary>The stored <see cref="EmailThrottle"/> state for one kind of email, or null for none.</summary>
    public Task<string?> GetEmailThrottleAsync(ApplicationUser user, ThrottledEmail kind) =>
        users.GetAuthenticationTokenAsync(user, AccountTokens.Provider, AccountTokens.EmailThrottle(kind));

    public async Task SetEmailThrottleAsync(ApplicationUser user, ThrottledEmail kind, string state) =>
        Ensure(await TrySetEmailThrottleAsync(user, kind, state));

    /// <summary>
    /// <see cref="SetEmailThrottleAsync"/> for an account that is not saved yet, where Identity validates the account
    /// again and a concurrent sign-up for the same address can already have been saved: the facade reads that refusal
    /// as the race it lost (PP1.12).
    /// </summary>
    public Task<IdentityResult> TrySetEmailThrottleAsync(ApplicationUser user, ThrottledEmail kind, string state) =>
        users.SetAuthenticationTokenAsync(user, AccountTokens.Provider, AccountTokens.EmailThrottle(kind), state);

    public Task<string> CreateEmailConfirmationTokenAsync(ApplicationUser user) =>
        users.GenerateEmailConfirmationTokenAsync(user);

    public async Task<bool> ConfirmEmailAsync(ApplicationUser user, string token) =>
        (await users.ConfirmEmailAsync(user, token)).Succeeded;

    public Task<string> CreatePasswordResetTokenAsync(ApplicationUser user) => users.GeneratePasswordResetTokenAsync(user);

    public Task<bool> IsValidPasswordResetTokenAsync(ApplicationUser user, string token) =>
        users.VerifyUserTokenAsync(
            user,
            users.Options.Tokens.PasswordResetTokenProvider,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose,
            token);

    /// <summary>Checks the token again, sets the password, and rotates the security stamp, voiding outstanding links.</summary>
    public Task<IdentityResult> ResetPasswordAsync(ApplicationUser user, string token, string newPassword) =>
        users.ResetPasswordAsync(user, token, newPassword);

    public Task<IdentityResult> ChangePasswordAsync(ApplicationUser user, string currentPassword, string newPassword) =>
        users.ChangePasswordAsync(user, currentPassword, newPassword);

    public async Task MarkEmailConfirmedAsync(ApplicationUser user)
    {
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            Ensure(await users.UpdateAsync(user));
        }
    }

    /// <summary>Stages a new security stamp, which ends the account's existing sessions once PP1.6 validates it.</summary>
    public async Task RotateSecurityStampAsync(ApplicationUser user) => Ensure(await users.UpdateSecurityStampAsync(user));

    public Task<bool> CheckPasswordAsync(ApplicationUser user, string password) => users.CheckPasswordAsync(user, password);

    public Task<bool> IsLockedOutAsync(ApplicationUser user) => users.IsLockedOutAsync(user);

    /// <summary>When the lockout ends, or null for none. Identity sets it from the system clock.</summary>
    public Task<DateTimeOffset?> GetLockoutEndAsync(ApplicationUser user) => users.GetLockoutEndDateAsync(user);

    /// <summary>
    /// Whether <paramref name="code"/> is an authenticator code this account already used (PP1.12): it matches a time
    /// step at or before the last one accepted. Read only; a right new code is checked by
    /// <see cref="VerifyAuthenticatorCodeAsync"/>.
    /// </summary>
    public async Task<bool> IsUsedAuthenticatorCodeAsync(ApplicationUser user, string code, DateTimeOffset now)
    {
        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key) || !Totp.TryNormalize(code, out var normalized)
            || Totp.Match(Base32.Decode(key), normalized, Totp.StepAt(now)) is not { } step)
        {
            return false;
        }

        var lastUsed = await users.GetAuthenticationTokenAsync(user, AccountTokens.Provider, AccountTokens.AuthenticatorLastStep);
        return TwoFactorRules.IsReplay(
            step,
            long.TryParse(lastUsed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var last)
                ? last
                : null);
    }

    /// <summary>Counts a wrong password or code; enough of them lock the account (Identity lockout options).</summary>
    public async Task RecordFailedAttemptAsync(ApplicationUser user) => Ensure(await users.AccessFailedAsync(user));

    public async Task ClearFailedAttemptsAsync(ApplicationUser user) => Ensure(await users.ResetAccessFailedCountAsync(user));

    public async Task ClearLockoutAsync(ApplicationUser user)
    {
        Ensure(await users.ResetAccessFailedCountAsync(user));
        Ensure(await users.SetLockoutEndDateAsync(user, null));
    }

    /// <summary>A new authenticator key (rotating the security stamp), returned in base32.</summary>
    public async Task<string> NewAuthenticatorKeyAsync(ApplicationUser user)
    {
        Ensure(await users.ResetAuthenticatorKeyAsync(user));
        Ensure(await users.RemoveAuthenticationTokenAsync(user, AccountTokens.Provider, AccountTokens.AuthenticatorLastStep));
        return await users.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException("The new authenticator key was not stored.");
    }

    public async Task<bool> HasAuthenticatorKeyAsync(ApplicationUser user) =>
        !string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(user));

    /// <summary>True for a valid code not used before; records it as used (<see cref="AuthenticatorCodeProvider"/>).</summary>
    public Task<bool> VerifyAuthenticatorCodeAsync(ApplicationUser user, string code) =>
        users.VerifyTwoFactorTokenAsync(user, AuthenticatorCodeProvider.Name, code);

    public async Task SetTwoFactorEnabledAsync(ApplicationUser user, bool enabled) =>
        Ensure(await users.SetTwoFactorEnabledAsync(user, enabled));

    public async Task<IReadOnlyList<string>> NewRecoveryCodesAsync(ApplicationUser user) =>
        [.. await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodes.Count) ?? []];

    /// <summary>Removes every recovery code and makes the old authenticator key useless.</summary>
    public async Task ForgetSecondFactorsAsync(ApplicationUser user)
    {
        await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
        Ensure(await users.ResetAuthenticatorKeyAsync(user));
        Ensure(await users.RemoveAuthenticationTokenAsync(user, AccountTokens.Provider, AccountTokens.AuthenticatorLastStep));
    }

    public async Task<bool> RedeemRecoveryCodeAsync(ApplicationUser user, string code) =>
        (await users.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded;

    public Task<int> CountRecoveryCodesAsync(ApplicationUser user) => users.CountRecoveryCodesAsync(user);

    /// <summary>
    /// The account's current security stamp. The BFF session is bound to it (PP1.6), so every rotation elsewhere in
    /// this module ends the account's other sessions.
    /// </summary>
    public Task<string> GetSecurityStampAsync(ApplicationUser user) => users.GetSecurityStampAsync(user);

    /// <summary>
    /// The platform roles on the account, for a session's role claims (PP1.4, PP1.6). Reading roles is not granting
    /// them: changing them is <see cref="AccountRolesDataLayer"/>'s alone, in the operator's process (ACCT-005).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetRolesAsync(ApplicationUser user) => [.. await users.GetRolesAsync(user)];

    /// <summary>Whether this session was signed out (PP1.12): one keyed, untracked read per forwarded request.</summary>
    public Task<bool> IsSessionRevokedAsync(string sessionId, CancellationToken cancellationToken) =>
        revocations.IsRevokedAsync(sessionId, cancellationToken);

    /// <summary>
    /// Stages a sign-out, unless the session is already signed out; true when something was staged for
    /// <see cref="SaveRevocationAsync"/>.
    /// </summary>
    public async Task<bool> StageRevocationAsync(RevokedSession revocation, CancellationToken cancellationToken)
    {
        if (await revocations.IsRevokedAsync(revocation.SessionId, cancellationToken))
        {
            return false;
        }

        revocations.Stage(revocation);
        return true;
    }

    /// <summary>Saves a staged sign-out; losing a race to a second sign-out of the same session is done too.</summary>
    public Task SaveRevocationAsync(string sessionId, CancellationToken cancellationToken) =>
        unitOfWork.SaveRevocationAsync(sessionId, cancellationToken);

    /// <summary>Deletes up to <paramref name="batchSize"/> sign-outs that no session can outlive any more.</summary>
    public Task<int> PurgeExpiredRevocationsAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
        revocations.PurgeExpiredAsync(now, batchSize, cancellationToken);

    // Staged changes fail only through Identity's own validators, which the facades have already satisfied.
    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"An account change was refused: {string.Join(", ", result.Errors.Select(e => e.Code))}.");
        }
    }
}
