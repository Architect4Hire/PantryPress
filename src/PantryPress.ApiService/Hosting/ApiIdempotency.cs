using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Managers.Idempotency;
using PantryPress.ServiceDefaults.EdgeHardening;

namespace PantryPress.ApiService.Hosting;

/// <summary>
/// <c>Idempotency-Key</c> for the writes that opt in with <see cref="IdempotentAttribute"/> (API-005, POST-005;
/// PP1.9): the first request with a key runs and its answer is stored; a retry from the same caller with the same key
/// and the same request gets that answer back, marked <c>Idempotent-Replayed: true</c>, without running again.
/// </summary>
internal static partial class ApiIdempotency
{
    public const string KeyHeader = "Idempotency-Key";

    /// <summary>On a replayed answer, so a client can tell a retry's answer from a first one.</summary>
    public const string ReplayedHeader = "Idempotent-Replayed";

    public const string InvalidCode = "idempotency-key-invalid";
    public const string InvalidTitle = "The Idempotency-Key header isn't in the format we expected.";
    public const string RequiredCode = "idempotency-key-required";
    public const string RequiredTitle = "This request needs an Idempotency-Key header.";
    public const string InUseCode = "idempotency-key-in-use";
    public const string InUseTitle = "We're still working on your earlier request. Please try again in a moment.";
    public const string ReusedCode = "idempotency-key-reused";
    public const string ReusedTitle = "This was sent as a retry of a different request, so nothing was changed.";

    /// <summary>The largest answer that is stored; an opted-in route answers small JSON (see <see cref="IdempotentAttribute"/>).</summary>
    public const int MaxStoredBodyBytes = (int)RequestLimits.AdminDefault;

    /// <summary>
    /// The headers a replay carries. An allow-list, so a cookie, the gateway's session headers, or anything a later
    /// route adds is never stored or replayed: a replay must not re-issue a session (PP1.6).
    /// </summary>
    public static readonly string[] ReplayedHeaders =
    [
        HeaderNames.ContentType,
        HeaderNames.Location,
        HeaderNames.ETag,
        HeaderNames.ContentLanguage,
    ];

    public static IServiceCollection AddApiIdempotency(this IServiceCollection services, IConfiguration configuration) =>
        services.AddIdempotency(configuration);

