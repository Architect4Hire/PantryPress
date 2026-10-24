using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace PantryPress.Gateway.Hosting;

/// <summary>
/// What the API is told about the caller, and what it is never told (ACCT-002; PP1.6, gateway.md). On every request
/// forwarded to the admin API this strips anything a client sent that could pass for the gateway's own voice, drops
/// the session cookie - the API has no use for cookies and must never receive this one - adds the gateway's secret to
/// every request, and for a complete session adds the account, its security stamp, and the session's id (PP1.12). A half-finished sign-in is refused here rather
/// than forwarded, so a right password alone reaches nothing but the second-step route.
/// </summary>
internal static class SessionIdentity
{
    public const string SecondFactorRequiredCode = "second-factor-required";

    public const string SecondFactorRequiredTitle = "Finish signing in with your authenticator code.";

    public static IReverseProxyApplicationBuilder UseSessionIdentity(this IReverseProxyApplicationBuilder proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        proxy.Use(async (context, next) =>
        {
            // The session cookie is the gateway's own business. Nothing it forwards needs it - not the API, which has
            // no cookie authentication, and not the Development dev server (a PP0.7 carry-over).
            if (!IsAdminApi(context))
            {
                Strip(context);
                await next(context);
                return;
            }

            // Read the cookies before the Cookie header goes, and only then take the client's own headers away.
            var complete = await context.AuthenticateAsync(SessionSchemes.Complete);
            var pending = complete.Succeeded
                ? AuthenticateResult.NoResult()
                : await context.AuthenticateAsync(SessionSchemes.SecondFactor);

            foreach (var header in SessionHeaders.Request)
            {
                context.Request.Headers.Remove(header);
            }

            Strip(context);

            if (pending.Succeeded)
            {
                await GatewayApi.WriteProblemAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    SecondFactorRequiredCode,
                    SecondFactorRequiredTitle);
                return;
            }

            // The gateway vouches for everything it forwards to the admin API, signed in or not. ApiService believes
            // the browser's address only behind this secret (PP1.8), so without it every anonymous route - sign-up,
            // confirming an address, password reset - would be limited by the gateway's own address: one allowance
            // shared by everyone, which one caller could spend (PP1.12). The secret names nobody; an identity needs
            // the account and stamp below as well.
            var secret = context.RequestServices.GetRequiredService<IOptions<GatewaySessionOptions>>().Value;
            context.Request.Headers[SessionHeaders.Secret] = secret.SharedSecret;
            context.Request.Headers[SessionHeaders.ViaProxy] = SessionHeaders.ViaProxyValue;

            if (SessionCookie.TryGetAccount(complete.Principal, out var account)
                && SessionCookie.Stamp(complete.Principal!) is { Length: > 0 } stamp
                && SessionCookie.SessionId(complete.Principal!) is { Length: > 0 } sessionId)
            {
                var user = complete.Principal!;
                context.User = user;
                context.Request.Headers[SessionHeaders.Account] = account.ToString("D");
                context.Request.Headers[SessionHeaders.Stamp] = stamp;
                context.Request.Headers[SessionHeaders.SessionId] = sessionId;
                if (SessionCookie.Has(user, SessionClaims.SecondFactorVerified))
                {
                    context.Request.Headers[SessionHeaders.SecondFactor] = SessionHeaders.True;
                }
            }

            await next(context);
        });

        return proxy;
    }

    /// <summary>
    /// What the API says back about the session itself: that it is over, or that this request rotated the security
    /// stamp it was bound to. Both headers are the gateway's to act on and remove; a browser never sees either.
    /// </summary>
    public static void AddSessionTransforms(TransformBuilderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(context.Route.RouteId, GatewayRoutes.AdminApi, StringComparison.Ordinal))
        {
            return;
        }

        context.AddResponseTransform(async transform =>
        {
            // Every header the gateway owns comes off here, whether it acts on it or not, so none can reach a
            // browser - including one added to the list later.
            var response = transform.HttpContext.Response;
            var owned = SessionHeaders.Response.ToDictionary(
                header => header,
                header => Take(response, header),
                StringComparer.OrdinalIgnoreCase);
            var ended = owned[SessionHeaders.Ended];
            var newStamp = owned[SessionHeaders.NewStamp];
            var passedSecondStep = string.Equals(
                owned[SessionHeaders.SecondStepPassed],
                SessionHeaders.SecondStepPassedValue,
                StringComparison.Ordinal);

            if (string.Equals(ended, SessionHeaders.EndedValue, StringComparison.Ordinal))
            {
                // The account is gone, or its password, second factor, or platform role changed. Clearing the cookie
                // is what turns a wall of 401s into "you've been signed out".
                await transform.HttpContext.SignOutAsync(SessionSchemes.Complete);
                return;
            }

            // This request rotated the stamp (a password change, two-step sign-in on or off). Re-issuing the cookie
            // against the new one ends the account's other sessions without signing this one out.
            if (newStamp is { Length: > 0 } stamp && transform.HttpContext.User is { } user
                && SessionCookie.TryGetAccount(user, out _))
            {
                await transform.HttpContext.SignInAsync(
                    SessionSchemes.Complete,
                    SessionCookie.WithStamp(user, stamp, passedSecondStep),
                    SessionCookie.Properties());
            }
        });
    }

    /// <summary>
    /// What the gateway keeps to itself on anything it forwards: the session cookies, which the API has no cookie
    /// authentication to read, and the antiforgery token, which is this gateway's own business (PP1.7).
    /// </summary>
    private static void Strip(HttpContext context)
    {
        context.Request.Headers.Remove(HeaderNames.Cookie);
        context.Request.Headers.Remove(GatewayAntiforgery.HeaderName);
    }

    private static bool IsAdminApi(HttpContext context) =>
        string.Equals(
            context.GetReverseProxyFeature().Route.Config.RouteId,
            GatewayRoutes.AdminApi,
            StringComparison.Ordinal);

    private static string? Take(HttpResponse response, string header)
    {
        if (!response.Headers.TryGetValue(header, out var values))
        {
            return null;
        }

        response.Headers.Remove(header);
        return values.Count == 1 ? values[0] : null;
    }
}
