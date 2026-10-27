using Microsoft.Extensions.Logging;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.DataLayer;

namespace PantryPress.Domain.Modules.Sites.Facades;

/// <summary>
/// Sends one invitation's email from the outbox, in the Worker (PP2.11) - the first site message: the delivery's scope has
/// already entered the site it was staged under, so the invitation reads through the site filter. Delivery is at least
/// once, so the email's id is the outbox message's id and the provider's <c>Message-ID</c> is the same on every
/// redelivery, as account email does it (backend.md records the exception). The link's token is made here, at delivery,
/// so the outbox never holds one; an invitation no longer open - used, replaced, expired, or its site deleted - is not
/// sent at all.
/// </summary>
internal sealed partial class SiteInvitationEmailHandler(
    SiteInvitationMailDataLayer data,
    SiteInvitationEmails emails,
    IClock clock,
    ILogger<SiteInvitationEmailHandler> logger) : IOutboxHandler<SiteInvitationEmailMessage>
{
    public async Task HandleAsync(
        SiteInvitationEmailMessage message,
        OutboxDelivery delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(delivery);
        if (delivery.SiteId is not { } site)
        {
            // Staged without a site, which InviteAsync never does: nothing a retry could find.
            throw new OutboxPermanentFailureException("An invitation email was staged without its site.");
        }

        var id = new SiteInvitationId(message.InvitationId);
        if (await data.ReadInvitationAsync(id, cancellationToken) is not { } invitation
            || !SiteInvitationRules.IsOpen(invitation.Status, invitation.ExpiresAt, clock.UtcNow)
            || await data.FindSiteAsync(site, cancellationToken) is not { } row)
        {
            // Nobody to invite any more. Nothing to retry, and nothing for an operator to do either.
            LogNotOpen(logger, delivery.MessageId);
            return;
        }

        EmailMessage email;
        try
        {
            email = emails.Invitation(
                delivery.MessageId,
                invitation.Email,
                row.Name,
                invitation.Role,
                SiteInvitationRules.LifetimeInDays(invitation.CreatedAt, invitation.ExpiresAt),
                data.CreateToken(site, id));
        }
        catch (ArgumentException)
        {
            // A stored address the email refuses will be refused on every attempt, so the message is poisoned now.
            throw new OutboxPermanentFailureException("The invitation's address cannot be written as an email recipient.");
        }

        try
        {
            await data.SendAsync(email, cancellationToken);
        }
        catch (EmailDeliveryException ex) when (!ex.IsTransient)
        {
            // A refused recipient will be refused again. A transient failure is rethrown as it is, and retried.
            throw new OutboxPermanentFailureException("The email provider refused the message for good.");
        }
        catch (FormatException)
        {
            throw new OutboxPermanentFailureException("The email provider could not read the recipient's address.");
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Invitation email {MessageId} was not sent: the invitation is no longer open, or its site is gone.")]
    private static partial void LogNotOpen(ILogger logger, Guid messageId);
}
