using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.Repositories;

namespace PantryPress.Domain.Modules.Sites.DataLayer;

/// <summary>
/// Composes the tenant root's repositories for creating a site and reading what its members see of it (ARCH-002; PP2.10).
/// Every read is keyed by a slug or host for existence only, by a site already resolved, or by the caller's account.
/// </summary>
internal sealed class SitesDataLayer(
    SiteRepository sites,
    SiteMemberRepository members,
    SiteHostRepository hosts,
    SitesUnitOfWork unitOfWork)
{
    /// <summary>
    /// Whether the slug or its subdomain is held by any site, deleted ones included: existence only, one answer for both,
    /// so the caller learns nothing about which or whose (SITE-002).
    /// </summary>
    public async Task<bool> IsTakenAsync(string slug, string host, CancellationToken cancellationToken) =>
        await sites.IsSlugHeldAsync(slug, cancellationToken) || await hosts.IsHostHeldAsync(host, cancellationToken);

    /// <summary>Stages a new site with its first Owner and its subdomain, to be saved together.</summary>
    public void StageNewSite(Site site, SiteMember owner, SiteHost subdomain)
    {
        sites.Add(site);
        members.Add(owner);
        hosts.Add(subdomain);
    }

    /// <summary>How many sites the account owns, deleted ones included.</summary>
    public Task<int> CountOwnedAsync(AccountId account, CancellationToken cancellationToken) =>
        members.CountOwnedAsync(account, cancellationToken);

    /// <summary>Saves the staged site; with a <paramref name="limit"/>, only while the account stays within it.</summary>
    public Task<NewSiteOutcome> SaveNewSiteAsync(
        SiteId site,
        string slug,
        string host,
        OwnedSiteLimit? limit,
        CancellationToken cancellationToken) =>
        unitOfWork.SaveNewSiteAsync(site, slug, host, limit, cancellationToken);

    /// <summary>The site, as a member with <paramref name="role"/> sees it, or null when it is deleted.</summary>
    public async Task<SiteDetail?> FindDetailAsync(SiteId site, SiteRole role, CancellationToken cancellationToken)
    {
        if (await sites.FindAsync(site, cancellationToken) is not { } row)
        {
            return null;
        }

        var canonical = await hosts.FindCanonicalAsync([site], cancellationToken);
        return Detail(row, role, CanonicalOf(canonical, site));
    }

    /// <summary>
    /// Up to <paramref name="limit"/> of the account's sites after <paramref name="afterSlug"/>, and whether more follow.
    /// </summary>
    public async Task<(IReadOnlyList<SiteSummary> Items, bool More)> ListAsync(
        AccountId account,
        string? afterSlug,
        int limit,
        CancellationToken cancellationToken)
    {
        // One more than the page, to know whether another follows without a count.
        var found = await sites.ListForMemberAsync(account, afterSlug, limit + 1, cancellationToken);
        var page = found.Take(limit).ToList();
        var canonical = await hosts.FindCanonicalAsync([.. page.Select(s => s.Id)], cancellationToken);
        return ([.. page.Select(s => new SiteSummary(s.Slug, s.Name, s.Status, s.Role, CanonicalOf(canonical, s.Id)))], found.Count > limit);
    }

    // Every site that is not deleted keeps a canonical host; the database guards losing one, not having one (tenancy.md,
    // "Tenant root"), so a site without one is a bug in whatever wrote it, and is named as that.
    private static string CanonicalOf(IReadOnlyDictionary<SiteId, string> canonical, SiteId site) =>
        canonical.TryGetValue(site, out var host)
            ? host
            : throw new InvalidOperationException($"Site {site} is not deleted but has no canonical host.");

    public static SiteDetail Detail(Site site, SiteRole role, string host) => new(
        site.Slug,
        site.Name,
        site.Tagline,
        site.Status,
        site.TimeZone,
        site.Locale,
        site.CreatedAt,
        role,
        host);
}
