namespace PantryPress.ApiService.Http;

/// <summary>
/// Opts a write into <c>Idempotency-Key</c> (API-005; PP1.9): a retry with the same key gets the first answer instead
/// of running again. <see cref="Hosting.IdempotencyConvention"/> refuses to start a host that puts this on a read, an
/// anonymous route, or a route with no authorization, because keys are scoped to the caller and an anonymous request
/// has none.
/// </summary>
/// <remarks>
/// <para>
/// Never on a route whose answer carries a secret - an authenticator key, recovery codes, a publishing key shown once
/// (API-001). The answer is stored to be replayed, and a secret must be neither stored nor shown twice; mark such a
/// route <see cref="AnswersWithSecretAttribute"/> and the host refuses the pair. Nor on a route whose answer carries
/// reader-submitted personal data (subscriber emails, comment authors): stored answers are not encrypted at rest
/// (PSEC-005).
/// </para>
/// <para>
/// A replay is answered after authentication, authorization policies, and the rate limiter, but before MVC filters and
/// the facade. So whatever decides whether the caller may still reach the resource - site membership above all (PP2) -
/// must be an authorization policy, or a replay would outlive a revoked membership for the key's 24 hours.
/// </para>
/// <para>
/// An opted-in route's answer is small JSON: one over 1 MiB is not stored, and a retry runs again. The route runs to
/// its end even when the caller goes away, so that the retry of a lost answer gets it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class IdempotentAttribute : Attribute
{
    /// <summary>Whether a request without a key is refused rather than run unprotected. Off: API-005 says "accepts".</summary>
    public bool Required { get; init; }
}
