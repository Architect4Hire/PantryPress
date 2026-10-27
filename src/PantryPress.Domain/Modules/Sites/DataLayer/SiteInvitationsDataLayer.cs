using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.Repositories;

namespace PantryPress.Domain.Modules.Sites.DataLayer;

/// <summary>
/// Composes the invitation, membership, and site repositories for inviting and accepting (ARCH-002; PP2.11). Invitations
/// are read through the site filter; the tenant root only by the site already entered and the caller's own account.
/// </summary>
internal sealed class SiteInvitationsDataLayer(
    SiteInvitationRepository invitations,
    SiteMemberRepository members,
    SiteRepository sites,
    SiteHostRepository hosts,
    SitesUnitOfWork unitOfWork,
    SiteInvitationTokens tokens)
{
    /// <summary>The site and invitation a link's token names, or false for anything that is not one of ours.</summary>
    public bool TryReadToken(string token, out SiteId site, out SiteInvitationId invitation) =>
        tokens.TryRead(token, out site, out invitation);

    /// <summary>The site's open invitations, other than the one to the address about to be invited again.</summary>
    public Task<int> CountOpenAsync(DateTimeOffset now, string exceptNormalizedEmail, CancellationToken cancellationToken) =>
        invitations.CountOpenAsync(now, exceptNormalizedEmail, cancellationToken);

    /// <summary>Stages a new invitation, to be saved with whatever else the facade staged.</summary>
    public void StageInvitation(SiteInvitation invitation) => invitations.Add(invitation);

    /// <summary>Saves the staged invitation, revoking the address's older ones on the site in the same transaction.</summary>
    public Task SaveInvitationAsync(SiteId site, SiteInvitation invitation, CancellationToken cancellationToken) =>
        unitOfWork.SaveInvitationAsync(site, invitation.Id, invitation.NormalizedEmail, cancellationToken);

    /// <summary>The site's invitation, tracked, or null.</summary>
    public Task<SiteInvitation?> FindInvitationAsync(SiteInvitationId invitation, CancellationToken cancellationToken) =>
        invitations.FindAsync(invitation, cancellationToken);

    /// <summary>The entered site's row, unless it is deleted.</summary>
    public Task<Site?> FindSiteAsync(SiteId site, CancellationToken cancellationToken) => sites.FindAsync(site, cancellationToken);

    /// <summary>Whether the account belongs to the site already, in any standing.</summary>
    public Task<bool> IsMemberAsync(SiteId site, AccountId account, CancellationToken cancellationToken) =>
        members.IsMemberAsync(site, account, cancellationToken);

    /// <summary>Stages the membership an accepted invitation grants.</summary>
    public void StageMember(SiteMember member) => members.Add(member);

    public Task<AcceptanceOutcome> SaveAcceptanceAsync(
        SiteId site,
        SiteInvitationId invitation,
        AccountId account,
        CancellationToken cancellationToken) =>
        unitOfWork.SaveAcceptanceAsync(site, invitation, account, cancellationToken);

    /// <summary>The site as a new member lists it.</summary>
    public async Task<SiteSummary> SummaryAsync(Site site, SiteRole role, CancellationToken cancellationToken)
    {
        // Every site that is not deleted keeps a canonical host (tenancy.md, "Tenant root"); one without is a bug in
        // whatever wrote it.
        var canonical = await hosts.FindCanonicalAsync([site.Id], cancellationToken);
        return canonical.TryGetValue(site.Id, out var host)
            ? new SiteSummary(site.Slug, site.Name, site.Status, role, host)
            : throw new InvalidOperationException($"Site {site.Id} is not deleted but has no canonical host.");
    }
}
