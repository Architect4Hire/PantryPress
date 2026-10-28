using FluentValidation;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.DataLayer;

namespace PantryPress.Domain.Modules.Sites.Facades;

/// <summary>
/// Joining a site through an invitation's link (SITE-010; PP2.11): <see cref="ISiteInvitationsFacade.AcceptAsync"/>. Its
/// own class because it is a resolver - the third place a request enters a site (tenancy.md rule 4) - and the boundary
/// test lists resolvers by file: the caller is no member yet, so no site policy can enter it, and the site comes from the
/// link the server sealed, never from anything else the request carries.
/// </summary>
internal sealed class SiteInvitationAcceptance(
    IValidator<AcceptInvitationRequest> validator,
    SiteInvitationsDataLayer data,
    IAccountAddressCheck addresses,
    ISiteContextWriter siteContextWriter,
    IAuditLog audit,
    IClock clock)
{
    public async Task<Result<SiteSummary>> AcceptAsync(
        AccountId account,
        AcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        // The site's row is the tenant root, read by the sealed id before any context exists, so a deleted site is never
        // entered at all. A live one is entered before its invitation is read, so the invitation comes through the site
        // filter like any site-owned row. If what follows refuses, the request ends inside that site; nothing after a
        // refusal reads or writes through it.
        if (!data.TryReadToken(request.Token, out var siteId, out var invitationId)
            || await data.FindSiteAsync(siteId, cancellationToken) is not { } site)
        {
            return SiteErrors.InvitationInvalid;
        }

        siteContextWriter.Enter(siteId);

        // An invitation adds a member and never changes one: not a role, and not a suspension. Asked first, so the retry
        // of an accept whose answer was lost hears that it already worked.
        if (await data.IsMemberAsync(siteId, account, cancellationToken))
        {
            return SiteErrors.AlreadyMember;
        }

        // One instant for the whole decision, so an invitation found open is still open when it is accepted.
        var now = clock.UtcNow;
        if (await data.FindInvitationAsync(invitationId, cancellationToken) is not { } invitation
            || !SiteInvitationRules.IsOpen(invitation.Status, invitation.ExpiresAt, now))
        {
            return SiteErrors.InvitationInvalid;
        }

        // Whoever follows the link must read the invited inbox: a forwarded or leaked link is worth nothing to anyone else.
        switch (await addresses.MatchAsync(account, invitation.Email, cancellationToken))
        {
            case AccountAddressMatch.Confirmed:
                break;
            case AccountAddressMatch.Unconfirmed:
                return AccountErrors.EmailNotVerified;
            default:
                return SiteErrors.InvitationForOtherAddress;
        }

        // PP14: ask the entitlements facade whether the site may have another member (ARCH-010), here, where one is added.
        SiteInvitationRules.Accept(invitation, account, now);
        data.StageMember(new SiteMember
        {
            SiteId = siteId,
            AccountId = account,
            Role = invitation.Role,
            Status = SiteMemberStatus.Active,
            CreatedAt = now,
        });
        audit.Record(SiteAudit.Joined(account, invitationId), SiteScope.Current);
        return await data.SaveAcceptanceAsync(siteId, invitationId, account, cancellationToken) switch
        {
            AcceptanceOutcome.Saved => await data.SummaryAsync(site, invitation.Role, cancellationToken),
            AcceptanceOutcome.AlreadyMember => SiteErrors.AlreadyMember,
            AcceptanceOutcome.NoLongerOpen => SiteErrors.InvitationInvalid,
            var outcome => throw new InvalidOperationException($"Unexpected outcome {outcome}."),
        };
    }
}
