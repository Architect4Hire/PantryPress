using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PantryPress.Site.Controllers;
using PantryPress.Site.Hosting;

namespace PantryPress.Tests.Site;

// PP2.13 (api-contract.md): every public route sits under the one public surface, which names its OpenAPI document, and a
// route outside it fails the start; and every public controller stands behind the status answers.
public sealed class PublicSurfaceTests
{
    [Theory]
    [InlineData("api/public/v1", "site")]
    [InlineData("api/public/v1/site", "")]
    public void A_route_on_the_surface_is_described_by_its_document(string controllerRoute, string actionRoute)
    {
        var controller = ControllerFor(controllerRoute, actionRoute);

        new PublicSurfaceConvention().Apply(ApplicationFor(controller));

        Assert.Equal(PublicSurface.V1Document, controller.ApiExplorer.GroupName);
    }

    [Theory]
    [InlineData("api/public", "site")] // unversioned
    [InlineData("api/public/v2", "site")] // a version nobody declared
    [InlineData("api/public/v10", "site")] // a prefix of the declared one is not it
    [InlineData("api/v1", "site")] // the admin surface, which lives on ApiService
    [InlineData("api/public/v1", "/site")] // an action that replaces the versioned prefix
    [InlineData("api/public/v1", "~/site")]
    public void A_route_outside_the_surface_fails_the_start(string controllerRoute, string actionRoute)
    {
        var controller = ControllerFor(controllerRoute, actionRoute);

        var error = Assert.Throws<InvalidOperationException>(() => new PublicSurfaceConvention().Apply(ApplicationFor(controller)));

        Assert.Contains(nameof(StandInController), error.Message, StringComparison.Ordinal);
        Assert.Contains(PublicSurface.V1Route, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("site/{siteId}", nameof(StandInController.Act))] // a route value
    [InlineData("site", nameof(StandInController.Take))] // a parameter
    public void Request_input_of_any_kind_fails_the_start_until_the_site_id_check_comes_with_it(string actionRoute, string method)
    {
        var controller = ControllerFor("api/public/v1", actionRoute, method);

        var error = Assert.Throws<InvalidOperationException>(() => new PublicSurfaceConvention().Apply(ApplicationFor(controller)));

        Assert.Contains("binds request input", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cancellation_token_is_not_request_input()
    {
        var controller = ControllerFor("api/public/v1", "site", nameof(StandInController.Wait));

        new PublicSurfaceConvention().Apply(ApplicationFor(controller));

        Assert.Equal(PublicSurface.V1Document, controller.ApiExplorer.GroupName);
    }

    [Fact]
    public void A_controller_with_no_route_fails_the_start()
    {
        var controller = new ControllerModel(typeof(StandInController).GetTypeInfo(), []);
        controller.Actions.Add(ActionFor(controller, route: null));

        var error = Assert.Throws<InvalidOperationException>(() => new PublicSurfaceConvention().Apply(ApplicationFor(controller)));

        Assert.Contains("no attribute route", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_host_puts_every_controller_on_the_surface_and_behind_the_status_answers()
    {
        await using var site = new SiteFactory("Development");
        var options = site.Services.GetRequiredService<IOptions<MvcOptions>>().Value;

        Assert.Contains(options.Conventions, c => c is PublicSurfaceConvention);
        // Global, not per controller, so a controller added later cannot forget it.
        Assert.Contains(options.Filters, f => f is TypeFilterAttribute { ImplementationType: var type } && type == typeof(SiteStatusFilter));
    }

    [Fact]
    public async Task The_public_api_writes_enums_as_kebab_case_strings()
    {
        await using var site = new SiteFactory("Development");
        var json = site.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
        var http = site.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;

        Assert.Equal("\"read-only\"", System.Text.Json.JsonSerializer.Serialize(Domain.Modules.Sites.Contracts.SiteStatus.ReadOnly, json));
        Assert.Equal("\"read-only\"", System.Text.Json.JsonSerializer.Serialize(Domain.Modules.Sites.Contracts.SiteStatus.ReadOnly, http));
    }

    private static ApplicationModel ApplicationFor(ControllerModel controller)
    {
        var application = new ApplicationModel();
        application.Controllers.Add(controller);
        return application;
    }

    private static ControllerModel ControllerFor(string controllerRoute, string actionRoute, string method = nameof(StandInController.Act))
    {
        var controller = new ControllerModel(typeof(StandInController).GetTypeInfo(), []);
        controller.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(controllerRoute)),
        });
        controller.Actions.Add(ActionFor(controller, actionRoute, method));
        return controller;
    }

    private static ActionModel ActionFor(ControllerModel controller, string? route, string method = nameof(StandInController.Act))
    {
        var info = typeof(StandInController).GetMethod(method)!;
        var action = new ActionModel(info, [])
        {
            Controller = controller,
        };
        foreach (var parameter in info.GetParameters())
        {
            action.Parameters.Add(new ParameterModel(parameter, []) { Action = action, ParameterName = parameter.Name! });
        }

        action.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = route is null ? null : new AttributeRouteModel(new RouteAttribute(route)),
        });
        return action;
    }

    private sealed class StandInController : ControllerBase
    {
        public IActionResult Act() => Ok();

        public IActionResult Take(string site) => Ok(site);

        public IActionResult Wait(CancellationToken cancellationToken) => Ok(cancellationToken.IsCancellationRequested);
    }
}
