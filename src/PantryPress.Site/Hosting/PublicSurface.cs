using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace PantryPress.Site.Hosting;

/// <summary>
/// The public site API's one surface (api-contract.md): every route sits under <see cref="V1Route"/> and is described by
/// the <see cref="V1Document"/> OpenAPI document. A breaking change adds a surface; it never edits this one.
/// </summary>
internal static class PublicSurface
{
    public const string V1Route = "api/public/v1";

    /// <summary>The document's name, which is also its path: <c>/openapi/public-v1.json</c>.</summary>
    public const string V1Document = "public-v1";

    public const string V1Title = "PantryPress public site API";
}

/// <summary>
/// Refuses to start a host with a controller outside <see cref="PublicSurface.V1Route"/> - unversioned, another version,
/// or an action whose absolute route escapes the prefix - and files every controller under the surface's document.
/// </summary>
/// <remarks>
/// It also refuses any input a request could bind - a parameter, a bound property, a route value - but a cancellation
/// token. Nothing a reader sends may name a site (tenancy.md rule 7), and the check that proves it of a request type is
/// ApiService's <c>SiteIdBindingConvention</c>; the first public endpoint that takes input (PP12) brings that check to this
/// host and lifts this refusal, so no input arrives here unchecked in the meantime.
/// </remarks>
internal sealed class PublicSurfaceConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);
        foreach (var controller in application.Controllers)
        {
            var name = controller.ControllerType.Name;
            var routes = RouteTemplates(controller).Distinct(StringComparer.Ordinal).ToList();
            if (routes.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{name} has no attribute route. Every public route sits under '{PublicSurface.V1Route}'.");
            }

            var outside = routes.Where(route => !Covers(route)).ToList();
            if (outside.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{name} is routed at {string.Join(", ", outside.Select(route => $"'{route}'"))}, outside the public "
                    + $"API surface '{PublicSurface.V1Route}'.");
            }

            var bound = controller.ControllerProperties.Where(p => p.BindingInfo is not null).Select(p => p.PropertyName)
                .Concat(controller.Actions.SelectMany(a => a.Parameters)
                    .Where(p => p.ParameterType != typeof(CancellationToken))
                    .Select(p => $"{p.Action.ActionMethod.Name}({p.ParameterName})"))
                .Concat(routes.Where(route => route.Contains('{', StringComparison.Ordinal)))
                .ToList();
            if (bound.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{name} binds request input ({string.Join(", ", bound)}). The public site takes none until the site-id "
                    + "binding check comes to this host with the first endpoint that needs input (PP12, tenancy.md rule 7).");
            }

            controller.ApiExplorer.GroupName = PublicSurface.V1Document;
        }
    }

    private static bool Covers(string route) =>
        route.Equals(PublicSurface.V1Route, StringComparison.Ordinal)
        || route.StartsWith(PublicSurface.V1Route + "/", StringComparison.Ordinal);

    // Each action's route as it is actually served: the controller's prefix combined with the action's template, so an
    // action that replaces the prefix with an absolute route is checked too.
    private static IEnumerable<string> RouteTemplates(ControllerModel controller)
    {
        var prefixes = controller.Selectors.Count > 0 ? controller.Selectors : [new SelectorModel()];
        foreach (var selector in controller.Actions.SelectMany(action => action.Selectors))
        {
            foreach (var prefix in prefixes)
            {
                var combined = AttributeRouteModel.CombineAttributeRouteModel(prefix.AttributeRouteModel, selector.AttributeRouteModel);
                if (combined?.Template is { Length: > 0 } template)
                {
                    yield return template.TrimStart('~').TrimStart('/');
                }
            }
        }
    }
}
