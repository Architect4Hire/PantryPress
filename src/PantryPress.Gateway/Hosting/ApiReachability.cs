using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PantryPress.Gateway.Hosting;

/// <summary>
/// Whether ApiService answers at all (gateway.md: the gateway reports API reachability). Reachability, not health: any
/// HTTP answer - 401, 404, 503 - means the API is there, and only a refused connection, a DNS failure, or a timeout
/// counts as unreachable. Unreachable is reported as degraded rather than unhealthy, so a brief API outage does not
/// take the gateway down with it; the admin app keeps loading and says what is wrong in plain language.
/// </summary>
internal static class ApiReachability
{
    public const string CheckName = "apiservice";
    public const string ClientName = "api-reachability";

    /// <summary>The API's liveness path. Any status counts, so it works wherever the endpoint is mapped (PP19.2).</summary>
    public static readonly Uri ProbePath = new("/alive", UriKind.Relative);

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    public static IServiceCollection AddApiReachabilityCheck(this IServiceCollection services)
    {
        var probe = services.AddHttpClient(ClientName, client =>
        {
            // The Aspire reference, resolved by service discovery; no address is written down (aspire.md).
            client.BaseAddress = new Uri("http://apiservice");
            client.Timeout = ProbeTimeout;
        });

        // A probe answers now or not at all: the standard handler's retries and 30-second timeout would outlast the
        // health check that asked.
#pragma warning disable EXTEXP0001 // Evaluation API; a rename should break this build, not quietly restore retries.
        probe.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        // Untagged, so it belongs to /health and never to /alive: liveness never calls a dependency (ServiceDefaults).
        services.AddHealthChecks().AddCheck<ApiReachabilityHealthCheck>(CheckName);
        return services;
    }
}

internal sealed class ApiReachabilityHealthCheck(IHttpClientFactory clients) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var client = clients.CreateClient(ApiReachability.ClientName);
        try
        {
            using var response = await client.GetAsync(
                ApiReachability.ProbePath,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return HealthCheckResult.Healthy($"The API answered with {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Degraded("The API is not answering.", exception);
        }
    }
}
