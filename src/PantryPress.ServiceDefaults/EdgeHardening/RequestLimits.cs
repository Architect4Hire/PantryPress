using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace PantryPress.ServiceDefaults.EdgeHardening;

/// <summary>
/// How much body a service will read before refusing (PP1.8). Kestrel's own default is 30 MB, which is a great deal
/// of parsing to hand an anonymous caller on routes that only ever carry small JSON.
/// </summary>
public sealed class RequestLimitOptions
{
    /// <summary>Bytes. Applies to every route in the host; see the note on <see cref="RequestLimits"/>.</summary>
    public long MaxBodyBytes { get; set; } = 1024 * 1024;
}

/// <summary>
/// One ceiling per host, applied to every route it serves.
/// </summary>
/// <remarks>
/// Deliberately not per route: nothing needs more than the default yet, and a per-route mechanism written now would
/// be a mechanism nothing exercises. PP6 (media uploads) and PP18 (the publishing API's uploads and its larger
/// publish payloads) are the two prompts that will need one, and both will have a route to hang it on and a test to
/// prove it - whether that is this middleware reading <c>IRequestSizeLimitMetadata</c> from the matched endpoint or
/// a limit keyed by route prefix. Until then: one number, stated plainly.
/// </remarks>
public static class RequestLimits
{
    /// <summary>One mebibyte: a long post's block document with room to spare.</summary>
    public const long AdminDefault = 1024 * 1024;

    /// <summary>
    /// The 413's problem vocabulary, set here rather than left to a host's customizer, so the refusal is a document with
    /// a stable code and words a person can read whatever the caller accepts (api-contract.md). <c>ApiProblems</c>,
    /// <c>SiteProblems</c>, and <c>GatewayApi</c> all map 413 to these constants (PP2.14), and a customizer that finds a
    /// code already set leaves it and the title alone.
    /// </summary>
    public const string TooLargeCode = "request-too-large";

    public const string TooLargeTitle = "That request is too large.";

    /// <summary>
    /// Sixty-four kibibytes. Everything a reader sends is small by rule - a rating, a comment, a signup, a contact
    /// message (public-endpoints.md) - and nothing anonymous should be able to make the public tier read more.
    /// </summary>
    public const long PublicDefault = 64 * 1024;

    public static IServiceCollection AddRequestLimits(this IServiceCollection services, long maxBodyBytes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBodyBytes);
        services.Configure<RequestLimitOptions>(options => options.MaxBodyBytes = maxBodyBytes);

        // The backstop, for a chunked body that never declares a length.
        services.Configure<KestrelServerOptions>(options => options.Limits.MaxRequestBodySize = maxBodyBytes);
        return services;
    }

    /// <summary>
    /// Refuses an over-sized request on its declared length, before anything reads a byte of it, and lowers the
    /// server's own ceiling for the rest. Early on purpose: the gateway would otherwise start streaming a body to
    /// ApiService and turn the refusal into a 502, and an antiforgery or authorization check that parses a body is
    /// work an anonymous caller should not be able to ask for.
    /// </summary>
    /// <remarks>
    /// The declared length is what is checked here. A chunked body that declares none is left to the server's
    /// ceiling, which applies when something reads it - inside YARP, for a forwarded request, so an over-sized
    /// chunked write surfaces as a 502 rather than a 413. Real clients send a length; PP6 is where chunked uploads
    /// become real.
    /// </remarks>
    public static IApplicationBuilder UseRequestLimits(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var limit = app.ApplicationServices.GetRequiredService<IOptions<RequestLimitOptions>>().Value.MaxBodyBytes;

        return app.Use(async (context, next) =>
        {
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
            {
                size.MaxRequestBodySize = limit;
            }

            if (context.Request.ContentLength > limit)
            {
                await WriteTooLargeAsync(context);
                return;
            }

            await next(context);
        });
    }

    /// <summary>
    /// The refusal, as the one error shape every PantryPress surface answers with (api-contract.md). Written through
    /// <see cref="Results.Problem(string?, string?, int?, string?, string?, IDictionary{string, object?})"/> rather
    /// than straight to <see cref="IProblemDetailsService"/>, because that service declines to write at all when the
    /// caller's <c>Accept</c> names neither JSON nor <c>*/*</c> - a browser form post, which is exactly the shape of
    /// an over-sized upload - and the caller would get a bodiless 413. This way the host's customizer still runs
    /// when it can, and the document is serialized here when it cannot.
    /// </summary>
    private static Task WriteTooLargeAsync(HttpContext context)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        return Results.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: TooLargeTitle,
            extensions: new Dictionary<string, object?> { ["code"] = TooLargeCode })
            .ExecuteAsync(context);
    }
}
