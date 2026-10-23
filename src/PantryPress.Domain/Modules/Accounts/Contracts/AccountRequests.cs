namespace PantryPress.Domain.Modules.Accounts.Contracts;

// Request and response records for the account API (api-contract.md: records in Contracts/, never entities). Values
// arrive straight from JSON, so validators treat every property as possibly null.

/// <summary>Sign up with an email address and a password (ACCT-001, ACCT-003).</summary>
public sealed record RegisterAccountRequest(string Email, string Password);

/// <summary>The account id and token from the link in the confirmation email.</summary>
public sealed record VerifyEmailRequest(Guid AccountId, string Token);

/// <summary>Send the confirmation email again.</summary>
public sealed record ResendVerificationRequest(string Email);

/// <summary>Send a password reset link (PP1.3).</summary>
public sealed record ForgotPasswordRequest(string Email);

/// <summary>The account id and token from the reset link, plus the new password.</summary>
public sealed record ResetPasswordRequest(Guid AccountId, string Token, string NewPassword);

/// <summary>Change the password while signed in.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>The account password, asked again before a security change.</summary>
public sealed record ConfirmPasswordRequest(string Password);

/// <summary>A six-digit code from the authenticator app.</summary>
public sealed record EnableTwoFactorRequest(string Code);

/// <summary>At sign-in: a code from the authenticator app, or one recovery code.</summary>
public sealed record SecondFactorRequest(string Code);

/// <summary>Whether two-step sign-in is on, and how many recovery codes are unused.</summary>
public sealed record TwoFactorStatus(bool IsEnabled, int RecoveryCodesLeft);

/// <summary>
/// What an authenticator app needs: the shared key to type in, and the <c>otpauth://</c> link a QR code is drawn from.
/// Secret: shown once, never logged, never cached.
/// </summary>
public sealed record AuthenticatorSetup(string SharedKey, string AuthenticatorUri);

/// <summary>New recovery codes. Shown once; only hashes are kept.</summary>
public sealed record RecoveryCodeList(IReadOnlyList<string> RecoveryCodes);

/// <summary>Sign in with an email address and a password (ACCT-002; PP1.6).</summary>
public sealed record SignInRequest(string Email, string Password);

/// <summary>
/// The second step of sign-in. The account and the security stamp both come from the half-finished session the gateway
/// holds, never from the person signing in. The stamp is what proves the first step happened: the only way to learn it
/// is to get the password right, so a code alone - even from something holding the gateway's secret - finishes
/// nothing, and a wrong code can only ever be counted against the account that really started the sign-in.
/// </summary>
public sealed record SecondFactorSignInRequest(Guid AccountId, string SecurityStamp, string Code);

/// <summary>
/// What the gateway needs to hold a session (PP1.6): who signed in, the security stamp the session is bound to, and
/// whether a second step is still owed. The stamp is a server-side value: it goes into the gateway's protected ticket
/// and never into a response the browser can read.
/// </summary>
public sealed record SessionStart(
    Guid AccountId,
    string Email,
    string SecurityStamp,
    bool SecondStepRequired,
    bool EmailVerified);

/// <summary>
/// Who a forwarded session belongs to, rebuilt from the database on every request (PP1.6), so a password change, a
/// two-step change, or a revoked platform role ends other sessions at their next request.
/// </summary>
public sealed record SessionIdentity(
    Guid AccountId,
    string Email,
    bool EmailVerified,
    bool TwoFactorEnabled,
    IReadOnlyList<string> Roles);
