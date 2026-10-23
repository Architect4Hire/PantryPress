using Microsoft.Extensions.Logging;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Platform.Business;
using PantryPress.Domain.Modules.Platform.Contracts;

namespace PantryPress.Domain.Modules.Platform.Facades;

/// <summary>
/// The audited platform-role path (ACCT-005; PP1.4). It orchestrates the Accounts module through
/// <see cref="IAccountRoles"/> and writes one audit line per real change: who asked, what changed, which account, and
/// why. Email addresses are masked; the durable audit log arrives at PP1.11 and reads the same events.
/// </summary>
internal sealed partial class PlatformAdminFacade(IAccountRoles accounts, ILogger<PlatformAdminFacade> logger)
    : IPlatformAdminFacade
{
    public async Task<Result<PlatformAdminOutcome>> GrantAsync(
        string email,
        string actor,
        string reason,
        CancellationToken cancellationToken)
    {
        var account = await accounts.FindByEmailAsync(email, cancellationToken);
        if (account is null)
        {
            return AccountErrors.AccountNotFound;
        }

        var granted = await accounts.GrantPlatformAdminAsync(account.Value, cancellationToken);
        if (!granted.IsSuccess)
        {
            return granted.Error;
        }

        if (granted.Value)
        {
            LogGranted(logger, actor, account.Value.Value, reason);
        }

        return new PlatformAdminOutcome(account.Value, granted.Value);
    }

    public async Task<Result<PlatformAdminOutcome>> RevokeAsync(
        AccountId account,
        string actor,
        string reason,
        CancellationToken cancellationToken)
    {
        var revoked = await accounts.RevokePlatformAdminAsync(account, cancellationToken);
        if (!revoked.IsSuccess)
        {
            return revoked.Error;
        }

        if (revoked.Value)
        {
            LogRevoked(logger, actor, account.Value, reason);
        }

        return new PlatformAdminOutcome(account, revoked.Value);
    }

    public Task<Result<IReadOnlyList<AccountId>>> ListAsync(CancellationToken cancellationToken) =>
        accounts.ListPlatformAdminsAsync(cancellationToken);

    [LoggerMessage(
        EventId = 1400,
        EventName = "PlatformAdminGranted",
        Level = LogLevel.Information,
        Message = "Audit: {Actor} granted PlatformAdmin to account {AccountId} ({Reason})")]
    private static partial void LogGranted(ILogger logger, string actor, Guid accountId, string reason);

    [LoggerMessage(
        EventId = 1401,
        EventName = "PlatformAdminRevoked",
        Level = LogLevel.Information,
        Message = "Audit: {Actor} revoked PlatformAdmin from account {AccountId} ({Reason})")]
    private static partial void LogRevoked(ILogger logger, string actor, Guid accountId, string reason);
}
