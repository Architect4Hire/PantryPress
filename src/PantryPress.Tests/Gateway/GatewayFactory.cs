using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
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
    TimeProvider? time = null,
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<GatewayEntryPoint>
{
    public const string ApiDestination = "apiservice.test";
    public const string AdminDestination = "admin.test";

    /// <summary>
    /// The admin app's own origin. HTTPS, because the session cookies are <c>Secure</c> and a cookie jar will not send
    /// a Secure cookie over plain HTTP - which is also true of the browser this stands in for.
    /// </summary>
    public static readonly Uri AdminOrigin = new("https://localhost");

    private readonly RecordingForwarder forwarder = new(forward);
    private readonly ConcurrentQueue<SessionCall> sessionCalls = new();
    private int probes;

    /// <summary>How many times the API reachability check called the API.</summary>
    public int Probes => probes;

    /// <summary>What the gateway forwarded, in order.</summary>
    public IReadOnlyCollection<ForwardedRequest> Forwarded => forwarder.Requests;

    /// <summary>What the gateway asked ApiService's internal session surface, in order.</summary>
    public IReadOnlyCollection<SessionCall> SessionCalls => sessionCalls;

    /// <summary>
    /// A client that keeps cookies, like a browser at <c>localhost</c>, and carries the antiforgery token the way
    /// the admin app will (PP1.7): it reads the request token from the cookie the gateway set and sends it back in
    /// <c>X-PP-Xsrf</c> on every write, fetching one with a read first when it has none - which is what the app does
    /// at startup. Tests that prove the refusals send their own headers through <see cref="CreateBare"/>.
    /// </summary>
    public HttpClient CreateBrowser()
    {
        var cookies = new CookieContainerHandler();
        return CreateDefaultClient(AdminOrigin, new XsrfHandler(cookies.Container), cookies);
    }

    /// <summary>A client that keeps no cookies, for tests that send a <c>Cookie</c> header of their own making.</summary>
    public HttpClient CreateBare() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = AdminOrigin, HandleCookies = false });

    /// <summary>
    /// A client on plain HTTP, which is how nobody reaches the gateway and therefore how PP1.8's redirect, HSTS, and
    /// trusted-proxy behaviour are exercised. Redirects are not followed: the answer itself is what is asserted.
    /// </summary>
    public HttpClient CreatePlain() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

    /// <summary>
    /// The address a test's request appears to come from. TestServer opens no socket, so the connection has no peer
    /// of its own; a request carrying this header is given one before any middleware runs, which is what the
    /// trusted-proxy step and the per-address limits key on.
    /// </summary>
    public const string RemoteAddressHeader = "X-Test-Remote";

    /// <summary>
    /// Every client this factory makes talks to <c>https://localhost</c>, because that is the only way the gateway
    /// is reached: Front Door terminates TLS in production, and locally the AppHost offers its HTTPS address alone
    /// (PP1.6). A <c>Secure</c> cookie and the antiforgery token it carries exist only on that scheme.
    /// </summary>
    protected override void ConfigureClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        base.ConfigureClient(client);
        client.BaseAddress = AdminOrigin;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("services:apiservice:http:0", $"http://{ApiDestination}");
        builder.UseSetting("services:admin:http:0", $"http://{AdminDestination}");

        // TestServer has no listening address for the HTTPS redirect to work out a port from, so the test host is
        // told the one a browser meets: https on 443, exactly as it is behind Front Door.
        builder.UseSetting("HTTPS_PORT", "443");

        // The host these tests reach the gateway on, in every environment they run it in: deployed configuration
        // names the deployed apex and Development names localhost, the local zone's apex (PP-D19), and a Production
        // test host is neither. It is also the address WebApplicationFactory puts on a client after ConfigureClient
        // has run, so a client made without options talks to it.
        builder.UseSetting("Gateway:Hosts:0", AdminOrigin.Host);

        // A deployed host refuses to start without a named proxy (PP1.8); a test host says the empty default is
        // deliberate, and the tests that care configure a peer of their own.
        builder.UseSetting("Edge:TrustedProxies:TrustNobody", "true");

        // Outside Development the gateway serves the admin app's build and will not start without one (PPL.3). Tests
        // share a stand-in; the ones about the build pass their own.
        if (!string.Equals(environment, "Development", StringComparison.Ordinal))
        {
            builder.UseSetting("Gateway:AdminApp:Root", AdminAppBuild.Root);
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        // The gateway refuses to start without the secret it proves itself to the API with (PP1.6).
        builder.UseSetting("Gateway:SharedSecret", TestSecrets.GatewaySecret);
        builder.ConfigureTestServices(services =>
        {
            // Ahead of every middleware the host adds, so the connection has a peer before anything reads one.
            services.AddSingleton<IStartupFilter, RemoteAddressFilter>();
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

    /// <summary>Gives the connection the address a test's request claims, before any middleware runs.</summary>
    private sealed class RemoteAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                if (context.Request.Headers.TryGetValue(RemoteAddressHeader, out var claimed)
                    && IPAddress.TryParse(claimed.ToString(), out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await following(context);
            });

            next(app);
        };
    }

    /// <summary>
    /// The admin app's side of PP1.7, as PP2 will configure Angular's <c>HttpClient</c>: the request token comes from
    /// the cookie, goes back in the header, and a write that finds no cookie bootstraps with a read first.
    /// </summary>
    private sealed class XsrfHandler(CookieContainer cookies) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
            {
                return await base.SendAsync(request, cancellationToken);
            }

            if (Token() is null)
            {
                using var bootstrap = new HttpRequestMessage(HttpMethod.Get, new Uri(AdminOrigin, SessionEndpoints.Base));
                using var answer = await base.SendAsync(bootstrap, cancellationToken);
            }

            if (Token() is { } token)
            {
                request.Headers.TryAddWithoutValidation(GatewayAntiforgery.HeaderName, token);
            }

            return await base.SendAsync(request, cancellationToken);
        }

        private string? Token() => cookies.GetCookies(AdminOrigin)
            .FirstOrDefault(cookie => cookie.Name == GatewayAntiforgery.TokenCookieName)?.Value;
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
