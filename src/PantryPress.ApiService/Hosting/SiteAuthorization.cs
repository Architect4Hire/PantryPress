using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using PantryPress.ApiService.Http;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.ApiService.Hosting;

/// <summary>
/// The site policies (SITE-010, auth.md; PP2.3): <c>SiteOwner</c>, <c>SiteEditor</c>, and <c>SiteAuthor</c>, each the
/// least role a route needs on the site its <c>{siteSlug}</c> names. They are decided by the authorization middleware,
/// ahead of the idempotency step, so a replayed answer never outlives a revoked membership (PP1.9). A caller who is not
/// a member gets the same 404 as for a site that does not exist (tenancy.md rule 4); a member whose role is too low gets
/// a 403, since they already know the site is there.
/// </summary>
internal static class SiteAuthorization
{
    /// <summary>The route value every admin site route carries (api-contract.md: <c>/api/v1/sites/{siteSlug}/…</c>).</summary>
    public const string SlugRouteValue = "siteSlug";

    public static AuthorizationBuilder AddSitePolicies(this AuthorizationBuilder authorization)
    {
        authorization.Services.AddScoped<IAuthorizationHandler, SiteRoleHandler>();
        authorization.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, SiteRefusalResultHandler>();
        foreach (var (name, role) in SitePolicies.LeastRole)
        {
            authorization.AddPolicy(name, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new SiteRoleRequirement(role)));
        }

        return authorization;
    }

    internal sealed record SiteRoleRequirement(SiteRole LeastRole) : IAuthorizationRequirement;

    /// <summary>Why a site policy refused, so the result handler can answer with the right status and code.</summary>
    internal sealed class SiteRefusal(IAuthorizationHandler handler, Error error) : AuthorizationFailureReason(handler, error.Title)
    {
        public Error Error { get; } = error;
    }

    /// <summary>
    /// Asks the Sites module whether the caller's membership covers the requirement and turns its answer into a success or
    /// a refusal; the rule itself is the module's. The facade is scoped to the request, so the several policies on one
    /// route share one lookup.
    /// </summary>
    private sealed class SiteRoleHandler(ISiteMembershipFacade memberships) : AuthorizationHandler<SiteRoleRequirement>
    {
        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SiteRoleRequirement requirement)
        {
            // An anonymous caller is the authenticated-user requirement's to refuse, with a 401. The middleware hands the
            // request in as the resource; anything else asking these policies gets no success from them.
            if (!context.User.TryGetAccountId(out var account) || context.Resource is not HttpContext http)
            {
                return;
            }

            // SitePolicyConvention keeps these policies to routes that carry the slug; a route without one finds nothing.
            var slug = http.GetRouteValue(SlugRouteValue) as string ?? string.Empty;
            var decision = await memberships.AuthorizeAsync(account, slug, requirement.LeastRole, http.RequestAborted);
            if (decision.IsSuccess)
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail(new SiteRefusal(this, decision.Error));
            }
        }
    }

    /// <summary>
    /// Answers a site policy's refusal with its own problem - 404 <c>site-not-found</c> or 403 <c>site-role-required</c> -
    /// and leaves every other outcome, the 401 challenge included, to the framework's handler.
    /// </summary>
    private sealed class SiteRefusalResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _framework = new();

        public Task HandleAsync(
            RequestDelegate next,
            HttpContext context,
            AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Forbidden
                && authorizeResult.AuthorizationFailure?.FailureReasons.OfType<SiteRefusal>().FirstOrDefault() is { } refusal)
            {
                return Results.Problem(
                    statusCode: ApiProblems.StatusFor(refusal.Error.Kind),
                    title: refusal.Error.Title,
                    extensions: new Dictionary<string, object?> { [ApiProblems.CodeKey] = refusal.Error.Code })
                    .ExecuteAsync(context);
            }

            return _framework.HandleAsync(next, context, policy, authorizeResult);
        }
    }
}
