using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Accounts.Business;

namespace PantryPress.Domain.Modules.Accounts.Repositories;

/// <summary>
/// The account repository: Identity's EF Core user store with three changes.
/// <list type="bullet">
/// <item>It never saves on its own (<see cref="UserStoreBase{TUser,TKey,TUserClaim,TUserLogin,TUserToken}.AutoSaveChanges"/>
/// off). Each facade call is one unit of work that <c>AccountsDataLayer.SaveChangesAsync</c> commits, so a multi-step
/// change (two-step sign-in on plus recovery codes) is saved whole or not at all, and a concurrent change to the same
/// account fails on its concurrency stamp.</item>
/// <item>The authenticator key is encrypted at rest with data protection.</item>
/// <item>Recovery codes are stored only as salted hashes (<see cref="RecoveryCodeHashes"/>), never as Identity's plain
/// text.</item>
/// </list>
/// </summary>
internal sealed partial class AccountUserStore : UserStore<ApplicationUser, IdentityRole<Guid>, PantryPressDbContext, Guid>
{
    private readonly IDataProtector _keyProtector;
    private readonly ILogger<AccountUserStore> _logger;

    public AccountUserStore(
        PantryPressDbContext context,
        IDataProtectionProvider dataProtection,
        ILogger<AccountUserStore> logger,
        IdentityErrorDescriber? describer = null)
        : base(context, describer)
    {
        AutoSaveChanges = false;
        _keyProtector = dataProtection.CreateProtector(AccountTokens.AuthenticatorKeyPurpose);
        _logger = logger;
    }

    /// <summary>
    /// Stages an update. Identity's version attaches the user again on every call, which for a user already tracked
    /// accepts the staged changes as its original values; a second update in the same unit of work would then check the
    /// wrong concurrency stamp and fail. A tracked user only needs a new stamp: the context already sees its changes,
    /// and the save checks the stamp it was loaded with.
    /// </summary>
    public override Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (Context.Entry(user).State == EntityState.Detached)
        {
            return base.UpdateAsync(user, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        return Task.FromResult(IdentityResult.Success);
    }

    public override Task SetAuthenticatorKeyAsync(ApplicationUser user, string key, CancellationToken cancellationToken) =>
        base.SetAuthenticatorKeyAsync(user, _keyProtector.Protect(key), cancellationToken);

    public override async Task<string?> GetAuthenticatorKeyAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var stored = await base.GetAuthenticatorKeyAsync(user, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        try
        {
            return _keyProtector.Unprotect(stored);
        }
        catch (CryptographicException)
        {
            // The key ring that encrypted it is gone. Codes from the app can no longer be checked; recovery codes
            // still work, and setting up the app again makes a new key.
            LogUnreadableKey(_logger, user.Id);
            return null;
        }
    }

    public override Task ReplaceCodesAsync(
        ApplicationUser user,
        IEnumerable<string> recoveryCodes,
        CancellationToken cancellationToken) =>
        SetTokenAsync(
            user,
            AccountTokens.Provider,
            AccountTokens.RecoveryCodeHashes,
            RecoveryCodeHashes.Create(recoveryCodes).ToString(),
            cancellationToken);

    public override async Task<bool> RedeemCodeAsync(ApplicationUser user, string code, CancellationToken cancellationToken)
    {
        var stored = await GetTokenAsync(user, AccountTokens.Provider, AccountTokens.RecoveryCodeHashes, cancellationToken);
        if (!RecoveryCodeHashes.TryParse(stored, out var hashes) || !hashes.TryRedeem(code))
        {
            return false;
        }

        await SetTokenAsync(user, AccountTokens.Provider, AccountTokens.RecoveryCodeHashes, hashes.ToString(), cancellationToken);
        return true;
    }

    public override async Task<int> CountCodesAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var stored = await GetTokenAsync(user, AccountTokens.Provider, AccountTokens.RecoveryCodeHashes, cancellationToken);
        return RecoveryCodeHashes.TryParse(stored, out var hashes) ? hashes.Count : 0;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The authenticator key for account {AccountId} cannot be decrypted.")]
    private static partial void LogUnreadableKey(ILogger logger, Guid accountId);
}

/// <summary>Names of the values PantryPress keeps in Identity's user-token table, and data protection purposes.</summary>
internal static class AccountTokens
{
    public const string Provider = "PantryPress";
    public const string RecoveryCodeHashes = "RecoveryCodeHashes";
    public const string AuthenticatorLastStep = "AuthenticatorLastStep";
    public const string AuthenticatorKeyPurpose = "PantryPress.Accounts.AuthenticatorKey.v1";
    public const string LinkTokenPurpose = "PantryPress.Accounts.LinkToken.v1";

    public static string EmailThrottle(ThrottledEmail kind) => kind switch
    {
        ThrottledEmail.Verification => "EmailThrottle.Verification",
        ThrottledEmail.PasswordReset => "EmailThrottle.PasswordReset",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
