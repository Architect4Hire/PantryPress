using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Managers.Email;

namespace PantryPress.Tests.Api;

// PP1.5: the OpenAPI documents and the reference UI that reads them exist in Development only, and reading them
// fetches nothing from the internet (the package embeds the script; the CDN fonts are off).
public sealed class ApiReferenceTests
{
    private static readonly Dictionary<string, string?> DevelopmentMail = new()
    {
        ["DevelopmentMail:Host"] = "localhost",
        ["DevelopmentMail:Port"] = "1025",
    };

    [Theory]
    [InlineData("/scalar")]
    [InlineData("/scalar/v1")]
    public async Task Development_serves_the_reference_UI_from_this_app(string path)
    {
        await using var api = ApiServiceFactory.WithoutDatabase("Development", DevelopmentMail);

        using var response = await api.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var page = await response.Content.ReadAsStringAsync();

        // The UI reads this app's document and loads its script from this app, so nothing is fetched from a CDN.
        Assert.Contains($"openapi/{ApiSurfaces.AdminV1Document}.json", page, StringComparison.Ordinal);
        Assert.DoesNotContain("cdn.", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", page, StringComparison.OrdinalIgnoreCase);
        foreach (var asset in new[] { "/scalar/scalar.js", "/scalar/scalar.aspnetcore.js" })
        {
            using var script = await api.CreateClient().GetAsync(asset);
            Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        }
    }

    [Theory]
    [InlineData("/scalar")]
    [InlineData("/scalar/v1")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/internal-v1")] // the gateway-only surface (PP1.6): least of all this one
    [InlineData("/openapi/internal-v1.json")]
    public async Task Outside_Development_there_is_no_document_and_no_UI(string path)
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            "Production",
            configureServices: services => services.AddSingleton<IEmailGateway>(new Fakes.RecordingEmailGateway()));

        using var response = await api.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
