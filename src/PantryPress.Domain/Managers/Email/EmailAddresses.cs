using System.Net.Mail;
using System.Text;

namespace PantryPress.Domain.Managers.Email;

/// <summary>
/// The one shape of email address PantryPress accepts from a person - an account's (ACCT-001) and an invited member's
/// (SITE-010; PP2.11) - and how two spellings of one address compare. Pure: no I/O.
/// </summary>
public static class EmailAddresses
{
    /// <summary>RFC 5321 path limit, the same bound <see cref="EmailMessage"/> enforces on recipients.</summary>
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

    /// <summary>
    /// The form two addresses are compared in: composed and upper-cased, as Identity's default normalizer has it, so
    /// "Ann@Example.com" and "ann@example.com" are one address. Only for addresses that pass <see cref="IsValid"/>.
    /// </summary>
    public static string Normalize(string email) => email.Normalize(NormalizationForm.FormC).ToUpperInvariant();
}
