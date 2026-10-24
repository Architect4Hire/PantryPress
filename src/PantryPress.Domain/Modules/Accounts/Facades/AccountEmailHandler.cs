using Microsoft.Extensions.Logging;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <summary>
/// Sends one account email from the outbox, in the Worker (PP1.10b). Delivery is at least once, so the email's id is
/// the outbox message's id: the provider's <c>Message-ID</c> is the same on every redelivery, and a mailbox that already
/// has it can recognize the repeat. That is the one way this handler is idempotent - a redelivery does send again, with
/// a freshly made link that works as well as the first - because a provider call cannot share a transaction with a
/// record of it (backend.md records the exception). Link tokens are made here, at delivery, so the outbox never holds
/// one; a link email that waited longer than its link would live, or a reset whose account changed since, is not sent.
/// </summary>
internal sealed partial class AccountEmailHandler(
    AccountsDataLayer data,
    AccountMailDataLayer mail,
    AccountEmails emails,
    IClock clock,
    ILogger<AccountEmailHandler> logger) : IOutboxHandler<AccountEmailMessage>
{
    public async Task HandleAsync(AccountEmailMessage message, OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(delivery);
        var account = new AccountId(message.AccountId);

        // Identity's user lookups take no cancellation token; the send below does.
        var user = await data.FindAsync(account);
        if (user?.Email is not { } to)
        {
            // Nobody left to tell. Nothing to retry, and nothing for an operator to do either.
            LogNoRecipient(logger, delivery.MessageId);
            return;
        }

        // Confirmed between the request and the delivery: the link would confirm nothing.
        if (message.Kind == AccountEmailKind.Verification && user.EmailConfirmed)
        {
            return;
        }

        var stamp = await data.GetSecurityStampAsync(user);
        if (LinkRules.IsStale(
            message.Kind,
            message.RequestedAt,
            clock.UtcNow,
            message.StampFingerprint,
            LinkRules.StampFingerprint(stamp)))
        {
            // The person can ask again; a link made now for an old request would be a fresh key to an old door.
            LogStale(logger, delivery.MessageId, message.Kind);
            return;
        }

        EmailMessage email;
        try
        {
            email = message.Kind switch
            {
                AccountEmailKind.Verification =>
                    emails.Verification(delivery.MessageId, to, account, await data.CreateEmailConfirmationTokenAsync(user)),
                AccountEmailKind.PasswordReset =>
                    emails.PasswordReset(delivery.MessageId, to, account, await data.CreatePasswordResetTokenAsync(user)),
                AccountEmailKind.PasswordChanged => emails.PasswordChanged(delivery.MessageId, to),
                AccountEmailKind.TwoFactorTurnedOn => emails.TwoFactorTurnedOn(delivery.MessageId, to),
                AccountEmailKind.TwoFactorTurnedOff => emails.TwoFactorTurnedOff(delivery.MessageId, to),
                AccountEmailKind.RecoveryCodesReplaced => emails.RecoveryCodesReplaced(delivery.MessageId, to),
                AccountEmailKind.RecoveryCodeUsed =>
                    emails.RecoveryCodeUsed(delivery.MessageId, to, await data.CountRecoveryCodesAsync(user)),
                _ => throw new OutboxPermanentFailureException("The account email kind is not one this Worker knows."),
            };
        }
        catch (ArgumentException)
        {
            // A stored address the email refuses will be refused on every attempt, so the message is poisoned now.
            throw new OutboxPermanentFailureException("The account's address cannot be written as an email recipient.");
        }

        try
        {
            await mail.SendAsync(email, cancellationToken);
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
        Message = "Account email {MessageId} was not sent: the account no longer exists.")]
    private static partial void LogNoRecipient(ILogger logger, Guid messageId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Account email {MessageId} ({Kind}) was not sent: its link would be older than allowed, or the account changed since.")]
    private static partial void LogStale(ILogger logger, Guid messageId, AccountEmailKind kind);
}
