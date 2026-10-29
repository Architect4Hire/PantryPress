namespace PantryPress.Domain.Modules.Accounts.Contracts;

/// <summary>
/// The accounts a developer's machine starts with (PP3.9a): sign-up stays closed until PPL.13, so a local run would
/// otherwise have nobody to sign in as. Registered only by the migration job in local Development
/// (<c>AddAccountsDevelopmentSeed</c>), so no request-serving host can reach it, exactly as the platform role's path is
/// the operator's process alone (ACCT-005). It confirms an address through this module's own store, never by writing
/// Identity's tables, and sends nothing: no confirmation email, no outbox message, no link token.
/// </summary>
public interface IDevelopmentAccounts
{
    /// <summary>
    /// The account that signs in with <paramref name="email"/>, created with that address already confirmed if it has
    /// none. An account that exists is returned untouched - the same password, security stamp, roles, and confirmation
    /// state - so a second run changes nothing.
    /// </summary>
    Task<DevelopmentAccount> EnsureAsync(string email, string password, CancellationToken cancellationToken);
}

/// <summary>What <see cref="IDevelopmentAccounts.EnsureAsync"/> did.</summary>
public enum DevelopmentAccountOutcome
{
    /// <summary>A new account, with its address confirmed.</summary>
    Created,

    /// <summary>The address already had an account, which was left exactly as it was.</summary>
    AlreadyThere,

    /// <summary>
    /// The configured password does not meet the platform's own password rules, which the seed will not bend. Nothing
    /// was created; the module logs what to change.
    /// </summary>
    PasswordRefused,
}

/// <summary>
/// An account the seed asked for. <see cref="Account"/> is null only for
/// <see cref="DevelopmentAccountOutcome.PasswordRefused"/>.
/// </summary>
public sealed record DevelopmentAccount(AccountId? Account, DevelopmentAccountOutcome Outcome);
