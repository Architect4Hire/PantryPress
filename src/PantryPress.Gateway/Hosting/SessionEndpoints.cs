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
internal static class SessionEndpoints
{
    public const string Base = "/api/v1/account/session";

    /// <summary>Ahead of the forwarded /api/v1 route (1) and the gateway's /api fallback (2).</summary>
    private const int SessionOrder = 0;

    public static WebApplication MapSessionEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        // The declared shapes are what the admin app's typed client is written from until the BFF publishes a
        // document of its own (recorded for PP2).
        var session = app.MapGroup(Base);
        session.MapPost(string.Empty, SignInAsync)
            .Produces<SessionView>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway);
        session.MapPost("/second-factor", CompleteSecondFactorAsync)
            .Produces<SessionView>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Cast so the handlers' IResult is written to the response rather than discarded as a RequestDelegate.
        session.MapGet(string.Empty, (Delegate)CurrentAsync)
            .Produces<SessionView>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        session.MapDelete(string.Empty, (Delegate)SignOutAsync)
            .Produces(StatusCodes.Status204NoContent);

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
        return await AnswerAsync(context, answer, secondFactorVerified: true);
    }

    private static async Task<IResult> CurrentAsync(HttpContext context)
    {
        NoStore(context);
        var complete = await context.AuthenticateAsync(SessionSchemes.Complete);
        if (SessionCookie.TryGetAccount(complete.Principal, out var account))
        {
            return View(SessionStage.Complete, account, complete.Principal!);
        }

        var pending = await context.AuthenticateAsync(SessionSchemes.SecondFactor);
        if (SessionCookie.TryGetAccount(pending.Principal, out var pendingAccount))
        {
            return View(SessionStage.SecondFactor, pendingAccount, pending.Principal!);
        }

        return await UnauthorizedAsync(context);
    }

    private static async Task<IResult> SignOutAsync(HttpContext context)
    {
        NoStore(context);

        // Both cookies: a sign-out in the middle of a two-step sign-in leaves nothing behind either.
        await context.SignOutAsync(SessionSchemes.Complete);
        await context.SignOutAsync(SessionSchemes.SecondFactor);
        return Results.NoContent();
    }

    private static async Task<IResult> AnswerAsync(HttpContext context, SessionAnswer answer, bool secondFactorVerified)
    {
        if (answer.Session is { } session)
        {
            return await IssueAsync(context, session, secondFactorVerified);
        }

        if (answer.Problem is { } problem)
        {
            if (problem.RetryAfter is { Length: > 0 } retryAfter)
            {
                // The plain-language "wait a quarter of an hour" keeps its machine-readable half.
                context.Response.Headers[HeaderNames.RetryAfter] = retryAfter;
            }

            return Results.Text(problem.Body, problem.ContentType ?? "application/problem+json", statusCode: problem.Status);
        }

        // The API answered something that is not the blogger's business (ApiSessions logged what). They get our own
        // failure, in plain language, rather than a puzzle such as "we couldn't find that" on a sign-in form.
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

        // A complete session replaces any half-finished one, so the shorter cookie never outlives its purpose.
        await context.SignOutAsync(SessionSchemes.SecondFactor);
        await context.SignInAsync(
            SessionSchemes.Complete,
            SessionCookie.CompletePrincipal(session, secondFactorVerified),
            SessionCookie.Properties());
        return Results.Ok(new SessionView(
            SessionStage.Complete,
            session.AccountId,
            session.Email,
            session.EmailVerified,
            secondFactorVerified));
    }

    private static IResult View(SessionStage stage, Guid account, System.Security.Claims.ClaimsPrincipal user) =>
        Results.Ok(new SessionView(
            stage,
            account,
            SessionCookie.Email(user),
            SessionCookie.Has(user, SessionClaims.EmailVerified),
            SessionCookie.Has(user, SessionClaims.SecondFactorVerified)));

    private static async Task<IResult> UnauthorizedAsync(HttpContext context)
    {
        await GatewayApi.WriteProblemAsync(context, StatusCodes.Status401Unauthorized);
        return Results.Empty;
    }

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";
}
