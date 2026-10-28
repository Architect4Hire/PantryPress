using FluentValidation;
using Microsoft.Extensions.Logging;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Outbox;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <summary>
/// Password reset and change (ACCT-001; PP1.3). Forgot-password answers the same way for every address, like sign-up.
/// Both reset and change rotate the security stamp, which voids outstanding links (and, from PP1.6, other sessions), and
/// email a notice so the owner hears about a change they did not make. Both are audited with the save (PP1.11).
/// </summary>
internal sealed partial class PasswordFacade(
    IValidator<ForgotPasswordRequest> forgotValidator,
    IValidator<ResetPasswordRequest> resetValidator,
    IValidator<ChangePasswordRequest> changeValidator,
    AccountsDataLayer data,
    IClock clock,
    IOutbox outbox,
    IAuditLog audit,
    ILogger<PasswordFacade> logger) : IPasswordFacade
{
    public async Task<Result> ForgotAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        if (await forgotValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var user = await data.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Result.Success;
        }

        var throttle = await data.GetEmailThrottleAsync(user, ThrottledEmail.PasswordReset);
        if (!EmailThrottle.TryTake(throttle, clock.UtcNow, out var allowance))
        {
            LogThrottled(logger, user.Id);
            return Result.Success;
        }

        // Staged with the throttle it spends; the Worker makes the one-hour link when it sends the email (PP1.10b), and
        // sends nothing if the account's stamp has moved since now - which would have voided a link made now.
        await data.SetEmailThrottleAsync(user, ThrottledEmail.PasswordReset, allowance);
        outbox.Enqueue(
            new AccountEmailMessage(
                user.Id,
                AccountEmailKind.PasswordReset,
                clock.UtcNow,
                LinkRules.StampFingerprint(await data.GetSecurityStampAsync(user))),
            SiteScope.None);
        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            LogNotSent(logger, user.Id);
        }

        return Result.Success;
    }

    public async Task<Result> ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        if (await resetValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var user = await data.FindAsync(new AccountId(request.AccountId));
        if (user is null || !await data.IsValidPasswordResetTokenAsync(user, request.Token))
        {
            return AccountErrors.InvalidResetLink;
        }

        var confirmedBefore = user.EmailConfirmed;
        var reset = await data.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!reset.Succeeded)
        {
            return PasswordRefused(reset, "newPassword");
        }

        // The link reached this inbox, so the address is confirmed, and the person who reads it is let back in.
        await data.MarkEmailConfirmedAsync(user);
        await data.ClearLockoutAsync(user);

        // The holder of the emailed link is the account acting on itself.
        var account = new AccountId(user.Id);
        if (TwoFactorRules.ResetClearsSecondFactor(confirmedBefore, user.TwoFactorEnabled))
        {
            // A second factor set up before anyone proved they read this inbox was not necessarily the owner's.
            await data.SetTwoFactorEnabledAsync(user, enabled: false);
            await data.ForgetSecondFactorsAsync(user);
            audit.Record(AccountAudit.OwnChange(AccountAudit.TwoFactorDisabled, account), SiteScope.None);
        }

        audit.Record(AccountAudit.OwnChange(AccountAudit.PasswordReset, account), SiteScope.None);
        outbox.Enqueue(new AccountEmailMessage(user.Id, AccountEmailKind.PasswordChanged, clock.UtcNow), SiteScope.None);
        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AccountErrors.ChangedElsewhere;
        }

        LogPasswordReset(logger, user.Id);
        return Result.Success;
    }

    public async Task<Result> ChangeAsync(
        AccountId account,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (await changeValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var user = await data.FindAsync(account);
        if (user is null)
        {
            return AccountErrors.AccountNotFound;
        }

        if (await AccountGuards.CheckPasswordAsync(data, audit, clock, user, request.CurrentPassword, cancellationToken) is { } refused)
        {
            return refused;
        }

        var changed = await data.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
        {
            return PasswordRefused(changed, "newPassword");
        }

        await data.ClearFailedAttemptsAsync(user);
        audit.Record(AccountAudit.OwnChange(AccountAudit.PasswordChanged, account), SiteScope.None);
        outbox.Enqueue(new AccountEmailMessage(user.Id, AccountEmailKind.PasswordChanged, clock.UtcNow), SiteScope.None);
        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AccountErrors.ChangedElsewhere;
        }

        LogPasswordChanged(logger, user.Id);
        return Result.Success;
    }

    // The validator already enforces the length rules; this covers anything Identity's own validators add.
    private static Error PasswordRefused(Microsoft.AspNetCore.Identity.IdentityResult result, string field) =>
        result.Errors.Any(e => e.Code == "InvalidToken")
            ? AccountErrors.InvalidResetLink
            : Error.Validation(new Dictionary<string, string[]> { [field] = ["Choose a different password."] });

    [LoggerMessage(Level = LogLevel.Information, Message = "A password reset email for account {AccountId} was throttled.")]
    private static partial void LogThrottled(ILogger logger, Guid accountId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A reset email for account {AccountId} was not sent: the account changed at the same time. Asking again sends one.")]
    private static partial void LogNotSent(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The password for account {AccountId} was reset.")]
    private static partial void LogPasswordReset(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The password for account {AccountId} was changed.")]
    private static partial void LogPasswordChanged(ILogger logger, Guid accountId);
}
