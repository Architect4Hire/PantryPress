using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace PantryPress.Site.Hosting;

/// <summary>
/// The public endpoints (PP2.13): controllers only, every one under the public surface and behind the status answers
/// (SITE-008, SEO-007), with the API's JSON conventions (api-contract.md: camel-case names, kebab-case enum strings).
/// </summary>
internal static class PublicControllers
{
    private const string ProblemContentType = "application/problem+json";

    /// <summary>
    /// MVC's core alone: no authorization, CORS, or views, since nobody signs in here and nothing is rendered by this host.
    /// </summary>
    public static IServiceCollection AddPublicControllers(this IServiceCollection services)
    {
        services.AddMvcCore(options =>
            {
                options.Conventions.Add(new PublicSurfaceConvention());
                options.Filters.Add<SiteStatusFilter>();

                // What every route can answer before or instead of its own work: no site at the Host, a site that is gone
                // or not open to readers, a fault. Declared globally, so a later controller's document says so too.
                options.Filters.Add(new ProducesResponseTypeAttribute<ProblemDetails>(StatusCodes.Status404NotFound, ProblemContentType));
                options.Filters.Add(new ProducesResponseTypeAttribute<ProblemDetails>(StatusCodes.Status410Gone, ProblemContentType));
                options.Filters.Add(new ProducesResponseTypeAttribute<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, ProblemContentType));
                options.Filters.Add(
                    new ProducesResponseTypeAttribute<ProblemDetails>(StatusCodes.Status500InternalServerError, ProblemContentType));
            })
            .AddApiExplorer()
            .AddJsonOptions(options => Configure(options.JsonSerializerOptions));

        // The OpenAPI generator reads these options rather than MVC's.
        services.ConfigureHttpJsonOptions(options => Configure(options.SerializerOptions));
        return services;
    }

    /// <summary>
    /// Refuses to start a host that serves anything but controllers, the health probes, and the Development OpenAPI
    /// document: a minimal endpoint would escape both the surface check and the status answers.
    /// </summary>
    public static void EnsureControllersOnly(this WebApplication app)
    {
        var strays = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is null && !IsAllowed(e))
            .Select(e => e.DisplayName ?? "an unnamed endpoint")
            .ToList();
        if (strays.Count > 0)
        {
            throw new InvalidOperationException(
                $"The public site serves {string.Join(", ", strays)} outside a controller. Every public endpoint is a "
                + $"controller under '{PublicSurface.V1Route}', so it gets the status answers.");
        }
    }

    private static bool IsAllowed(Endpoint endpoint) =>
        endpoint is RouteEndpoint { RoutePattern.RawText: { } pattern }
        && (pattern is "/health" or "/alive" || pattern.StartsWith("/openapi/", StringComparison.Ordinal));

    private static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false));
    }
}
