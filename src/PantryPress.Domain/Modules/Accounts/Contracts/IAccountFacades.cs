using PantryPress.Domain.Managers.Results;

namespace PantryPress.Domain.Modules.Accounts.Contracts;

/// <summary>Sign-up and email confirmation (ACCT-001, ACCT-003; PP1.2).</summary>
public interface IAccountRegistrationFacade
{
    /// <summary>
    /// Creates an unverified account and emails a confirmation link. Succeeds the same way when the address already
    /// has an account, so the response never reveals which addresses are registered.
    /// </summary>
    Task<Result> RegisterAsync(RegisterAccountRequest request, CancellationToken cancellationToken);

    Task<Result> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken);

    /// <summary>Emails a new confirmation link if the address has an unverified account; succeeds either way.</summary>
    Task<Result> ResendVerificationAsync(ResendVerificationRequest request, CancellationToken cancellationToken);
}

/// <summary>Password reset and change (ACCT-001; PP1.3).</summary>
public interface IPasswordFacade
{
    /// <summary>Emails a one-hour reset link if the address has an account; succeeds either way.</summary>
    Task<Result> ForgotAsync(ForgotPasswordRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a new password from a reset link. The link proves the person reads that inbox, so it also confirms the
    /// email address and clears a lockout.
    /// </summary>
    Task<Result> ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken);

    Task<Result> ChangeAsync(AccountId account, ChangePasswordRequest request, CancellationToken cancellationToken);
}

/// <summary>Optional authenticator-app two-step sign-in with recovery codes (ACCT-001; PP1.3).</summary>
public interface ITwoFactorFacade
{
    Task<Result<TwoFactorStatus>> GetStatusAsync(AccountId account, CancellationToken cancellationToken);

    /// <summary>Makes a new shared key for the authenticator app. Refused while two-step sign-in is on.</summary>
    Task<Result<AuthenticatorSetup>> BeginSetupAsync(AccountId account, CancellationToken cancellationToken);

    /// <summary>Turns two-step sign-in on once the app shows a valid code, and returns the first recovery codes.</summary>
    Task<Result<RecoveryCodeList>> EnableAsync(
        AccountId account,
        EnableTwoFactorRequest request,
        CancellationToken cancellationToken);

    Task<Result> DisableAsync(AccountId account, ConfirmPasswordRequest request, CancellationToken cancellationToken);

    /// <summary>Replaces every recovery code; the old ones stop working.</summary>
    Task<Result<RecoveryCodeList>> RegenerateRecoveryCodesAsync(
        AccountId account,
        ConfirmPasswordRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// The second step of sign-in, for the PP1.6 session flow: an authenticator code (each accepted once) or a recovery
    /// code (each usable once). Wrong codes count toward lockout.
    /// </summary>
    Task<Result> VerifySecondFactorAsync(
        AccountId account,
        SecondFactorRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Whether an account may publish (ACCT-001: verified email before publishing). The Posts module asks this before
/// any publish or schedule from PP5.
/// </summary>
public interface IAccountPublishingEligibility
{
    /// <summary>Success, <see cref="AccountErrors.EmailNotVerified"/>, or <see cref="AccountErrors.AccountNotFound"/>.</summary>
    Task<Result> CheckAsync(AccountId account, CancellationToken cancellationToken);
}

/// <summary>
/// The sign-in steps behind the BFF session (ACCT-002, PP-D13; PP1.6). The gateway holds the session and calls these;
/// the session itself is a cookie there, so nothing here issues or reads one. Sign-in goes through this facade rather
/// than Identity's <c>SignInManager</c>, because the account store does not save on its own.
/// </summary>
public interface ISessionFacade
{
    /// <summary>
    /// Checks an email address and password. One neutral <see cref="AccountErrors.InvalidCredentials"/> covers an
    /// unknown address and a wrong password alike, and both do the same password-hashing work, so neither the answer
    /// nor its timing says whether an address has an account. A locked account is told to wait.
    /// </summary>
    Task<Result<SessionStart>> SignInAsync(SignInRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Completes a sign-in that owes a second step, with an authenticator or recovery code. The account and the
    /// <paramref name="securityStamp"/> come from the gateway's half-finished session; a stamp that no longer matches
    /// means the sign-in is over - the password changed, the second factor changed, or a role did - and the code is
    /// never even checked, so no attempt is counted against the account.
    /// </summary>
    Task<Result<SessionStart>> CompleteSecondFactorAsync(
        AccountId account,
        string securityStamp,
        SecondFactorRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Who this session belongs to, or null when there is no session to speak of: the account is gone, its security
    /// stamp has moved on - which is what ends the account's other sessions - or the account has two-step sign-in and
    /// <paramref name="secondFactorVerified"/> says this session never passed it. Called on every forwarded request,
    /// so it stays one keyed read.
    /// </summary>
    Task<SessionIdentity?> ResolveAsync(
        AccountId account,
        string securityStamp,
        bool secondFactorVerified,
        CancellationToken cancellationToken);

    /// <summary>
    /// The account's current security stamp, or null when the account is gone. A request that rotated the stamp -
    /// a password change, two-step sign-in on or off - hands it back to the gateway, so that request's own session
    /// carries on while the account's other sessions end.
    /// </summary>
    Task<string?> CurrentStampAsync(AccountId account, CancellationToken cancellationToken);
}
