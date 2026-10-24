using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.ApiService.Hosting;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Managers.Results;

namespace PantryPress.Tests.Api;

// PP1.9: no route in ApiService opts into Idempotency-Key yet (the account routes either have no caller or answer with
// secrets), so these stand-in controllers do, and are added only to the hosts that ask for them. Every action counts
// its runs in the ledger by the name in its body, so tests sharing one database never see each other's runs.

public sealed record ProbeItem(string Name);

public sealed record ProbeCreated(string Name, int Run);

/// <summary>How often each probe ran, and a gate a test can close to hold a run open mid-request.</summary>
public sealed class IdempotencyProbeLedger
{
    private readonly ConcurrentDictionary<string, int> _runs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Hold> _holds = new(StringComparer.Ordinal);

    public int Runs(string name) => _runs.TryGetValue(name, out var runs) ? runs : 0;

    public int Run(string name) => _runs.AddOrUpdate(name, 1, (_, runs) => runs + 1);

    /// <summary>Holds the next run of <paramref name="name"/> inside its action until the hold is released.</summary>
    public Hold HoldNext(string name)
    {
        var hold = new Hold();
        Assert.True(_holds.TryAdd(name, hold));
        return hold;
    }

    public async Task WaitIfHeldAsync(string name)
    {
        if (_holds.TryRemove(name, out var hold))
        {
            hold.Entered.TrySetResult();
            await hold.Released.Task;
        }
    }

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _leaving = new(StringComparer.Ordinal);

    /// <summary>The caller of the request marked with <paramref name="name"/> goes away (see <see cref="IdempotencyProbes"/>).</summary>
    public void Leave(string name)
    {
        Assert.True(_leaving.TryRemove(name, out var gone));
        gone.Cancel();
    }

    internal CancellationToken WillLeave(string name, CancellationTokenSource gone)
    {
        Assert.True(_leaving.TryAdd(name, gone));
        return gone.Token;
    }

    public sealed class Hold
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => Released.TrySetResult();
    }
}

[Route(ApiSurfaces.AdminV1Route + "/idempotency-probe")]
[Authorize]
[ProducesResponseType<ValidationProblemDetails>(400, ApiProblems.ContentType)] // a controller's own 400 outranks the key's
public sealed class IdempotencyProbeController(IdempotencyProbeLedger ledger) : ApiControllerBase
{
    public const string Path = "/" + ApiSurfaces.AdminV1Route + "/idempotency-probe";

    [HttpPost("items")]
    [Idempotent]
    [ProducesResponseType<ProbeCreated>(201)]
    public async Task<IActionResult> Create(ProbeItem item)
    {
        var run = ledger.Run(item.Name);
        await ledger.WaitIfHeldAsync(item.Name);
        return Created($"{Path}/items/{item.Name}-{run}", new ProbeCreated(item.Name, run));
    }

    [HttpPost("other-items")]
    [Idempotent]
    public IActionResult CreateOther(ProbeItem item) => Ok(new ProbeCreated(item.Name, ledger.Run(item.Name)));

    [HttpPost("required")]
    [Idempotent(Required = true)]
    public IActionResult Required(ProbeItem item)
    {
        ledger.Run(item.Name);
        return NoContent();
    }

    [HttpPost("fails")]
    [Idempotent]
    public IActionResult Fails(ProbeItem item)
    {
        ledger.Run(item.Name);
        throw new InvalidOperationException("The probe failed, as asked.");
    }

    [HttpPost("refuses")]
    [Idempotent]
    public IActionResult Refuses(ProbeItem item)
    {
        ledger.Run(item.Name);
        return Failure(Error.Validation(new Dictionary<string, string[]> { ["name"] = ["Pick another name."] }));
    }

    [HttpPost("plain")]
    public IActionResult Plain(ProbeItem item) => Ok(new ProbeCreated(item.Name, ledger.Run(item.Name)));

    [HttpPost("session")]
    [Idempotent]
    public IActionResult Session(ProbeItem item)
    {
        var run = ledger.Run(item.Name);
        Response.Headers.SetCookie = "probe=1; Path=/; Secure; HttpOnly";
        Response.Headers[GatewaySession.NewStampHeader] = "ROTATED";
        return Ok(new ProbeCreated(item.Name, run));
    }

