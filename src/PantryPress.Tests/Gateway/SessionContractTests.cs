using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Yarp.ReverseProxy.Model;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;
using PantryPress.Gateway.Hosting;

namespace PantryPress.Tests.Gateway;

// PP1.6: the gateway and ApiService are separate assemblies on purpose - the gateway holds no reference to the Domain
// (gateway.md) - so the vocabularies they share are written twice. These tests are what keeps the two copies the same:
// a rename on one side alone fails here instead of at a blogger's sign-in.
public sealed class SessionContractTests
{
    [Fact]
    public void The_gateway_and_the_api_name_the_session_headers_identically()
    {
        Assert.Equal(GatewaySession.SecretHeader, SessionHeaders.Secret);
        Assert.Equal(GatewaySession.AccountHeader, SessionHeaders.Account);
        Assert.Equal(GatewaySession.StampHeader, SessionHeaders.Stamp);
        Assert.Equal(GatewaySession.SecondFactorHeader, SessionHeaders.SecondFactor);
        Assert.Equal(GatewaySession.True, SessionHeaders.True);
        Assert.Equal(GatewaySession.EndedHeader, SessionHeaders.Ended);
        Assert.Equal(GatewaySession.Ended, SessionHeaders.EndedValue);
        Assert.Equal(GatewaySession.NewStampHeader, SessionHeaders.NewStamp);

        // And the list the gateway strips is the list the API reads.
        Assert.Equal(GatewaySession.RequestHeaders, SessionHeaders.Request);
    }

    [Fact]
    public void The_gateways_copy_of_the_sign_in_answer_matches_the_apis_record()
    {
        var api = Properties(typeof(PantryPress.Domain.Modules.Accounts.Contracts.SessionStart));
        var gateway = Properties(typeof(PantryPress.Gateway.Hosting.SessionStart));

        Assert.Equal(api.Keys.Order(StringComparer.Ordinal), gateway.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, type) in api)
        {
            // Same type, or the nullable form: the gateway reads SecondStepRequired as bool? so a missing or renamed
            // field fails the sign-in instead of quietly meaning "no second step owed".
            Assert.True(
                gateway[name] == type || gateway[name] == typeof(Nullable<>).MakeGenericType(type),
                $"{name} is {gateway[name]} in the gateway and {type} in the API.");
        }
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status405MethodNotAllowed)]
    [InlineData(StatusCodes.Status413PayloadTooLarge)]
    [InlineData(StatusCodes.Status415UnsupportedMediaType)]
    [InlineData(StatusCodes.Status429TooManyRequests)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    [InlineData(StatusCodes.Status502BadGateway)]
    [InlineData(StatusCodes.Status504GatewayTimeout)]
    public void The_gateway_and_the_api_refuse_with_the_same_code_and_words(int status)
    {
        // One error shape across the whole /api surface, whoever answered (api-contract.md).
        Assert.Equal(ApiProblems.CodeFor(status), GatewayApi.CodeFor(status));
        Assert.Equal(ApiProblems.TitleFor(status), GatewayApi.TitleFor(status));
    }

    [Fact]
    public void The_sessions_own_routes_sit_under_the_declared_admin_surface()
    {
        // The gateway answers part of /api/v1 itself, so its path cannot drift off the versioned prefix the API
        // declares (api-contract.md).
        Assert.StartsWith("/" + ApiSurfaces.AdminV1Route + "/", SessionEndpoints.Base, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_only_api_paths_the_gateway_answers_itself_are_the_session_routes_and_the_fallback()
    {
        // The API refuses to start when a controller sits outside a declared surface; the gateway has no such
        // machinery, so this is the guard: a new unversioned or undeclared gateway route fails here. YARP's own
        // configured routes are left out - those are the forwarded ones, declared in configuration (gateway.md).
        await using var gateway = new GatewayFactory("Development");

        var patterns = gateway.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<RouteModel>() is null)
            .Select(endpoint => "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .Where(pattern => pattern.StartsWith("/api/", StringComparison.Ordinal) || pattern == "/api")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [SessionEndpoints.Base + "/", SessionEndpoints.Base + "/second-factor", "/api/{**rest}"],
            patterns);
    }

    [Fact]
    public async Task A_method_the_session_routes_do_not_answer_is_the_apis_business()
    {
        // The gateway owns four method-and-path pairs, not the path itself: anything else under /api/v1 is forwarded,
        // and ApiService answers for it (there is no such route there, so a real run gets its problem 404).
        await using var gateway = new GatewayFactory("Development");

        using var response = await gateway.CreateBrowser().PutAsJsonAsync(SessionEndpoints.Base, new { email = "x" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(gateway.Forwarded);
        Assert.Equal("PUT", forwarded.Method);
        Assert.Equal(GatewayFactory.ApiDestination, forwarded.Uri.Host);
    }

    private static Dictionary<string, Type> Properties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
}
