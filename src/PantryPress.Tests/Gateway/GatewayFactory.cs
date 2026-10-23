using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PantryPress.Gateway.Hosting;
using Yarp.ReverseProxy.Forwarder;

namespace PantryPress.Tests.Gateway;

/// <summary>
/// PantryPress.Gateway in memory (PP1.5). Both clusters resolve through service-discovery settings to addresses that
/// name the destination, and YARP's forwarder is replaced by a handler that records what it was asked to forward, so
/// which cluster answers a path is asserted without a destination listening anywhere.
/// </summary>
/// <remarks>
/// <paramref name="probe"/> answers the API reachability health check. It defaults to a refused connection, so no test
/// ever reaches the network: the health tests pass their own. <paramref name="forward"/> answers a forwarded request;
/// it defaults to 200, and a test that needs the API to be down has it throw.
/// </remarks>
internal sealed class GatewayFactory(
    string environment,
    Func<HttpResponseMessage>? probe = null,
    Func<HttpResponseMessage>? forward = null,
    Func<HttpRequestMessage, HttpResponseMessage>? sessions = null,
    TimeProvider? time = null) : WebApplicationFactory<GatewayEntryPoint>
{
    public const string ApiDestination = "apiservice.test";
    public const string AdminDestination = "admin.test";

    /// <summary>
    /// The admin app's own origin. HTTPS, because the session cookies are <c>Secure</c> and a cookie jar will not send
    /// a Secure cookie over plain HTTP - which is also true of the browser this stands in for.
    /// </summary>
    public static readonly Uri AdminOrigin = new("https://app.localhost");

    private readonly RecordingForwarder forwarder = new(forward);
    private readonly ConcurrentQueue<SessionCall> sessionCalls = new();
    private int probes;

    /// <summary>How many times the API reachability check called the API.</summary>
    public int Probes => probes;

    /// <summary>What the gateway forwarded, in order.</summary>
    public IReadOnlyCollection<ForwardedRequest> Forwarded => forwarder.Requests;

    /// <summary>What the gateway asked ApiService's internal session surface, in order.</summary>
    public IReadOnlyCollection<SessionCall> SessionCalls => sessionCalls;

    /// <summary>A client that keeps cookies, like a browser at <c>app.localhost</c>.</summary>
    public HttpClient CreateBrowser() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = AdminOrigin, HandleCookies = true });

    /// <summary>A client that keeps no cookies, for tests that send a <c>Cookie</c> header of their own making.</summary>
    public HttpClient CreateBare() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = AdminOrigin, HandleCookies = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("services:apiservice:http:0", $"http://{ApiDestination}");
        builder.UseSetting("services:admin:http:0", $"http://{AdminDestination}");

        // The gateway refuses to start without the secret it proves itself to the API with (PP1.6).
        builder.UseSetting("Gateway:SharedSecret", TestSecrets.GatewaySecret);
        builder.ConfigureTestServices(services =>
        {
            // Session tickets are protected with keys of this host's own, so tests never touch the developer's key
            // ring - and a ticket from one test host is unreadable by another, which is what a forged cookie is.
            services.Replace(ServiceDescriptor.Singleton<IDataProtectionProvider, EphemeralDataProtectionProvider>());

            // Ticket lifetimes are read from this when a test supplies it, so a session can be left to run out.
            if (time is not null)
            {
                services.Replace(ServiceDescriptor.Singleton(time));
            }

            services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory>(forwarder));
            // The internal session surface. Nothing listens anywhere: the test answers for ApiService and the call
            // is recorded, secret header included.
            services.AddHttpClient(ApiSessions.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new SessionsHandler(sessionCalls, sessions));

            services.AddHttpClient(ApiReachability.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new ProbeHandler(() =>
                {
                    Interlocked.Increment(ref probes);
                    return probe?.Invoke() ?? throw new HttpRequestException("Connection refused.");
                }));
        });
    }

    internal sealed record ForwardedRequest(
        string Method,
        Uri Uri,
        string? Host,
        IReadOnlyDictionary<string, string> Headers)
    {
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>One call to the internal session surface: where it went, what it carried, and the secret it showed.</summary>
    internal sealed record SessionCall(Uri Uri, string Body, string? Secret);

    private sealed class SessionsHandler(
        ConcurrentQueue<SessionCall> calls,
        Func<HttpRequestMessage, HttpResponseMessage>? answer) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            calls.Enqueue(new SessionCall(
                request.RequestUri!,
                body,
                request.Headers.TryGetValues("X-PP-Gateway", out var secret) ? string.Join(",", secret) : null));

            return answer?.Invoke(request)
                ?? throw new InvalidOperationException("This test did not expect the gateway to call the session API.");
        }
    }

    private sealed class ProbeHandler(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(answer());
    }

    private sealed class RecordingForwarder(Func<HttpResponseMessage>? answer) : IForwarderHttpClientFactory
    {
        private readonly ConcurrentQueue<ForwardedRequest> requests = new();

        public IReadOnlyCollection<ForwardedRequest> Requests => requests;

        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
            new(new RecordingHandler(requests, answer));

        // The request message is disposed once the forwarder is done with it, so everything asserted on is copied out.
        private sealed class RecordingHandler(ConcurrentQueue<ForwardedRequest> requests, Func<HttpResponseMessage>? answer)
            : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                requests.Enqueue(new ForwardedRequest(
                    request.Method.Method,
                    request.RequestUri!,
                    request.Headers.Host,
                    request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase)));
                if (answer is not null)
                {
                    return Task.FromResult(answer());
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("forwarded"),
                    RequestMessage = request,
                });
            }
        }
    }
}
