using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PantryPress.ServiceDefaults.EdgeHardening;

/// <summary>
/// The response headers every PantryPress answer carries (PSEC-002; PP1.8): a strict content-security policy, HSTS
/// where a browser can reach us, and the three smaller headers that close off sniffing, framing, and referrer leaks.
/// </summary>
/// <remarks>
/// Written on <em>every</em> answer, including the ones that never reach an endpoint - a 413, a cross-origin refusal,
/// a problem 404 - because a header that depends on which middleware answered is a header that goes missing exactly
/// when something has gone wrong. Values are policy, so they live here rather than in configuration; the one
/// deployment knob is whether HSTS is on, which is off outside Production so a developer's browser is never pinned.
/// </remarks>
public sealed class SecurityHeaderOptions
{
    /// <summary>Content-Security-Policy for an answer that a browser may render as a document.</summary>
    public string DocumentContentSecurityPolicy { get; set; } = ContentSecurityPolicies.Document;

    /// <summary>
    /// Content-Security-Policy for an API answer. JSON is not a document, so everything is denied: the policy matters
    /// if a browser is ever talked into rendering one.
    /// </summary>
    public string ApiContentSecurityPolicy { get; set; } = ContentSecurityPolicies.Api;

    /// <summary>Paths that get <see cref="ApiContentSecurityPolicy"/> instead of the document policy.</summary>
    public string ApiPathPrefix { get; set; } = "/api";

    public string ReferrerPolicy { get; set; } = ReferrerPolicies.Admin;

    public string PermissionsPolicy { get; set; } = PermissionsPolicies.DenyAll;

    /// <summary>
    /// Whether to claim HTTPS for a year. Only meaningful where a browser reaches us directly: the gateway and the
    /// public site, not the internal APIs. Never sent over plain HTTP, where it would be ignored anyway.
    /// </summary>
    public bool Hsts { get; set; }

    public int HstsMaxAgeSeconds { get; set; } = 31_536_000;

    /// <summary>
    /// Covers subdomains of the host that sends it, which for a host PantryPress owns is exactly what we want and
    /// costs nothing. It is <em>not</em> free on a blogger's custom domain: DOM-001 allows an apex, and a year of
    /// <c>includeSubDomains</c> from <c>example.com</c> pins every host under it - mail, shop, a legacy HTTP-only
    /// one - none of which PantryPress serves or was given permission to speak for, and leaving the platform does
    /// not unpin them. So the public tier turns this off (PP9.6 may generate it per host once a domain is verified
    /// and the blogger has opted in). <c>preload</c> is absent everywhere: a one-way door owned by browser vendors.
    /// </summary>
    public bool HstsIncludeSubDomains { get; set; } = true;
}

/// <summary>Referrer policies by surface.</summary>
public static class ReferrerPolicies
{
    /// <summary>The admin app has no reason to tell anyone where a blogger came from.</summary>
    public const string Admin = "no-referrer";

    /// <summary>
    /// A public site does: referral traffic is how a blogger's outbound links are credited back, so the origin
    /// travels on a same-scheme cross-origin request and the path never does.
    /// </summary>
    public const string Public = "strict-origin-when-cross-origin";
}

/// <summary>
/// Permissions policies by surface. Each denies the features it lists; a feature nobody has listed keeps the
/// browser's own default, which for most is the page itself - so this is a deny-list that grows as browsers do, not
/// a statement that everything is off.
/// </summary>
public static class PermissionsPolicies
{
    /// <summary>
    /// The Privacy Sandbox advertising features are denied alongside the device ones. PantryPress's analytics are
    /// cookie-free by default and its ads are opt-in (MON-005), so a page that has not asked for an ad network has
    /// no business joining an interest group or running an auction. PP15 adds back what a chosen network needs.
    /// </summary>
    private const string Advertising =
        "attribution-reporting=(), browsing-topics=(), join-ad-interest-group=(), private-aggregation=(), "
        + "run-ad-auction=(), shared-storage=(), shared-storage-select-url=()";

    private const string Features =
        "accelerometer=(), ambient-light-sensor=(), autoplay=(), battery=(), camera=(), "
        + "display-capture=(), encrypted-media=(), fullscreen=(self), geolocation=(), gyroscope=(), idle-detection=(), "
        + "local-fonts=(), magnetometer=(), microphone=(), midi=(), payment=(), picture-in-picture=(), "
        + "publickey-credentials-get=(), serial=(), usb=(), xr-spatial-tracking=(), " + Advertising;

    public const string DenyAll = Features + ", screen-wake-lock=()";

    /// <summary>
    /// The public site differs in one feature: cook mode keeps the screen awake while someone has their hands in a
    /// bowl, so <c>screen-wake-lock</c> is allowed to the page itself and to nothing else.
    /// </summary>
    public const string Public = Features + ", screen-wake-lock=(self)";
}

/// <summary>Content-security policies by surface.</summary>
public static class ContentSecurityPolicies
{
    /// <summary>An API answer is not a document: nothing may load, nothing may frame it, nothing may be posted to.</summary>
    public const string Api =
        "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; sandbox";