    /// <summary>After authentication, authorization, and the rate limiter: a key belongs to a caller who was let in.</summary>
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app) =>
        app.UseMiddleware<IdempotencyMiddleware>();

    /// <summary>
    /// Reads the key: one header value of 1 to 128 visible ASCII characters, bare or as a structured-field string
    /// (<c>"…"</c>), which is how the IETF draft spells it. A quote, a backslash, or a comma is never part of a key:
    /// the first two would need escaping in the quoted form, and a comma is how a proxy joins two header values.
    /// </summary>
    public static bool TryParseKey(StringValues values, out string key)
    {
        key = string.Empty;
        if (values is not [{ } value])
        {
            return false;
        }

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        if (value.Length is 0 or > IdempotencyRequest.MaxKeyLength
            || value.Any(c => c is < '!' or > '~' or '"' or '\\' or ','))
        {
            return false;
        }

        key = value;
        return true;
    }

    /// <summary>Whose keys these are. An account today; PP18 adds the publishing key as a caller of its own.</summary>
    public static bool TryGetCallerScope(ClaimsPrincipal user, out string scope)
    {
        scope = user.TryGetAccountId(out var account) ? $"account:{account.Value:D}" : string.Empty;
        return scope.Length > 0;
    }

    /// <summary>
    /// What was asked: the method, the path and query exactly as sent, and the body. A key sent again with any of them
    /// different is a different request, not a retry. The caller and the key go in first, so the stored hash of a body
    /// is never a bare hash of that body.
    /// </summary>
    public static async Task<byte[]> FingerprintAsync(
        HttpRequest request,
        string caller,
        string key,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(
            $"{caller}\n{key}\n{request.Method.ToUpperInvariant()}\n"
            + $"{request.PathBase.ToUriComponent()}{request.Path.ToUriComponent()}{request.QueryString.ToUriComponent()}\n"));

        // Buffered so the action can still read it; the body was already capped at 1 MiB (RequestLimits).
        request.EnableBuffering();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        request.Body.Position = 0;
        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Whether an answer is the one a retry should get. Not a server failure, a rate limit, a timeout, or a 401: a retry
    /// of any of those might go differently, so the key is given back and the retry runs. Everything else, a refusal
    /// included, is the request's answer.
    /// </summary>
    public static bool IsStorable(int status) =>
        status is < StatusCodes.Status500InternalServerError
            and not StatusCodes.Status401Unauthorized
            and not StatusCodes.Status408RequestTimeout
            and not StatusCodes.Status429TooManyRequests;

    public static Task WriteProblemAsync(HttpContext context, int status, string code, string title) =>
        Results.Problem(
            statusCode: status,
            title: title,
            extensions: new Dictionary<string, object?> { [ApiProblems.CodeKey] = code })
            .ExecuteAsync(context);

    private sealed partial class IdempotencyMiddleware(
        RequestDelegate next,
        IIdempotencyStore store,
        ILogger<IdempotencyMiddleware> logger)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            var idempotent = context.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>();
            var values = context.Request.Headers[KeyHeader];
            if (idempotent is null || (values.Count == 0 && !idempotent.Required))
            {
                await next(context);
                return;
            }

            if (values.Count == 0)
            {
                await WriteProblemAsync(context, StatusCodes.Status400BadRequest, RequiredCode, RequiredTitle);
                return;
            }

            if (!TryParseKey(values, out var key))
            {
                await WriteProblemAsync(context, StatusCodes.Status400BadRequest, InvalidCode, InvalidTitle);
                return;
            }

            // IdempotencyConvention lets [Idempotent] only onto routes that require a signed-in caller, and
            // authorization has run by now, so a missing caller here is a bug rather than a request to refuse.
            if (!TryGetCallerScope(context.User, out var caller))
            {
                throw new InvalidOperationException(
                    $"{context.GetEndpoint()?.DisplayName} reached the idempotency step with no caller to scope its key to.");
            }

            var fingerprint = await FingerprintAsync(context.Request, caller, key, context.RequestAborted);
            var claim = await store.ClaimAsync(new IdempotencyRequest(caller, key, fingerprint), context.RequestAborted);
            switch (claim)
            {
                case IdempotencyClaim.Completed completed:
                    await ReplayAsync(context, completed.Response);
                    return;
                case IdempotencyClaim.InProgress waiting:
                    context.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(waiting.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                    await WriteProblemAsync(context, StatusCodes.Status409Conflict, InUseCode, InUseTitle);
                    return;
                case IdempotencyClaim.Mismatch:
                    await WriteProblemAsync(context, StatusCodes.Status422UnprocessableEntity, ReusedCode, ReusedTitle);
                    return;
                case IdempotencyClaim.Acquired acquired:
                    await RunAsync(context, acquired.Lease);
                    return;
            }
        }

        /// <summary>
        /// Runs the claimed request and records how it ended. The request runs to its end even if the caller goes
        /// away: a lost answer is exactly what a retry with this key is for, so the work is finished, the whole answer
        /// is kept, and the retry gets it. The caller's own stream may fail once it has gone; the copy carries on.
        /// </summary>
        private async Task RunAsync(HttpContext context, IdempotencyLease lease)
        {
            var callerGone = context.RequestAborted;
            var original = context.Response.Body;
            await using var capture = new CapturingStream(original, MaxStoredBodyBytes, callerGone);
            context.Response.Body = capture;
            context.RequestAborted = CancellationToken.None;
            var finished = false;
            try
            {
                await next(context);

                // Bytes a writer advanced but did not flush are still in the pipe over the copy.
                await context.Response.BodyWriter.FlushAsync(CancellationToken.None);
                await capture.FlushAsync(CancellationToken.None);
                finished = true;
            }
            finally
            {
                context.Response.Body = original;
                context.RequestAborted = callerGone;

                // Neither call takes the request's token: a caller that went away still has an answer to replay on its
                // retry, and a claim left behind would answer "still working" until its lease ran out.
                var stored = finished && IsStorable(context.Response.StatusCode) && !capture.Overflowed
                    ? Snapshot(context.Response, capture)
                    : null;
                if (stored is { FitsTheStore: true })
                {
                    await QuietlyAsync(() => store.CompleteAsync(lease, stored, CancellationToken.None));
                }
                else
                {
                    if (finished && (capture.Overflowed || stored is { FitsTheStore: false }))
                    {
                        LogTooLargeToStore(logger, context.GetEndpoint()?.DisplayName, MaxStoredBodyBytes);
                    }

                    await QuietlyAsync(() => store.ReleaseAsync(lease, CancellationToken.None));
                }
            }
        }

        // The caller already has its answer, or the request is already failing: neither may be replaced by a failure to
        // record it. A claim left neither completed nor released is held until its lease ends - retries wait, then
        // one takes it over and runs.
        private async Task QuietlyAsync(Func<Task> record)
        {
            try
            {
                await record();
            }
#pragma warning disable CA1031 // Any failure here is logged and left to the lease.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogRecordFailed(logger, exception);
            }
        }

        private static StoredResponse Snapshot(HttpResponse response, CapturingStream capture)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in ReplayedHeaders)
            {
                if (response.Headers.TryGetValue(name, out var value) && value.Count > 0)
                {
                    headers[name] = value.ToString();
                }
            }

            return new StoredResponse(response.StatusCode, headers, capture.Captured);
        }

        private static async Task ReplayAsync(HttpContext context, StoredResponse stored)
        {
            var response = context.Response;
            response.StatusCode = stored.StatusCode;
            foreach (var (name, value) in stored.Headers.Where(h => ReplayedHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase)))
            {
                response.Headers[name] = value;
            }

            response.Headers[ReplayedHeader] = "true";
            if (stored.Body.Length > 0)
            {
                response.ContentLength = stored.Body.Length;
                await response.Body.WriteAsync(stored.Body, context.RequestAborted);
            }
        }

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "{Endpoint} answered more than {Limit} bytes, or with headers too long to keep, so its answer was "
                + "not stored and a retry will run again. An [Idempotent] route must answer small JSON.")]
        private static partial void LogTooLargeToStore(ILogger logger, string? endpoint, int limit);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "An idempotency claim could not be completed or released; retries will wait for its lease to end.")]
        private static partial void LogRecordFailed(ILogger logger, Exception exception);
    }

    /// <summary>
    /// Passes every write straight through to the response and keeps a copy, up to a limit. Past the limit the copy is
    /// dropped - the caller still gets the whole answer, but it will not be stored. Once the caller has gone, its
    /// stream may refuse writes; from then on only the copy is written.
    /// </summary>
    private sealed class CapturingStream(Stream inner, int limit, CancellationToken callerGone) : Stream
    {
        private MemoryStream? _copy = new();
        private bool _innerGone;

        public bool Overflowed => _copy is null;

        public byte[] Captured => _copy?.ToArray() ?? [];

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
            Keep(buffer.AsSpan(offset, count));
            ToCaller(() => inner.Write(buffer, offset, count));
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Keep(buffer.Span);
            await ToCaller(() => inner.WriteAsync(buffer, cancellationToken).AsTask());
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => ToCaller(inner.Flush);

        public override Task FlushAsync(CancellationToken cancellationToken) => ToCaller(() => inner.FlushAsync(cancellationToken));

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _copy?.Dispose();
            }

            base.Dispose(disposing);
        }

        // A failure writing to a caller who has gone is expected and ends writing to them; any other failure is real.
        private void ToCaller(Action write)
        {
            if (_innerGone)
            {
                return;
            }

            try
            {
                write();
            }
            catch (Exception) when (callerGone.IsCancellationRequested)
            {
                _innerGone = true;
            }
        }

        private async Task ToCaller(Func<Task> write)
        {
            if (_innerGone)
            {
                return;
            }

            try
            {
                await write();
            }
            catch (Exception) when (callerGone.IsCancellationRequested)
            {
                _innerGone = true;
            }
        }

        private void Keep(ReadOnlySpan<byte> bytes)
        {
            if (_copy is null)
            {
                return;
            }

            if (_copy.Length + bytes.Length > limit)
            {
                _copy.Dispose();
                _copy = null;
                return;
            }

            _copy.Write(bytes);
        }
    }
}
