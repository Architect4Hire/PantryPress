using FluentValidation;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.DataLayer;

namespace PantryPress.Domain.Modules.Sites.Facades;

/// <inheritdoc cref="ISiteMembersFacade"/>
internal sealed class SiteMembersFacade(
    IValidator<ListSiteMembersQuery> listValidator,
    IValidator<ChangeMemberRoleRequest> roleValidator,
    SiteMembersDataLayer data,
    ISiteMembershipFacade memberships,
    IAccountDirectory directory,
    ISiteContext siteContext,
    IAuditLog audit) : ISiteMembersFacade
{
    public async Task<Result<SiteMemberPage>> ListAsync(
        AccountId account,
        string siteSlug,
        ListSiteMembersQuery query,
        CancellationToken cancellationToken)
    {
        if (await listValidator.CheckAsync(query, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var owned = await OwnedSiteAsync(account, siteSlug, cancellationToken);
        if (!owned.IsSuccess)
        {
            return owned.Error;
        }

        SiteMemberPosition? after = SiteMemberCursor.TryRead(query.Cursor, out var position) ? position : null;
        var (items, more) = await data.ListAsync(
            owned.Value,
            after,
            query.Limit ?? SiteMemberCursor.DefaultLimit,
            cancellationToken);

        // Who each member is, from Accounts, for this page's members only (IAccountDirectory).
        var addresses = await directory.FindAddressesAsync([.. items.Select(m => m.AccountId)], cancellationToken);
        var page = items
            .Select(m => new SiteMemberItem(
                m.AccountId.Value,
                addresses.GetValueOrDefault(m.AccountId),
                m.Role,
                m.Status,
                m.CreatedAt,
                m.AccountId == account))
            .ToList();
        var next = more ? SiteMemberCursor.After(new SiteMemberPosition(items[^1].CreatedAt, items[^1].AccountId)) : null;
        return new SiteMemberPage(page, next);
    }

    public async Task<Result> ChangeRoleAsync(
        AccountId account,
        string siteSlug,
        string memberId,
        ChangeMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (await roleValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var owned = await OwnedSiteAsync(account, siteSlug, cancellationToken);
        if (!owned.IsSuccess)
        {
            return owned.Error;
        }

        var site = owned.Value;
        var newRole = request.Role!.Value;
        if (await FindMemberAsync(site, memberId, cancellationToken) is not { } member)
        {
            return SiteErrors.MemberNotFound;
        }

        // A repeat changes nothing, and records nothing.
        if (member.Role == newRole)
        {
            return Result.Success;
        }

        var rule = SiteRoles.CanChangeRole(
            member.Role,
            member.Status,
            newRole,
            await data.CountActiveOwnersAsync(site, cancellationToken));
        if (!rule.IsSuccess)
        {
            return rule;
        }

        audit.Record(SiteAudit.RoleChanged(account, member.AccountId, member.Role, newRole), SiteScope.Current);
        SiteMembersDataLayer.StageRoleChange(member, newRole);
        return Answer(await data.SaveMemberChangeAsync(site, member.AccountId, newRole, cancellationToken));
    }

    public async Task<Result> RemoveAsync(
        AccountId account,
        string siteSlug,
        string memberId,
        CancellationToken cancellationToken)
    {
        var owned = await OwnedSiteAsync(account, siteSlug, cancellationToken);
        if (!owned.IsSuccess)
        {
            return owned.Error;
        }

        var site = owned.Value;
        if (await FindMemberAsync(site, memberId, cancellationToken) is not { } member)
        {
            return SiteErrors.MemberNotFound;
        }

        var rule = SiteRoles.CanRemove(member.Role, member.Status, await data.CountActiveOwnersAsync(site, cancellationToken));
        if (!rule.IsSuccess)
        {
            return rule;
        }

        data.StageRemoval(member);
        audit.Record(SiteAudit.Removed(account, member.AccountId), SiteScope.Current);
        return Answer(await data.SaveMemberChangeAsync(site, member.AccountId, newRole: null, cancellationToken));
    }

    // The site the route's Owner policy admitted the caller to. The policy already looked this up and entered its site;
    // asking again reads the request's cached answer. A mismatch would mean the route and the policy disagree about the
    // site, which is a bug.
    private async Task<Result<SiteId>> OwnedSiteAsync(AccountId account, string siteSlug, CancellationToken cancellationToken)
    {
        if (await memberships.FindAsync(account, siteSlug, cancellationToken) is not { } membership)
        {
            return SiteErrors.SiteNotFound;
        }

        if (membership.Role != SiteRole.Owner)
        {
            return SiteErrors.SiteRoleRequired;
        }

        if (membership.SiteId != siteContext.Required)
        {
            throw new InvalidOperationException("The request's site is not the one its route names.");
        }

        return membership.SiteId;
    }

    // The member the route's id names on the entered site, tracked, or null. The id is read only in the one spelling the
    // list writes - lower-case, hyphenated - so a member has one address and a retry's key one request; any other
    // spelling, an unknown id, and another site's member are all no member here, and one answer.
    private Task<SiteMember?> FindMemberAsync(SiteId site, string memberId, CancellationToken cancellationToken) =>
        Guid.TryParseExact(memberId, "D", out var id) && id != Guid.Empty
            && string.Equals(id.ToString("D"), memberId, StringComparison.Ordinal)
            ? data.FindMemberAsync(site, new AccountId(id), cancellationToken)
            : Task.FromResult<SiteMember?>(null);

    private static Result Answer(MemberChangeOutcome outcome) => outcome switch
    {
        MemberChangeOutcome.Saved => Result.Success,
        MemberChangeOutcome.LastOwner => SiteErrors.LastOwner,
        MemberChangeOutcome.NotFound => SiteErrors.MemberNotFound,
        MemberChangeOutcome.Changed => SiteErrors.MemberChanged,
        _ => throw new InvalidOperationException($"Unexpected outcome {outcome}."),
    };
}
