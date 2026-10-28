namespace PantryPress.Domain.Managers.Results;

/// <summary>What kind of expected failure an <see cref="Error"/> is. Hosts map each kind to one HTTP status.</summary>
public enum ErrorKind
{
    /// <summary>The input breaks a rule; <see cref="Error.Fields"/> says which fields.</summary>
    Validation,

    /// <summary>The request is well formed but cannot be applied as asked (a bad or expired link, a wrong code).</summary>
    Rejected,

    NotFound,

    /// <summary>The current state does not allow the change (already on, changed by someone else).</summary>
    Conflict,

    /// <summary>The caller is known but not allowed to do this.</summary>
    Forbidden,

    /// <summary>Too many attempts; the caller should wait before trying again.</summary>
    TooManyAttempts,

    /// <summary>What was asked for is there but cannot be answered for now (a site not open yet, or closed for a while).</summary>
    Unavailable,

    /// <summary>What was asked for is gone for good (a deleted site).</summary>
    Gone,
}

/// <summary>
/// An expected failure returned by a facade (backend.md). <see cref="Code"/> is stable and machine-readable;
/// <see cref="Title"/> is plain language a blogger can read (api-contract.md). Neither ever carries personal data.
/// </summary>
public sealed record Error(ErrorKind Kind, string Code, string Title)
{
    /// <summary>Plain-language messages per field, for <see cref="ErrorKind.Validation"/> errors only.</summary>
    public IReadOnlyDictionary<string, string[]> Fields { get; init; } = new Dictionary<string, string[]>();

    /// <summary>
    /// How long to wait before trying again, for <see cref="ErrorKind.TooManyAttempts"/> and
    /// <see cref="ErrorKind.Unavailable"/>: hosts send it as <c>Retry-After</c>, so the plain title has a machine-readable
    /// half (PP1.12, SEO-007).
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    public static Error Validation(IReadOnlyDictionary<string, string[]> fields) =>
        new(ErrorKind.Validation, "validation-failed", "Some of the details need another look.") { Fields = fields };
}
