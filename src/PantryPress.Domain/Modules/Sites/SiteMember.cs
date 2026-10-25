using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites;

/// <summary>
/// An account's membership of a site, with its role (SITE-010; PP2.3). Membership is what grants a site, so like the
/// <see cref="Site"/> it is read before any site context exists - to resolve an admin route's site, and to list the
/// caller's sites - and is written when a site is created, before there is a context to stamp from. It carries a
/// required <see cref="SiteId"/> but is not <c>ISiteOwned</c> and has no query filter: every query is keyed by the
/// caller's account or by a site already resolved, and only the Sites module reaches the table (tenancy.md, "Tenant
/// root"). Every site keeps at least one active Owner; the database refuses any change that would leave none.
/// </summary>
public sealed class SiteMember
{
    public SiteId SiteId { get; init; }

    public AccountId AccountId { get; init; }

    public SiteRole Role { get; set; }

    public SiteMemberStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The concurrency token: the database moves it on every update, and a save from a stale read fails.</summary>
    public byte[] Version { get; set; } = [];
}
