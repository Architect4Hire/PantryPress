using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
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

/// <summary>How saving an accepted invitation ended (PP2.11).</summary>
internal enum AcceptanceOutcome
{
    Saved,

    /// <summary>The account became a member of the site some other way in the meantime. Nothing was saved.</summary>
    AlreadyMember,

    /// <summary>The invitation was used or replaced in the meantime. Nothing was saved.</summary>
    NoLongerOpen,
}

/// <summary>How saving a member's new role, or their removal, ended (PP2.11b).</summary>
internal enum MemberChangeOutcome
{
    Saved,

    /// <summary>The change would now leave the site with no active owner. Nothing was saved.</summary>
    LastOwner,

    /// <summary>The member was removed in the meantime, and the change was a new role. Nothing was saved.</summary>
    NotFound,

    /// <summary>Someone else changed the same member in the meantime. Nothing was saved.</summary>
    Changed,
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
    /// Saves a new invitation the facade staged - with its email and audit entry - after revoking every invitation still
    /// pending for the same address on the request's site, in one transaction, so the old links stop working exactly when
    /// the new one is made. The revoke is one statement over the filtered set, so an invitation accepted at that moment is
    /// either accepted first, and left alone, or revoked first, and its acceptance then fails on its version. Two invitations
    /// to one address at once take turns under a lock, so the later revokes the earlier. A retry whose first commit landed
    /// meets its own invitation, and counts as saved.
    /// </summary>
    public Task SaveInvitationAsync(
        SiteId site,
        SiteInvitationId invitation,
        string normalizedEmail,
        CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(
            async token =>
            {
                if (await db.Set<SiteInvitation>().AnyAsync(i => i.Id == invitation, token))
                {
                    db.ChangeTracker.AcceptAllChanges();
                    return;
                }

                await using var transaction = await db.Database.BeginTransactionAsync(token);

                // Invitations to one address on one site take turns, so each revokes whatever the one before it left open
                // and the filtered unique index - at most one pending per address - is never what settles it. The lock is
                // named by the site and a hash of the address, never the address itself, and ends with the transaction.
                var resource = $"sites.invitation:{site.Value:N}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)))}";
                await db.Database.ExecuteSqlAsync(
                    $"EXEC sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction'",
                    token);

                await db.Set<SiteInvitation>()
                    .Where(i => i.Status == SiteInvitationStatus.Pending && i.NormalizedEmail == normalizedEmail)
                    .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, SiteInvitationStatus.Revoked), token);

                // Changes are accepted only once the transaction commits, so a retried attempt saves them again.
                await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, token);
                await transaction.CommitAsync(token);
                db.ChangeTracker.AcceptAllChanges();
            },
            cancellationToken);

    /// <summary>
    /// Saves an accepted invitation and the membership it grants (PP2.11). The invitation's version and the membership's key
    /// are the last word on two requests at once: if either save fails, this asks again what happened. A retry that meets
    /// its own membership and acceptance counts as saved; a membership that came another way is
    /// <see cref="AcceptanceOutcome.AlreadyMember"/>; an invitation no longer pending is
    /// <see cref="AcceptanceOutcome.NoLongerOpen"/>.
    /// </summary>
    public async Task<AcceptanceOutcome> SaveAcceptanceAsync(
        SiteId site,
        SiteInvitationId invitation,
        AccountId account,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return AcceptanceOutcome.Saved;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var joined = await db.Set<SiteMember>().AnyAsync(m => m.SiteId == site && m.AccountId == account, cancellationToken);
            if (joined && await db.Set<SiteInvitation>().AnyAsync(
                i => i.Id == invitation && i.Status == SiteInvitationStatus.Accepted && i.AcceptedBy == account,
                cancellationToken))
            {
                return AcceptanceOutcome.Saved;
            }

            if (joined)
            {
                return AcceptanceOutcome.AlreadyMember;
            }

            if (!await db.Set<SiteInvitation>().AnyAsync(
                i => i.Id == invitation && i.Status == SiteInvitationStatus.Pending,
                cancellationToken))
            {
                return AcceptanceOutcome.NoLongerOpen;
            }

            throw;
        }
    }

    /// <summary>
    /// Saves a member's new role, or their removal, that the facade staged with its audit entry (PP2.11b). The facade
    /// applied <c>SiteRoles</c>' last-owner rule to what it read; a change racing this one can still make it untrue, and
    /// then the database has the last word: the owner trigger refuses (51003), two owners leaving at the same instant
    /// deadlock and one is rolled back (1205), and the member's version refuses a save from a stale read. This asks the
    /// database again rather than reading its error numbers, as for a new site, though the asking is SQL Server's own
    /// locking read: whatever ended the save, if the change would now leave the site with no active owner, that is the answer - and if the member already stands as asked, the change is made, by this
    /// request's retried commit or by someone else's. A removal of a member who is gone is made - by a retried commit of
    /// this one, or by another Owner removing them at the same moment; a new role for one is
    /// <see cref="MemberChangeOutcome.NotFound"/>. A stale read, or a fault that outlasted its retries with the member still
    /// as read, is <see cref="MemberChangeOutcome.Changed"/>: trying again may work. What nothing explains is thrown.
    /// </summary>
    /// <param name="newRole">The member's new role, or null for their removal.</param>
    public async Task<MemberChangeOutcome> SaveMemberChangeAsync(
        SiteId site,
        AccountId member,
        SiteRole? newRole,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return MemberChangeOutcome.Saved;
        }
        catch (Exception failure) when (failure is DbUpdateException or RetryLimitExceededException)
        {
            // Nothing staged may go out with a later save: not the change, and not its audit entry.
            db.ChangeTracker.Clear();

            // Read as the owner trigger reads, with locks: a committed-snapshot read would see the site from before a change
            // still in flight - the one that won a deadlock against this save, say - and answer as if it never happened. A
            // locking read waits for it to end. The site's memberships are few, and read once, so the member and the owner
            // count agree.
            var rows = await db.Set<SiteMember>()
                .FromSql($"SELECT * FROM [SiteMembers] WITH (READCOMMITTEDLOCK) WHERE [SiteId] = {site.Value}")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            if (rows.SingleOrDefault(m => m.AccountId == member) is not { } current)
            {
                return newRole is null ? MemberChangeOutcome.Saved : MemberChangeOutcome.NotFound;
            }

            if (newRole == current.Role)
            {
                return MemberChangeOutcome.Saved;
            }

            var owners = rows.Count(m => m.Role == SiteRole.Owner && m.Status == SiteMemberStatus.Active);
            var rule = newRole is { } role
                ? SiteRoles.CanChangeRole(current.Role, current.Status, role, owners)
                : SiteRoles.CanRemove(current.Role, current.Status, owners);
            if (!rule.IsSuccess)
            {
                return MemberChangeOutcome.LastOwner;
            }

            if (failure is DbUpdateConcurrencyException or RetryLimitExceededException)
            {
                return MemberChangeOutcome.Changed;
            }

            throw;
        }
    }

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
