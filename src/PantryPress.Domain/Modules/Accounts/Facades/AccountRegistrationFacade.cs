using FluentValidation;
using Microsoft.Extensions.Logging;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;
using PantryPress.Domain.Modules.Accounts.Repositories;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <summary>
/// Sign-up and email confirmation (ACCT-001, ACCT-003; PP1.2). No enumeration: a new address and a registered one get
/// the same answer, both hash the password, and email is sent by the Worker from the outbox (PP1.10b), so neither the
/// body, the status, nor the time to send reveals which addresses have accounts.
/// </summary>
internal sealed partial class AccountRegistrationFacade(
    IValidator<RegisterAccountRequest> registerValidator,
    IValidator<VerifyEmailRequest> verifyValidator,
    IValidator<ResendVerificationRequest> resendValidator,
    AccountsDataLayer data,
    IClock clock,
    IOutbox outbox,
    ILogger<AccountRegistrationFacade> logger) : IAccountRegistrationFacade
{
    public async Task<Result> RegisterAsync(RegisterAccountRequest request, CancellationToken cancellationToken)
    {
        if (await registerValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (await data.FindByEmailAsync(request.Email) is not null)
        {
            data.HashPasswordForTiming(request.Password);
            return Result.Success;
        }

        // The email doubles as the Identity user name, which Identity requires and keeps unique.
        var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
        var created = await data.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            // Duplicate errors mean another sign-up for the address got there first: the same answer as "existing".
            // Anything else is a password rule Identity holds beyond the validator's.
            return created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName")
                ? Result.Success
                : Error.Validation(new Dictionary<string, string[]>
                {
                    ["password"] = ["Choose a different password."],
                });
        }

        // The first confirmation email is staged with the account, so the two are saved together or not at all. Staging
        // validates the account again, and a concurrent sign-up for the address may have been saved meanwhile: the same
        // answer as "existing", as at creation.
        if (await StageVerificationAsync(user) == Staged.AddressTaken
            || await data.SaveNewAccountAsync(request.Email, cancellationToken) == SaveOutcome.EmailTaken)
        {
            return Result.Success;
        }

        LogRegistered(logger, user.Id);
        return Result.Success;
    }

    public async Task<Result> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        if (await verifyValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var user = await data.FindAsync(new AccountId(request.AccountId));
        if (user is null || !await data.ConfirmEmailAsync(user, request.Token))
        {
            return AccountErrors.InvalidVerificationLink;
        }

        // Confirming twice with a valid link is not an error: the first save may have succeeded on a lost response.
        return await data.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
            ? Result.Success
            : AccountErrors.ChangedElsewhere;
    }

    public async Task<Result> ResendVerificationAsync(ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        if (await resendValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var user = await data.FindByEmailAsync(request.Email);
        if (user is { EmailConfirmed: false })
        {
            await SendVerificationAsync(user, cancellationToken);
        }

        return Result.Success;
    }

    // A resend: staged with the throttle it spends and saved, so both are saved or neither is. A failed save sends
    // nothing, and the person can simply ask again.
    private async Task SendVerificationAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (await StageVerificationAsync(user) == Staged.Yes
            && await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            LogNotSent(logger, user.Id);
        }
    }

    // The email, and the throttle it spends, staged for the caller's one save. The Worker makes the link when it sends
    // the email (PP1.10b).
    private async Task<Staged> StageVerificationAsync(ApplicationUser user)
    {
        var throttle = await data.GetEmailThrottleAsync(user, ThrottledEmail.Verification);
        if (!EmailThrottle.TryTake(throttle, clock.UtcNow, out var allowance))
        {
            LogThrottled(logger, user.Id);
            return Staged.Throttled;
        }

        var set = await data.TrySetEmailThrottleAsync(user, ThrottledEmail.Verification, allowance);
        if (!set.Succeeded)
        {
            return set.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName")
                ? Staged.AddressTaken
                : throw new InvalidOperationException(
                    $"An account change was refused: {string.Join(", ", set.Errors.Select(e => e.Code))}.");
        }

        outbox.Enqueue(new AccountEmailMessage(user.Id, AccountEmailKind.Verification, clock.UtcNow), siteId: null);
        return Staged.Yes;
    }

    private enum Staged
    {
        Yes,
        Throttled,

        /// <summary>A new account's address was taken by a concurrent sign-up while it was being staged.</summary>
        AddressTaken,
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {AccountId} was created.")]
    private static partial void LogRegistered(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "A confirmation email for account {AccountId} was throttled.")]
    private static partial void LogThrottled(ILogger logger, Guid accountId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A confirmation email for account {AccountId} was not sent: the account changed at the same time. Asking again sends one.")]
    private static partial void LogNotSent(ILogger logger, Guid accountId);
}
