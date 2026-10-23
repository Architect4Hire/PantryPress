namespace PantryPress.Gateway.Contracts;

// What the admin app sends and reads on the gateway's own session routes (api-contract.md: request and response types
// are records in Contracts/). The gateway has no modules, so they live here, beside the routes that are its own.

/// <summary>How far along a sign-in is. Serialized kebab-case, like every enum on the API.</summary>
internal enum SessionStage
{
    /// <summary>Signed in and able to act.</summary>
    Complete,

    /// <summary>The password was right; a code from the authenticator app or a recovery code is still owed.</summary>
    SecondFactor,
}

/// <summary>
/// What the admin app is told about its session: never the security stamp, and never a token (ACCT-002).
/// </summary>
internal sealed record SessionView(
    SessionStage Stage,
    Guid AccountId,
    string Email,
    bool EmailVerified,
    bool SecondFactorVerified);

internal sealed record SignInBody(string? Email, string? Password);

internal sealed record SecondFactorBody(string? Code);
