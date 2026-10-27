using Microsoft.EntityFrameworkCore;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Repositories;

/// <summary>How saving a new site ended.</summary>
internal enum NewSiteOutcome
{
    Saved,

    /// <summary>
    /// The slug or its host was taken, or reserved, between the check and the save. Nothing was saved.
    /// </summary>
    Unavailable,

    /// <summary>The account already owns as many sites as its limit allows. Nothing was saved.</summary>
    OverLimit,
}

/// <summary>At most <paramref name="Max"/> sites owned by <paramref name="Account"/>, the new one included.</summary>
internal readonly record struct OwnedSiteLimit(AccountId Account, int Max);

/// <summary>
/// Commits what the Sites repositories staged for one facade call, and turns EF Core's save failures into outcomes, so
/// nothing above the repositories sees EF Core.
/// </summary>
internal sealed class SitesUnitOfWork(PantryPressDbContext db)
{
    /// <summary>
    /// Saves a new site, its first Owner, and its subdomain - and whatever else the facade staged with them - in one
    /// transaction. The unique slug and host indexes and the reserved-slug trigger are the last word on two sign-ups
    /// racing for one address (PP2.8, PP2.9). Asking again keeps this provider-neutral and keeps the database's own
    /// message from ever reaching a caller: if the address is now held or reserved, this one lost the race - unless the
    /// site holding it is this one. The context retries a save whose connection failed, and a commit that landed before
    /// the failure meets its own rows on the retry: that is a save, never a refusal of the caller's own new site.
    /// <para>
    /// With a <paramref name="limit"/>, the account's owned sites are counted again inside the transaction, under update
    /// and range locks on the account's memberships, so two creates at the same instant cannot both pass the facade's
    /// count: the second waits for the first to commit and then sees its site.
    /// </para>
    /// </summary>
    public async Task<NewSiteOutcome> SaveNewSiteAsync(
        SiteId site,
        string slug,
        string host,
        OwnedSiteLimit? limit,
        CancellationToken cancellationToken)
    {
        try
        {
            if (limit is not { } cap)
            {
                await db.SaveChangesAsync(cancellationToken);
                return NewSiteOutcome.Saved;
            }

            // The context retries on a transient fault, so a transaction of its own runs inside the retry strategy, which
            // runs all of this again from the top.
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(
                async token =>
                {
                    if (await db.Set<Site>().AnyAsync(s => s.Id == site, token))
                    {
                        return NewSiteOutcome.Saved;
                    }

                    await using var transaction = await db.Database.BeginTransactionAsync(token);
                    var owned = await db.Set<SiteMember>()
                        .FromSql($"""
                            SELECT * FROM [SiteMembers] WITH (UPDLOCK, HOLDLOCK)
                            WHERE [AccountId] = {cap.Account.Value} AND [Role] = {(int)SiteRole.Owner}
                            """)
                        .CountAsync(token);
                    if (owned >= cap.Max)
                    {
                        // Nothing staged may go out with a later save.
                        db.ChangeTracker.Clear();
                        return NewSiteOutcome.OverLimit;
                    }

                    await db.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                    return NewSiteOutcome.Saved;
                },
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await db.Set<Site>().AnyAsync(s => s.Id == site && s.Slug == slug, cancellationToken))
            {
                return NewSiteOutcome.Saved;
            }

            if (await db.Set<Site>().AnyAsync(s => s.Slug == slug, cancellationToken)
                || await db.Set<SiteHost>().AnyAsync(h => h.Host == host, cancellationToken)
                || await db.Set<ReservedSlug>().AnyAsync(r => r.Slug == slug, cancellationToken))
            {
                return NewSiteOutcome.Unavailable;
            }

            throw;
        }
    }
}
