namespace PantryPress.ApiService.Http;

/// <summary>
/// Marks a route whose answer carries a secret that is shown once - an authenticator key, recovery codes, a publishing
/// key (API-001). <see cref="Hosting.IdempotencyConvention"/> refuses to start a host that also puts
/// <see cref="IdempotentAttribute"/> on it: a stored answer would keep the secret at rest and show it a second time.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class AnswersWithSecretAttribute : Attribute;
