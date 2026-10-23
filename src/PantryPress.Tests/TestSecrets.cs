namespace PantryPress.Tests;

/// <summary>
/// Secrets the test hosts run with. Only the gateway and ApiService ever hold the shared secret in a real run (PP1.6);
/// in tests both hosts get this one, and tests that forge a request use a different value on purpose.
/// </summary>
internal static class TestSecrets
{
    /// <summary>The gateway's secret. Long enough to pass the startup check both hosts make.</summary>
    public const string GatewaySecret = "test-gateway-secret-0123456789abcdef";

    /// <summary>What an attacker might present instead. Same length, so only the value differs.</summary>
    public const string WrongGatewaySecret = "test-gateway-secret-fedcba9876543210";
}