    [HttpPost("large")]
    [Idempotent]
    public IActionResult Large(ProbeItem item)
    {
        ledger.Run(item.Name);
        return File(new byte[ApiIdempotency.MaxStoredBodyBytes + 1], "application/octet-stream");
    }
}

// Misplaced [Idempotent], each of which must stop the host from starting.
[Route(ApiSurfaces.AdminV1Route + "/idempotency-misuse/read")]
[Authorize]
public sealed class IdempotentReadController : ApiControllerBase
{
    [HttpGet]
    [Idempotent]
    public IActionResult Read() => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/idempotency-misuse/anonymous")]
[Authorize]
public sealed class IdempotentAnonymousController : ApiControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [Idempotent]
    public IActionResult Write() => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/idempotency-misuse/unguarded")]
public sealed class IdempotentUnguardedController : ApiControllerBase
{
    [HttpPost]
    [Idempotent]
    public IActionResult Write() => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/idempotency-misuse/secret")]
[Authorize]
public sealed class IdempotentSecretController : ApiControllerBase
{
    [HttpPost]
    [Idempotent]
    [AnswersWithSecret]
    public IActionResult Write() => NoContent();
}

[Route(ApiSurfaces.AdminV1Route + "/idempotency-misuse/session")]
[Authorize]
public sealed class IdempotentSessionController : ApiControllerBase
{
    [HttpPost]
    [Idempotent]
    [RefreshesSession]
    public IActionResult Write() => NoContent();
}

[Route(ApiSurfaces.InternalV1Route + "/idempotency-misuse")]
[GatewayOnly]
[Authorize]
public sealed class IdempotentInternalController : ApiControllerBase
{
    [HttpPost]
    [Idempotent]
    public IActionResult Write() => NoContent();
}

internal static class IdempotencyProbes
{
    /// <summary>
    /// Marks a request whose caller will go away mid-request when the test says so. The in-memory test server does not
    /// surface a cancelled client the way Kestrel does, so this does it: the request's abort token fires, and from then
    /// on the response stream refuses writes, as a closed connection's does.
    /// </summary>
    public const string LeavesHeader = "X-Test-Caller-Leaves";

    /// <summary>Adds exactly <paramref name="controllers"/> to the host, and the ledger they count in.</summary>
    public static void AddProbes(this IServiceCollection services, IdempotencyProbeLedger ledger, params Type[] controllers)
    {
        services.AddSingleton(ledger);
        services.AddMvcCore().ConfigureApplicationPartManager(
            manager => manager.FeatureProviders.Add(new Only(controllers)));
        services.AddSingleton<IStartupFilter>(new CallerLeaves(ledger));
    }

    private sealed class CallerLeaves(IdempotencyProbeLedger ledger) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                if (context.Request.Headers[LeavesHeader] is not [{ } name])
                {
                    await following(context);
                    return;
                }

                using var gone = new CancellationTokenSource();
                var token = ledger.WillLeave(name, gone);
                context.RequestAborted = token;
                context.Response.Body = new ClosedWhenGone(context.Response.Body, token);
                await following(context);
            });
            next(app);
        };
    }

    private sealed class ClosedWhenGone(Stream inner, CancellationToken gone) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Refuse();
            inner.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Refuse();
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
            Refuse();
            inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            Refuse();
            return inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private void Refuse()
        {
            if (gone.IsCancellationRequested)
            {
                throw new IOException("The caller has gone.");
            }
        }
    }

    private sealed class Only(Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in controllers)
            {
                feature.Controllers.Add(controller.GetTypeInfo());
            }
        }
    }
}

internal static class IdempotencyHttp
{
    public static HttpRequestMessage Post(string route, string name, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{IdempotencyProbeController.Path}/{route}")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new ProbeItem(name)),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(ApiIdempotency.KeyHeader, key);
        }

        return request;
    }

    public static bool IsReplayed(this HttpResponseMessage response) =>
        response.Headers.TryGetValues(ApiIdempotency.ReplayedHeader, out var values) && values.Single() == "true";

    public static string NewKey() => $"key-{Guid.NewGuid():N}";

    public static string NewName() => $"probe-{Guid.NewGuid():N}";

    public static string? Header(this HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(",", values)
            : null;
}
