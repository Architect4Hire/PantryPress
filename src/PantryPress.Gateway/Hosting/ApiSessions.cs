using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using PantryPress.Gateway.Contracts;

namespace PantryPress.Gateway.Hosting;

/// <summary>The secret this gateway proves itself to ApiService with. Set by the AppHost locally, Key Vault at PP19.</summary>
public sealed class GatewaySessionOptions
{
    /// <summary>Shared with ApiService and nothing else. Never logged, never sent to a browser.</summary>
    public string SharedSecret { get; set; } = string.Empty;

    public const int MinimumSecretLength = 32;

    public bool IsValid() => SharedSecret.Length >= MinimumSecretLength;
}

/// <summary>A failure from ApiService that is the blogger's to see, kept exactly as it came, with its stable code.</summary>
internal sealed record RelayedProblem(int Status, string? ContentType, string Body, string? RetryAfter, string? Code);

/// <summary>
/// What came back: a value, a problem to relay, or neither - an answer that is not the blogger's business (the secret
/// does not match, the API is broken or unreachable), which the caller turns into the gateway's own failure.
/// </summary>
internal sealed record ApiAnswer<T>(T? Value, RelayedProblem? Problem)
    where T : class
{
    /// <summary>For a call whose success carries no body (signing out).</summary>
    public bool Succeeded { get; init; }
}

/// <summary>
/// The sign-in checks, asked of ApiService's internal surface (PP1.6, PP1.12). The gateway holds the session but owns
/// none of the rules: passwords, codes, lockout, sign-outs, and the security stamp all belong to the Accounts facades.
/// This client is the only thing that speaks to that surface, and the only holder of the secret it is reached with.
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
    private static readonly Uri Current = new("/api/internal/v1/sessions/current", UriKind.Relative);
    private static readonly Uri End = new("/api/internal/v1/sessions/end", UriKind.Relative);

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

    public async Task<ApiAnswer<SessionStart>> SignInAsync(string email, string password, CancellationToken cancellationToken) =>
        Started(await PostAsync<CredentialsRequest, SessionStart>(Credentials, new(email, password), cancellationToken));

    public async Task<ApiAnswer<SessionStart>> CompleteSecondFactorAsync(
        Guid account,
        string securityStamp,
        string code,
        CancellationToken cancellationToken) =>
        Started(await PostAsync<SecondFactorSignInRequest, SessionStart>(
            SecondFactor,
            new(account, securityStamp, code),
            cancellationToken));

    /// <summary>Whether a complete session still stands, as the database sees it now (PP1.12).</summary>
    public Task<ApiAnswer<CurrentSession>> CurrentAsync(
        Guid account,
        string securityStamp,
        string sessionId,
        bool secondFactorVerified,
        CancellationToken cancellationToken) =>
        PostAsync<CurrentSessionRequest, CurrentSession>(
            Current,
            new(account, securityStamp, sessionId, secondFactorVerified),
            cancellationToken);

    /// <summary>Signs the session out for good, so a copy of its cookie stops working too (PP1.12).</summary>
    public Task<ApiAnswer<object>> EndAsync(Guid account, string sessionId, CancellationToken cancellationToken) =>
        PostAsync<EndSessionRequest, object>(End, new(account, sessionId), cancellationToken);

    // A sign-in answer that does not say whether a second step is owed is not safe to turn into any session.
    private static ApiAnswer<SessionStart> Started(ApiAnswer<SessionStart> answer) =>
        answer.Value is { SecondStepRequired: null }
            ? throw new InvalidOperationException(
                "ApiService did not say whether the sign-in owes a second step; the session is not safe to issue.")
            : answer;

    private async Task<ApiAnswer<TAnswer>> PostAsync<TBody, TAnswer>(Uri path, TBody body, CancellationToken cancellationToken)
        where TAnswer : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation(SessionHeaders.Secret, options.Value.SharedSecret);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // The API is down, unreachable, or took longer than a person should wait. That is ours to say, as a 502,
            // not an unhandled 500 (PP1.12).
            LogUnreachable(logger, ex, path.OriginalString);
            return new ApiAnswer<TAnswer>(null, null);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    return new ApiAnswer<TAnswer>(null, null) { Succeeded = true };
                }

                var value = await response.Content.ReadFromJsonAsync<TAnswer>(cancellationToken)
                    ?? throw new InvalidOperationException($"ApiService answered {path.OriginalString} without a body.");
                return new ApiAnswer<TAnswer>(value, null) { Succeeded = true };
            }

            if (!Relayable.Contains(response.StatusCode))
            {
                // A 404 here means the secret does not match - the gateway is talking to an API that does not know it -
                // and anything else is the API failing. Neither is something to show a person signing in.
                LogUnexpectedAnswer(logger, (int)response.StatusCode, path.OriginalString);
                return new ApiAnswer<TAnswer>(null, null);
            }

            var problem = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ApiAnswer<TAnswer>(
                null,
                new RelayedProblem(
                    (int)response.StatusCode,
                    response.Content.Headers.ContentType?.ToString(),
                    problem,
                    response.Headers.TryGetValues(HeaderNames.RetryAfter, out var retryAfter)
                        ? string.Join(",", retryAfter)
                        : null,
                    CodeOf(problem)));
        }
    }

    // The stable code from the API's problem document, for the few the session itself acts on (a sign-in that ran out,
    // a session that ended). The document itself is relayed untouched.
    private static string? CodeOf(string problem)
    {
        try
        {
            using var document = JsonDocument.Parse(problem);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(GatewayApi.CodeKey, out var code)
                && code.ValueKind == JsonValueKind.String
                    ? code.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The session API answered {Status} for {Path}. A 404 means the shared secret does not match.")]
    private static partial void LogUnexpectedAnswer(ILogger logger, int status, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "The session API could not be reached for {Path}.")]
    private static partial void LogUnreachable(ILogger logger, Exception exception, string path);
}
