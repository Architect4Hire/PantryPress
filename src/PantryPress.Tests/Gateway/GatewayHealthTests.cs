using System.Net;

namespace PantryPress.Tests.Gateway;

// PP1.5: the gateway reports whether ApiService is reachable (gateway.md). Reachability, not health: the gateway must
// not take itself down because the API answered badly, and must never call the API to prove it is alive.
public sealed class GatewayHealthTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)] // /alive is mapped in Development only until PP19.2
    [InlineData(HttpStatusCode.ServiceUnavailable)] // the API is there, just not ready
    public async Task Any_answer_from_the_api_counts_as_reachable(HttpStatusCode status)
    {
        await using var gateway = new GatewayFactory("Development", () => new HttpResponseMessage(status));

        using var response = await gateway.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, gateway.Probes);
    }

    [Fact]
    public async Task An_unreachable_api_is_reported_without_taking_the_gateway_down()
    {
        await using var gateway = new GatewayFactory("Development");

        using var response = await gateway.CreateClient().GetAsync("/health");

        // Degraded, not unhealthy: 200, so Aspire and the edge leave the gateway serving the admin app.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, gateway.Probes);
    }

    [Fact]
    public async Task Liveness_never_calls_the_api()
    {
        await using var gateway = new GatewayFactory("Development");

        using var response = await gateway.CreateClient().GetAsync("/alive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, gateway.Probes);
    }
}
