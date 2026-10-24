using System.Text.Json;
using PantryPress.ApiService.Hosting;

namespace PantryPress.Tests.Api;

// PP1.9: [Idempotent] goes only on a write with a signed-in caller, or the host refuses to start; and every opted-in
// route documents the key and the answers it adds.
public sealed class IdempotencyConventionTests
{
    [Theory]
    [InlineData(typeof(IdempotentReadController), "Only POST, PUT, PATCH, and DELETE")]
    [InlineData(typeof(IdempotentAnonymousController), "without requiring a signed-in caller")]
    [InlineData(typeof(IdempotentUnguardedController), "without requiring a signed-in caller")]
    [InlineData(typeof(IdempotentSecretController), "show it twice")]
    [InlineData(typeof(IdempotentSessionController), "make the facade idempotent instead")]
    [InlineData(typeof(IdempotentInternalController), "internal surface")]
    public async Task A_misplaced_idempotent_route_fails_startup(Type controller, string reason)
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            configureServices: services => services.AddProbes(new IdempotencyProbeLedger(), controller));

        var error = Assert.ThrowsAny<Exception>(() => api.Server);

        var refused = Assert.IsType<InvalidOperationException>(Unwrap(error));
        Assert.Contains(controller.Name, refused.Message, StringComparison.Ordinal);
        Assert.Contains(reason, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_opted_in_route_documents_the_key_and_the_answers_it_adds()
    {
        await using var api = ApiServiceFactory.WithoutDatabase(
            "Development",
            new Dictionary<string, string?> { ["DevelopmentMail:Host"] = "localhost", ["DevelopmentMail:Port"] = "1025" },
            services => services.AddProbes(new IdempotencyProbeLedger(), typeof(IdempotencyProbeController)));

        using var response = await api.CreateClient().GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        var items = paths.GetProperty(IdempotencyProbeController.Path + "/items").GetProperty("post");
        var key = Assert.Single(
            items.GetProperty("parameters").EnumerateArray(),
            p => p.GetProperty("name").GetString() == ApiIdempotency.KeyHeader);
        Assert.Equal("header", key.GetProperty("in").GetString());
        Assert.False(key.TryGetProperty("required", out var required) && required.GetBoolean());
        var schema = key.GetProperty("schema");
        Assert.Equal(130, schema.GetProperty("maxLength").GetInt32()); // 128, plus the quotes of the quoted form
        Assert.Equal(ApiOpenApi.KeyPattern, schema.GetProperty("pattern").GetString());
        Assert.Contains("For 24 hours", key.GetProperty("description").GetString(), StringComparison.Ordinal);
        var responses = items.GetProperty("responses");
        Assert.True(responses.GetProperty("201").GetProperty("headers").TryGetProperty(ApiIdempotency.ReplayedHeader, out _));
        Assert.True(responses.GetProperty("409").GetProperty("headers").TryGetProperty("Retry-After", out _));
        Assert.True(responses.TryGetProperty("422", out _));

        // The controller's own 400 (a validation problem) is not replaced by the key's plainer one.
        var badRequest = responses.GetProperty("400").GetRawText();
        Assert.Contains("ValidationProblemDetails", badRequest, StringComparison.Ordinal);

        var requiredRoute = paths.GetProperty(IdempotencyProbeController.Path + "/required").GetProperty("post");
        Assert.True(Assert.Single(
            requiredRoute.GetProperty("parameters").EnumerateArray(),
            p => p.GetProperty("name").GetString() == ApiIdempotency.KeyHeader).GetProperty("required").GetBoolean());

        // A route that did not opt in says nothing about a key.
        var plain = paths.GetProperty(IdempotencyProbeController.Path + "/plain").GetProperty("post");
        Assert.False(plain.TryGetProperty("parameters", out var parameters)
            && parameters.EnumerateArray().Any(p => p.GetProperty("name").GetString() == ApiIdempotency.KeyHeader));
        Assert.False(plain.GetProperty("responses").TryGetProperty("422", out _));
    }

    [Fact]
    public async Task No_route_in_this_service_opts_in_yet()
    {
        // The account routes either have no caller to scope a key to or answer with secrets that must not be stored
        // (IdempotentAttribute). The first real opt-ins are PP2's site writes and PP18's publishing API; until then a
        // key sent to this service is accepted and ignored, which this pins so the change is deliberate.
        await using var api = ApiServiceFactory.WithoutDatabase(
            "Development",
            new Dictionary<string, string?> { ["DevelopmentMail:Host"] = "localhost", ["DevelopmentMail:Port"] = "1025" });

        using var response = await api.CreateClient().GetAsync("/openapi/v1.json");

        Assert.DoesNotContain(ApiIdempotency.KeyHeader, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static Exception Unwrap(Exception error)
    {
        while (error is AggregateException or System.Reflection.TargetInvocationException && error.InnerException is not null)
        {
            error = error.InnerException!;
        }

        return error;
    }
}
