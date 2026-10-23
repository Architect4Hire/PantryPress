using System.Diagnostics.CodeAnalysis;

namespace PantryPress.Domain.Modules.Platform.Business;

/// <summary>
/// The operator's list of platform administrators, as it arrives from configuration: one string holding email
/// addresses separated by commas, semicolons, or new lines. One value keeps it to one secret in Key Vault and one
/// environment variable, which is what the operator hands the migration job (ACCT-005).
/// </summary>
public static class PlatformAdminList
{
    private static readonly char[] Separators = [',', ';', '\n', '\r'];

    /// <summary>
    /// The distinct addresses in order, ignoring casing and surrounding whitespace. False when an entry is not an
    /// email address, which fails the operator's configuration at startup rather than half-applying the list.
    /// </summary>
    public static bool TryParse(string? configured, [NotNullWhen(true)] out IReadOnlyList<string>? emails)
    {
        emails = null;
        var parsed = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in (configured ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var email = entry.Trim();
            if (email.Length == 0)
            {
                continue;
            }

            if (!IsEmailAddress(email))
            {
                return false;
            }

            if (seen.Add(email))
            {
                parsed.Add(email);
            }
        }

        emails = parsed;
        return true;
    }

    // Deliberately shallow: the address only has to find an account that already exists, and Identity's own
    // normalizer does the matching.
    private static bool IsEmailAddress(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at > 0
            && at == email.LastIndexOf('@')
            && at < email.Length - 1
            && !email.Any(char.IsWhiteSpace);
    }
}
