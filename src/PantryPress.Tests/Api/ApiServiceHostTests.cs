using System.Net;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Modules.Accounts.Contracts;
using static PantryPress.Tests.Api.RegistrationApiTests;

namespace PantryPress.Tests.Api;

// PP1.2b: the ApiService foundation (ARCH-002, api-contract.md). Nothing here reaches the database: every request is
// refused or answered before a facade would read an account.
public sealed class ApiServiceHostTests
{
    [Fact]
    public async Task Unknown_routes_get_a_problem_with_a_code_and_no_caching()
    {
        await using var api = ApiServiceFactory.WithoutDatabase();

        using var response = await api.CreateClient().GetAsync("/api/v1/nothing-here");

        Assert.True(response.Headers.CacheControl?.NoStore);
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task Malformed_json_gets_a_plain_validation_problem_without_parser_details()
    {
        await using var api = ApiServiceFactory.WithoutDatabase();
        using var content = new StringContent("""{"accountId":"not-a-guid","token":"x"}""", Encoding.UTF8, "application/json");

        using var problem = await AssertProblemAsync(
            await api.CreateClient().PostAsync("/api/v1/account/email/verify", content),
            HttpStatusCode.BadRequest,
            "validation-failed");

        var raw = problem.RootElement.GetRawText();
        Assert.Equal("This isn't in the format we expected.", problem.RootElement.GetProperty("errors").GetProperty("accountId")[0].GetString());
        Assert.DoesNotContain("System.", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("LineNumber", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Facade_validation_errors_are_problems_keyed_by_json_field()
    {
        await using var api = ApiServiceFactory.WithoutDatabase();

        using var problem = await AssertProblemAsync(
            await api.CreateClient().PostAsJsonAsync("/api/v1/account/register", new RegisterAccountRequest("not an email", "")),
            HttpStatusCode.BadRequest,
            "validation-failed");

        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal("Enter an email address like name@example.com.", errors.GetProperty("email")[0].GetString());
        Assert.Equal("Choose a password.", errors.GetProperty("password")[0].GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Signed_in_routes_answer_401_until_the_session_arrives_in_PP1_6()
    {
        await using var api = ApiServiceFactory.WithoutDatabase();

        await AssertProblemAsync(
            await api.CreateClient().GetAsync("/api/v1/account/two-factor"),
            HttpStatusCode.Unauthorized,
            "sign-in-required");
    }

    [Fact]
    public async Task Unhandled_exceptions_become_a_generic_500_problem()
    {
        // A signed-in call reaches the facade, which opens the unreachable database and throws.
        await using var api = ApiServiceFactory.WithoutDatabase();

        using var problem = await AssertProblemAsync(
            await api.CreateClientFor(Guid.NewGuid()).GetAsync("/api/v1/account/two-factor"),
            HttpStatusCode.InternalServerError,
            "unexpected-error");

        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        Assert.False(problem.RootElement.TryGetProperty("exception", out _));
    }

    [Fact]
    public async Task Anonymous_account_routes_share_one_bucket_when_no_address_is_known()
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            extraSettings: new Dictionary<string, string?> { ["RateLimits:AnonymousAccountPerIpPerMinute"] = "2" });
        var client = api.CreateClient();
        var invalid = new ForgotPasswordRequest("not an email");

        using (var first = await client.PostAsJsonAsync("/api/v1/account/password/forgot", invalid))
        using (var second = await client.PostAsJsonAsync("/api/v1/account/register", new RegisterAccountRequest("x", "y")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync("/api/v1/account/password/forgot", invalid);
        Assert.NotNull(limited.Headers.RetryAfter);
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "too-many-requests");
    }

    [Fact]
    public async Task Development_publishes_an_OpenAPI_document_with_the_account_routes()
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            "Development",
            new Dictionary<string, string?> { ["DevelopmentMail:Host"] = "localhost", ["DevelopmentMail:Port"] = "1025" });

        using var response = await api.CreateClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(
            [
                "/api/v1/account/email/resend",
                "/api/v1/account/email/verify",
                "/api/v1/account/password/change",
                "/api/v1/account/password/forgot",
                "/api/v1/account/password/reset",
                "/api/v1/account/register",
                "/api/v1/account/two-factor",
                "/api/v1/account/two-factor/disable",
                "/api/v1/account/two-factor/enable",
                "/api/v1/account/two-factor/recovery-codes",
                "/api/v1/account/two-factor/setup",
            ],
            paths.Order(StringComparer.Ordinal));

        // Every failure is declared as a problem with a code, and each route lists what its facade can return.
        var operations = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(op => (Route: $"{op.Name.ToUpperInvariant()} {path.Name}", Op: op.Value)))
            .ToDictionary(o => o.Route, o => o.Op);
        foreach (var (route, operation) in operations)
        {
            foreach (var failure in operation.GetProperty("responses").EnumerateObject().Where(r => r.Name[0] is '4' or '5'))
            {
                Assert.True(
                    failure.Value.GetProperty("content").TryGetProperty("application/problem+json", out _),
                    $"{route} {failure.Name} is not declared as a problem.");
            }
        }

        // 413, 429, and 500 are the three any route can answer, declared globally (ApiHttp): a body over the cap
        // since PP1.8, a rate limit, and a fault. The admin app reaches these routes only through the gateway, so the
        // gateway's own answers are declared too (PP1.12): 401 for a half-finished or ended session, 400 and 403 from
        // its antiforgery and same-origin checks on a write, and 502 and 504 when the service cannot be reached.
        Assert.Equal(["202", "400", "401", "403", "413", "415", "429", "500", "502", "504"], Statuses(operations["POST /api/v1/account/register"]));
        Assert.Equal(["204", "400", "401", "403", "404", "409", "413", "415", "429", "500", "502", "504"], Statuses(operations["POST /api/v1/account/password/change"]));
        Assert.Equal(["200", "400", "401", "403", "404", "409", "413", "415", "429", "500", "502", "504"], Statuses(operations["POST /api/v1/account/two-factor/enable"]));
        Assert.Equal(["204", "400", "401", "403", "404", "409", "413", "415", "429", "500", "502", "504"], Statuses(operations["POST /api/v1/account/two-factor/disable"]));
        Assert.Equal(["200", "401", "404", "413", "429", "500", "502", "504"], Statuses(operations["GET /api/v1/account/two-factor"]));

        // A status the route declares keeps its own meaning and gains the gateway's; the 401 says what it can mean on
        // that route, signed in or not.
        string Description(string route, string status) =>
            operations[route].GetProperty("responses").GetProperty(status).GetProperty("description").GetString() ?? string.Empty;
        Assert.Contains("antiforgery-failed", Description("POST /api/v1/account/register", "400"), StringComparison.Ordinal);
        Assert.Contains("cross-origin-blocked", Description("POST /api/v1/account/two-factor/enable", "403"), StringComparison.Ordinal);
        Assert.Contains("session-ended", Description("POST /api/v1/account/password/change", "401"), StringComparison.Ordinal);
        Assert.Contains("second-factor-required", Description("POST /api/v1/account/password/change", "401"), StringComparison.Ordinal);
        Assert.DoesNotContain("session-ended", Description("POST /api/v1/account/register", "401"), StringComparison.Ordinal);
        Assert.Contains("second-factor-required", Description("POST /api/v1/account/register", "401"), StringComparison.Ordinal);

        // What a retry after a lost answer gets, on the writes that cannot simply be repeated (PP1.12).
        foreach (var route in new[] { "POST /api/v1/account/password/reset", "POST /api/v1/account/password/change", "POST /api/v1/account/two-factor/enable", "POST /api/v1/account/two-factor/disable" })
        {
            Assert.Contains("retry", operations[route].GetProperty("description").GetString(), StringComparison.Ordinal);
        }

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "ProblemDetails", "ValidationProblemDetails" })
        {
            Assert.Equal("string", schemas.GetProperty(name).GetProperty("properties").GetProperty("code").GetProperty("type").GetString());
            Assert.Contains("code", schemas.GetProperty(name).GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        }

        // A declared 400 is a validation problem or a plain refusal, so the field messages are optional (PP1.12).
        Assert.DoesNotContain(
            "errors",
            schemas.GetProperty("ValidationProblemDetails").GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Framework_refusals_get_plain_titles()
    {
        await using var api = ApiServiceFactory.WithoutDatabase();
        using var content = new StringContent("email=cook@example.com", Encoding.UTF8, "text/plain");

        using var problem = await AssertProblemAsync(
            await api.CreateClient().PostAsync("/api/v1/account/register", content),
            HttpStatusCode.UnsupportedMediaType,
            "unsupported-media-type");

        Assert.Equal("Send the details as JSON.", problem.RootElement.GetProperty("title").GetString());
    }

    private static List<string> Statuses(JsonElement operation) =>
        [.. operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task Outside_Development_the_api_starts_with_no_email_provider_and_no_link_origin()
    {
        // Account email is staged in the outbox and sent by the Worker (PP1.10b): this host sends none itself, so the
        // refusals PP1.2 gave it - no provider, no link origin - are the Worker's now (AccountEmailDeliveryTests).
        await using var api = ApiServiceFactory.WithoutDatabase(
            "Production",
            extraSettings: new Dictionary<string, string?> { ["Accounts:AppBaseUrl"] = null });

        _ = api.Server;

        Assert.Null(api.Services.GetService<PantryPress.Domain.Managers.Email.IEmailGateway>());
    }
}