    /// <summary>
    /// The admin app: its own origin and nothing else. <c>style-src 'unsafe-inline'</c> is Angular's component styles,
    /// which a prerendered page carries inline. The gateway serves those pages as static files (PPL.3), so a nonce
    /// cannot replace it; per-document hashes could. <c>script-src</c> admits no inline script at all, and the gateway
    /// refuses to start on a build whose documents carry one.
    /// </summary>
    public const string Document =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; "
        + "img-src 'self' data: blob:; font-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self'; "
        + "connect-src 'self'; media-src 'self' blob:; worker-src 'self' blob:; manifest-src 'self'; "
        + "upgrade-insecure-requests";

    /// <summary>
    /// The admin dev server needs what a dev server needs: evaluated bundles and a websocket for hot reload. Local
    /// Development only, and the production policy above is what a browser meets anywhere else.
    /// </summary>
    public const string DevelopmentDocument =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; "
        + "img-src 'self' data: blob:; font-src 'self' data:; style-src 'self' 'unsafe-inline'; "
        + "script-src 'self' 'unsafe-inline' 'unsafe-eval'; connect-src 'self' ws: wss:; media-src 'self' blob:; "
        + "worker-src 'self' blob:; manifest-src 'self'";

    /// <summary>
    /// ApiService's Scalar reference page, which starts itself with an inline module script whose text carries the
    /// page's own configuration. The document policy with that one script admitted; still nothing from another origin.
    /// The page exists in Development only (PP1.5), so this policy is never served anywhere else.
    /// </summary>
    public const string DevelopmentApiReference =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; "
        + "img-src 'self' data: blob:; font-src 'self'; style-src 'self' 'unsafe-inline'; "
        + "script-src 'self' 'unsafe-inline'; connect-src 'self'; media-src 'self' blob:; worker-src 'self' blob:; "
        + "manifest-src 'self'";

    /// <summary>
    /// A placeholder for the public site until the per-site policy is generated from the blogger's settings, which is
    /// where ad and analytics origins earn their place (MON-005, PP9). Strict on purpose: a site that has not opted
    /// into anything should load nothing third-party.
    /// </summary>
    /// <remarks>
    /// Two things for whoever writes the generated one. It belongs with the HTML: readers' documents come from the
    /// Angular SSR server, so this constant only ever dresses this tier's own answers, and the generated policy
    /// should travel in the theme snapshot - built by the Worker from the site's enabled integrations, versioned and
    /// purged with the page it describes. <see cref="SecurityHeaders.Set"/> never overwrites a header something
    /// downstream already set, which is the seam for that. And inline content must be admitted by
    /// <c>'sha256-…'</c> carried in the snapshot, never by a nonce: output has to be byte-identical (ARCH-005) and
    /// is cached at Front Door, so a per-request value cannot be part of it.
    /// </remarks>
    public const string Site =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; "
        + "img-src 'self' data:; font-src 'self'; style-src 'self'; script-src 'self'; connect-src 'self'; "
        + "upgrade-insecure-requests";
}

public static class SecurityHeaders
{
    public const string ContentTypeOptions = "X-Content-Type-Options";
    public const string FrameOptions = "X-Frame-Options";
    public const string ReferrerPolicy = "Referrer-Policy";
    public const string PermissionsPolicy = "Permissions-Policy";
    public const string ContentSecurityPolicy = "Content-Security-Policy";
    public const string StrictTransportSecurity = "Strict-Transport-Security";

    public static IServiceCollection AddSecurityHeaders(
        this IServiceCollection services,
        Action<SecurityHeaderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure(configure);
        return services;
    }

    /// <summary>
    /// Writes the headers as the response starts, so an answer from any middleware carries them, and leaves alone any
    /// header something downstream set deliberately.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.ApplicationServices.GetRequiredService<IOptions<SecurityHeaderOptions>>().Value;
        var hsts = options.HstsIncludeSubDomains
            ? string.Create(CultureInfo.InvariantCulture, $"max-age={options.HstsMaxAgeSeconds}; includeSubDomains")
            : string.Create(CultureInfo.InvariantCulture, $"max-age={options.HstsMaxAgeSeconds}");

        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                Set(headers, ContentTypeOptions, "nosniff");
                Set(headers, FrameOptions, "DENY");
                Set(headers, ReferrerPolicy, options.ReferrerPolicy);
                Set(headers, PermissionsPolicy, options.PermissionsPolicy);
                Set(
                    headers,
                    ContentSecurityPolicy,
                    context.Request.Path.StartsWithSegments(options.ApiPathPrefix, StringComparison.OrdinalIgnoreCase)
                        ? options.ApiContentSecurityPolicy
                        : options.DocumentContentSecurityPolicy);

                // Over plain HTTP a browser ignores it, and sending it would only invite a stripping proxy to think
                // it had done something.
                if (options.Hsts && context.Request.IsHttps)
                {
                    Set(headers, StrictTransportSecurity, hsts);
                }

                return Task.CompletedTask;
            });

            return next(context);
        });
    }

    private static void Set(IHeaderDictionary headers, string name, string value)
    {
        if (!string.IsNullOrEmpty(value) && !headers.ContainsKey(name))
        {
            headers[name] = value;
        }
    }
}
