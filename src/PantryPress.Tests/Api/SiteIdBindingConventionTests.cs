using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Tests.Api;

// PP2.4: nothing a client sends can name a site (tenancy.md rule 7). A route that would bind a site id from its address,
// query string, a header, a form, or anywhere in its body fails the host's start; a route that only reads the context
// starts.
public sealed class SiteIdBindingConventionTests
{
    [Theory]
    [InlineData(typeof(SiteIdInTheRouteController), "would take a site id from the address")]
    [InlineData(typeof(SiteIdInTheQueryController), "binds a site id")]
    [InlineData(typeof(SiteIdInAHeaderController), "binds a site id")]
    [InlineData(typeof(SiteIdTypedBodyController), "binds a site id")]
    [InlineData(typeof(SiteIdNamedInTheBodyController), "PlantedNamed.SiteId")]
    [InlineData(typeof(SiteIdRenamedInTheBodyController), "PlantedRenamed.Owner")]
    [InlineData(typeof(SiteIdDeepInTheBodyController), "PlantedOuter.Items[].Site")]
    [InlineData(typeof(SiteIdInACollectionController), "List`1<PlantedInner>")]
    [InlineData(typeof(SiteIdOnTheControllerController), "SiteIdOnTheControllerController.Site")]
    [InlineData(typeof(SiteIdInAnInterfaceCollectionController), "IReadOnlyList`1<PlantedInner>")]
    [InlineData(typeof(SiteIdsInAQueryInterfaceController), "binds a site id")]
    [InlineData(typeof(SiteIdsInTheQueryController), "binds a site id")]
    [InlineData(typeof(SiteIdInAFormController), "binds a site id")]
    [InlineData(typeof(SiteIdRenamedInAQueryModelController), "PlantedQuery.Owner")]
    [InlineData(typeof(SiteIdInADerivedTypeController), "PlantedBlock(PlantedSiteBlock).Site")]
    public async Task A_route_that_takes_a_site_from_the_request_fails_startup(Type controller, string reason)
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            configureServices: services => services.AddProbes(new IdempotencyProbeLedger(), controller));

        var error = Assert.ThrowsAny<Exception>(() => api.Server);

        var refused = Assert.IsType<InvalidOperationException>(Unwrap(error));
        Assert.Contains(controller.Name, refused.Message, StringComparison.Ordinal);
        Assert.Contains(reason, refused.Message, StringComparison.Ordinal);
    }

    // The context as a service, a body without a site, and a slug in the route are what a site route is made of.
    [Fact]
    public async Task A_route_that_reads_the_context_starts()
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            configureServices: services => services.AddProbes(new IdempotencyProbeLedger(), typeof(SiteContextProbeController)));

        Assert.NotNull(api.Server);
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

public sealed record PlantedNamed(string Name, Guid SiteId);

public sealed record PlantedRenamed(string Name, [property: JsonPropertyName("site_id")] Guid Owner);

public sealed record PlantedInner(string Name, SiteId Site);

public sealed record PlantedOuter(string Name, PlantedInner[] Items);

public sealed class PlantedQuery
{
    public string? Name { get; set; }

    [FromHeader(Name = "X-PP-Site-Id")]
    public Guid Owner { get; set; }
}

[JsonDerivedType(typeof(PlantedTextBlock), "text")]
[JsonDerivedType(typeof(PlantedSiteBlock), "site")]
public abstract record PlantedBlock;

public sealed record PlantedTextBlock(string Text) : PlantedBlock;

public sealed record PlantedSiteBlock(SiteId Site) : PlantedBlock;

[Route(ApiSurfaces.AdminV1Route + "/planted/{siteId:guid}/route")]
[Authorize]
public sealed class SiteIdInTheRouteController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get() => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/query")]
[Authorize]
public sealed class SiteIdInTheQueryController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] Guid siteId) => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/header")]
[Authorize]
public sealed class SiteIdInAHeaderController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get([FromHeader(Name = "X-PP-Site-Id")] string? site) => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/typed")]
[Authorize]
public sealed class SiteIdTypedBodyController : ApiControllerBase
{
    [HttpPost]
    public IActionResult Post([FromBody] SiteId target) => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/named")]
[Authorize]
public sealed class SiteIdNamedInTheBodyController : ApiControllerBase
{
    [HttpPost]
    public IActionResult Post(PlantedNamed request) => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/renamed")]
[Authorize]
public sealed class SiteIdRenamedInTheBodyController : ApiControllerBase
{
    [HttpPost]
    public IActionResult Post(PlantedRenamed request) => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/deep")]
[Authorize]
public sealed class SiteIdDeepInTheBodyController : ApiControllerBase
{
    [HttpPost]
    public IActionResult Post(PlantedOuter request) => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/collection")]
[Authorize]
public sealed class SiteIdInACollectionController : ApiControllerBase
{
    [HttpPost]
    public IActionResult Post(List<PlantedInner> request) => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/property")]
[Authorize]
public sealed class SiteIdOnTheControllerController : ApiControllerBase
{
    [FromQuery(Name = "site_id")]
    public Guid Site { get; set; }

    [HttpGet]
    public IActionResult Get() => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/interface-collection")]
[Authorize]
public sealed class SiteIdInAnInterfaceCollectionController : ApiControllerBase
{
    [HttpPost]
    public IActionResult Post([FromBody] IReadOnlyList<PlantedInner> items) => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/query-interface")]
[Authorize]
public sealed class SiteIdsInAQueryInterfaceController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] IEnumerable<Guid> siteId) => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/plural")]
[Authorize]
public sealed class SiteIdsInTheQueryController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery(Name = "site_ids")] Guid[] targets) => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/form")]
[Authorize]
public sealed class SiteIdInAFormController : ApiControllerBase
{
    [HttpPost]
    public IActionResult Post([FromForm] Guid siteId) => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/query-model")]
[Authorize]
public sealed class SiteIdRenamedInAQueryModelController : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] PlantedQuery query) => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/planted/derived")]
[Authorize]
public sealed class SiteIdInADerivedTypeController : ApiControllerBase
{
    [HttpPost]
    public IActionResult Post(PlantedBlock block) => NoContent();
}
