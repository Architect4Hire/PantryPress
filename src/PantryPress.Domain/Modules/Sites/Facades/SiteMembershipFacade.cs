using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.DataLayer;

namespace PantryPress.Domain.Modules.Sites.Facades;

/// <inheritdoc cref="ISiteMembershipFacade"/>
internal sealed class SiteMembershipFacade(SiteMembershipDataLayer data) : ISiteMembershipFacade
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
        SiteRole leastRole,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(account, siteSlug, cancellationToken) is not { } membership)
        {
            return SiteErrors.SiteNotFound;
        }

        return SiteRoles.Covers(membership.Role, leastRole) ? membership : SiteErrors.SiteRoleRequired;
    }
}
