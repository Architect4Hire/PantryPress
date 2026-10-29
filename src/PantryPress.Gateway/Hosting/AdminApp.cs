using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;
using PantryPress.ServiceDefaults.EdgeHardening;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace PantryPress.Gateway.Hosting;

/// <summary>
/// The platform home's pages (PP-D19, ADR 0019; PPL.3): which paths are public documents, which of those a signed-in
/// visitor is sent past, and where the admin app's build lives.
/// </summary>
/// <remarks>
/// The two lists mirror <c>PUBLIC_PATHS</c> and <c>SIGNED_IN_REDIRECT_PATHS</c> in the admin app's
/// <c>public-paths.ts</c>, and a test reads that file as text and fails when they drift. They are written here as URL
/// paths (<c>/</c>, <c>/pricing</c>), where the app writes router paths (<c>''</c>, <c>'pricing'</c>).
/// </remarks>
internal sealed class AdminAppOptions
{
    public const string Section = "Gateway:AdminApp";

    /// <summary>
    /// The folder holding the admin app's build (<c>dist/pantrypress-admin/browser</c>), absolute or under the content
    /// root. Required outside Development, where nothing else serves a page; refused in Development, where the dev
    /// server's catch-all route serves every page instead.
    /// </summary>
    public string? Root { get; set; }

    /// <summary>The prerendered documents, each served from <c>{path}/index.html</c> (<c>/</c> from <c>index.html</c>).</summary>
    public IList<string> PublicPaths { get; } = [];

    /// <summary>The public paths a complete session is redirected away from: the landing page, never a legal page.</summary>
    public IList<string> SignedInRedirectPaths { get; } = [];

    /// <summary>Where a signed-in visitor is sent instead. It is never itself redirected, so the redirect cannot loop.</summary>
    public string SignedInHome { get; set; } = "/dashboard";
}

/// <summary>
/// Serves the admin app's build on the apex outside Development (PPL.3): the prerendered document for each public path,
/// the browser-rendered <c>index.csr.html</c> for every other page path, and the build's own files. Documents are
/// <c>no-cache</c> because <c>/</c> answers a signed-in visitor differently; only fingerprinted files are long-lived.
/// </summary>
internal static partial class AdminApp
{
    /// <summary>The document every browser-rendered route starts from (Angular's name for it once prerendering is on).</summary>
    public const string BrowserDocument = "index.csr.html";

    public const string DocumentCacheControl = "no-cache";
    public const string ImmutableCacheControl = "public, max-age=31536000, immutable";
    public const string RedirectCacheControl = "no-store";

    /// <summary>What any page or file answer that sets or clears a cookie gets instead of its own caching.</summary>
    public const string PersonalCacheControl = "no-cache, no-store";

    // Last of everything: after the forwarded /api/v1 route (Order 1) and the gateway's own /api 404 (Order 2), so no
    // /api path is ever a page.
    private const int PageOrder = 1000;

    private const string HtmlContentType = "text/html; charset=utf-8";

    private static readonly FrozenSet<string> DataScriptTypes =
        new[] { "application/json", "application/ld+json" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static AdminAppOptions AddAdminApp(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = new AdminAppOptions();
        configuration.GetSection(AdminAppOptions.Section).Bind(options);
        var devServerRoute = configuration.GetSection($"ReverseProxy:Routes:{GatewayRoutes.AdminDevServer}").Exists();
        Validate(options, environment.IsDevelopment(), devServerRoute);

        services.AddSingleton(options);
        if (!string.IsNullOrEmpty(options.Root))
        {
            services.AddSingleton(Build.Open(Path.GetFullPath(options.Root, environment.ContentRootPath), options));
        }

        return options;
    }

    /// <summary>
    /// The rules the configuration is held to before the gateway starts. A path list that disagreed with itself would
    /// either redirect for ever or redirect a page that must stay readable, so it is refused rather than served.
    /// </summary>
    internal static void Validate(AdminAppOptions options, bool development, bool devServerRoute)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.PublicPaths.Count == 0 || !options.PublicPaths.All(IsPagePath)
            || options.PublicPaths.Distinct(StringComparer.Ordinal).Count() != options.PublicPaths.Count)
        {
            throw Refuse(
                "Gateway:AdminApp:PublicPaths must list each public page once, as a lower-case path from the root with "
                + "no trailing slash (\"/\", \"/pricing\"), outside /api.");
        }

        if (options.SignedInRedirectPaths.Count == 0
            || !options.SignedInRedirectPaths.All(path => options.PublicPaths.Contains(path)))
        {
            throw Refuse("Gateway:AdminApp:SignedInRedirectPaths must name public paths only (PPL-001).");
        }

