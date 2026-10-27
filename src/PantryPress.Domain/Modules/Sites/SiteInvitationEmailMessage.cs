using PantryPress.Domain.Managers.Outbox;

namespace PantryPress.Domain.Modules.Sites;

/// <summary>
/// Send an invitation's email, through the transactional outbox (PP2.11, backend.md). Staged with the invitation, under
/// the request's site (<c>SiteScope.Current</c>), so the email goes out if and only if the invitation was saved. It
/// carries the invitation's id and nothing else: the address is read, and the link's token made, by the Worker when it
/// sends, so no address or live token ever sits in the outbox.
/// </summary>
/// <remarks>The invitation's own status and expiry decide whether it is still worth sending; nothing else is carried.</remarks>
internal sealed record SiteInvitationEmailMessage(Guid InvitationId) : IOutboxMessage
{
    public static string OutboxType => "sites.invitation-email";
}
