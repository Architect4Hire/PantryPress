using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.Extensions.Logging;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;
using PantryPress.Domain.Modules.Accounts.Repositories;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <summary>
/// The sign-in steps behind the BFF session (ACCT-002, PP-D13; PP1.6). The gateway holds the session, so nothing here
/// touches a cookie: these calls say who signed in and bind the session to the account's security stamp. Sign-in goes
/// through this facade rather than Identity's <c>SignInManager</c>, because the account store does not save on its own
/// and lockout has to be committed even when the request fails.
/// </summary>
internal sealed partial class SessionFacade(
    IValidator<SignInRequest> signInValidator,
    IValidator<CurrentSessionRequest> currentValidator,
    IValidator<EndSessionRequest> endValidator,
    AccountsDataLayer data,
    ITwoFactorFacade twoFactor,
    IAuditLog audit,
    IClock clock,
    ILogger<SessionFacade> logger) : ISessionFacade
{
    public async Task<Result<SessionStart>> SignInAsync(SignInRequest request, CancellationToken cancellationToken)
    {
        if (await signInValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var user = await data.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // The same hashing work as a real attempt, so timing does not say whether the address has an account.
            data.HashPasswordForTiming(request.Password);
            return AccountErrors.InvalidCredentials;
        }

        if (await AccountGuards.LockedOutAsync(data, clock, user) is { } locked)
        {
            return locked;
        }

        if (!await data.CheckPasswordAsync(user, request.Password))
        {
            // Counts toward lockout and is committed straight away, so a failed sign-in still leaves its mark.
            return await AccountGuards.FailAsync(data, audit, clock, user, AccountErrors.InvalidCredentials, cancellationToken);
        }

        await data.ClearFailedAttemptsAsync(user);
        if (await data.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return AccountErrors.ChangedElsewhere;
        }

        if (user.TwoFactorEnabled)
        {
            LogSecondStepOwed(logger, user.Id);
        }
        else if ((await data.GetRolesAsync(user)).Contains(AccountRoles.PlatformAdmin, StringComparer.Ordinal))
        {
            // The PlatformAdmin policy requires a session that passed the second step (auth.md), so this account
            // signs in but reaches no operator work. It fails closed; the warning is how the operator hears about it.
            LogPlatformAdminWithoutSecondStep(logger, user.Id);
        }

        return await StartAsync(user, secondStepRequired: user.TwoFactorEnabled);
    }

    public async Task<Result<SessionStart>> CompleteSecondFactorAsync(
        AccountId account,
        string securityStamp,
        SecondFactorRequest request,
        CancellationToken cancellationToken)
    {
        // The stamp is checked before the code, and before anything is counted against the account: without proof
        // that the password step really happened, there is no sign-in here to finish.
        var user = await data.FindAsync(account);
        if (user is null || !await MatchesStampAsync(user, securityStamp))
        {
            LogSignInExpired(logger, account.Value);
            return AccountErrors.SignInExpired;
        }

        // The code, lockout, recovery-code redemption, and the "one code, once" rule all belong to two-step sign-in
        // (PP1.3); this step only turns a verified code into a complete session.
        var verified = await twoFactor.VerifySecondFactorAsync(account, request, cancellationToken);
        if (!verified.IsSuccess)
        {
            return verified.Error;
        }

        return await StartAsync(user, secondStepRequired: false);
    }

    public async Task<SessionIdentity?> ResolveAsync(
        AccountId account,
        string securityStamp,
        string sessionId,
        bool secondFactorVerified,
        CancellationToken cancellationToken)
    {
        var user = await data.FindAsync(account);
        if (user is null)
        {
            return null;
        }

        // Signed out on purpose (PP1.12): this cookie is over, wherever it is presented from.
        if (await data.IsSessionRevokedAsync(sessionId, cancellationToken))
        {
            LogSignedOutSessionUsed(logger, user.Id);
            return null;
        }

        // The stamp moves whenever the password, the second factor, or a platform role changes, which is what ends
        // the account's other sessions.
        if (!await MatchesStampAsync(user, securityStamp))
        {
            LogStampMoved(logger, user.Id);
            return null;
        }

        // An account with two-step sign-in may only act on a session that passed it. Turning it on rotates the stamp,
        // so a session from before should already be gone; this is the rule that makes it certain either way.
        if (!SessionRules.MayAct(user.TwoFactorEnabled, secondFactorVerified))
        {
            LogSecondStepMissing(logger, user.Id);
            return null;
        }

        return new SessionIdentity(
            user.Id,
            user.Email!,
            user.EmailConfirmed,
            user.TwoFactorEnabled,
            SessionRules.PassedSecondStep(user.TwoFactorEnabled, secondFactorVerified),
            await data.GetRolesAsync(user));
    }

    public async Task<Result<CurrentSession>> CurrentAsync(
        CurrentSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (await currentValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var identity = await ResolveAsync(
            new AccountId(request.AccountId),
            request.SecurityStamp,
            request.SessionId,
            request.SecondFactorVerified,
            cancellationToken);
        return identity is null
            ? AccountErrors.SessionEnded
            : new CurrentSession(identity.AccountId, identity.Email, identity.EmailVerified, identity.SecondFactorVerified);
    }

    public async Task<Result> EndAsync(EndSessionRequest request, CancellationToken cancellationToken)
    {
        if (await endValidator.CheckAsync(request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        // Remembered for longer than any session can live, so the cookie is refused for as long as it could be used.
        var now = clock.UtcNow;
        var staged = await data.StageRevocationAsync(
            new RevokedSession
            {
                SessionId = request.SessionId,
                AccountId = request.AccountId,
                RevokedAt = now,
                ExpiresAt = now + SessionLimits.RevocationRetention,
            },
            cancellationToken);
        if (staged)
        {
            await data.SaveRevocationAsync(request.SessionId, cancellationToken);
        }

        LogSignedOut(logger, request.AccountId);
        return Result.Success;
    }

    public async Task<string?> CurrentStampAsync(AccountId account, CancellationToken cancellationToken)
    {
        var user = await data.FindAsync(account);
        return user is null ? null : await data.GetSecurityStampAsync(user);
    }

    // Compared in constant time: a stamp is a server-side value and the one proof a sign-in step really happened.
    private async Task<bool> MatchesStampAsync(ApplicationUser user, string securityStamp) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(await data.GetSecurityStampAsync(user)),
            Encoding.UTF8.GetBytes(securityStamp));

    private async Task<SessionStart> StartAsync(ApplicationUser user, bool secondStepRequired)
    {
        if (!secondStepRequired)
        {
            LogSignedIn(logger, user.Id);
        }

        return new SessionStart(
            user.Id,
            user.Email!,
            await data.GetSecurityStampAsync(user),
            secondStepRequired,
            user.EmailConfirmed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {AccountId} signed in.")]
    private static partial void LogSignedIn(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {AccountId} owes a second sign-in step.")]
    private static partial void LogSecondStepOwed(ILogger logger, Guid accountId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Account {AccountId} holds PlatformAdmin without two-step sign-in, so it cannot reach operator work.")]
    private static partial void LogPlatformAdminWithoutSecondStep(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "A session for account {AccountId} was signed out.")]
    private static partial void LogSignedOut(ILogger logger, Guid accountId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A signed-out session for account {AccountId} was presented again; it was refused.")]
    private static partial void LogSignedOutSessionUsed(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "A session for account {AccountId} ended: its security stamp moved.")]
    private static partial void LogStampMoved(ILogger logger, Guid accountId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "A session for account {AccountId} ended: the account has two-step sign-in and this session never passed it.")]
    private static partial void LogSecondStepMissing(ILogger logger, Guid accountId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "A second sign-in step for account {AccountId} was refused: there is no first step it belongs to.")]
    private static partial void LogSignInExpired(ILogger logger, Guid accountId);
}
