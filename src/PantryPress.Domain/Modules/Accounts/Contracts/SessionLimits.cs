namespace PantryPress.Domain.Modules.Accounts.Contracts;

/// <summary>
/// How long the gateway's sessions can live, as far as the API needs to know (ACCT-002; PP1.12). The gateway holds the
/// session and has no reference to this assembly, so it keeps its own copy of the lifetime and a test pins that copy
/// within <see cref="RevocationRetention"/>.
/// </summary>
public static class SessionLimits
{
    /// <summary>A session id is 32 random bytes in base64url (43 characters); anything much longer is not one of ours.</summary>
    public const int MaxSessionIdLength = 64;

    /// <summary>
    /// How long a sign-out is remembered. Longer than any session can live - the gateway's absolute lifetime is 12
    /// hours - so a signed-out cookie is refused for as long as it could otherwise be used, and then forgotten.
    /// </summary>
    public static readonly TimeSpan RevocationRetention = TimeSpan.FromHours(24);

    /// <summary>The shape the gateway mints: base64url characters only, within the length.</summary>
    public static bool IsValidSessionId(string? sessionId) =>
        sessionId is { Length: > 0 and <= MaxSessionIdLength }
        && sessionId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
