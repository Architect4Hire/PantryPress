using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Repositories;

namespace PantryPress.Domain.Modules.Accounts.DataLayer;

/// <summary>
/// Tokens for emailed links, in the same shape as Identity's data-protection provider (encrypted and authenticated, bound
/// to the account, the purpose, and the security stamp) but timed by <see cref="IClock"/>: Identity's provider reads
/// the system clock, which backend.md rules out and tests cannot control. A changed security stamp (a new password, a
/// change to two-step sign-in) voids every outstanding link, which makes a reset link single-use.
/// </summary>
internal abstract class LinkTokenProvider(IDataProtectionProvider dataProtection, IClock clock, string name, TimeSpan lifetime)
    : IUserTwoFactorTokenProvider<ApplicationUser>
{
    private const string Version = "1";

    private readonly IDataProtector _protector = dataProtection.CreateProtector(AccountTokens.LinkTokenPurpose, name);

    public TimeSpan Lifetime { get; } = lifetime;

    // Link tokens are never a second sign-in factor.
    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ApplicationUser> manager, ApplicationUser user) =>
        Task.FromResult(false);

    public async Task<string> GenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var stamp = await manager.GetSecurityStampAsync(user);
        var payload = string.Join(
            '\n',
            Version,
            clock.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            user.Id.ToString("N"),
            purpose,
            stamp);
        return Base64Url.EncodeToString(_protector.Protect(Encoding.UTF8.GetBytes(payload)));
    }

    public async Task<bool> ValidateAsync(
        string purpose,
        string token,
        UserManager<ApplicationUser> manager,
        ApplicationUser user)
    {
        string[] parts;
        try
        {
            parts = Encoding.UTF8.GetString(_protector.Unprotect(Base64Url.DecodeFromChars(token))).Split('\n');
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }

        if (parts is not [Version, var issued, var accountId, var tokenPurpose, var stamp]
            || !long.TryParse(issued, NumberStyles.None, CultureInfo.InvariantCulture, out var issuedSeconds))
        {
            return false;
        }

        var age = clock.UtcNow - DateTimeOffset.FromUnixTimeSeconds(issuedSeconds);
        return age >= TimeSpan.Zero
            && age <= Lifetime
            && accountId == user.Id.ToString("N")
            && tokenPurpose == purpose
            && stamp == await manager.GetSecurityStampAsync(user);
    }
}

/// <summary>Email confirmation links: 24 hours (approved at PP1.2).</summary>
internal sealed class EmailConfirmationTokenProvider(IDataProtectionProvider dataProtection, IClock clock)
    : LinkTokenProvider(dataProtection, clock, Name, TimeSpan.FromHours(24))
{
    public const string Name = "PantryPress.EmailConfirmation";
}

/// <summary>Password reset links: one hour (approved at PP1.3), and single-use through the security stamp.</summary>
internal sealed class PasswordResetTokenProvider(IDataProtectionProvider dataProtection, IClock clock)
    : LinkTokenProvider(dataProtection, clock, Name, TimeSpan.FromHours(1))
{
    public const string Name = "PantryPress.PasswordReset";
}

/// <summary>
/// Authenticator-app codes (RFC 6238), replacing Identity's provider for two reasons: Identity reads the system clock,
/// and it accepts the same code again for as long as it stays valid. Each accepted code's time step is stored with the
/// account, and a code from that step or an earlier one is refused (RFC 6238 section 5.2). Two requests racing with the
/// same code both write the account, so the second fails on its concurrency stamp when the unit of work is saved. The
/// replay rule lives here rather than in Business because Identity calls token providers itself; the arithmetic is in
/// <see cref="Totp"/>.
/// </summary>
internal sealed class AuthenticatorCodeProvider(IClock clock) : IUserTwoFactorTokenProvider<ApplicationUser>
{
    public const string Name = "PantryPress.Authenticator";

    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ApplicationUser> manager, ApplicationUser user) =>
        !string.IsNullOrEmpty(await manager.GetAuthenticatorKeyAsync(user));

    // The app makes the codes; the server never sends one.
    public Task<string> GenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user) =>
        Task.FromResult(string.Empty);

    public async Task<bool> ValidateAsync(
        string purpose,
        string token,
        UserManager<ApplicationUser> manager,
        ApplicationUser user)
    {
        var key = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key) || !Totp.TryNormalize(token, out var code))
        {
            return false;
        }

        if (Totp.Match(Base32.Decode(key), code, Totp.StepAt(clock.UtcNow)) is not { } step)
        {
            return false;
        }

        var lastUsed = await manager.GetAuthenticationTokenAsync(user, AccountTokens.Provider, AccountTokens.AuthenticatorLastStep);
        if (long.TryParse(lastUsed, NumberStyles.None, CultureInfo.InvariantCulture, out var last) && step <= last)
        {
            return false;
        }

        var recorded = await manager.SetAuthenticationTokenAsync(
            user,
            AccountTokens.Provider,
            AccountTokens.AuthenticatorLastStep,
            step.ToString(CultureInfo.InvariantCulture));
        return recorded.Succeeded;
    }
}
