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
        "Too many tries. Wait 15 minutes, then try again.");

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

    public static readonly Error EmailNotVerified = new(
        ErrorKind.Forbidden,
        "email-not-verified",
        "Confirm your email address before you publish. Check your inbox for our email.");
}
