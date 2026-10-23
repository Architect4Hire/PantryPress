using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;

namespace PantryPress.Tests.Api;

// PP1.5: every route belongs to a declared, versioned API surface, and the surface decides which OpenAPI document
// describes it (api-contract.md). A route outside one fails startup rather than shipping unversioned.
public sealed class ApiVersioningTests
{
    [Fact]
    public void A_versioned_route_is_described_by_its_surfaces_document()
    {
        var controller = ControllerFor("api/v1/account", "register");

        new ApiSurfaceConvention().Apply(ApplicationFor(controller));

        Assert.Equal(ApiSurfaces.AdminV1Document, controller.ApiExplorer.GroupName);
    }

    [Theory]
    [InlineData("api/account", null)] // unversioned
    [InlineData("api/v2/account", null)] // a version nobody declared
    [InlineData("api/publishing/v1/posts", null)] // PP18's surface, not declared yet
    [InlineData("v1/account", null)] // outside /api
    [InlineData("api/v1/account", "/api/account")] // an action that replaces the versioned prefix
    public void A_route_outside_every_surface_fails_startup(string controllerRoute, string? actionRoute)
    {
        var controller = ControllerFor(controllerRoute, actionRoute ?? "register");

        var error = Assert.Throws<InvalidOperationException>(
            () => new ApiSurfaceConvention().Apply(ApplicationFor(controller)));

        Assert.Contains(nameof(StandInController), error.Message, StringComparison.Ordinal);
        Assert.Contains(ApiSurfaces.AdminV1Route, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_controller_with_no_route_fails_startup()
    {
        var controller = new ControllerModel(typeof(StandInController).GetTypeInfo(), []);
        controller.Actions.Add(ActionFor(controller, route: null));

        var error = Assert.Throws<InvalidOperationException>(
            () => new ApiSurfaceConvention().Apply(ApplicationFor(controller)));

        Assert.Contains("no attribute route", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_declared_surface_covers_another()
    {
        // ApiSurfaces.For takes the first match, so nested prefixes would group a route into whichever surface came
        // first instead of failing, and two surfaces sharing a document would merge.
        foreach (var surface in ApiSurfaces.All)
        {
            Assert.DoesNotContain(ApiSurfaces.All.Where(other => other != surface), other => surface.Covers(other.RoutePrefix));
        }

        Assert.Equal(ApiSurfaces.All.Count, ApiSurfaces.All.Select(s => s.Document).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Every_route_this_service_exposes_belongs_to_its_surfaces_document()
    {
        await using var api = ApiServiceFactory.WithoutDatabase();

        var descriptions = api.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups.Items.SelectMany(group => group.Items)
            .ToList();

        Assert.NotEmpty(descriptions);
        foreach (var description in descriptions)
        {
            // Phrased for more than one surface, so PP18's publishing routes extend this test instead of breaking it.
            var surface = ApiSurfaces.For(description.RelativePath ?? string.Empty);
            Assert.NotNull(surface);
            Assert.Equal(surface.Document, description.GroupName);
        }
    }

    [Fact]
    public async Task No_endpoint_under_api_escapes_the_surfaces()
    {
        // The convention covers controllers. A minimal-API route would not reach it, so every routable /api endpoint is
        // checked here as well (the reference UI and the documents sit outside /api).
        await using var api = ApiServiceFactory.WithoutDatabase();

        var patterns = api.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .Where(pattern => pattern.TrimStart('/').StartsWith("api/", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(patterns);
        foreach (var pattern in patterns)
        {
            Assert.NotNull(ApiSurfaces.For(pattern.TrimStart('/')));
        }
    }

    [Fact]
    public async Task The_admin_document_describes_the_admin_surface_and_nothing_else()
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            "Development",
            new Dictionary<string, string?> { ["DevelopmentMail:Host"] = "localhost", ["DevelopmentMail:Port"] = "1025" });

        using var response = await api.CreateClient().GetAsync($"/openapi/{ApiSurfaces.AdminV1Document}.json");

        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ApiSurfaces.AdminV1.Title, document.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal(
            ApiSurfaces.AdminV1.Version,
            document.RootElement.GetProperty("info").GetProperty("version").GetString());
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            Assert.StartsWith("/" + ApiSurfaces.AdminV1Route + "/", path.Name, StringComparison.Ordinal);
        }
    }

    private static ApplicationModel ApplicationFor(ControllerModel controller)
    {
        var application = new ApplicationModel();
        application.Controllers.Add(controller);
        return application;
    }

    private static ControllerModel ControllerFor(string controllerRoute, string actionRoute)
    {
        var controller = new ControllerModel(typeof(StandInController).GetTypeInfo(), []);
        controller.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(controllerRoute)),
        });
        controller.Actions.Add(ActionFor(controller, actionRoute));
        return controller;
    }

    private static ActionModel ActionFor(ControllerModel controller, string? route)
    {
        var action = new ActionModel(typeof(StandInController).GetMethod(nameof(StandInController.Act))!, [])
        {
            Controller = controller,
        };
        action.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = route is null ? null : new AttributeRouteModel(new RouteAttribute(route)),
        });
        return action;
    }

    private sealed class StandInController
    {
        public void Act()
        {
        }
    }
}
