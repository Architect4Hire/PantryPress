using System.Net;
using System.Net.Http.Json;
using PantryPress.ApiService.Hosting;

namespace PantryPress.Tests.Gateway;

// PP1.9: the key belongs to the API, so the gateway passes it through untouched, and passes the API's word that an
// answer was replayed back to the admin app.
public sealed class IdempotencyForwardingTests
{
    [Fact]
    public async Task The_key_reaches_the_api_and_the_replay_marker_reaches_the_browser()
    {
        await using var gateway = new GatewayFactory(
            "Development",
            sessions: _ => SessionFixtures.Accepted(),
            forward: () =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Created);
                response.Headers.Add(ApiIdempotency.ReplayedHeader, "true");
                return response;
            });
        using var browser = await gateway.SignedInAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sites/pie/posts")
        {
            Content = JsonContent.Create(new { title = "Pie" }),
        };
        request.Headers.Add(ApiIdempotency.KeyHeader, "\"8e03978e-40d5-43e8-bc93-6894a57f9324\"");

        using var response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues(ApiIdempotency.ReplayedHeader)));
        Assert.Equal(
            "\"8e03978e-40d5-43e8-bc93-6894a57f9324\"",
            gateway.Forwarded.Last().Header(ApiIdempotency.KeyHeader));
    }
}
