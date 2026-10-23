using Microsoft.AspNetCore.Mvc.Filters;
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
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var executed = await next();
        var http = context.HttpContext;
        if (executed.Exception is not null
            || http.Response.StatusCode is < StatusCodes.Status200OK or >= StatusCodes.Status300MultipleChoices
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
        }
    }
}
