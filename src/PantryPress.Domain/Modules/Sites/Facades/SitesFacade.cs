using FluentValidation;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.DataLayer;
using PantryPress.Domain.Modules.Sites.Repositories;

namespace PantryPress.Domain.Modules.Sites.Facades;

/// <inheritdoc cref="ISitesFacade"/>
internal sealed class SitesFacade(
    IValidator<CreateSiteRequest> createValidator,
    IValidator<ListSitesQuery> listValidator,
    IValidator<SlugAvailabilityQuery> availabilityValidator,
    SitesDataLayer data,
    SiteHostMapDataLayer hostMap,
    ISiteMembershipFacade memberships,
    IAccountPublishingEligibility confirmedEmail,
    ISiteContext siteContext,
    ISiteContextWriter siteContextWriter,
    IAuditLog audit,
    IClock clock,
    IOptions<SitesOptions> options) : ISitesFacade
{
    public async Task<Result<SiteDetail>> CreateAsync(
        AccountId account,
        CreateSiteRequest request,
        CancellationToken cancellationToken)
    {
        if (await createValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        // PP14: ask the entitlements facade whether the account may create another site (ARCH-010), here. Until then, and
        // after it too, an account whose address is not confirmed owns at most a few sites: a slug is never reissued, so
        // this is what keeps throwaway accounts from taking addresses. Asked before the address, so such an account
        // learns nothing about addresses once it is over the limit. The save counts again, under a lock.
        var limit = await UnconfirmedLimitAsync(account, cancellationToken);
        if (limit is { } cap && await data.CountOwnedAsync(account, cancellationToken) >= cap.Max)
        {
            return AccountErrors.EmailNotVerified;
        }

        if (await FindAvailableAsync(request.Slug, cancellationToken) is not { } slug)
        {
            return SiteErrors.SlugUnavailable;
        }

        var host = SiteSubdomains.For(slug, options.Value.SubdomainZone);
        var now = clock.UtcNow;

        // The one place a site id is made (tenancy.md, "Tenant root"): on the server, never from anything a client sent.
        var id = new SiteId(Guid.NewGuid());
        var site = new Site
        {
            Id = id,
            Slug = slug,
            Name = request.Name.Trim(),
            Tagline = request.Tagline?.Trim() ?? string.Empty,

            // Live from the start, with no page data: the public tier answers "coming soon" until a first build, and
            // nothing would move a site out of Provisioning until the provisioning job exists (tenant-provisioning G-01).
            Status = SiteStatus.Live,
            TimeZone = request.TimeZone,
            Locale = request.Locale ?? SiteText.DefaultLocale,
            CreatedAt = now,
        };

        // The site, its first Owner, and its subdomain go in one save, so no site is ever without either: the database
        // guards losing an owner or a canonical host, not having one (tenancy.md, "Tenant root").
        data.StageNewSite(
            site,
            new SiteMember
            {
                SiteId = id,
                AccountId = account,
                Role = SiteRole.Owner,
                Status = SiteMemberStatus.Active,
                CreatedAt = now,
            },
            new SiteHost
            {
                Id = new SiteHostId(Guid.NewGuid()),
                SiteId = id,
                Host = host,
                Kind = SiteHostKind.Subdomain,
                IsCanonical = true,
                Status = SiteHostStatus.Active,
                CreatedAt = now,
            });

        // The request had no site - creating one is account work - and from here it works on the new one, so the audit
        // entry is filed under it like every later site action: creation is the second place a site is entered, beside
        // the site policy's decision (tenancy.md rule 4). If the save then loses the address, the request ends inside a
        // site that was never saved; nothing after this reads or writes through it, and the refusal is its answer.
        siteContextWriter.Enter(id);
        audit.Record(SiteAudit.SiteCreated(account, id), SiteScope.Current);
        switch (await data.SaveNewSiteAsync(id, slug, host, limit, cancellationToken))
        {
            case NewSiteOutcome.Saved:
                // Someone may have asked for the address before it was taken, and the public tier remembers that it named
                // nothing; forgetting it now means the new site answers at once (tenant-provisioning G-02; PP2.12).
                await hostMap.RefreshAsync([host]);
                return SitesDataLayer.Detail(site, SiteRole.Owner, host);
            case NewSiteOutcome.Unavailable:
                return SiteErrors.SlugUnavailable;
            case NewSiteOutcome.OverLimit:
                return AccountErrors.EmailNotVerified;
            case var outcome:
                throw new InvalidOperationException($"Unexpected outcome {outcome}.");
        }
    }

    // The limit on sites for an account whose email address is not confirmed, or null once it is. An account the Accounts
    // module cannot find is treated as unconfirmed: the limit is the safe answer.
    private async Task<OwnedSiteLimit?> UnconfirmedLimitAsync(AccountId account, CancellationToken cancellationToken) =>
        (await confirmedEmail.CheckAsync(account, cancellationToken)).IsSuccess
            ? null
            : new OwnedSiteLimit(account, options.Value.UnconfirmedAccountSiteLimit);

    public async Task<Result<SitePage>> ListAsync(AccountId account, ListSitesQuery query, CancellationToken cancellationToken)
    {
        if (await listValidator.CheckAsync(query, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        string? after = null;
        if (query.Cursor is not null && SiteListCursor.TryRead(query.Cursor, out var slug))
        {
            after = slug;
        }

        var (items, more) = await data.ListAsync(account, after, query.Limit ?? SiteListCursor.DefaultLimit, cancellationToken);
        return new SitePage(items, more ? SiteListCursor.After(items[^1].Slug) : null);
    }

    public async Task<Result<SiteDetail>> GetAsync(AccountId account, string siteSlug, CancellationToken cancellationToken)
    {
        // The policy already looked this up and entered its site; asking again reads the request's cached answer. A
        // mismatch would mean the route and the policy disagree about the site, which is a bug.
        if (await memberships.FindAsync(account, siteSlug, cancellationToken) is not { } membership)
        {
            return SiteErrors.SiteNotFound;
        }

        if (membership.SiteId != siteContext.Required)
        {
            throw new InvalidOperationException("The request's site is not the one its route names.");
        }

        return await data.FindDetailAsync(membership.SiteId, membership.Role, cancellationToken) is { } detail
            ? detail
            : SiteErrors.SiteNotFound;
    }

    public async Task<Result<SlugAvailability>> CheckAvailabilityAsync(
        SlugAvailabilityQuery query,
        CancellationToken cancellationToken)
    {
        if (await availabilityValidator.CheckAsync(query, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var stored = query.Slug.ToLowerInvariant();
        return new SlugAvailability(stored, await FindAvailableAsync(stored, cancellationToken) is not null);
    }

    // The slug's stored form when a site could take it now, or null when not - reserved, held by a site of any status,
    // or its subdomain held - one answer for every reason (SITE-002). The shape was checked already.
    private async Task<string?> FindAvailableAsync(string requested, CancellationToken cancellationToken)
    {
        var policy = SlugPolicy.Check(requested);
        if (!policy.IsSuccess)
        {
            return null;
        }

        var slug = policy.Value;
        return await data.IsTakenAsync(slug, SiteSubdomains.For(slug, options.Value.SubdomainZone), cancellationToken)
            ? null
            : slug;
    }
}
