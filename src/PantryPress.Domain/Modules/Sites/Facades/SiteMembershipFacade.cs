using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.DataLayer;

namespace PantryPress.Domain.Modules.Sites.Facades;

/// <inheritdoc cref="ISiteMembershipFacade"/>
internal sealed class SiteMembershipFacade(SiteMembershipDataLayer data, ISiteContextWriter siteContext) : ISiteMembershipFacade
{
    // Registered scoped, so this lives exactly as long as the request. Keyed by the account and the stored-form slug, so
    // "Pie" and "pie" are one lookup and no account ever reads another's answer.
    private readonly Dictionary<(AccountId Account, string Slug), SiteMembership?> _found = [];

    public async Task<SiteMembership?> FindAsync(AccountId account, string siteSlug, CancellationToken cancellationToken)
    {
        if (!SiteSlugs.TryNormalize(siteSlug, out var slug))
        {
            return null;
        }

        if (!_found.TryGetValue((account, slug), out var membership))
        {
            membership = await data.FindAsync(account, slug, cancellationToken);
            _found[(account, slug)] = membership;
        }

        return membership;
    }

    public async Task<Result<SiteMembership>> AuthorizeAsync(
        AccountId account,
        string siteSlug,
        IReadOnlyCollection<SiteRole> leastRoles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(leastRoles);
        if (leastRoles.Count == 0)
        {
            throw new ArgumentException("A site decision needs the least role the route requires.", nameof(leastRoles));
        }

        if (await FindAsync(account, siteSlug, cancellationToken) is not { } membership)
        {
            return SiteErrors.SiteNotFound;
        }

        if (!leastRoles.All(leastRole => SiteRoles.Covers(membership.Role, leastRole)))
        {
            return SiteErrors.SiteRoleRequired;
        }

        // The site comes from the slug the route names and the caller's own membership, never from anything else the
        // request carries (tenancy.md rules 4 and 7).
        siteContext.Enter(membership.SiteId);
        return membership;
    }
}
