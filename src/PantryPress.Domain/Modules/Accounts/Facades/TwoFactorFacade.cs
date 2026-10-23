using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;
using PantryPress.Domain.Modules.Accounts.Repositories;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <summary>
/// Optional two-step sign-in with an authenticator app and recovery codes (ACCT-001; PP1.3). Turning it on needs a
/// working code; turning it off or replacing recovery codes needs the password. Wrong passwords and codes count toward
/// lockout, and every change emails a notice.
/// </summary>
internal sealed partial class TwoFactorFacade(
    IValidator<EnableTwoFactorRequest> enableValidator,
    IValidator<ConfirmPasswordRequest> passwordValidator,
    IValidator<SecondFactorRequest> secondFactorValidator,
    AccountsDataLayer data,
    AccountEmails emails,
    IEmailQueue emailQueue,
    IOptions<Microsoft.AspNetCore.Identity.IdentityOptions> identityOptions,
    ILogger<TwoFactorFacade> logger) : ITwoFactorFacade
{
    public async Task<Result<TwoFactorStatus>> GetStatusAsync(AccountId account, CancellationToken cancellationToken)
    {
        var user = await data.FindAsync(account);
        if (user is null)
        {
            return AccountErrors.AccountNotFound;
        }

        return new TwoFactorStatus(user.TwoFactorEnabled, user.TwoFactorEnabled ? await data.CountRecoveryCodesAsync(user) : 0);
    }

    public async Task<Result<AuthenticatorSetup>> BeginSetupAsync(AccountId account, CancellationToken cancellationToken)
    {
        var user = await data.FindAsync(account);
        if (user is null)
        {
            return AccountErrors.AccountNotFound;
        }

        if (user.TwoFactorEnabled)
        {
            return AccountErrors.TwoFactorAlreadyOn;
        }

        // A fresh key every time: a key shown before (and perhaps seen by someone else) is never turned on later.
        var key = await data.NewAuthenticatorKeyAsync(user);
        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AccountErrors.ChangedElsewhere;
        }

        var issuer = identityOptions.Value.Tokens.AuthenticatorIssuer;
        return new AuthenticatorSetup(FormatKey(key), AuthenticatorUri(issuer, user.Email!, key));
    }

    public async Task<Result<RecoveryCodeList>> EnableAsync(
        AccountId account,
        EnableTwoFactorRequest request,
        CancellationToken cancellationToken)
    {
        if (await enableValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var user = await data.FindAsync(account);
        if (user is null)
        {
            return AccountErrors.AccountNotFound;
        }

        if (user.TwoFactorEnabled)
        {
            return AccountErrors.TwoFactorAlreadyOn;
        }

        if (!await data.HasAuthenticatorKeyAsync(user))
        {
            return AccountErrors.TwoFactorSetupRequired;
        }

        if (await data.IsLockedOutAsync(user))
        {
            return AccountErrors.TooManyAttempts;
        }

        if (!await data.VerifyAuthenticatorCodeAsync(user, request.Code))
        {
            return await AccountGuards.FailAsync(data, user, AccountErrors.InvalidCode, cancellationToken);
        }

        await data.SetTwoFactorEnabledAsync(user, enabled: true);
        var codes = await data.NewRecoveryCodesAsync(user);
        await data.ClearFailedAttemptsAsync(user);
        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AccountErrors.ChangedElsewhere;
        }

        LogEnabled(logger, user.Id);
        emailQueue.Enqueue(emails.TwoFactorTurnedOn(Guid.NewGuid(), user.Email!));
        return new RecoveryCodeList(codes);
    }

    public async Task<Result> DisableAsync(
        AccountId account,
        ConfirmPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (await passwordValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var user = await data.FindAsync(account);
        if (user is null)
        {
            return AccountErrors.AccountNotFound;
        }

        if (await AccountGuards.CheckPasswordAsync(data, user, request.Password, cancellationToken) is { } refused)
        {
            return refused;
        }

        // Turning off what is already off succeeds, so a retried request does not show an error.
        if (!user.TwoFactorEnabled)
        {
            return Result.Success;
        }

        await data.SetTwoFactorEnabledAsync(user, enabled: false);
        await data.ForgetSecondFactorsAsync(user);
        await data.ClearFailedAttemptsAsync(user);
        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AccountErrors.ChangedElsewhere;
        }

        LogDisabled(logger, user.Id);
        emailQueue.Enqueue(emails.TwoFactorTurnedOff(Guid.NewGuid(), user.Email!));
        return Result.Success;
    }

    public async Task<Result<RecoveryCodeList>> RegenerateRecoveryCodesAsync(
        AccountId account,
        ConfirmPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (await passwordValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var user = await data.FindAsync(account);
        if (user is null)
        {
            return AccountErrors.AccountNotFound;
        }

        if (await AccountGuards.CheckPasswordAsync(data, user, request.Password, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (!user.TwoFactorEnabled)
        {
            return AccountErrors.TwoFactorOff;
        }

        var codes = await data.NewRecoveryCodesAsync(user);
        await data.ClearFailedAttemptsAsync(user);
        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AccountErrors.ChangedElsewhere;
        }

        LogCodesReplaced(logger, user.Id);
        emailQueue.Enqueue(emails.RecoveryCodesReplaced(Guid.NewGuid(), user.Email!));
        return new RecoveryCodeList(codes);
    }

    public async Task<Result> VerifySecondFactorAsync(
        AccountId account,
        SecondFactorRequest request,
        CancellationToken cancellationToken)
    {
        if (await secondFactorValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var user = await data.FindAsync(account);
        if (user is null)
        {
            return AccountErrors.AccountNotFound;
        }

        if (!user.TwoFactorEnabled)
        {
            return AccountErrors.TwoFactorOff;
        }

        if (await data.IsLockedOutAsync(user))
        {
            return AccountErrors.TooManyAttempts;
        }

        var usedRecoveryCode = !Totp.TryNormalize(request.Code, out var code);
        var accepted = usedRecoveryCode
            ? await data.RedeemRecoveryCodeAsync(user, request.Code)
            : await data.VerifyAuthenticatorCodeAsync(user, code);
        if (!accepted)
        {
            return await AccountGuards.FailAsync(data, user, AccountErrors.InvalidCode, cancellationToken);
        }

        await data.ClearFailedAttemptsAsync(user);
        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            // Two requests raced with the same code; the other one won and this one is refused.
            return AccountErrors.InvalidCode;
        }

        if (usedRecoveryCode)
        {
            LogRecoveryCodeUsed(logger, user.Id);
            emailQueue.Enqueue(emails.RecoveryCodeUsed(Guid.NewGuid(), user.Email!, await data.CountRecoveryCodesAsync(user)));
        }

        return Result.Success;
    }

    // Groups of four, lower case, as authenticator apps show keys typed by hand.
    private static string FormatKey(string key) =>
        string.Join(' ', key.ToLowerInvariant().Chunk(4).Select(chunk => new string(chunk)));

    // Key URI format (google-authenticator wiki); defaults written out so every app reads the same settings.
    private static string AuthenticatorUri(string issuer, string email, string key) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(email)}" +
        $"?secret={key}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Totp.Digits}&period={Totp.StepSeconds}";

    [LoggerMessage(Level = LogLevel.Information, Message = "Two-step sign-in was turned on for account {AccountId}.")]
    private static partial void LogEnabled(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Two-step sign-in was turned off for account {AccountId}.")]
    private static partial void LogDisabled(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovery codes were replaced for account {AccountId}.")]
    private static partial void LogCodesReplaced(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "A recovery code was used for account {AccountId}.")]
    private static partial void LogRecoveryCodeUsed(ILogger logger, Guid accountId);
}
