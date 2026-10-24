using PantryPress.Domain.Managers.Results;

namespace PantryPress.Domain.Modules.Accounts.Contracts;

/// <summary>
/// The Accounts module's expected failures. Codes are stable (api-contract.md); titles are what a blogger reads. None
/// says whether an email address has an account.
/// </summary>
public static class AccountErrors
{
    public static readonly Error InvalidVerificationLink = new(
        ErrorKind.Rejected,
        "invalid-verification-link",
        "This confirmation link doesn't work anymore. Ask for a new one and use the newest email.");

    public static readonly Error InvalidResetLink = new(
        ErrorKind.Rejected,
        "invalid-reset-link",
        "This reset link doesn't work anymore. Ask for a new one and use the newest email.");

    public static readonly Error IncorrectPassword = new(
        ErrorKind.Rejected,
        "incorrect-password",
        "That password isn't right.");

    public static readonly Error InvalidCode = new(
        ErrorKind.Rejected,
        "invalid-code",
        "That code isn't right. Check your authenticator app and try the newest code.");

    public static readonly Error TooManyAttempts = new(
        ErrorKind.TooManyAttempts,
        "too-many-attempts",
        "Too many tries. Wait a few minutes, then try again.");

    /// <summary>
    /// <see cref="TooManyAttempts"/> for a lockout with <paramref name="wait"/> left: the title says how long in whole
    /// minutes, and <see cref="Error.RetryAfter"/> carries the exact wait for <c>Retry-After</c> (PP1.12).
    /// </summary>
    public static Error TooManyAttemptsFor(TimeSpan wait)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes));
        return TooManyAttempts with
        {
            Title = minutes == 1
                ? "Too many tries. Wait a minute, then try again."
                : $"Too many tries. Wait {minutes.ToString(System.Globalization.CultureInfo.InvariantCulture)} minutes, then try again.",
            RetryAfter = wait,
        };
    }

    /// <summary>
    /// A right code sent again - a retry after a lost answer, or a code read over a shoulder (PP1.12). Refused, because
    /// a code works once, but not counted toward lockout: it is not a guess.
    /// </summary>
    public static readonly Error CodeAlreadyUsed = new(
        ErrorKind.Rejected,
        "code-already-used",
        "That code was already used. Wait for the next one in your authenticator app.");

    public static readonly Error TwoFactorAlreadyOn = new(
        ErrorKind.Conflict,
        "two-factor-already-on",
        "Two-step sign-in is already on.");

    public static readonly Error TwoFactorSetupRequired = new(
        ErrorKind.Conflict,
        "two-factor-setup-required",
        "Start setup first to get a key for your authenticator app.");

    public static readonly Error TwoFactorOff = new(
        ErrorKind.Conflict,
        "two-factor-off",
        "Two-step sign-in is off for this account.");

    public static readonly Error ChangedElsewhere = new(
        ErrorKind.Conflict,
        "changed-elsewhere",
        "Your account was changed somewhere else at the same time. Please try again.");

    /// <summary>
    /// A half-finished sign-in cannot be finished: it took too long, or the account changed underneath it (PP1.6).
    /// </summary>
    public static readonly Error SignInExpired = new(
        ErrorKind.Rejected,
        "sign-in-expired",
        "That sign-in didn't finish in time. Please start again.");

    /// <summary>
    /// The session is over: signed out, its password or second factor changed, or the account is gone (PP1.6,
    /// PP1.12). Told apart from a plain "sign in" so the admin app can say "you were signed out". On the internal surface
    /// it is a 400 the gateway acts on; the admin app only ever meets it as the gateway's or the session scheme's 401.
    /// </summary>
    public static readonly Error SessionEnded = new(
        ErrorKind.Rejected,
        "session-ended",
        "You've been signed out. Sign in again to pick up where you left off.");

    public static readonly Error AccountNotFound = new(
        ErrorKind.NotFound,
        "account-not-found",
        "We couldn't find that account.");

    /// <summary>
    /// Sign-in refused (PP1.6). One error for an unknown address and a wrong password alike, so the answer never says
    /// which addresses have accounts.
    /// </summary>
    public static readonly Error InvalidCredentials = new(
        ErrorKind.Rejected,
        "invalid-credentials",
        "That email address and password don't match.");

    /// <summary>
    /// The address is not confirmed yet. Publishing waits for it (ACCT-001), and so does turning on two-step sign-in
    /// (PP1.12), so the title names neither.
    /// </summary>
    public static readonly Error EmailNotVerified = new(
        ErrorKind.Forbidden,
        "email-not-verified",
        "Confirm your email address first. Check your inbox for our email.");
}