        if (!IsPagePath(options.SignedInHome) || options.PublicPaths.Contains(options.SignedInHome))
        {
            throw Refuse(
                "Gateway:AdminApp:SignedInHome must be a page path that is not public, or a signed-in visitor would be "
                + "redirected to a page that redirects.");
        }

        var root = !string.IsNullOrEmpty(options.Root);
        if (development && root)
        {
            throw Refuse(
                "Gateway:AdminApp:Root is not used in Development: the admin dev server serves every page through the "
                + $"'{GatewayRoutes.AdminDevServer}' route. Leave it unset.");
        }

        if (!development && !root)
        {
            throw Refuse(
                "Gateway:AdminApp:Root must name the admin app's build (dist/pantrypress-admin/browser). Outside "
                + "Development the gateway serves every page itself (PPL.3).");
        }

        if (root && devServerRoute)
        {
            throw Refuse(
                $"The '{GatewayRoutes.AdminDevServer}' route and Gateway:AdminApp:Root would both serve every page. "
                + "The dev server's route belongs to Development alone.");
        }
    }

    /// <summary>
    /// Sends a complete session at <c>/</c> or <c>/pricing</c> to the dashboard before any document is read (PPL-001,
    /// ADR 0019). It reads the gateway's own cookie alone and asks ApiService nothing: a session the API has since
    /// ended is still sent on, and the dashboard's guard then meets <c>session-ended</c>, which clears the cookie. A
    /// half-finished sign-in is another scheme and is not a session here. Runs in every environment, ahead of the pages
    /// and of the Development dev server alike.
    /// </summary>
    /// <remarks>
    /// The router reads <c>/pricing/</c> and <c>/pricing;x=1</c> as <c>/pricing</c>, so a public path spelled any other
    /// way is first sent to its own spelling (301). Otherwise that spelling would reach the landing page in the browser
    /// without passing this check, and be a second copy of the page besides.
    /// </remarks>
    public static IApplicationBuilder UseSignedInRedirect(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.ApplicationServices.GetRequiredService<AdminAppOptions>();
        var publicPaths = options.PublicPaths.ToFrozenSet(StringComparer.Ordinal);
        var redirected = options.SignedInRedirectPaths.ToFrozenSet(StringComparer.Ordinal);
        var home = options.SignedInHome;

        return app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            {
                await next(context);
                return;
            }

            var path = request.Path.Value ?? "/";
            var canonical = CanonicalSpelling(path);
            if (!string.Equals(canonical, path, StringComparison.Ordinal) && publicPaths.Contains(canonical))
            {
                context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                context.Response.Headers.Location = canonical + request.QueryString;
                context.Response.Headers.CacheControl = DocumentCacheControl;
                return;
            }

            if (redirected.Contains(path)
                && context.User.Identity?.AuthenticationType == SessionSchemes.Complete
                && SessionCookie.TryGetAccount(context.User, out _))
            {
                context.Response.StatusCode = StatusCodes.Status302Found;
                context.Response.Headers.Location = home;
                context.Response.Headers.CacheControl = RedirectCacheControl;
                return;
            }

            await next(context);
        });
    }

    /// <summary>The pages and the build's files, when this gateway serves the build (outside Development).</summary>
    public static WebApplication MapAdminApp(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.Services.GetService<Build>() is not { } build)
        {
            return app;
        }

        app.MapMethods("/{**path}", [HttpMethods.Get, HttpMethods.Head], build.AnswerAsync)
            .Add(endpoint =>
            {
                if (endpoint is RouteEndpointBuilder route)
                {
                    route.Order = PageOrder;
                }
            });

        return app;
    }

    /// <summary>
    /// In Development the dev server answers every page; its documents are made <c>no-cache</c> on the way through,
    /// as the built ones are, so a browser never keeps a landing page a signed-in visitor should not get.
    /// </summary>
    public static void AddDevServerTransforms(TransformBuilderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(context.Route.RouteId, GatewayRoutes.AdminDevServer, StringComparison.Ordinal))
        {
            return;
        }

        context.AddResponseTransform(transform =>
        {
            var response = transform.HttpContext.Response;
            if (response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
            {
                response.Headers.CacheControl = DocumentCacheControl;
            }

            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// A file Angular fingerprinted, by its path in the build: a bundle at the root (<c>main-YXEA34CX.js</c>,
    /// <c>chunk-x_XEu4qQ.js</c>) or a file a stylesheet referenced, under <c>media/</c> (<c>media/font-ABCD2345.woff2</c>).
    /// Its contents can never change under its name. A file copied from the app's <c>public/</c> folder keeps the name it
    /// was given, however hash-like, so it is never one.
    /// </summary>
    internal static bool IsFingerprinted(string relativePath) => Fingerprint().IsMatch(relativePath);

    /// <summary>A path as the router reads it: no matrix parameters, and no trailing slash but the root's.</summary>
    internal static string CanonicalSpelling(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var segments = path.Split('/').Select(segment => segment.Split(';')[0]);
        var joined = string.Join('/', segments).TrimEnd('/');
        return joined.Length == 0 ? "/" : joined;
    }

    /// <summary>
    /// A served document must run under <c>script-src 'self'</c> (PSEC-002): an inline script or an inline event
    /// handler would be blocked, and the page would break quietly. JSON data blocks (hydration state, JSON-LD) are
    /// never run, so they pass.
    /// </summary>
    internal static string? InlineScriptIn(string html)
    {
        foreach (Match script in ScriptTag().Matches(html))
        {
            var attributes = script.Groups["attributes"].Value;
            if (SourceAttribute().IsMatch(attributes))
            {
                continue;
            }

            var type = TypeAttribute().Match(attributes);
            if (!type.Success || !DataScriptTypes.Contains(type.Groups["type"].Value.Trim()))
            {
                return script.Value;
            }
        }

        return EventHandler().Match(html) is { Success: true } handler ? handler.Value : null;
    }

    private static bool IsPagePath(string? path) =>
        path is not null
        && path.StartsWith('/')
        && (path.Length == 1 || !path.EndsWith('/'))
        && PagePath().IsMatch(path)
        && !new PathString(path).StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException Refuse(string message) => new(message);

    [GeneratedRegex("^(/[a-z0-9-]+)*/?$", RegexOptions.CultureInvariant)]
    private static partial Regex PagePath();

    // Angular's bundle names, and esbuild's base32 hash on the files it copies for a stylesheet.
    [GeneratedRegex(
        @"^(?:(?:main|polyfills|styles|chunk|worker)-[A-Za-z0-9_]{8}\.(?:js|css)|media/[A-Za-z0-9_.-]+-[A-Z2-7]{8}\.[A-Za-z0-9]+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Fingerprint();

    [GeneratedRegex(@"<script\b(?<attributes>[^>]*)>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();

    [GeneratedRegex(@"\ssrc\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SourceAttribute();

    [GeneratedRegex(@"\stype\s*=\s*[""']?(?<type>[^""'\s>]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TypeAttribute();

    // An on* attribute in a tag, read attribute by attribute so a quoted value ("Sell online = easy") is never mistaken
    // for one.
    [GeneratedRegex(
        @"<[a-z][a-z0-9-]*(?:[\s/]+[^\s=>""'/]+(?:\s*=\s*(?:""[^""]*""|'[^']*'|[^\s""'>]+))?)*?[\s/]+on[a-z]+\s*=",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex EventHandler();

    /// <summary>The build on disk, checked once at startup: every document present and runnable under the policy.</summary>
    internal sealed class Build
    {
        private static readonly FileExtensionContentTypeProvider ContentTypes = new();

        private readonly PhysicalFileProvider files;
        private readonly FrozenDictionary<string, string> documents;
        private readonly PathString home;

        private Build(PhysicalFileProvider files, FrozenDictionary<string, string> documents, PathString home)
        {
            this.files = files;
            this.documents = documents;
            this.home = home;
        }

        public static Build Open(string root, AdminAppOptions options)
        {
            if (!Directory.Exists(root))
            {
                throw Refuse($"Gateway:AdminApp:Root ({root}) does not exist. Build the admin app into it first.");
            }

            // Sensitive files - dotfiles, hidden and system files - are never served, and the provider refuses any
            // path that would leave the root.
            var files = new PhysicalFileProvider(root);
            var documents = options.PublicPaths.ToFrozenDictionary(
                path => path,
                path => Check(files, path == "/" ? "index.html" : $"{path.TrimStart('/')}/index.html"),
                StringComparer.Ordinal);
            Check(files, BrowserDocument);
            return new Build(files, documents, new PathString(options.SignedInHome));
        }

        public Task AnswerAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "/";

            // Never a page, whatever a route mistake let through: the gateway's own /api answer stands. Nor a probe's
            // path: deployed, nothing maps /health or /alive yet (PP19.2), and those paths skip the host check and the
            // HTTPS redirect, so a page there would answer any host over plain HTTP.
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                || EdgeHttps.Exempt.Any(probe => context.Request.Path.StartsWithSegments(probe, StringComparison.OrdinalIgnoreCase)))
            {
                return NotFoundAsync(context);
            }

            if (documents.TryGetValue(path, out var document))
            {
                return ServeDocumentAsync(context, document);
            }

            // Everything under the dashboard is a page, a last segment with a dot in it included (a domain, a file name).
            // Elsewhere a page path has no dot in its last segment, and anything that does is a file of the build or
            // nothing: a missing file is a 404, never the app's document with a 200, which a stale chunk request would
            // try to run as a script. HTML files are only ever reached by their page path, so the signed-in redirect
            // cannot be stepped around by asking for /index.html.
            var name = path[(path.LastIndexOf('/') + 1)..];
            if (context.Request.Path.StartsWithSegments(home, StringComparison.Ordinal)
                || !name.Contains('.', StringComparison.Ordinal)
                || name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                return ServeDocumentAsync(context, BrowserDocument);
            }

            var file = files.GetFileInfo(path);
            if (!file.Exists || file.IsDirectory || file.PhysicalPath is null
                || !ContentTypes.TryGetContentType(name, out var contentType))
            {
                return NotFoundAsync(context);
            }

            var cache = IsFingerprinted(path.TrimStart('/')) ? ImmutableCacheControl : DocumentCacheControl;
            return ServeAsync(context, file, contentType, cache);
        }

        // An answer that sets or clears a cookie is nobody else's, whatever it carries: a session past its lifetime is
        // signed out by the cookie handler before this runs, and a renewal is written as the answer starts, so the
        // check is made then, when every Set-Cookie is known. Without it a year-long immutable file answer could carry
        // one person's cookie into the edge's cache.
        private static void SetCacheControl(HttpContext context, string cacheControl)
        {
            context.Response.Headers.CacheControl = cacheControl;
            context.Response.OnStarting(() =>
            {
                if (context.Response.Headers.SetCookie.Count > 0)
                {
                    context.Response.Headers.CacheControl = PersonalCacheControl;
                }

                return Task.CompletedTask;
            });
        }

        // The gateway's own problem 404, no-store like every refusal it writes.
        private static Task NotFoundAsync(HttpContext context) =>
            GatewayApi.WriteProblemAsync(context, StatusCodes.Status404NotFound);

        private static Task ServeAsync(HttpContext context, IFileInfo file, string contentType, string cacheControl)
        {
            SetCacheControl(context, cacheControl);
            return TypedResults.PhysicalFile(
                    file.PhysicalPath!,
                    contentType,
                    lastModified: file.LastModified,
                    entityTag: new EntityTagHeaderValue($"\"{file.LastModified.ToUnixTimeMilliseconds():x}-{file.Length:x}\""))
                .ExecuteAsync(context);
        }

        private static string Check(PhysicalFileProvider files, string relativePath)
        {
            var file = files.GetFileInfo(relativePath);
            if (!file.Exists || file.IsDirectory || file.PhysicalPath is null)
            {
                throw Refuse(
                    $"The admin app's build has no {relativePath} under Gateway:AdminApp:Root. Every public path is "
                    + "prerendered to a document of its own, and every other page starts from "
                    + $"{BrowserDocument} (PPL.2).");
            }

            if (InlineScriptIn(File.ReadAllText(file.PhysicalPath)) is { } script)
            {
                throw Refuse(
                    $"{relativePath} in the admin app's build carries inline script ({Shorten(script)}), which "
                    + "the admin app's content-security policy blocks (script-src 'self'). Build without it: no "
                    + "critical-CSS inlining and no event replay (PPL.3).");
            }

            return relativePath;
        }

        private static string Shorten(string text) => text.Length <= 80 ? text : text[..80] + "…";

        // A document is small and read whole each time, and its tag is its content's hash: a rebuild that kept a document's
        // length and time (an image build that normalizes times) still gets a new tag, so a browser holding the old
        // document never revalidates it against chunks that no longer exist.
        private async Task ServeDocumentAsync(HttpContext context, string relativePath)
        {
            var file = files.GetFileInfo(relativePath);
            var content = await File.ReadAllBytesAsync(file.PhysicalPath!, context.RequestAborted);
            SetCacheControl(context, DocumentCacheControl);
            await TypedResults.Bytes(
                    content,
                    HtmlContentType,
                    lastModified: file.LastModified,
                    entityTag: new EntityTagHeaderValue($"\"{Convert.ToHexStringLower(SHA256.HashData(content))[..32]}\""))
                .ExecuteAsync(context);
        }
    }
}
