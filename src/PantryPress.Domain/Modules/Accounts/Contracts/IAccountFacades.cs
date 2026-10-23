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
