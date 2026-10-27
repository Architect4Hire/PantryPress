using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PantryPress.Tests.Api;

// PP2.10: the site routes' contract (api-contract.md), with no database behind them: what the admin document declares for
// each route, and the zone new sites' addresses sit under, which the API will not start without.
public sealed class SitesApiContractTests
{
    [Fact]
    public async Task The_admin_document_declares_every_answer_of_the_site_routes()
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            "Development",
            new Dictionary<string, string?> { ["DevelopmentMail:Host"] = "localhost", ["DevelopmentMail:Port"] = "1025" });

        using var response = await api.CreateClient().GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        JsonElement Operation(string path, string method) => paths.GetProperty(path).GetProperty(method);

        // A write gains the idempotency key's 400, 409, and 422 and the gateway's antiforgery and same-origin answers; the
        // site route gains its policy's 404, and no 403, because every member is at least an Author.
        Assert.Equal(
            ["201", "400", "401", "403", "409", "413", "415", "422", "429", "500", "502", "504"],
            Statuses(Operation("/api/v1/sites", "post")));
        Assert.Equal(["200", "400", "401", "413", "429", "500", "502", "504"], Statuses(Operation("/api/v1/sites", "get")));
        Assert.Equal(
            ["200", "401", "404", "413", "429", "500", "502", "504"],
            Statuses(Operation("/api/v1/sites/{siteSlug}", "get")));
        Assert.Equal(["200", "400", "401", "413", "429", "500", "502", "504"], Statuses(Operation("/api/v1/slug-availability", "get")));

        var create = Operation("/api/v1/sites", "post");
        Assert.Contains("retry", create.GetProperty("description").GetString(), StringComparison.Ordinal);
        var responses = create.GetProperty("responses");
        Assert.True(responses.GetProperty("201").GetProperty("headers").TryGetProperty("Location", out _));
        var conflict = responses.GetProperty("409");
        Assert.Contains("slug-unavailable", conflict.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains(
            "idempotency-key-in-use",
            conflict.GetProperty("headers").GetProperty("Retry-After").GetProperty("description").GetString(),
            StringComparison.Ordinal);

        // The address to check is required; the list's parameters say what they take.
        var slug = Operation("/api/v1/slug-availability", "get").GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "slug");
        Assert.True(slug.GetProperty("required").GetBoolean());
        Assert.All(
            Operation("/api/v1/sites", "get").GetProperty("parameters").EnumerateArray(),
            p => Assert.True(p.TryGetProperty("description", out var description) && description.GetString() is { Length: > 0 }, p.GetRawText()));
        Assert.Contains(
            create.GetProperty("parameters").EnumerateArray(),
            p => p.GetProperty("name").GetString() == "Idempotency-Key"
                && !(p.TryGetProperty("required", out var required) && required.GetBoolean()));

        // Enums are kebab-case strings (api-contract.md), and no answer carries a site id.
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Contains("live", schemas.GetProperty("SiteStatus").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("owner", schemas.GetProperty("SiteRole").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        foreach (var name in new[] { "SiteDetail", "SiteSummary", "SlugAvailability", "CreateSiteRequest" })
        {
            Assert.DoesNotContain(
                schemas.GetProperty(name).GetProperty("properties").EnumerateObject(),
                p => p.Name.Contains("id", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_api_will_not_start_with_a_negative_limit_for_unconfirmed_accounts()
    {
        using var api = ApiServiceFactory.WithoutDatabase(
            "Production",
            new Dictionary<string, string?> { ["Sites:UnconfirmedAccountSiteLimit"] = "-1" });

        var refused = Assert.ThrowsAny<Exception>(() => api.Server);

        Assert.IsType<OptionsValidationException>(refused.GetBaseException(), exactMatch: false);
        Assert.Contains("Sites:UnconfirmedAccountSiteLimit", refused.GetBaseException().Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PantryPress.app")]
    [InlineData("pantrypress.app:443")]
    [InlineData(".pantrypress.app")]
    public void The_api_will_not_start_without_a_zone_for_new_sites(string? zone)
    {
        using var api = ApiServiceFactory.WithoutDatabase(
            "Production",
            new Dictionary<string, string?> { ["Sites:SubdomainZone"] = zone });

        var refused = Assert.ThrowsAny<Exception>(() => api.Server);

        Assert.IsType<OptionsValidationException>(refused.GetBaseException(), exactMatch: false);
        Assert.Contains("Sites:SubdomainZone", refused.GetBaseException().Message, StringComparison.Ordinal);
    }

    // Creating sites has its own small hourly allowance per account, and a refused create still spends it, so an account
    // cannot take address after address by sending bad requests in between (PP2.10).
    [Fact]
    public async Task Creating_sites_has_its_own_small_allowance()
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            extraSettings: new Dictionary<string, string?> { ["RateLimits:SiteCreatesPerAccountPerHour"] = "2" });
        using var client = api.CreateClientFor(Guid.NewGuid());
        var refused = new { slug = "-bad", name = "Pie", timeZone = "America/Chicago" };

        using var first = await client.PostAsJsonAsync("/api/v1/sites", refused);
        using var second = await client.PostAsJsonAsync("/api/v1/sites", refused);
        using var third = await client.PostAsJsonAsync("/api/v1/sites", refused);
        using var other = await api.CreateClientFor(Guid.NewGuid()).PostAsJsonAsync("/api/v1/sites", refused);

        Assert.Equal(
            [HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests, HttpStatusCode.BadRequest],
            new[] { first.StatusCode, second.StatusCode, third.StatusCode, other.StatusCode });
        Assert.True(third.Headers.RetryAfter is not null);
    }

    private static List<string> Statuses(JsonElement operation) =>
        [.. operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order(StringComparer.Ordinal)];
}
