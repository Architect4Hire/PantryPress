using Microsoft.AspNetCore.Identity;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.Repositories;

namespace PantryPress.Domain.Modules.Accounts.DataLayer;

/// <summary>
/// The Accounts data layer (ARCH-002): composes Identity's user manager over <see cref="AccountUserStore"/> and the
/// <see cref="AccountsUnitOfWork"/> for the facades. Nothing here saves except <see cref="SaveChangesAsync"/> and
/// <see cref="SaveNewAccountAsync"/>; every other call only stages changes.
/// </summary>
internal sealed class AccountsDataLayer(AccountUserManager users, AccountsUnitOfWork unitOfWork)
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
        Ensure(await users.SetAuthenticationTokenAsync(user, AccountTokens.Provider, AccountTokens.EmailThrottle(kind), state));

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

    public Task<bool> IsInRoleAsync(ApplicationUser user, string role) => users.IsInRoleAsync(user, role);

    /// <summary>Stages the role; the role row itself is seeded by a migration and never created here.</summary>
    public async Task AddToRoleAsync(ApplicationUser user, string role) => Ensure(await users.AddToRoleAsync(user, role));

    public async Task RemoveFromRoleAsync(ApplicationUser user, string role) =>
        Ensure(await users.RemoveFromRoleAsync(user, role));

    public Task<IList<ApplicationUser>> GetAccountsInRoleAsync(string role) => users.GetUsersInRoleAsync(role);

    /// <summary>Stages a new security stamp, which ends the account's existing sessions once PP1.6 validates it.</summary>
    public async Task RotateSecurityStampAsync(ApplicationUser user) => Ensure(await users.UpdateSecurityStampAsync(user));

    public Task<bool> CheckPasswordAsync(ApplicationUser user, string password) => users.CheckPasswordAsync(user, password);

    public Task<bool> IsLockedOutAsync(ApplicationUser user) => users.IsLockedOutAsync(user);

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
