namespace PantryPress.ApiService.Http;

/// <summary>
/// One API surface this service exposes: a route prefix that carries the surface's version, the OpenAPI document that
/// describes it, and the version that document publishes (api-contract.md). The document's name and the version are
/// separate because a document may be named for its surface, as the publishing API's will be at PP18, while its
/// version stays a version. A breaking change adds a surface; it never changes one.
/// </summary>
internal sealed record ApiSurface(string RoutePrefix, string Document, string Version, string Title)
{
    /// <summary>Whether an MVC route template (no leading slash) belongs to this surface.</summary>
    public bool Covers(string routeTemplate) =>
        routeTemplate.Equals(RoutePrefix, StringComparison.Ordinal)
        || routeTemplate.StartsWith(RoutePrefix + "/", StringComparison.Ordinal);
}

/// <summary>
/// Every API surface in this service. The gateway forwards the admin surface and nothing else (gateway.md), and
/// <see cref="Hosting.ApiSurfaceConvention"/> refuses to start a host whose routes sit outside one of these.
/// </summary>
internal static class ApiSurfaces
{
    /// <summary>The admin API the blogger's app reaches through the gateway (ACCT-002).</summary>
    public const string AdminV1Route = "api/v1";

    /// <summary>The admin document's name, which is also its path: <c>/openapi/v1.json</c>.</summary>
    public const string AdminV1Document = "v1";

    public static readonly ApiSurface AdminV1 = new(AdminV1Route, AdminV1Document, "v1", "PantryPress admin API");

    // PP18 adds the publishing surface ("api/publishing/v1") with its own published document (API-009). Until it is
    // listed here, a controller routed under it fails startup like any other undeclared route.
    public static readonly IReadOnlyList<ApiSurface> All = [AdminV1];

    public static ApiSurface? For(string routeTemplate) => All.FirstOrDefault(s => s.Covers(routeTemplate));
}
