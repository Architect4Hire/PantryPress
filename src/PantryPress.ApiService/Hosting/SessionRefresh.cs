using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.ApiService.Hosting;

/// <summary>
/// Marks a route that can rotate the calling account's security stamp - changing a password, or turning two-step
/// sign-in on or off. The stamp is what the gateway's session is bound to, so a rotation would otherwise sign the
/// caller out of the very request they just made. This hands the new stamp back to the gateway, which re-issues its
/// cookie against it: the account's <em>other</em> sessions end, and this one carries on (PP1.6).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class RefreshesSessionAttribute : Attribute, IAsyncActionFilter
{
    /// <summary>
    /// The route passes the second step itself - it turns two-step sign-in on with a valid code - so the gateway's
    /// re-issued session carries the second-step claim (PP1.12).
    /// </summary>
    public bool PassesSecondStep { get; init; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var executed = await next();
        var http = context.HttpContext;

        // The result has not been written yet - an action filter runs before it is - so the response still says 200
        // whatever the action decided. Its status is on the result itself. A refusal (a 409 after a lost save race
        // above all) must hand nothing back: the stamp it staged was never saved, and a cookie re-issued against it
        // would end the caller's own session at their next request.
        if (executed.Exception is not null
            || !Succeeded(executed.Result)
            || !http.User.TryGetAccountId(out var account))
        {
            return;
        }

        var current = await http.RequestServices
            .GetRequiredService<ISessionFacade>()
            .CurrentStampAsync(account, http.RequestAborted);

        // Only when it actually moved: the common case adds no header and no work for the gateway.
        if (current is { Length: > 0 }
            && !string.Equals(current, http.Request.Headers[GatewaySession.StampHeader], StringComparison.Ordinal))
        {
            http.Response.Headers[GatewaySession.NewStampHeader] = current;
            if (PassesSecondStep)
            {
                http.Response.Headers[GatewaySession.SecondStepPassedHeader] = GatewaySession.SecondStepPassed;
            }
        }
    }

    // An ObjectResult with no status of its own is written as 200.
    internal static bool Succeeded(IActionResult? result) =>
        result switch
        {
            IStatusCodeActionResult { StatusCode: { } status } => status is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices,
            ObjectResult => true,
            _ => false,
        };
}
