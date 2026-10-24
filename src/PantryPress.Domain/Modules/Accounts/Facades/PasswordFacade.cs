using FluentValidation;
using Microsoft.Extensions.Logging;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;
using PantryPress.Domain.Modules.Accounts.Repositories;

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
    AccountEmails emails,
    IEmailQueue emailQueue,
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

        await data.SetEmailThrottleAsync(user, ThrottledEmail.PasswordReset, allowance);
        var token = await data.CreatePasswordResetTokenAsync(user);
        if (await data.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved)
        {
            emailQueue.Enqueue(emails.PasswordReset(Guid.NewGuid(), user.Email!, new AccountId(user.Id), token));
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

        var reset = await data.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!reset.Succeeded)
        {
            return PasswordRefused(reset, "newPassword");
        }

        // The link reached this inbox, so the address is confirmed, and the person who reads it is let back in.
        await data.MarkEmailConfirmedAsync(user);
        await data.ClearLockoutAsync(user);

        // The holder of the emailed link is the account acting on itself.
        audit.Record(AccountAudit.OwnChange(AccountAudit.PasswordReset, new AccountId(user.Id)), siteId: null);
        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AccountErrors.ChangedElsewhere;
        }

        LogPasswordReset(logger, user.Id);
        emailQueue.Enqueue(emails.PasswordChanged(Guid.NewGuid(), user.Email!));
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

        if (await AccountGuards.CheckPasswordAsync(data, audit, user, request.CurrentPassword, cancellationToken) is { } refused)
        {
            return refused;
        }

        var changed = await data.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
        {
            return PasswordRefused(changed, "newPassword");
        }

        await data.ClearFailedAttemptsAsync(user);
        audit.Record(AccountAudit.OwnChange(AccountAudit.PasswordChanged, account), siteId: null);
        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AccountErrors.ChangedElsewhere;
        }

        LogPasswordChanged(logger, user.Id);
        emailQueue.Enqueue(emails.PasswordChanged(Guid.NewGuid(), user.Email!));
        return Result.Success;
    }

    // The validator already enforces the length rules; this covers anything Identity's own validators add.
    private static Error PasswordRefused(Microsoft.AspNetCore.Identity.IdentityResult result, string field) =>
        result.Errors.Any(e => e.Code == "InvalidToken")
            ? AccountErrors.InvalidResetLink
            : Error.Validation(new Dictionary<string, string[]> { [field] = ["Choose a different password."] });

    [LoggerMessage(Level = LogLevel.Information, Message = "A password reset email for account {AccountId} was throttled.")]
    private static partial void LogThrottled(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The password for account {AccountId} was reset.")]
    private static partial void LogPasswordReset(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The password for account {AccountId} was changed.")]
    private static partial void LogPasswordChanged(ILogger logger, Guid accountId);
}
