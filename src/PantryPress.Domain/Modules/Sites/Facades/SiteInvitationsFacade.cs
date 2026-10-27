using FluentValidation;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.DataLayer;

namespace PantryPress.Domain.Modules.Sites.Facades;

/// <inheritdoc cref="ISiteInvitationsFacade"/>
internal sealed class SiteInvitationsFacade(
    IValidator<InviteMemberRequest> inviteValidator,
    SiteInvitationsDataLayer data,
    ISiteMembershipFacade memberships,
    SiteInvitationAcceptance acceptance,
    ISiteContext siteContext,
    IOutbox outbox,
    IAuditLog audit,
    IClock clock,
    IOptions<SitesOptions> options) : ISiteInvitationsFacade
{
    public async Task<Result<SiteInvitationDetail>> InviteAsync(
        AccountId account,
        string siteSlug,
        InviteMemberRequest request,
        CancellationToken cancellationToken)
    {
        if (await inviteValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        // The Owner policy already looked this up and entered its site; asking again reads the request's cached answer.
        // A mismatch would mean the route and the policy disagree about the site, which is a bug.
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

        // PP14: ask the entitlements facade whether the site may have another member (ARCH-010), here.
        var now = clock.UtcNow;
        var normalizedEmail = EmailAddresses.Normalize(request.Email);
        if (await data.CountOpenAsync(now, normalizedEmail, cancellationToken) >= options.Value.MaxOpenInvitations)
        {
            return SiteErrors.TooManyInvitations;
        }

        // Nothing is asked about the address: whether it has an account, or is a member here already, is the invited
        // person's business when they accept, so the answer never tells an Owner which addresses are registered.
        var invitation = new SiteInvitation
        {
            Id = new SiteInvitationId(Guid.NewGuid()),
            Email = request.Email,
            NormalizedEmail = normalizedEmail,
            Role = request.Role!.Value,
            Status = SiteInvitationStatus.Pending,
            InvitedBy = account,
            CreatedAt = now,
            ExpiresAt = SiteInvitationRules.ExpiryFrom(now, options.Value.InvitationLifetime),
        };

        data.StageInvitation(invitation);
        outbox.Enqueue(new SiteInvitationEmailMessage(invitation.Id.Value), SiteScope.Current);
        audit.Record(SiteAudit.Invited(account, invitation.Id), SiteScope.Current);
        await data.SaveInvitationAsync(siteContext.Required, invitation, cancellationToken);
        return new SiteInvitationDetail(invitation.Id.Value, invitation.Role, invitation.ExpiresAt);
    }

    /// <summary>Accepting is its own class, the one that enters a site from a link (tenancy.md rule 4), and only it.</summary>
    public Task<Result<SiteSummary>> AcceptAsync(
        AccountId account,
        AcceptInvitationRequest request,
        CancellationToken cancellationToken) =>
        acceptance.AcceptAsync(account, request, cancellationToken);
}
