using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Business;

/// <summary>When an invitation can still be used, and how long one is made to last (SITE-010; PP2.11). Pure.</summary>
public static class SiteInvitationRules
{
    /// <summary>
    /// Open: <see cref="SiteInvitationStatus.Pending"/> and not yet expired. Expiry is checked against the clock at the
    /// moment of asking, so an invitation stops working at <paramref name="expiresAt"/> with nothing run to close it.
    /// </summary>
    public static bool IsOpen(SiteInvitationStatus status, DateTimeOffset expiresAt, DateTimeOffset now) =>
        status == SiteInvitationStatus.Pending && now < expiresAt;

    /// <summary>
    /// Marks an open invitation accepted by <paramref name="account"/> at <paramref name="now"/>: the one way out of
    /// <see cref="SiteInvitationStatus.Pending"/> that records who and when, as the schema's accepted check requires
    /// (PP2.14). An invitation that is not open at <paramref name="now"/> is a caller's bug.
    /// </summary>
    internal static void Accept(SiteInvitation invitation, AccountId account, DateTimeOffset now)
    {
        if (!IsOpen(invitation.Status, invitation.ExpiresAt, now))
        {
            throw new InvalidOperationException("Only an open invitation can be accepted.");
        }

        invitation.Status = SiteInvitationStatus.Accepted;
        invitation.AcceptedBy = account;
        invitation.AcceptedAt = now;
    }

    /// <summary>
    /// Whether a site with <paramref name="open"/> other open invitations may have another (<c>Sites:MaxOpenInvitations</c>):
    /// the facade's first look and the unit of work's locked second look ask the same rule (PP2.14).
    /// </summary>
    public static bool HasRoomForAnother(int open, int max) => open < max;

    /// <summary>When an invitation made at <paramref name="now"/> stops working.</summary>
    public static DateTimeOffset ExpiryFrom(DateTimeOffset now, TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        return now + lifetime;
    }

    /// <summary>
    /// How many whole days an invitation from <paramref name="createdAt"/> to <paramref name="expiresAt"/> lasts, rounded
    /// down and never under one, as its email says it: "works for 7 days".
    /// </summary>
    public static int LifetimeInDays(DateTimeOffset createdAt, DateTimeOffset expiresAt) =>
        Math.Max(1, (int)Math.Floor((expiresAt - createdAt).TotalDays));
}
