namespace PantryPress.Domain.Modules.Accounts.Business;

/// <summary>
/// Who may hold the platform role (ACCT-005). Pure: the facade that changes a role asks, and does what it is told.
/// </summary>
internal static class PlatformRoleRules
{
    /// <summary>
    /// Only an account whose address is confirmed may be granted the role (PP1.12). The operator's list names email
    /// addresses, and an unconfirmed account is only someone's claim to an address: without this, whoever signed up
    /// first with an address on the list would become an operator without ever receiving an email sent to it.
    /// Revoking never needs it.
    /// </summary>
    public static bool MayBeGranted(bool emailConfirmed) => emailConfirmed;

    /// <summary>
    /// Already as asked, so nothing is saved and the security stamp stays put: running the operator's list again never
    /// ends anyone's sessions.
    /// </summary>
    public static bool IsNoChange(bool holdsRole, bool grant) => holdsRole == grant;
}

/// <summary>Two-step sign-in rules (ACCT-001; PP1.3, PP1.6). Pure.</summary>
internal static class TwoFactorRules
{
    /// <summary>
    /// An authenticator code from a time step at or before the last one accepted is a replay (RFC 6238 section 5.2): it
    /// is refused, because a code works once. The token provider refuses it; the facade, told it was a replay, does not
    /// count it toward lockout (PP1.12).
    /// </summary>
    public static bool IsReplay(long step, long? lastAcceptedStep) => lastAcceptedStep is { } last && step <= last;

    /// <summary>
    /// Two-step sign-in can be set up only on an account whose address is confirmed (PP1.12). A second factor on an
    /// unconfirmed account would belong to whoever signed up with the address first, and the real owner - who gets the
    /// account back by resetting the password - would then meet a stranger's authenticator at every sign-in.
    /// </summary>
    public static bool MaySetUp(bool emailConfirmed) => emailConfirmed;

    /// <summary>
    /// A reset that confirms an address for the first time also clears a second factor the account already had (PP1.12).
    /// Since <see cref="MaySetUp"/>, only an account from before it can be in that state, and its second factor was set
    /// up by someone who never proved they read the inbox the reset link just reached.
    /// </summary>
    public static bool ResetClearsSecondFactor(bool emailConfirmedBefore, bool twoFactorEnabled) =>
        !emailConfirmedBefore && twoFactorEnabled;
}

/// <summary>Whether a forwarded session may act, and what it has proved (ACCT-002, auth.md; PP1.6). Pure.</summary>
internal static class SessionRules
{
    /// <summary>An account with two-step sign-in may act only on a session that passed it.</summary>
    public static bool MayAct(bool twoFactorEnabled, bool sessionPassedSecondStep) =>
        !twoFactorEnabled || sessionPassedSecondStep;

    /// <summary>
    /// Whether the session counts as having passed a second step now (PP1.12). Only while the account still has
    /// two-step sign-in: a session that passed it before the account turned it off has nothing left to vouch for, so
    /// it no longer meets a policy that requires the second step (the <c>PlatformAdmin</c> policy, auth.md).
    /// </summary>
    public static bool PassedSecondStep(bool twoFactorEnabled, bool sessionPassedSecondStep) =>
        twoFactorEnabled && sessionPassedSecondStep;
}

/// <summary>
/// How long the links in account email work (approved at PP1.2 and PP1.3), and when one queued in the outbox is too old
/// to send (PP1.12). The Worker makes a link when it sends the email, so a message that waited - a Worker that was down,
/// no email provider yet - must not turn an old request into a fresh, working link.
/// </summary>
internal static class LinkRules
{
    public static readonly TimeSpan EmailConfirmationLifetime = TimeSpan.FromHours(24);

    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether a queued link email is no longer worth sending: older than the link it would carry, or - for a reset - the
    /// account's security stamp has moved since the reset was asked for (a new password, a change to two-step sign-in),
    /// which would have voided a link made at the time. Notices without a link never go stale.
    /// </summary>
    public static bool IsStale(
        AccountEmailKind kind,
        DateTimeOffset requestedAt,
        DateTimeOffset now,
        string? stampWhenRequested,
        string currentStamp) => kind switch
        {
            AccountEmailKind.Verification => now - requestedAt > EmailConfirmationLifetime,
            AccountEmailKind.PasswordReset => now - requestedAt > PasswordResetLifetime
                || !string.Equals(stampWhenRequested, currentStamp, StringComparison.Ordinal),
            _ => false,
        };

    /// <summary>
    /// A short, one-way fingerprint of a security stamp, so a queued reset can tell whether the stamp moved without the
    /// stamp itself - a server-side secret - sitting in the outbox.
    /// </summary>
    public static string StampFingerprint(string stamp) =>
        System.Buffers.Text.Base64Url.EncodeToString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stamp)).AsSpan(0, 16));
}

/// <summary>Lockout rules (ACCT-001; PP1.3, PP1.11). Pure.</summary>
internal static class LockoutRules
{
    /// <summary>Wrong passwords or codes before the account locks (approved at PP1.3).</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>How long a locked account waits (approved at PP1.3).</summary>
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Audited only for the attempt that locks the account: neither the attempts before it nor a refused attempt on an
    /// account that is already locked (PP1.11).
    /// </summary>
    public static bool LockedByThisAttempt(bool lockedBefore, bool lockedAfter) => !lockedBefore && lockedAfter;

    /// <summary>
    /// How long a locked account still has to wait, for <c>Retry-After</c> (PP1.12): never less than a second, so a
    /// lockout that is just ending is not read as "go ahead now", and never more than the configured lockout, so a clock
    /// skew cannot tell a person to wait longer than the rule says.
    /// </summary>
    public static TimeSpan Remaining(DateTimeOffset? lockoutEnd, DateTimeOffset now, TimeSpan lockout)
    {
        var remaining = (lockoutEnd ?? now) - now;
        return remaining < TimeSpan.FromSeconds(1)
            ? TimeSpan.FromSeconds(1)
            : remaining > lockout ? lockout : remaining;
    }
}
