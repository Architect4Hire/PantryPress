using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Yarp.ReverseProxy.Model;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Modules.Accounts.Contracts;
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
        Assert.Equal(GatewaySession.SessionIdHeader, SessionHeaders.SessionId);
        Assert.Equal(GatewaySession.SecondStepPassedHeader, SessionHeaders.SecondStepPassed);
        Assert.Equal(GatewaySession.SecondStepPassed, SessionHeaders.SecondStepPassedValue);
        Assert.Equal(GatewaySession.ViaProxyHeader, SessionHeaders.ViaProxy);
        Assert.Equal(GatewaySession.ViaProxy, SessionHeaders.ViaProxyValue);

        // And the list the gateway strips is the list the API reads.
        Assert.Equal(GatewaySession.RequestHeaders, SessionHeaders.Request);
    }

    [Fact]
    public void The_gateways_copy_of_the_sign_in_answer_matches_the_apis_record()
    {
        var api = Properties(typeof(PantryPress.Domain.Modules.Accounts.Contracts.SessionStart));
        var gateway = Properties(typeof(PantryPress.Gateway.Contracts.SessionStart));

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
    [InlineData(typeof(PantryPress.Domain.Modules.Accounts.Contracts.CurrentSession), typeof(PantryPress.Gateway.Contracts.CurrentSession))]
    [InlineData(typeof(PantryPress.Domain.Modules.Accounts.Contracts.CurrentSessionRequest), typeof(PantryPress.Gateway.Contracts.CurrentSessionRequest))]
    [InlineData(typeof(PantryPress.Domain.Modules.Accounts.Contracts.EndSessionRequest), typeof(PantryPress.Gateway.Contracts.EndSessionRequest))]
    [InlineData(typeof(PantryPress.Domain.Modules.Accounts.Contracts.SecondFactorSignInRequest), typeof(PantryPress.Gateway.Contracts.SecondFactorSignInRequest))]
    [InlineData(typeof(PantryPress.Domain.Modules.Accounts.Contracts.SignInRequest), typeof(PantryPress.Gateway.Contracts.CredentialsRequest))]
    public void The_gateways_copies_of_the_internal_records_match_the_apis(Type api, Type gateway)
    {
        // PP1.12 added the current-session check and sign-out to the internal surface; every record crossing it is
        // written twice, so a rename on either side fails here.
        Assert.Equal(Properties(api), Properties(gateway));
    }

    [Fact]
    public void Both_sides_name_an_ended_session_the_same_way()
    {
        Assert.Equal(GatewayApi.SessionEndedCode, AccountErrors.SessionEnded.Code);
        Assert.Equal(GatewayApi.SessionEndedTitle, AccountErrors.SessionEnded.Title);
        Assert.Equal(GatewayApi.SessionEndedCode, GatewaySession.EndedCode);
        Assert.Equal(SessionEndpoints.SignInExpiredCode, AccountErrors.SignInExpired.Code);
    }

    [Fact]
    public void A_sign_out_is_remembered_for_longer_than_any_session_can_live()
    {
        // Otherwise the API would forget a sign-out while the cookie it ended could still be presented (PP1.12).
        Assert.True(SessionCookie.CompleteAbsoluteLifetime < SessionLimits.RevocationRetention);
        Assert.True(SessionCookie.CompleteLifetime <= SessionCookie.CompleteAbsoluteLifetime);
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

    [Theory]
    [InlineData("POST", "", new[] { 200, 400, 403, 409, 413, 415, 429, 502 })]
    [InlineData("POST", "/second-factor", new[] { 200, 400, 401, 403, 409, 413, 415, 429, 502 })]
    [InlineData("GET", "", new[] { 200, 401, 413, 429, 502 })]
    [InlineData("DELETE", "", new[] { 204, 400, 403, 413, 429 })]
    public async Task Each_session_route_declares_every_answer_it_can_give(string method, string path, int[] statuses)
    {
        // The admin client is written from these until the BFF publishes a document (PP2), so a status the route can
        // answer and does not declare is a status the client will not expect (PP1.12): the browser boundary's 400 and
        // 403 on a write, the 409 the API relays, and 502 when the API cannot be asked.
        await using var gateway = new GatewayFactory("Development");

        var endpoint = gateway.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(e => "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/') == SessionEndpoints.Base + path + (path.Length == 0 ? "/" : string.Empty)
                && e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true);

        Assert.Equal(
            statuses,
            endpoint.Metadata.OfType<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>()
                .Select(m => m.StatusCode)
                .Distinct()
                .Order());
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
