using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.Tests.Hosting;

// PP1.8: the shared trusted-proxy rule itself, on a host small enough to say back what it saw. The service tests
// prove each host opted into the right shape; these prove what the shapes do - and in particular that believing a
// peer about an address is not the same as letting it choose a host, which on the public tier is the tenant
// (PSEC-004, tenancy.md).
public sealed class EdgeMiddlewareTests
{
    private const string Proxy = "10.0.0.4";
    private const string Browser = "203.0.113.9";

    [Fact]
    public async Task With_no_trusted_peer_a_request_speaks_only_for_itself()
    {
        using var host = await EchoAsync();

        var seen = await AskAsync(host, "cook.localhost", address: Proxy, headers: new Dictionary<string, string>
        {
            ["X-Forwarded-For"] = Browser,
            ["X-Forwarded-Proto"] = "https",
            ["X-Forwarded-Host"] = "other-tenant.localhost",
            ["X-Azure-FDID"] = "a-front-door-id",
        });

        Assert.Equal("cook.localhost", seen.Host);
        Assert.Equal(Proxy, seen.Address);
        Assert.Equal("http", seen.Scheme);

        // And nothing of what it claimed survives for anything downstream to read.
        Assert.Empty(seen.Forwarding);
    }

    [Fact]
    public async Task A_trusted_peer_is_believed_about_the_address_but_not_the_host()
    {
        // The public tier's shape. Choosing the host would be choosing whose site a reader sees.
        using var host = await EchoAsync(trusted: Proxy, forwardHost: false);

        var seen = await AskAsync(host, "cook.localhost", address: Proxy, headers: new Dictionary<string, string>
        {
            ["X-Forwarded-For"] = Browser,
            ["X-Forwarded-Proto"] = "https",
            ["X-Forwarded-Host"] = "other-tenant.localhost",
        });

        Assert.Equal(Browser, seen.Address);
        Assert.Equal("https", seen.Scheme);
        Assert.Equal("cook.localhost", seen.Host);
    }

    [Fact]
    public async Task A_trusted_peer_may_name_the_host_only_where_that_was_asked_for()
    {
        // The gateway's shape: one host of its own, and the same-origin and antiforgery checks read it.
        using var host = await EchoAsync(trusted: Proxy, forwardHost: true);

        var seen = await AskAsync(host, "internal-name", address: Proxy, headers: new Dictionary<string, string>
        {
            ["X-Forwarded-Host"] = "pantrypress.app",
        });

        Assert.Equal("pantrypress.app", seen.Host);
    }

    [Fact]
    public async Task A_stranger_is_believed_about_nothing_even_where_a_peer_is_trusted()
    {
        using var host = await EchoAsync(trusted: Proxy, forwardHost: true);

        var seen = await AskAsync(host, "cook.localhost", address: "198.51.100.7", headers: new Dictionary<string, string>
        {
            ["X-Forwarded-For"] = Browser,
            ["X-Forwarded-Host"] = "other-tenant.localhost",
        });

        Assert.Equal("198.51.100.7", seen.Address);
        Assert.Equal("cook.localhost", seen.Host);
    }

    private sealed record Seen(string Host, string Scheme, string? Address, IReadOnlyList<string> Forwarding);

    private static async Task<Seen> AskAsync(
        IHost host,
        string requestHost,
        string address,
        IReadOnlyDictionary<string, string> headers)
    {
        var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/echo");
        request.Headers.Host = requestHost;
        request.Headers.TryAddWithoutValidation(RemoteAddressHeader, address);
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Seen>())!;
    }

    private const string RemoteAddressHeader = "X-Test-Remote";

    /// <summary>A host with nothing but the edge rule under test and one endpoint that says what reached it.</summary>
    private static async Task<IHost> EchoAsync(string? trusted = null, bool forwardHost = false)
    {
        var settings = new Dictionary<string, string?> { ["ForwardLimit"] = "1" };
        if (trusted is not null)
        {
            settings["KnownProxies:0"] = trusted;
        }

        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddTrustedProxies(
                    new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
                services.Configure<TrustedProxyOptions>(options => options.ForwardHost = forwardHost);
            });
            web.Configure(app =>
            {
                // Stands in for the connection's peer, which TestServer does not give one.
                app.Use(async (context, next) =>
                {
                    if (context.Request.Headers.TryGetValue(RemoteAddressHeader, out var claimed)
                        && IPAddress.TryParse(claimed.ToString(), out var parsed))
                    {
                        context.Connection.RemoteIpAddress = parsed;
                    }

                    await next(context);
                });

                app.UseTrustedProxies();
                app.UseForwardingHeaderHygiene();
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapGet("/echo", (HttpContext context) => new Seen(
                    context.Request.Host.Value ?? string.Empty,
                    context.Request.Scheme,
                    context.Connection.RemoteIpAddress?.ToString(),
                    TrustedProxies.Forwarding.Where(context.Request.Headers.ContainsKey).ToList())));
            });
        });

        var host = await builder.StartAsync();
        return host;
    }
}
