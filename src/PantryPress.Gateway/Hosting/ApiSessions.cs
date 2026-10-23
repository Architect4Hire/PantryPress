using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace PantryPress.Gateway.Hosting;

/// <summary>The secret this gateway proves itself to ApiService with. Set by the AppHost locally, Key Vault at PP19.</summary>
public sealed class GatewaySessionOptions
{
    /// <summary>Shared with ApiService and nothing else. Never logged, never sent to a browser.</summary>
    public string SharedSecret { get; set; } = string.Empty;

    public const int MinimumSecretLength = 32;

    public bool IsValid() => SharedSecret.Length >= MinimumSecretLength;
}

/// <summary>
/// What ApiService says about a sign-in (its <c>SessionStart</c>). <see cref="SecondStepRequired"/> is nullable so a
/// renamed or missing field fails the sign-in instead of quietly reading as "no second step needed", which would hand
/// a complete session to an account that owes one; a parity test pins the shape to the API's record.
/// </summary>
internal sealed record SessionStart(
    Guid AccountId,
    string Email,
    string SecurityStamp,
    bool? SecondStepRequired,
    bool EmailVerified);

/// <summary>A failure from ApiService that is the blogger's to see, kept exactly as it came.</summary>
internal sealed record RelayedProblem(int Status, string? ContentType, string Body, string? RetryAfter);

/// <summary>
/// What came back: a session, a problem to relay, or neither - an answer that is not the blogger's business (the
/// secret does not match, the API is broken), which the caller turns into the gateway's own failure.
/// </summary>
internal sealed record SessionAnswer(SessionStart? Session, RelayedProblem? Problem);

/// <summary>
/// The sign-in checks, asked of ApiService's internal surface (PP1.6). The gateway holds the session but owns none of
/// the rules: passwords, codes, lockout, and the security stamp all belong to the Accounts facades. This client is
/// the only thing that speaks to that surface, and the only holder of the secret it is reached with.
/// </summary>
internal sealed partial class ApiSessions(
    HttpClient client,
    IOptions<GatewaySessionOptions> options,
    ILogger<ApiSessions> logger)
{
    public const string ClientName = "api-sessions";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly Uri Credentials = new("/api/internal/v1/sessions/credentials", UriKind.Relative);
    private static readonly Uri SecondFactor = new("/api/internal/v1/sessions/second-factor", UriKind.Relative);

    /// <summary>
    /// The refusals a person signing in is meant to read: their details were wrong, their code was wrong, their
    /// account is locked, or the sign-in took too long. Anything else is ours to explain, not theirs to puzzle over.
    /// </summary>
    private static readonly HttpStatusCode[] Relayable =
    [
        HttpStatusCode.BadRequest,
        HttpStatusCode.Conflict,
        HttpStatusCode.TooManyRequests,
    ];

    public static IServiceCollection AddApiSessions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GatewaySessionOptions>()
            .Bind(configuration)
            .Validate(
                o => o.IsValid(),
                $"Gateway:SharedSecret must be at least {GatewaySessionOptions.MinimumSecretLength} characters and "
                + "match ApiService's. The gateway proves itself with it before the API reads any identity.")
            .ValidateOnStart();

        // The Aspire reference, resolved by service discovery; no address is written down (aspire.md).
        var sessions = services.AddHttpClient<ApiSessions>(
            ClientName,
            http =>
            {
                http.BaseAddress = new Uri("http://apiservice");

                // Someone is waiting at a sign-in form. Failing fast and saying so beats a long silence.
                http.Timeout = Timeout;
            });

        // A refusal here is an answer, not a fault: the standard handler treats a 429 as transient and retries it,
        // which fights the lockout it is reporting - it waits out the API's Retry-After and turns a "wait 15 minutes"
        // into a timeout - and its circuit breaker would take sign-in down after a handful of refusals.
#pragma warning disable EXTEXP0001 // Evaluation API; a rename should break this build, not quietly restore retries.
        sessions.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        return services;
    }

    public Task<SessionAnswer> SignInAsync(string email, string password, CancellationToken cancellationToken) =>
        PostAsync(Credentials, new CredentialsBody(email, password), cancellationToken);

    public Task<SessionAnswer> CompleteSecondFactorAsync(
        Guid account,
        string securityStamp,
        string code,
        CancellationToken cancellationToken) =>
        PostAsync(SecondFactor, new SecondFactorBody(account, securityStamp, code), cancellationToken);

    private async Task<SessionAnswer> PostAsync<TBody>(Uri path, TBody body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation(SessionHeaders.Secret, options.Value.SharedSecret);

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var session = await response.Content.ReadFromJsonAsync<SessionStart>(cancellationToken)
                ?? throw new InvalidOperationException("ApiService accepted a sign-in without describing it.");
            if (session.SecondStepRequired is null)
            {
                throw new InvalidOperationException(
                    "ApiService did not say whether the sign-in owes a second step; the session is not safe to issue.");
            }

            return new SessionAnswer(session, null);
        }

        if (!Relayable.Contains(response.StatusCode))
        {
            // A 404 here means the secret does not match - the gateway is talking to an API that does not know it -
            // and anything else is the API failing. Neither is something to show a person signing in.
            LogUnexpectedAnswer(logger, (int)response.StatusCode, path.OriginalString);
            return new SessionAnswer(null, null);
        }

        return new SessionAnswer(
            null,
            new RelayedProblem(
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                await response.Content.ReadAsStringAsync(cancellationToken),
                response.Headers.TryGetValues(HeaderNames.RetryAfter, out var retryAfter)
                    ? string.Join(",", retryAfter)
                    : null));
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The session API answered {Status} for {Path}. A 404 means the shared secret does not match.")]
    private static partial void LogUnexpectedAnswer(ILogger logger, int status, string path);

    private sealed record CredentialsBody(string Email, string Password);

    private sealed record SecondFactorBody(Guid AccountId, string SecurityStamp, string Code);
}
