using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.Repositories;

namespace PantryPress.Domain.Modules.Sites.DataLayer;

/// <summary>
/// Composes the site and membership repositories to answer one question: the caller's standing on the site a slug
/// names (ARCH-002). The site's id is found first and handed back only together with an active membership.
/// </summary>
internal sealed class SiteMembershipDataLayer(SiteRepository sites, SiteMemberRepository members)
{
    public async Task<SiteMembership?> FindAsync(AccountId account, string slug, CancellationToken cancellationToken)
    {
        if (await sites.FindIdBySlugAsync(slug, cancellationToken) is not { } site)
        {
            return null;
        }

        return await members.FindActiveRoleAsync(site, account, cancellationToken) is { } role
            ? new SiteMembership(site, role)
            : null;
    }
}
