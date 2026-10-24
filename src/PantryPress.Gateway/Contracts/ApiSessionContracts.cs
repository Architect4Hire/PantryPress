namespace PantryPress.Gateway.Contracts;

// What this gateway sends to and reads from ApiService's internal session surface (PP1.6, PP1.12). They mirror the
// API's own records in Accounts.Contracts - the gateway holds no reference to that assembly - and parity tests pin each
// copy to its original, so a rename on one side fails a test instead of a blogger's sign-in.

/// <summary>
/// What ApiService says about a sign-in (its <c>SessionStart</c>). <see cref="SecondStepRequired"/> is nullable so a
/// renamed or missing field fails the sign-in instead of quietly reading as "no second step needed", which would hand
/// a complete session to an account that owes one.
/// </summary>
internal sealed record SessionStart(
    Guid AccountId,
    string Email,
    string SecurityStamp,
    bool? SecondStepRequired,
    bool EmailVerified);

/// <summary>A complete session the database still stands behind (the API's <c>CurrentSession</c>, PP1.12).</summary>
internal sealed record CurrentSession(Guid AccountId, string Email, bool EmailVerified, bool SecondFactorVerified);

internal sealed record CredentialsRequest(string Email, string Password);

internal sealed record SecondFactorSignInRequest(Guid AccountId, string SecurityStamp, string Code);

internal sealed record CurrentSessionRequest(Guid AccountId, string SecurityStamp, string SessionId, bool SecondFactorVerified);

internal sealed record EndSessionRequest(Guid AccountId, string SessionId);
