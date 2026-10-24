using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using PantryPress.ApiService.Http;

namespace PantryPress.ApiService.Hosting;

/// <summary>
/// Where <see cref="IdempotentAttribute"/> may go (PP1.9), checked when the host starts: only on a write, and only on a
/// route that names its caller, because a key is scoped to the caller and replaying one anonymous caller's answer to
/// another would be a leak. Every opted-in route also declares the answers the key adds.
/// </summary>
/// <remarks>
/// Controllers only: a minimal-API endpoint carrying the attribute is not checked here. ApiService maps no minimal
/// endpoint under <c>/api</c> today; one that ever wants a key needs this check extended first.
/// </remarks>
internal sealed class IdempotencyConvention : IApplicationModelConvention
{
    private static readonly HashSet<string> Writes = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
    };

    private static readonly int[] DeclaredByTheKey =
    [
        StatusCodes.Status400BadRequest,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity,
    ];

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            foreach (var action in controller.Actions.Where(a => a.Attributes.OfType<IdempotentAttribute>().Any()))
            {
                var name = $"{controller.ControllerType.Name}.{action.ActionMethod.Name}";
                var methods = action.Selectors
                    .SelectMany(s => s.ActionConstraints.OfType<HttpMethodActionConstraint>())
                    .SelectMany(c => c.HttpMethods)
                    .ToList();
                if (methods.Count == 0 || methods.Any(m => !Writes.Contains(m)))
                {
                    throw new InvalidOperationException(
                        $"{name} is [Idempotent] but answers {(methods.Count == 0 ? "every method" : string.Join(", ", methods))}. "
                        + "Only POST, PUT, PATCH, and DELETE take an Idempotency-Key.");
                }

                var attributes = controller.Attributes.Concat(action.Attributes).ToList();
                if (attributes.OfType<IAllowAnonymous>().Any() || !attributes.OfType<IAuthorizeData>().Any())
                {
                    throw new InvalidOperationException(
                        $"{name} is [Idempotent] without requiring a signed-in caller. Keys are scoped to the caller, "
                        + "so an anonymous route has nothing to scope them to.");
                }

                if (attributes.OfType<AnswersWithSecretAttribute>().Any())
                {
                    throw new InvalidOperationException(
                        $"{name} is [Idempotent] and [AnswersWithSecret]. A stored answer would keep the secret at rest "
                        + "and show it twice.");
                }

                // A rotated stamp ends the caller's session, so the retry of a lost answer is refused as signed out
                // before it reaches the key - and a replay could not carry the new stamp anyway.
                if (attributes.OfType<RefreshesSessionAttribute>().Any())
                {
                    throw new InvalidOperationException(
                        $"{name} is [Idempotent] and [RefreshesSession]. A retry after a lost answer is signed out before "
                        + "the key is read; make the facade idempotent instead.");
                }

                // The internal surface answers a stranger with the 404 of a route that is not there, from an MVC filter
                // that runs after the key would already have been read and answered.
                if (attributes.OfType<GatewayOnlyAttribute>().Any())
                {
                    throw new InvalidOperationException(
                        $"{name} is [Idempotent] on the gateway's internal surface, which never takes an Idempotency-Key.");
                }

                // A malformed key (400), a key still in use (409), and a key reused for another request (422). Added
                // only where the route has not declared the status itself, on the action or its controller: its own
                // 400 is usually a validation problem, and the more specific schema should win.
                var declared = action.Filters.Concat(controller.Filters).OfType<IApiResponseMetadataProvider>().ToList();
                foreach (var status in DeclaredByTheKey)
                {
                    if (!declared.Any(p => p.StatusCode == status))
                    {
                        action.Filters.Add(new ProducesResponseTypeAttribute<ProblemDetails>(status, ApiProblems.ContentType));
                    }
                }
            }
        }
    }
}
