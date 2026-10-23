using Microsoft.AspNetCore.Antiforgery;

namespace PantryPress.Gateway.Hosting;

/// <summary>
/// Antiforgery for the admin surface (ACCT-002; PP1.7, gateway.md). The session rides in a cookie, so a form on
/// another site could otherwise make a signed-in person's browser write through this gateway. Every state-changing
/// request under <c>/api/v1</c> therefore has to carry a token the admin app could only have read from this origin:
/// the gateway's own session routes included, because a sign-in a stranger triggers is a forgery too.
/// </summary>
/// <remarks>
/// Two cookies and a header, all three the admin app's contract (PP2 writes the typed client against these names):
/// <see cref="CookieName"/> holds ASP.NET Core's cookie token and stays <c>HttpOnly</c>; <see cref="TokenCookieName"/>
/// holds the request token and is the one piece of this the app is meant to read; it sends it back in
/// <see cref="HeaderName"/>. Both are <c>__Host-</c> prefixed for the PSEC-003 reason the session cookie is: tenant
/// sites run third-party script on sibling subdomains, and that prefix is what stops one of them writing either
/// cookie.
/// <para>
/// The check runs before any session cookie is read, so the token is bound to this origin and not to an account. That
/// is the deliberate trade: a token survives signing in, the second step, a stamp rotation, and signing out, which
/// means one lifecycle rule instead of four re-minting points - and no window where a valid token is refused because
/// the session underneath it moved on. What binds the token to us is the <c>__Host-</c> prefix, the same-origin policy
/// that keeps another site from reading the cookie, and <see cref="GatewayCors.UseSameOriginWrites"/>.
/// </para>
/// </remarks>
internal static class GatewayAntiforgery
{
    /// <summary>ASP.NET Core's cookie token. Never read by the admin app.</summary>
    public const string CookieName = "__Host-pp-antiforgery";

    /// <summary>The request token the admin app reads and echoes back.</summary>
    public const string TokenCookieName = "__Host-pp-xsrf";

    public const string HeaderName = "X-PP-Xsrf";

    public const string FailedCode = "antiforgery-failed";

    public const string FailedTitle = "Please reload the page and try again.";

    public static IServiceCollection AddGatewayAntiforgery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddAntiforgery(options =>
        {
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;

            // Always, in every environment, for the reason the session cookie gives: a cookie allowed over plain HTTP
            // once is a cookie that can be taken over plain HTTP.
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

            // Stricter than the session cookie, and it costs nothing: a token is only ever needed on a request the
            // admin app makes from its own page, never on a navigation that arrives from somewhere else.
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.Cookie.Domain = null;
            options.Cookie.IsEssential = true;
            options.HeaderName = HeaderName;

            // Response headers are PP1.8's to set, deliberately and on every answer, rather than appearing on
            // whichever response happened to mint a token.
            options.SuppressXFrameOptionsHeader = true;
        });
    }

    /// <summary>
    /// Requires a token on every write under the admin surface, and hands the admin app one on a read when it has
    /// none - its first call at startup is such a read. Paths outside <c>/api/v1</c> are left exactly as PP1.5 left
    /// them: nothing under <c>/api</c> is reachable there, so there is nothing to forge.
    /// </summary>
    public static IApplicationBuilder UseGatewayAntiforgery(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(GatewayRoutes.AdminApiPath, StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            if (!context.Request.IsHttps)
            {
                // There is no token to hand out over plain HTTP - the cookie that carries it is Secure, so a browser
                // would never send it back - and therefore no write to accept either. Since PP1.8 a plain-HTTP
                // request is redirected to TLS before it gets here, so this is the backstop behind that redirect
                // rather than the thing a caller meets.
                if (IsWrite(context.Request.Method))
                {
                    await GatewayApi.WriteProblemAsync(context, StatusCodes.Status400BadRequest, FailedCode, FailedTitle);
                    return;
                }

                await next(context);
                return;
            }

            if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            {
                if (!context.Request.Cookies.ContainsKey(TokenCookieName))
                {
                    Issue(context, antiforgery);
                }

                await next(context);
                return;
            }

            if (!IsWrite(context.Request.Method))
            {
                // OPTIONS and TRACE change nothing. A preflight never gets this far: closed CORS answers it.
                await next(context);
                return;
            }

            // Before anything can read the body. ASP.NET Core's token store falls back to the posted form when the
            // content type is a form one, so validating a token-less multipart write would parse the whole body -
            // work an anonymous caller must not be able to ask for - and a malformed one throws InvalidDataException
            // rather than the AntiforgeryValidationException below, which would make a 500 of a documented 400.
            if (!context.Request.Headers.ContainsKey(HeaderName))
            {
                Issue(context, antiforgery);
                await GatewayApi.WriteProblemAsync(context, StatusCodes.Status400BadRequest, FailedCode, FailedTitle);
                return;
            }

            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                // A token can go stale for honest reasons - a lost cookie, a gateway restart with a new key ring - so
                // the refusal carries a fresh pair and the next attempt simply works.
                Issue(context, antiforgery);
                await GatewayApi.WriteProblemAsync(context, StatusCodes.Status400BadRequest, FailedCode, FailedTitle);
                return;
            }

            await next(context);
        });
    }

    private static bool IsWrite(string method) =>
        HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);

    /// <summary>
    /// Stores the cookie token and puts the matching request token where the admin app can read it. Same attributes as
    /// the cookie token, minus <c>HttpOnly</c>: this one exists to be read by the app's own script.
    /// </summary>
    private static void Issue(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        if (tokens.RequestToken is not { Length: > 0 } token)
        {
            return;
        }

        context.Response.Cookies.Append(TokenCookieName, token, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
        });
    }
}
