using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.Repositories;

namespace PantryPress.Domain.Modules.Sites.DataLayer;

/// <summary>
/// What the Worker reads to send an invitation's email, and its way to the provider (ARCH-002; PP2.11). Registered only
/// where invitation email is sent - the Worker - so no other host needs an email gateway. The delivery's scope has entered
/// the invitation's site, so the invitation is read through the filter.
/// </summary>
internal sealed class SiteInvitationMailDataLayer(
    SiteInvitationRepository invitations,
    SiteRepository sites,
    SiteInvitationTokens tokens,
    IEmailGateway gateway)
{
    public Task<SiteInvitation?> ReadInvitationAsync(SiteInvitationId invitation, CancellationToken cancellationToken) =>
        invitations.ReadAsync(invitation, cancellationToken);

    /// <summary>The site's row, unless it is deleted.</summary>
    public Task<Site?> FindSiteAsync(SiteId site, CancellationToken cancellationToken) => sites.FindAsync(site, cancellationToken);

    /// <summary>A fresh token for the invitation's link: every one made for it works alike until the invitation is used.</summary>
    public string CreateToken(SiteId site, SiteInvitationId invitation) => tokens.Create(site, invitation);

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => gateway.SendAsync(message, cancellationToken);
}
