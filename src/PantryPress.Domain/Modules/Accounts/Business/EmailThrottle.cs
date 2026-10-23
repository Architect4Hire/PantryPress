using System.Globalization;

namespace PantryPress.Domain.Modules.Accounts.Business;

/// <summary>Account emails anyone can trigger by typing an address, so each is throttled per account.</summary>
internal enum ThrottledEmail
{
    Verification,
    PasswordReset,
}

/// <summary>
/// The per-address email throttle (ACCT-003, approved at PP1.2): at most one email of a kind per minute and five per
/// hour for one account, so nobody can flood an inbox through sign-up, resend, or forgot-password. A throttled request
/// still gets the same response, so the throttle never reveals that an account exists. Pure: the caller stores the
/// state string with the account.
/// </summary>
internal static class EmailThrottle
{
    public static readonly TimeSpan MinimumGap = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);
    public const int MaxPerWindow = 5;

    /// <summary>
    /// Decides whether one more email may be sent now. <paramref name="state"/> is what the last call returned, or null
    /// for none. On true, <paramref name="next"/> is the state to store; on false it is the unchanged state.
    /// </summary>
    public static bool TryTake(string? state, DateTimeOffset now, out string next)
    {
        var (windowStart, count, lastSent) = Parse(state);

        if (lastSent is { } last && now - last < MinimumGap)
        {
            next = state!;
            return false;
        }

        if (windowStart is not { } start || now - start >= Window || now < start)
        {
            windowStart = now;
            count = 0;
        }

        if (count >= MaxPerWindow)
        {
            next = state!;
            return false;
        }

        next = Format(windowStart.Value, count + 1, now);
        return true;
    }

    private static (DateTimeOffset? WindowStart, int Count, DateTimeOffset? LastSent) Parse(string? state)
    {
        var parts = state?.Split(':');
        if (parts is not { Length: 3 }
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var start)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var last))
        {
            return (null, 0, null);
        }

        return (DateTimeOffset.FromUnixTimeSeconds(start), count, DateTimeOffset.FromUnixTimeSeconds(last));
    }

    private static string Format(DateTimeOffset windowStart, int count, DateTimeOffset lastSent) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{windowStart.ToUnixTimeSeconds()}:{count}:{lastSent.ToUnixTimeSeconds()}");
}
