using Microsoft.AspNetCore.Authentication;
using Microsoft.Net.Http.Headers;
using PantryPress.Gateway.Contracts;

namespace PantryPress.Gateway.Hosting;

/// <summary>
/// Sign-in, sign-out, and the current session (ACCT-002, PSEC-003; PP1.6). These four are the gateway's own: it holds
/// the session, so it is the one place a cookie is written or cleared, and no token ever reaches the browser. They
/// keep the account-scoped shape of the rest of the account API (api-contract.md), so the admin app sees one API, and
/// they sit ahead of the forwarded route, which would otherwise send these paths to ApiService.
/// </summary>
internal static partial class SessionEndpoints
{
    public const string Base = "/api/v1/account/session";

    /// <summary>The API's code for a half-finished sign-in that can no longer be finished (its <c>AccountErrors</c>).</summary>
    public const string SignInExpiredCode = "sign-in-expired";

    /// <summary>Ahead of the forwarded /api/v1 route (1) and the gateway's /api fallback (2).</summary>
    private const int SessionOrder = 0;

    public static WebApplication MapSessionEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        // The declared shapes are what the admin app's typed client is written from until the BFF publishes a
        // document of its own (recorded for PP2). Each route declares every answer it can give, the refusals the
        // browser boundary makes ahead of it included (PP1.12): 400 antiforgery-failed and 403 cross-origin-blocked
        // on a write, 413 on any of them, and 502 when ApiService cannot be asked.
        var session = app.MapGroup(Base);
        // The two authentication attempts are limited per address (PP1.8); nothing else here is an attempt.
        session.MapPost(string.Empty, SignInAsync)
            .RequireRateLimiting(GatewayRateLimits.SessionPerIp)
            .Produces<SessionView>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway);
        session.MapPost("/second-factor", CompleteSecondFactorAsync)
            .RequireRateLimiting(GatewayRateLimits.SessionPerIp)
            .Produces<SessionView>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        // Cast so the handlers' IResult is written to the response rather than discarded as a RequestDelegate. Both
        // ask the API, so both are limited per address too (PP1.12).
        session.MapGet(string.Empty, (Delegate)CurrentAsync)
            .RequireRateLimiting(GatewayRateLimits.SessionChecksPerIp)
            .Produces<SessionView>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway);
        session.MapDelete(string.Empty, (Delegate)SignOutAsync)
            .RequireRateLimiting(GatewayRateLimits.SessionChecksPerIp)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // Any of the four can answer 413: the body cap is refused ahead of routing (PP1.8).
        ((IEndpointConventionBuilder)session).ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        ((IEndpointConventionBuilder)session).Add(endpoint =>
        {
            if (endpoint is RouteEndpointBuilder route)
            {
                route.Order = SessionOrder;
            }
        });

        return app;
    }

    private static async Task<IResult> SignInAsync(
        SignInBody? body,
        HttpContext context,
        ApiSessions api,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        var answer = await api.SignInAsync(body?.Email ?? string.Empty, body?.Password ?? string.Empty, cancellationToken);
        return await AnswerAsync(context, answer, secondFactorVerified: false);
    }

    private static async Task<IResult> CompleteSecondFactorAsync(
        SecondFactorBody? body,
        HttpContext context,
        ApiSessions api,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        var pending = await context.AuthenticateAsync(SessionSchemes.SecondFactor);
        if (!SessionCookie.TryGetAccount(pending.Principal, out var account)
            || SessionCookie.Stamp(pending.Principal!) is not { Length: > 0 } stamp)
        {
            // Nothing to finish: the half-finished session expired, was never started, or is already complete.
            return await UnauthorizedAsync(context);
        }

        var answer = await api.CompleteSecondFactorAsync(account, stamp, body?.Code ?? string.Empty, cancellationToken);
        if (answer.Problem is { Code: SignInExpiredCode })
        {
            // That sign-in can never be finished now, so its cookie goes too, and the current session says "sign in"
            // rather than "enter your code" for the rest of its five minutes (PP1.12).
            await context.SignOutAsync(SessionSchemes.SecondFactor);
        }

        return await AnswerAsync(context, answer, secondFactorVerified: true);
    }

    private static async Task<IResult> CurrentAsync(HttpContext context, ApiSessions api, CancellationToken cancellationToken)
    {
        NoStore(context);
        var complete = await context.AuthenticateAsync(SessionSchemes.Complete);
        if (SessionCookie.TryGetAccount(complete.Principal, out var account)
            && SessionCookie.Stamp(complete.Principal!) is { Length: > 0 } stamp
            && SessionCookie.SessionId(complete.Principal!) is { Length: > 0 } sessionId)
        {
            // The database's word, not the cookie's (PP1.12): a session that was signed out, or whose password or second
            // factor changed, is over now - and an address confirmed since sign-in shows as confirmed.
            var answer = await api.CurrentAsync(
                account,
                stamp,
                sessionId,
                SessionCookie.Has(complete.Principal!, SessionClaims.SecondFactorVerified),
                cancellationToken);
            if (answer.Value is { } current)
            {
                return Results.Ok(new SessionView(
                    SessionStage.Complete,
                    current.AccountId,
                    current.Email,
                    current.EmailVerified,
                    current.SecondFactorVerified));
            }

            // The API's backstop ceiling: relayed as it came, wait included, rather than turned into a 502.
            if (answer.Problem is { Status: StatusCodes.Status429TooManyRequests } busy)
            {
                if (busy.RetryAfter is { Length: > 0 } retryAfter)
                {
                    context.Response.Headers[HeaderNames.RetryAfter] = retryAfter;
                }

                return Results.Text(busy.Body, busy.ContentType ?? "application/problem+json", statusCode: busy.Status);
            }

            if (answer.Problem is { Code: { } code } && code == GatewayApi.SessionEndedCode)
            {
                await context.SignOutAsync(SessionSchemes.Complete);
                await GatewayApi.WriteProblemAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    GatewayApi.SessionEndedCode,
                    GatewayApi.SessionEndedTitle);
                return Results.Empty;
            }

            await GatewayApi.WriteProblemAsync(context, StatusCodes.Status502BadGateway);
            return Results.Empty;
        }

        var pending = await context.AuthenticateAsync(SessionSchemes.SecondFactor);
        if (SessionCookie.TryGetAccount(pending.Principal, out var pendingAccount))
        {
            return Results.Ok(new SessionView(
                SessionStage.SecondFactor,
                pendingAccount,
                SessionCookie.Email(pending.Principal!),
                SessionCookie.Has(pending.Principal!, SessionClaims.EmailVerified),
                SecondFactorVerified: false));
        }

        return await UnauthorizedAsync(context);
    }

    private static async Task<IResult> SignOutAsync(
        HttpContext context,
        ApiSessions api,
        ILogger<ApiSessions> logger,
        CancellationToken cancellationToken)
    {
        NoStore(context);

        // Revoked at the API first, so a copy of this cookie stops working wherever it is (PP1.12). If the API cannot be
        // asked the cookies still go - the person asked to be signed out on this device, and is - and the failure is
        // logged as an error, because until the cookie expires a copy of it would still work.
        var complete = await context.AuthenticateAsync(SessionSchemes.Complete);
        if (SessionCookie.TryGetAccount(complete.Principal, out var account)
            && SessionCookie.SessionId(complete.Principal!) is { Length: > 0 } sessionId)
        {
            var ended = await api.EndAsync(account, sessionId, cancellationToken);
            if (!ended.Succeeded)
            {
                LogSignOutNotRevoked(logger, account);
            }
        }

        // Both cookies: a sign-out in the middle of a two-step sign-in leaves nothing behind either.
        await context.SignOutAsync(SessionSchemes.Complete);
        await context.SignOutAsync(SessionSchemes.SecondFactor);
        return Results.NoContent();
    }

    private static async Task<IResult> AnswerAsync(
        HttpContext context,
        ApiAnswer<SessionStart> answer,
        bool secondFactorVerified)
    {
        if (answer.Value is { } session)
        {
            return await IssueAsync(context, session, secondFactorVerified);
        }

        if (answer.Problem is { } problem)
        {
            if (problem.RetryAfter is { Length: > 0 } retryAfter)
            {
                // The plain-language "wait a few minutes" keeps its machine-readable half.
                context.Response.Headers[HeaderNames.RetryAfter] = retryAfter;
            }

            return Results.Text(problem.Body, problem.ContentType ?? "application/problem+json", statusCode: problem.Status);
        }

        // The API answered something that is not the blogger's business, or could not be reached (ApiSessions logged
        // what). They get our own failure, in plain language, rather than a puzzle such as "we couldn't find that".
        await GatewayApi.WriteProblemAsync(context, StatusCodes.Status502BadGateway);
        return Results.Empty;
    }

    private static async Task<IResult> IssueAsync(HttpContext context, SessionStart session, bool secondFactorVerified)
    {
        if (session.SecondStepRequired == true)
        {
            await context.SignInAsync(
                SessionSchemes.SecondFactor,
                SessionCookie.SecondFactorPrincipal(session),
                SessionCookie.Properties());
            return Results.Ok(new SessionView(
                SessionStage.SecondFactor,
                session.AccountId,
                session.Email,
                session.EmailVerified,
                SecondFactorVerified: false));
        }

        // A complete session replaces any half-finished one, so the shorter cookie never outlives its purpose. It gets
        // an id of its own to be signed out by, and the time its absolute lifetime runs from (PP1.12).
        var now = context.RequestServices.GetRequiredService<GatewayClock>().UtcNow;
        await context.SignOutAsync(SessionSchemes.SecondFactor);
        await context.SignInAsync(
            SessionSchemes.Complete,
            SessionCookie.CompletePrincipal(session, secondFactorVerified, SessionCookie.NewSessionId(), now),
            SessionCookie.Properties());
        return Results.Ok(new SessionView(
            SessionStage.Complete,
            session.AccountId,
            session.Email,
            session.EmailVerified,
            secondFactorVerified));
    }

    private static async Task<IResult> UnauthorizedAsync(HttpContext context)
    {
        await GatewayApi.WriteProblemAsync(context, StatusCodes.Status401Unauthorized);
        return Results.Empty;
    }

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Account {AccountId} signed out, but the API could not revoke the session; a copy of its cookie works until it expires.")]
    private static partial void LogSignOutNotRevoked(ILogger logger, Guid accountId);
}
