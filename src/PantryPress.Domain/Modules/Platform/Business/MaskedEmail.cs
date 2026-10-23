namespace PantryPress.Domain.Modules.Platform.Business;

/// <summary>
/// An email address shortened for a log line (backend.md: never log a full address). Enough for the operator to
/// recognize which entry of their own list needs attention, not enough to be a mailing list.
/// </summary>
public static class MaskedEmail
{
    public static string From(string? email)
    {
        var address = (email ?? string.Empty).Trim();
        var at = address.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
        {
            return "***";
        }

        return $"{address[0]}***{address[at..]}";
    }
}
