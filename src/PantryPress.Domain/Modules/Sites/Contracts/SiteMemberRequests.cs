namespace PantryPress.Domain.Modules.Sites.Contracts;

// Request and response records for a site's members (api-contract.md: records in Contracts/, never entities). Values
// arrive straight from JSON, so validators treat every property as possibly null. No record here carries a site id: the
// site is named by the route's slug, and a member by their account's id within it (tenancy.md rule 7).

/// <summary>One page of the site's members, in the order they joined (SITE-010; PP2.11b).</summary>
/// <param name="Limit">How many at most, 1 to 100; missing means 50.</param>
/// <param name="Cursor">The <see cref="SiteMemberPage.NextCursor"/> of the page before, or missing for the first.</param>
public sealed record ListSiteMembersQuery(int? Limit, string? Cursor);

/// <summary>Give a member another role on the site.</summary>
/// <param name="Role">The member's new role: <c>owner</c>, <c>editor</c>, or <c>author</c>. Required.</param>
public sealed record ChangeMemberRoleRequest(SiteRole? Role);

/// <summary>A member of the site as its Owner sees them.</summary>
/// <param name="Id">The member's id, which names them in this site's member routes.</param>
/// <param name="Email">The address the member signs in with; null only while their account is being closed.</param>
/// <param name="JoinedAt">When they became a member: when the site was created, or when they accepted an invitation.</param>
/// <param name="IsYou">Whether this member is the caller.</param>
public sealed record SiteMemberItem(
    Guid Id,
    string? Email,
    SiteRole Role,
    SiteMemberStatus Status,
    DateTimeOffset JoinedAt,
    bool IsYou);

/// <summary>A page of the site's members; <paramref name="NextCursor"/> is null on the last.</summary>
public sealed record SiteMemberPage(IReadOnlyList<SiteMemberItem> Items, string? NextCursor);
