using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Platform.Business;
using PantryPress.Domain.Modules.Platform.Contracts;

namespace PantryPress.Domain.Modules.Platform.Facades;

/// <summary>
/// Applies <see cref="PlatformOptions.Admins"/>, the operator's list, as the whole truth about who holds
/// <c>PlatformAdmin</c> (ACCT-005), by the rule in <see cref="PlatformAdminReconciliation"/>: addresses on the list are
/// granted, holders that are not on it are revoked, and a run with nothing to change changes nothing. An address with no
/// account, or whose account has not confirmed it, is a warning and resolves to nobody - so a holder in that state is
/// revoked - and the next run picks it up (PP1.12).
/// <para>
/// A failure throws rather than returning a result, deliberately: the only caller is the migration job, whose non-zero
/// exit is what keeps every dependent from starting on a half-applied list, so a result would only be rethrown there.
/// </para>
/// </summary>
internal sealed partial class PlatformAdminReconciler(
    IPlatformAdminFacade admins,
    IOptions<PlatformOptions> options,
    ILogger<PlatformAdminReconciler> logger) : IPlatformAdminReconciler
{
    private const string ListedReason = "listed in the operator's PlatformAdmin list";
    private const string UnlistedReason = "no longer in the operator's PlatformAdmin list";

    public async Task<PlatformAdminReconcileReport> RunAsync(CancellationToken cancellationToken)
    {
        if (!PlatformAdminList.TryParse(options.Value.Admins, out var listed))
        {
            // The options validator refuses this at startup; a host that skipped it must not fall through to an empty
            // list, which would read as "revoke everyone".
            throw new InvalidOperationException("Platform:Admins is not a list of email addresses.");
        }

        if (!PlatformAdminReconciliation.HasOpinion(listed))
        {
            LogNoList(logger);
            return PlatformAdminReconcileReport.NotConfigured;
        }

        var intended = new HashSet<AccountId>();
        var granted = 0;
        var unresolved = 0;

        foreach (var email in listed)
        {
            var outcome = await admins.GrantAsync(email, PlatformAdminActors.OperatorConfiguration, ListedReason, cancellationToken);
            if (!outcome.IsSuccess)
            {
                if (outcome.Error.Code == AccountErrors.AccountNotFound.Code)
                {
                    // Nobody has signed up with this address yet, so nobody holds the role under it either.
                    LogNoAccount(logger, MaskedEmail.From(email));
                }
                else if (outcome.Error.Code == AccountErrors.EmailNotVerified.Code)
                {
                    // Someone signed up with the address but has never answered an email sent to it, so it is not yet
                    // known to be the operator's. It resolves to nobody, which also revokes the role if it is held.
                    LogUnconfirmed(logger, MaskedEmail.From(email));
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Granting PlatformAdmin to {MaskedEmail.From(email)} failed with '{outcome.Error.Code}'.");
                }

                unresolved++;
                continue;
            }

            intended.Add(outcome.Value.Account);
            if (outcome.Value.Changed)
            {
                granted++;
            }
        }

        var holders = await admins.ListAsync(cancellationToken);
        if (!holders.IsSuccess)
        {
            throw new InvalidOperationException($"Reading the PlatformAdmin holders failed with '{holders.Error.Code}'.");
        }

        var revoked = 0;
        foreach (var holder in PlatformAdminReconciliation.Unlisted(holders.Value, intended))
        {
            var outcome = await admins.RevokeAsync(holder, PlatformAdminActors.OperatorConfiguration, UnlistedReason, cancellationToken);
            if (!outcome.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Revoking PlatformAdmin from account {holder.Value} failed with '{outcome.Error.Code}'.");
            }

            if (outcome.Value.Changed)
            {
                revoked++;
            }
        }

        LogApplied(logger, listed.Count, granted, revoked, unresolved);
        return new PlatformAdminReconcileReport(true, granted, revoked, unresolved);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "No PlatformAdmin list is configured; platform roles were left as they are")]
    private static partial void LogNoList(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The PlatformAdmin list names {MaskedEmail}, which has no account yet; it was skipped")]
    private static partial void LogNoAccount(ILogger logger, string maskedEmail);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The PlatformAdmin list names {MaskedEmail}, whose account has not confirmed its email address; it was not granted")]
    private static partial void LogUnconfirmed(ILogger logger, string maskedEmail);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "PlatformAdmin list applied: {Listed} listed, {Granted} granted, {Revoked} revoked, {Unresolved} without a confirmed account")]
    private static partial void LogApplied(ILogger logger, int listed, int granted, int revoked, int unresolved);
}
