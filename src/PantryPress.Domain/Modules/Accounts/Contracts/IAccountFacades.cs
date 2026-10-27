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

    /// <summary>
    /// Makes a new shared key for the authenticator app. Refused while two-step sign-in is on, and with
    /// <see cref="AccountErrors.EmailNotVerified"/> until the address is confirmed (PP1.12), as is turning it on.
    /// </summary>
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

/// <summary>How an account's own email address compares with one that something was sent to.</summary>
public enum AccountAddressMatch
{
    /// <summary>The account's address is this one, and confirmed.</summary>
    Confirmed,

    /// <summary>The account's address is this one, but not confirmed yet.</summary>
    Unconfirmed,

    /// <summary>The account's address is another one, or there is no such account.</summary>
    OtherAddress,
}

/// <summary>
/// Whether an account holds an email address, for a module that sent a link to that address and must know the person
/// following it reads that inbox: a site invitation (SITE-010; PP2.11). The answer is about the caller's own account
/// only, and never hands the account's address to the module that asks.
/// </summary>
public interface IAccountAddressCheck
{
    /// <summary>
    /// How <paramref name="account"/>'s address compares with <paramref name="email"/>, in the form
    /// <c>EmailAddresses.Normalize</c> compares them.
    /// </summary>
    Task<AccountAddressMatch> MatchAsync(AccountId account, string email, CancellationToken cancellationToken);
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
    /// stamp has moved on - which is what ends the account's other sessions - the session was signed out
    /// (<paramref name="sessionId"/>, PP1.12), or the account has two-step sign-in and
    /// <paramref name="secondFactorVerified"/> says this session never passed it. Called on every forwarded request,
    /// so it stays a few keyed reads: the account, whether the session was signed out, and the account's roles.
    /// </summary>
    Task<SessionIdentity?> ResolveAsync(
        AccountId account,
        string securityStamp,
        string sessionId,
        bool secondFactorVerified,
        CancellationToken cancellationToken);

    /// <summary>
    /// The current session as the gateway's <c>GET /session</c> reports it (PP1.12): what the database says now, not
    /// what the cookie said at sign-in. <see cref="AccountErrors.SessionEnded"/> for a session that
    /// <see cref="ResolveAsync"/> would refuse.
    /// </summary>
    Task<Result<CurrentSession>> CurrentAsync(CurrentSessionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Signs one session out for good (PP1.12): its id is remembered until no ticket carrying it can still be alive,
    /// so the cookie is refused from then on wherever it comes from. The account's other sessions carry on. Signing
    /// out a session that is already signed out succeeds.
    /// </summary>
    Task<Result> EndAsync(EndSessionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The account's current security stamp, or null when the account is gone. A request that rotated the stamp -
    /// a password change, two-step sign-in on or off - hands it back to the gateway, so that request's own session
    /// carries on while the account's other sessions end.
    /// </summary>
    Task<string?> CurrentStampAsync(AccountId account, CancellationToken cancellationToken);
}

/// <summary>
/// Housekeeping the Worker runs for the Accounts module (PP1.12): forgetting sign-outs that no session can outlive.
/// </summary>
public interface IAccountMaintenance
{
    /// <summary>Deletes up to <paramref name="batchSize"/> expired sign-outs and returns how many it deleted.</summary>
    Task<int> PurgeExpiredSessionRevocationsAsync(int batchSize, CancellationToken cancellationToken);
}
