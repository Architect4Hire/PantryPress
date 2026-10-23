using System.Collections.Concurrent;
using System.Net;
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
    Func<HttpResponseMessage>? forward = null) : WebApplicationFactory<GatewayEntryPoint>
{
    public const string ApiDestination = "apiservice.test";
    public const string AdminDestination = "admin.test";

    private readonly RecordingForwarder forwarder = new(forward);
    private int probes;

    /// <summary>How many times the API reachability check called the API.</summary>
    public int Probes => probes;

    /// <summary>What the gateway forwarded, in order.</summary>
    public IReadOnlyCollection<ForwardedRequest> Forwarded => forwarder.Requests;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("services:apiservice:http:0", $"http://{ApiDestination}");
        builder.UseSetting("services:admin:http:0", $"http://{AdminDestination}");
        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory>(forwarder));
            services.AddHttpClient(ApiReachability.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new ProbeHandler(() =>
                {
                    Interlocked.Increment(ref probes);
                    return probe?.Invoke() ?? throw new HttpRequestException("Connection refused.");
                }));
        });
    }

    internal sealed record ForwardedRequest(string Method, Uri Uri, string? Host);

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
                requests.Enqueue(new ForwardedRequest(request.Method.Method, request.RequestUri!, request.Headers.Host));
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
