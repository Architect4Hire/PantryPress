using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Tests.Api;

// PP2.3: a route that names a site carries a site policy, a site policy sits only on such a route on the admin surface,
// or the host refuses to start (tenancy.md rule 4); and the admin document declares what the policies answer.
public sealed class SitePolicyConventionTests
{
    [Theory]
    [InlineData(typeof(SiteRouteWithoutPolicyController), "does not require a site policy")]
    [InlineData(typeof(AnonymousSiteRouteController), "does not require a site policy")]
    [InlineData(typeof(SitePolicyWithoutSiteController), "its route names no site")]
    [InlineData(typeof(SiteRouteSometimesController), "both with and without")]
    [InlineData(typeof(InternalSitePolicyController), "outside 'api/v1'")]
    [InlineData(typeof(SiteRouteByAnotherNameController), "with something other than a required {siteSlug}")]
    [InlineData(typeof(SiteRouteByLiteralController), "with something other than a required {siteSlug}")]
    [InlineData(typeof(SiteRouteWithOptionalSlugController), "with something other than a required {siteSlug}")]
    public async Task A_misplaced_site_route_or_policy_fails_startup(Type controller, string reason)
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            configureServices: services => services.AddProbes(new IdempotencyProbeLedger(), controller));

        var error = Assert.ThrowsAny<Exception>(() => api.Server);

        var refused = Assert.IsType<InvalidOperationException>(Unwrap(error));
        Assert.Contains(controller.Name, refused.Message, StringComparison.Ordinal);
        Assert.Contains(reason, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_site_route_documents_what_its_policy_answers()
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            "Development",
            new Dictionary<string, string?> { ["DevelopmentMail:Host"] = "localhost", ["DevelopmentMail:Port"] = "1025" },
            services => services.AddProbes(new IdempotencyProbeLedger(), typeof(SitePolicyProbeController)));

        using var response = await api.CreateClient().GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var responses = document.RootElement.GetProperty("paths")
            .GetProperty($"/{ApiSurfaces.AdminV1Route}/sites/{{siteSlug}}/policy-probe/owner")
            .GetProperty("get")
            .GetProperty("responses");
        Assert.Contains(SiteErrors.SiteNotFound.Code, responses.GetProperty("404").GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains(SiteErrors.SiteRoleRequired.Code, responses.GetProperty("403").GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    private static Exception Unwrap(Exception error)
    {
        while (error is AggregateException or System.Reflection.TargetInvocationException && error.InnerException is not null)
        {
            error = error.InnerException!;
        }

        return error;
    }
}

[Route(ApiSurfaces.AdminV1Route + "/sites/{siteSlug}/misplaced/no-policy")]
[Authorize]
public sealed class SiteRouteWithoutPolicyController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get() => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/sites/{siteSlug}/misplaced/anonymous")]
[Authorize(Policy = SitePolicies.SiteAuthor)]
public sealed class AnonymousSiteRouteController : ApiControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get() => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/misplaced/no-site")]
[Authorize(Policy = SitePolicies.SiteEditor)]
public sealed class SitePolicyWithoutSiteController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get() => NoContent();
}

[Authorize(Policy = SitePolicies.SiteEditor)]
public sealed class SiteRouteSometimesController : ApiControllerBase
{
    [HttpGet(ApiSurfaces.AdminV1Route + "/sites/{siteSlug}/misplaced/sometimes")]
    [HttpGet(ApiSurfaces.AdminV1Route + "/misplaced/sometimes")]
    public IActionResult Get() => NoContent();
}

// PP2.14: a site named after "sites/" by another route value would get no site policy at all.
[Route(ApiSurfaces.AdminV1Route + "/sites/{slug}/misplaced/renamed")]
[Authorize]
public sealed class SiteRouteByAnotherNameController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get() => NoContent();
}

// A literal beside {siteSlug}: a site whose slug is the same word could never be reached here.
[Route(ApiSurfaces.AdminV1Route + "/sites/mine")]
[Authorize]
public sealed class SiteRouteByLiteralController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get() => NoContent();
}

// An optional slug would also answer the account-level list at "sites".
[Route(ApiSurfaces.AdminV1Route + "/sites/{siteSlug?}")]
[Authorize(Policy = SitePolicies.SiteAuthor)]
public sealed class SiteRouteWithOptionalSlugController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get() => NoContent();
}

[Route(ApiSurfaces.InternalV1Route + "/sites/{siteSlug}/misplaced")]
[GatewayOnly]
[Authorize(Policy = SitePolicies.SiteOwner)]
public sealed class InternalSitePolicyController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get() => NoContent();
}
