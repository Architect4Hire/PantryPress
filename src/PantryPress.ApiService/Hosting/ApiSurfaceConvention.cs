using Microsoft.AspNetCore.Mvc.ApplicationModels;
using PantryPress.ApiService.Http;

namespace PantryPress.ApiService.Hosting;

/// <summary>
/// The versioning convention (api-contract.md): every route sits under a declared, versioned API surface, and the
/// surface decides which OpenAPI document describes it. A route that belongs to no surface - an unversioned
/// "api/account", or an "api/v2" nobody declared - fails startup instead of shipping.
/// </summary>
internal sealed class ApiSurfaceConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            var routes = RouteTemplates(controller).Distinct(StringComparer.Ordinal).ToList();
            var name = controller.ControllerType.Name;
            if (routes.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{name} has no attribute route. Every controller is routed under an API surface: {Declared}.");
            }

            var unknown = routes.Where(route => ApiSurfaces.For(route) is null).ToList();
            if (unknown.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{name} is routed at {string.Join(", ", unknown.Select(route => $"'{route}'"))}, which belongs to no " +
                    $"API surface. Declared surfaces: {Declared}.");
            }

            var surfaces = routes.Select(route => ApiSurfaces.For(route)!).Distinct().ToList();
            if (surfaces.Count > 1)
            {
                throw new InvalidOperationException(
                    $"{name} spans the API surfaces {string.Join(", ", surfaces.Select(s => s.RoutePrefix))}. One " +
                    "controller belongs to one surface, so one document describes it.");
            }

            // The internal surface exists for the gateway alone, so every controller on it must refuse a caller that
            // cannot present the gateway's secret. Enforced here rather than per controller: forgetting the attribute
            // on a later one would leave an anonymous route naming any account it liked.
            if (surfaces[0] == ApiSurfaces.InternalV1
                && !controller.Attributes.OfType<GatewayOnlyAttribute>().Any())
            {
                throw new InvalidOperationException(
                    $"{name} is routed under '{ApiSurfaces.InternalV1Route}' without [GatewayOnly]. Every route on "
                    + "the internal surface is the gateway's alone.");
            }

            controller.ApiExplorer.GroupName = surfaces[0].Document;
        }
    }

    private static string Declared => string.Join(", ", ApiSurfaces.All.Select(s => s.RoutePrefix));

    /// <summary>
    /// What each action is actually routed at: the controller's prefix combined with the action's template, so an
    /// action that replaces the prefix with an absolute route is checked too.
    /// </summary>
    private static IEnumerable<string> RouteTemplates(ControllerModel controller) =>
        controller.Actions.SelectMany(action => RouteTemplates(controller, action));

    /// <summary>What one action is routed at, the same way (also read by <see cref="SitePolicyConvention"/>).</summary>
    internal static IEnumerable<string> RouteTemplates(ControllerModel controller, ActionModel action)
    {
        var prefixes = controller.Selectors.Count > 0 ? controller.Selectors : [new SelectorModel()];
        foreach (var selector in action.Selectors)
        {
            foreach (var prefix in prefixes)
            {
                var combined = AttributeRouteModel.CombineAttributeRouteModel(
                    prefix.AttributeRouteModel,
                    selector.AttributeRouteModel);
                if (combined?.Template is { Length: > 0 } template)
                {
                    yield return template.TrimStart('~').TrimStart('/');
                }
            }
        }
    }
}
