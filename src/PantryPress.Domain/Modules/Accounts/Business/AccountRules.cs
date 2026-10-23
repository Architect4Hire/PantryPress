using System.Net.Mail;

namespace PantryPress.Domain.Modules.Accounts.Business;

/// <summary>Email address rules for accounts (ACCT-001). Pure: no I/O.</summary>
internal static class EmailAddressRules
{
    /// <summary>RFC 5321 path limit, the same bound <c>EmailMessage</c> enforces on recipients.</summary>
    public const int MaxLength = 254;

    /// <summary>One bare address with a dotted domain: no display name, comments, or surrounding spaces.</summary>
    public static bool IsValid(string? email) =>
        !string.IsNullOrEmpty(email)
        && email.Length <= MaxLength
        && MailAddress.TryCreate(email, out var address)
        && address.Address == email
        && string.IsNullOrEmpty(address.DisplayName)
        && address.Host.Contains('.', StringComparison.Ordinal)
        && !email.Any(char.IsControl);

    /// <summary>The domain part, lower-cased; only for addresses that pass <see cref="IsValid"/>.</summary>
    public static string DomainOf(string email) => email[(email.LastIndexOf('@') + 1)..].ToLowerInvariant();
}

/// <summary>
/// Password rules (ACCT-001, approved at PP1.2): 12 to 128 characters and no composition rules, following current
/// guidance that length beats character classes. The upper bound keeps hashing cost bounded.
/// </summary>
internal static class PasswordRules
{
    public const int MinLength = 12;
    public const int MaxLength = 128;
}
