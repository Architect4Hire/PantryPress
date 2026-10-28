using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PantryPress.Domain.Managers.Persistence;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Repositories;

/// <summary>
/// Commits what the Sites repositories staged for one facade call, and turns EF Core's save failures into outcomes, so
/// nothing above the repositories sees EF Core.
/// </summary>
internal sealed class SitesUnitOfWork(PantryPressDbContext db, ISiteContext siteContext, SiteInvitationRepository invitations)
{
    /// <summary>
    /// Saves a new invitation the facade staged - with its email and audit entry - after revoking every invitation still
    /// pending for the same address on the request's site, in one transaction, so the old links stop working exactly when
    /// the new one is made. The revoke is one statement over the filtered set, so an invitation accepted at that moment is
    /// either accepted first, and left alone, or revoked first, and its acceptance then fails on its version. Invitations on
    /// one site take turns under a lock, so the later revokes the earlier, and the site's open invitations are counted
    /// again under it: two Owners inviting at once cannot both pass the facade's count (PP2.14). A retry whose first commit
    /// landed meets its own invitation, and counts as saved. The lock is named by the site the request entered, the same
    /// one the filter reads, so the two can never disagree.
    /// </summary>
    public async Task<InvitationOutcome> SaveInvitationAsync(
        SiteInvitationId invitation,
        string normalizedEmail,
        DateTimeOffset now,
        int maxOpen,
        CancellationToken cancellationToken)
    {
        InvitationOutcome outcome;
        try
        {
            outcome = await db.Database.CreateExecutionStrategy().ExecuteAsync(
                async token =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(token);
                    await LockAsync($"sites.invitations:{siteContext.Required.Value:N}", token);

                    // Asked under the lock: a retry whose first commit was still in flight waits for it here, then meets
                    // its own invitation, rather than revoking it and saving it again (PP2.14).
                    if (await db.Set<SiteInvitation>().AnyAsync(i => i.Id == invitation, token))
                    {
                        return InvitationOutcome.Saved;
                    }

                    var open = await invitations.CountOpenAsync(now, normalizedEmail, token);
                    if (!SiteInvitationRules.HasRoomForAnother(open, maxOpen))
                    {
                        return InvitationOutcome.TooMany;
                    }

                    await db.Set<SiteInvitation>()
                        .Where(i => i.Status == SiteInvitationStatus.Pending && i.NormalizedEmail == normalizedEmail)
                        .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, SiteInvitationStatus.Revoked), token);

                    // Changes are accepted only once the transaction commits, so a retried attempt saves them again.
                    await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, token);
                    await transaction.CommitAsync(token);
                    return InvitationOutcome.Saved;
                },
                cancellationToken);
        }
        catch
        {
            // Nothing staged may go out with a later save, whatever ended this one.
            db.ChangeTracker.Clear();
            throw;
        }

        if (outcome == InvitationOutcome.Saved)
        {
            db.ChangeTracker.AcceptAllChanges();
        }
        else
        {
            // Nothing staged may go out with a later save: not the invitation, its email, or its audit entry.
            db.ChangeTracker.Clear();
        }

        return outcome;
    }

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
    /// racing for one address (PP2.8, PP2.9). Asking again rather than reading SQL Server's error numbers keeps the
    /// database's own message from ever reaching a caller: if the address is now held or reserved, this one lost the race - unless the
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
                    await using var transaction = await db.Database.BeginTransactionAsync(token);

                    // The count is the lock: the locking read of this raw query is why it is not the repository's own.
                    var owned = await db.Set<SiteMember>()
                        .FromSql($"""
                            SELECT * FROM [SiteMembers] WITH (UPDLOCK, HOLDLOCK)
                            WHERE [AccountId] = {cap.Account.Value} AND [Role] = {(int)SiteRole.Owner}
                            """)
                        .CountAsync(token);

                    // Asked under the lock: a retry whose first commit was still in flight waits for it there, then meets
                    // its own site here - counted among the owned ones, so asking after the comparison would refuse the
                    // creator's own new site (PP2.14).
                    if (await db.Set<Site>().AnyAsync(s => s.Id == site, token))
                    {
                        db.ChangeTracker.AcceptAllChanges();
                        return NewSiteOutcome.Saved;
                    }

                    if (!cap.Allows(owned))
                    {
                        // Nothing staged may go out with a later save.
                        db.ChangeTracker.Clear();
                        return NewSiteOutcome.OverLimit;
                    }

                    // Changes are accepted only once the transaction commits. Accepted at the save, a commit that failed
                    // would leave nothing to save on the retry, which would then commit an empty transaction and call the
                    // site saved (PP2.14).
                    await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, token);
                    await transaction.CommitAsync(token);
                    db.ChangeTracker.AcceptAllChanges();
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

    /// <summary>
    /// Takes an exclusive application lock that ends with the transaction, and fails rather than carry on without it: one
    /// not granted within five seconds, or a request chosen as a deadlock victim, leaves the lock ungranted (PP2.14).
    /// </summary>
    private Task LockAsync(string resource, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            DECLARE @granted int;
            EXEC @granted = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;
            IF @granted < 0 THROW 51011, 'The application lock was not granted.', 1;
            """,
            cancellationToken);
}
