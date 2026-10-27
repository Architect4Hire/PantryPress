using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Repositories;

/// <summary>
/// A site's invitations (PP2.11). Site-owned, so the global filter keeps every read here to the site the request entered,
/// and with no site entered a read throws (tenancy.md rule 2); nothing here names a site.
/// </summary>
internal sealed class SiteInvitationRepository(PantryPressDbContext db)
{
    /// <summary>The site's invitation with this id, tracked so accepting it saves the change, or null.</summary>
    public Task<SiteInvitation?> FindAsync(SiteInvitationId invitation, CancellationToken cancellationToken) =>
        db.Set<SiteInvitation>().SingleOrDefaultAsync(i => i.Id == invitation, cancellationToken);

    /// <summary>The site's invitation with this id, read only, as the Worker reads it to send its email.</summary>
    public Task<SiteInvitation?> ReadAsync(SiteInvitationId invitation, CancellationToken cancellationToken) =>
        db.Set<SiteInvitation>().AsNoTracking().SingleOrDefaultAsync(i => i.Id == invitation, cancellationToken);

    /// <summary>
    /// How many of the site's invitations are open at <paramref name="now"/> - pending and not yet expired - leaving out
    /// the one to <paramref name="exceptNormalizedEmail"/>, which inviting that address again replaces.
    /// </summary>
    public Task<int> CountOpenAsync(DateTimeOffset now, string exceptNormalizedEmail, CancellationToken cancellationToken) =>
        db.Set<SiteInvitation>().CountAsync(
            i => i.Status == SiteInvitationStatus.Pending && i.ExpiresAt > now && i.NormalizedEmail != exceptNormalizedEmail,
            cancellationToken);

    public void Add(SiteInvitation invitation) => db.Set<SiteInvitation>().Add(invitation);
}
