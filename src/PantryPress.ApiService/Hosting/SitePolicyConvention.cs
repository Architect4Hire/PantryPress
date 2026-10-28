using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.ApiService.Hosting;

/// <summary>
/// Where the site policies go (PP2.3, tenancy.md rule 4), checked when the host starts. A route that names a site with
/// <c>{siteSlug}</c> requires one of <see cref="SitePolicies"/>, or any signed-in account could reach any site through
/// it; a site policy sits only on such a route, and only on the admin surface - the publishing API takes its site from
/// the key (PP18), never from the route.
/// </summary>
/// <remarks>
/// Controllers only, like <see cref="IdempotencyConvention"/>: ApiService maps no minimal endpoint under <c>/api</c>.
/// </remarks>
internal sealed class SitePolicyConvention : IApplicationModelConvention
{
    private static readonly string SlugSegment = "{" + SiteAuthorization.SlugRouteValue;

    // The segment straight after "sites/" names a site, so it is exactly the value the policy reads - required, with at
    // most a constraint. Another value would reach a site with no policy; a literal is a word some site could take as its
    // slug (api-contract.md); an optional or defaulted slug would also answer the account-level list at "sites" (PP2.14).
    private static readonly Regex OtherSiteValue = new(
        @"(?:^|/)sites/(?!\{" + SiteAuthorization.SlugRouteValue + @"(?::[^}?=]+)?\}(?:/|$))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            foreach (var action in controller.Actions)
            {
                var name = $"{controller.ControllerType.Name}.{action.ActionMethod.Name}";
                var routes = ApiSurfaceConvention.RouteTemplates(controller, action).ToList();
                if (routes.FirstOrDefault(OtherSiteValue.IsMatch) is { } other)
                {
                    throw new InvalidOperationException(
                        $"{name} names a site after 'sites/' with something other than a required {{{SiteAuthorization.SlugRouteValue}}} "
                        + $"({other}). Only that value carries a site policy; anything else would reach a site with none, collide "
                        + "with a slug, or answer the account-level list (PP2.14).");
                }

                var namesSite = routes.Any(r => r.Contains(SlugSegment, StringComparison.OrdinalIgnoreCase));
                var attributes = controller.Attributes.Concat(action.Attributes).ToList();
                var sitePolicy = attributes.OfType<IAuthorizeData>().Any(a => a.Policy is { } p && SitePolicies.LeastRole.ContainsKey(p));

                if (namesSite && !routes.All(r => r.Contains(SlugSegment, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"{name} is routed both with and without {{{SiteAuthorization.SlugRouteValue}}}. A site route names its "
                        + "site on every path, so one policy decision fits all of them.");
                }

                if (namesSite && (!sitePolicy || attributes.OfType<IAllowAnonymous>().Any()))
                {
                    throw new InvalidOperationException(
                        $"{name} names a site with {{{SiteAuthorization.SlugRouteValue}}} but does not require a site policy "
                        + $"({string.Join(", ", SitePolicies.LeastRole.Keys)}). Without one, any signed-in account reaches any site.");
                }

                if (sitePolicy && !namesSite)
                {
                    throw new InvalidOperationException(
                        $"{name} requires a site policy but its route names no site. The policy decides by "
                        + $"{{{SiteAuthorization.SlugRouteValue}}} and would refuse every caller.");
                }

                if (sitePolicy && routes.Any(r => ApiSurfaces.For(r) != ApiSurfaces.AdminV1))
                {
                    throw new InvalidOperationException(
                        $"{name} requires a site policy outside '{ApiSurfaces.AdminV1Route}'. Site membership authorizes admin "
                        + "routes only; the publishing API's site comes from its key.");
                }
            }
        }
    }
}
