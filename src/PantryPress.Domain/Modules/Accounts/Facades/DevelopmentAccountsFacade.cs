using Microsoft.Extensions.Logging;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.DataLayer;

namespace PantryPress.Domain.Modules.Accounts.Facades;

/// <inheritdoc cref="IDevelopmentAccounts"/>
/// <remarks>
/// It goes through the same store and the same unit of work as sign-up (PP1.2), so the account it writes is the account
/// sign-up would have written, then confirms the address in the same save rather than mailing a link: the one thing a
/// developer cannot do for themselves while sign-up is closed. Nothing is staged in the outbox, so the Worker sends
/// nothing. The address is never logged, as everywhere else in this module.
/// </remarks>
internal sealed partial class DevelopmentAccountsFacade(
    AccountsDataLayer data,
    IAuditLog audit,
    ILogger<DevelopmentAccountsFacade> logger) : IDevelopmentAccounts
{
    public async Task<DevelopmentAccount> EnsureAsync(string email, string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (await data.FindByEmailAsync(email) is { } existing)
        {
            // Left as it is, password and all: a second run must change nothing a developer has since done to it.
            return new DevelopmentAccount(new AccountId(existing.Id), DevelopmentAccountOutcome.AlreadyThere);
        }

        // The email doubles as the Identity user name, as at sign-up.
        var user = new ApplicationUser { UserName = email, Email = email };
        var created = await data.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            if (created.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal)))
            {
                LogPasswordRefused(logger, PasswordRules.MinLength);
                return new DevelopmentAccount(null, DevelopmentAccountOutcome.PasswordRefused);
            }

            // Anything else is a bug in the seed - a malformed address, a duplicate the read above missed - and the
            // migration job fails on it. The codes name no secret and no address.
            throw new InvalidOperationException(
                $"The development seed could not create an account: {string.Join(", ", created.Errors.Select(e => e.Code))}.");
        }

        // The point of the operation: the address is the developer's own machine, so it is confirmed without a link.
        await data.MarkEmailConfirmedAsync(user);

        var account = new AccountId(user.Id);
        audit.Record(
            new AuditEvent(AccountAudit.DevelopmentSeeded, AuditActor.DevelopmentSeed, AccountAudit.Target(account)),
            SiteScope.None);
        if (await data.SaveNewAccountAsync(email, cancellationToken) != SaveOutcome.Saved)
        {
            throw new InvalidOperationException("The development seed could not save a new account.");
        }

        LogCreated(logger, user.Id);
        return new DevelopmentAccount(account, DevelopmentAccountOutcome.Created);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The development seed created account {AccountId} with its address already confirmed.")]
    private static partial void LogCreated(ILogger logger, Guid accountId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "DevSeed:Password does not meet the platform's password rules (at least {MinLength} characters), so "
            + "no development account was created. Set a longer one and run again.")]
    private static partial void LogPasswordRefused(ILogger logger, int minLength);
}
