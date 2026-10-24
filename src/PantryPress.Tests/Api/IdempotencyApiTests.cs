using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using PantryPress.ApiService.Hosting;
using PantryPress.Domain.Managers.Email;
using PantryPress.MigrationService;
using PantryPress.Tests.Fakes;
using PantryPress.Tests.Persistence;
using static PantryPress.Tests.Api.IdempotencyHttp;

namespace PantryPress.Tests.Api;

// PP1.9: Idempotency-Key end to end - ApiService in memory against the migrated SQL schema, with probe routes that count
// their runs (API-005: a replay returns the original result). Keys, names, and accounts are fresh per test, so the
// shared database never couples two tests.
[Trait("Category", "Integration")]
[Collection(MigratedDatabase.CollectionName)]
public sealed class IdempotencyApiTests(MigratedDatabase database) : IAsyncLifetime
{
    private readonly IdempotencyProbeLedger _ledger = new();
    private readonly Guid _account = Guid.NewGuid();
    private ApiServiceFactory _api = null!;

    private FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        var connectionString = await database.App.GetConnectionStringAsync(MigrationServiceExtensions.ConnectionName);
        _api = new ApiServiceFactory(
            "Testing",
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:pantrypress"] = connectionString,
                ["Accounts:AppBaseUrl"] = "https://app.localhost",
                ["Gateway:SharedSecret"] = TestSecrets.GatewaySecret,
            },
            services =>
            {
                services.AddSingleton<IEmailGateway>(new RecordingEmailGateway());
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time));
                services.Replace(ServiceDescriptor.Singleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()));
                services.AddProbes(_ledger, typeof(IdempotencyProbeController));
            });
        _ = _api.Server;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task A_retry_with_the_same_key_gets_the_first_answer_without_running_again()
    {
        using var client = _api.CreateClientFor(_account);
        var (name, key) = (NewName(), NewKey());

        using var first = await client.SendAsync(Post("items", name, key));
        using var retry = await client.SendAsync(Post("items", name, key));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.False(first.IsReplayed());
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.True(retry.IsReplayed());
        Assert.Equal(first.Headers.Location, retry.Headers.Location);
        Assert.Equal(first.Content.Headers.ContentType, retry.Content.Headers.ContentType);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        Assert.Equal(1, _ledger.Runs(name));

        // The replay is still private to the caller, like every admin answer.
        Assert.Equal("no-store", retry.Header("Cache-Control"));
    }

    [Fact]
    public async Task A_caller_that_goes_away_mid_request_gets_the_whole_answer_on_its_retry()
    {
        // The case the key exists for: the write happened, the answer was lost on the way back, and the client retries.
        using var client = _api.CreateClientFor(_account);
        var (name, key) = (NewName(), NewKey());
        var hold = _ledger.HoldNext(name);
        var request = Post("items", name, key);
        request.Headers.Add(IdempotencyProbes.LeavesHeader, name);
        var lost = client.SendAsync(request);
        await hold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        _ledger.Leave(name);
        hold.Release();

        // Whatever reached a caller who had gone is beside the point; nobody was there to read it.
        (await lost.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();

        // The work carries on without its caller; the retry waits for it, then gets its answer.
        var retry = await RetryWhileInProgressAsync(client, () => Post("items", name, key));
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.True(retry.IsReplayed());
        Assert.Equal(new ProbeCreated(name, 1), await retry.Content.ReadFromJsonAsync<ProbeCreated>());
        Assert.Equal($"{IdempotencyProbeController.Path}/items/{name}-1", retry.Headers.Location?.OriginalString);
        Assert.Equal(1, _ledger.Runs(name));
        retry.Dispose();
    }

    [Fact]
    public async Task A_replay_still_needs_a_signed_in_caller()
    {
        using var cook = _api.CreateClientFor(_account);
        using var anonymous = _api.CreateClientFor(null);
        var (name, key) = (NewName(), NewKey());
        using var first = await cook.SendAsync(Post("items", name, key));

        using var retry = await anonymous.SendAsync(Post("items", name, key));

        Assert.Equal(HttpStatusCode.Unauthorized, retry.StatusCode);
        Assert.False(retry.IsReplayed());
        Assert.Equal(1, _ledger.Runs(name));
    }

    [Fact]
    public async Task A_key_sent_with_a_different_body_or_path_is_refused_and_nothing_runs()
    {
        using var client = _api.CreateClientFor(_account);
        var (name, key) = (NewName(), NewKey());
        using var first = await client.SendAsync(Post("items", name, key));

        using var otherBody = await client.SendAsync(Post("items", NewName(), key));
        using var otherPath = await client.SendAsync(Post("other-items", name, key));

        foreach (var refused in new[] { otherBody, otherPath })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Equal(ApiIdempotency.ReusedCode, await refused.CodeAsync());
            Assert.Equal(ApiIdempotency.ReusedTitle, await TitleAsync(refused));
        }

        Assert.Equal(1, _ledger.Runs(name));
    }

    [Fact]
    public async Task Retries_while_the_first_request_is_still_running_are_told_to_wait_and_none_of_them_runs()
    {
        using var client = _api.CreateClientFor(_account);
        var (name, key) = (NewName(), NewKey());
        var hold = _ledger.HoldNext(name);

        var first = client.SendAsync(Post("items", name, key));
        await hold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var retries = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.SendAsync(Post("items", name, key))));
        foreach (var retry in retries)
        {
            Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
            Assert.Equal("1", retry.Header("Retry-After"));
            Assert.Equal(ApiIdempotency.InUseCode, await retry.CodeAsync());
            Assert.Equal(ApiIdempotency.InUseTitle, await TitleAsync(retry));
            retry.Dispose();
        }

        hold.Release();
        using var finished = await first;
        Assert.Equal(HttpStatusCode.Created, finished.StatusCode);

        using var after = await client.SendAsync(Post("items", name, key));
        Assert.True(after.IsReplayed());
        Assert.Equal(1, _ledger.Runs(name));
    }

    [Fact]
    public async Task Concurrent_first_requests_with_one_key_run_exactly_once()
    {
        using var client = _api.CreateClientFor(_account);
        var (name, key) = (NewName(), NewKey());

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.SendAsync(Post("items", name, key))));

        Assert.Equal(1, _ledger.Runs(name));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created && !r.IsReplayed());
        Assert.All(responses, r => Assert.True(
            r.StatusCode == HttpStatusCode.Conflict || (r.StatusCode == HttpStatusCode.Created),
            $"Unexpected {(int)r.StatusCode}."));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task One_callers_key_is_not_another_callers_key()
    {
        using var cook = _api.CreateClientFor(_account);
        using var other = _api.CreateClientFor(Guid.NewGuid());
        var (name, key) = (NewName(), NewKey());

        using var first = await cook.SendAsync(Post("items", name, key));
        using var second = await other.SendAsync(Post("items", name, key));

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(second.IsReplayed());
        Assert.Equal(2, (await second.Content.ReadFromJsonAsync<ProbeCreated>())!.Run);
        Assert.NotEqual(first.Headers.Location, second.Headers.Location);
        Assert.Equal(2, _ledger.Runs(name));
    }

    [Fact]
    public async Task A_key_is_remembered_for_24_hours_and_then_runs_as_new()
    {
        using var client = _api.CreateClientFor(_account);
        var (name, key) = (NewName(), NewKey());
        using var first = await client.SendAsync(Post("items", name, key));

        Time.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        using var stillRemembered = await client.SendAsync(Post("items", name, key));
        Time.Advance(TimeSpan.FromSeconds(1));
        using var forgotten = await client.SendAsync(Post("items", name, key));

        Assert.True(stillRemembered.IsReplayed());
        Assert.False(forgotten.IsReplayed());
        Assert.Equal(HttpStatusCode.Created, forgotten.StatusCode);
        Assert.Equal(2, _ledger.Runs(name));
    }

    [Fact]
    public async Task An_abandoned_claim_is_taken_over_after_its_lease_and_the_late_finisher_changes_nothing()
    {
        using var client = _api.CreateClientFor(_account);
        var (name, key) = (NewName(), NewKey());
        var hold = _ledger.HoldNext(name);
        var stuck = client.SendAsync(Post("items", name, key));
        await hold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Five minutes on, the first request looks lost to a crash: a retry takes the key over and runs.
        Time.Advance(TimeSpan.FromMinutes(5));
        using var takeover = await client.SendAsync(Post("items", name, key));
        Assert.Equal(HttpStatusCode.Created, takeover.StatusCode);
        Assert.False(takeover.IsReplayed());
        var replacement = await takeover.Content.ReadFromJsonAsync<ProbeCreated>();

        // The first one finishes after all. Its claim was replaced, so it stores nothing.
        hold.Release();
        using var late = await stuck;
        Assert.Equal(HttpStatusCode.Created, late.StatusCode);

        using var replay = await client.SendAsync(Post("items", name, key));
        Assert.True(replay.IsReplayed());
        Assert.Equal(replacement, await replay.Content.ReadFromJsonAsync<ProbeCreated>());
        Assert.Equal(2, _ledger.Runs(name));
    }

    [Fact]
    public async Task An_unexpected_failure_gives_the_key_back_so_a_retry_runs()
    {
        using var client = _api.CreateClientFor(_account);
        var (name, key) = (NewName(), NewKey());

        using var first = await client.SendAsync(Post("fails", name, key));
        using var retry = await client.SendAsync(Post("fails", name, key));

        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, retry.StatusCode);
        Assert.False(retry.IsReplayed());
        Assert.Equal(2, _ledger.Runs(name));
    }

    [Fact]
    public async Task A_refusal_is_the_requests_answer_and_is_replayed()
    {
        using var client = _api.CreateClientFor(_account);
        var (name, key) = (NewName(), NewKey());

        using var first = await client.SendAsync(Post("refuses", name, key));
        using var retry = await client.SendAsync(Post("refuses", name, key));

        Assert.Equal(HttpStatusCode.BadRequest, retry.StatusCode);
        Assert.True(retry.IsReplayed());
        Assert.Equal("validation-failed", await retry.CodeAsync());
        Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        Assert.Equal(1, _ledger.Runs(name));
    }

    [Fact]
    public async Task Without_a_key_an_opted_in_route_runs_every_time()
    {
        using var client = _api.CreateClientFor(_account);
        var name = NewName();

        using var first = await client.SendAsync(Post("items", name));
        using var second = await client.SendAsync(Post("items", name));

        Assert.False(second.IsReplayed());
        Assert.Equal(2, _ledger.Runs(name));
    }

    [Fact]
    public async Task A_route_that_did_not_opt_in_ignores_the_key()
    {
        using var client = _api.CreateClientFor(_account);
        var (name, key) = (NewName(), NewKey());

        using var first = await client.SendAsync(Post("plain", name, key));
        using var second = await client.SendAsync(Post("plain", name, key));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(second.IsReplayed());
        Assert.Equal(2, _ledger.Runs(name));
    }

    [Fact]
    public async Task A_route_that_requires_a_key_refuses_a_request_without_one()
    {
        using var client = _api.CreateClientFor(_account);
        var name = NewName();

        using var without = await client.SendAsync(Post("required", name));
        using var with = await client.SendAsync(Post("required", name, NewKey()));

        Assert.Equal(HttpStatusCode.BadRequest, without.StatusCode);
        Assert.Equal(ApiIdempotency.RequiredCode, await without.CodeAsync());
        Assert.Equal(ApiIdempotency.RequiredTitle, await TitleAsync(without));
        Assert.Equal(HttpStatusCode.NoContent, with.StatusCode);
        Assert.Equal(1, _ledger.Runs(name));
    }

    [Fact]
    public async Task A_malformed_key_is_refused_before_anything_runs()
    {
        using var client = _api.CreateClientFor(_account);
        var name = NewName();

        using var tooLong = await client.SendAsync(Post("items", name, new string('k', 129)));
        using var twice = Post("items", name, NewKey());
        twice.Headers.TryAddWithoutValidation(ApiIdempotency.KeyHeader, NewKey());
        using var twoKeys = await client.SendAsync(twice);

        foreach (var refused in new[] { tooLong, twoKeys })
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal(ApiIdempotency.InvalidCode, await refused.CodeAsync());
            Assert.Equal(ApiIdempotency.InvalidTitle, await TitleAsync(refused));
        }

        Assert.Equal(0, _ledger.Runs(name));
    }

    [Fact]
    public async Task A_quoted_key_is_the_same_key_and_keys_differ_by_case()
    {
        using var client = _api.CreateClientFor(_account);
        var name = NewName();
        var key = $"Key-{Guid.NewGuid():N}";

        using var bare = await client.SendAsync(Post("items", name, key));
        using var quoted = await client.SendAsync(Post("items", name, $"\"{key}\""));
        using var lower = await client.SendAsync(Post("items", name, key.ToLowerInvariant()));

        Assert.True(quoted.IsReplayed());
        Assert.False(lower.IsReplayed());
        Assert.Equal(2, _ledger.Runs(name));
    }

    [Fact]
    public async Task A_replay_never_carries_a_cookie_or_the_gateways_session_headers()
    {
        using var client = _api.CreateClientFor(_account);
        var (name, key) = (NewName(), NewKey());

        using var first = await client.SendAsync(Post("session", name, key));
        using var replay = await client.SendAsync(Post("session", name, key));

        Assert.NotNull(first.Header("Set-Cookie"));
        Assert.NotNull(first.Header(GatewaySession.NewStampHeader));
        Assert.True(replay.IsReplayed());
        Assert.Null(replay.Header("Set-Cookie"));
        Assert.Null(replay.Header(GatewaySession.NewStampHeader));
        Assert.Equal(1, _ledger.Runs(name));
    }

    [Fact]
    public async Task An_answer_too_large_to_store_is_delivered_whole_and_a_retry_runs_again()
    {
        using var client = _api.CreateClientFor(_account);
        var (name, key) = (NewName(), NewKey());

        using var first = await client.SendAsync(Post("large", name, key));
        using var retry = await client.SendAsync(Post("large", name, key));

        Assert.Equal(ApiIdempotency.MaxStoredBodyBytes + 1, (await first.Content.ReadAsByteArrayAsync()).Length);
        Assert.False(retry.IsReplayed());
        Assert.Equal(2, _ledger.Runs(name));
    }

    private static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("title").GetString();
    }

    private static async Task<HttpResponseMessage> RetryWhileInProgressAsync(HttpClient client, Func<HttpRequestMessage> request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var response = await client.SendAsync(request(), timeout.Token);
            if (response.StatusCode != HttpStatusCode.Conflict)
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
        }
    }
}
