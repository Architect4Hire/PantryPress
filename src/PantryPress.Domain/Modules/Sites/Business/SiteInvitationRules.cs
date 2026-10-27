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
