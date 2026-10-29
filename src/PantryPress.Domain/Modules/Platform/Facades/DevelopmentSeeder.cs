using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Platform.Business;
using PantryPress.Domain.Modules.Platform.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Platform.Facades;

/// <summary>
/// Seeds a developer's machine (PP3.9a): the two accounts of <see cref="DevelopmentSeedData"/> with their addresses
/// confirmed, then the two sites the first of them owns, each created through <see cref="ISitesFacade"/> exactly as the
/// API creates one (PP2.10) - the site, its Owner membership, and its canonical subdomain in one save, with its own
/// audit entry. Nothing here writes a row itself, mints a site id, or touches another site's data.
/// <para>
/// So each site's own audit entry names the owner account, as every site creation's does, while the accounts the seed
/// makes name the seed (<see cref="AuditActorKind.DevelopmentSeed"/>): the sites are created the way PP2.10 creates
/// them, and giving the facade an actor of its own would change a production path for a development one.
/// </para>
/// <para>
/// It is registered only on a Development host, and only there does the migration job resolve this implementation
/// rather than <see cref="DisabledDevelopmentSeeder"/>.
/// </para>
/// <para>
/// A failure throws rather than returning a result, like <see cref="PlatformAdminReconciler"/>: the only caller is the
/// migration job, whose non-zero exit is what tells the developer to look. The two expected outcomes are not failures -
/// no password set, and an address or a slug that is already there - and neither stops the job.
/// </para>
/// </summary>
internal sealed partial class DevelopmentSeeder(
    IDevelopmentAccounts accounts,
    IServiceScopeFactory scopes,
    IOptions<DevelopmentSeedOptions> options,
    ILogger<DevelopmentSeeder> logger) : IDevelopmentSeeder
{
    public async Task<DevelopmentSeedReport> RunAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
        {
            LogNoPassword(logger);
            return DevelopmentSeedReport.NotConfigured;
        }

        // The owner of the seeded sites, and a second account that joins none of them: an invitation needs someone to
        // send it to who is not a member yet.
        if (await EnsureAsync(DevelopmentSeedData.OwnerEmail, cancellationToken) is not { } owner
            || await EnsureAsync(DevelopmentSeedData.EditorEmail, cancellationToken) is not { } editor)
        {
            // The Accounts module has already said what to change about the password; nothing was written.
            return DevelopmentSeedReport.NotConfigured;
        }

        var created = new[] { owner, editor }.Count(a => a.Outcome == DevelopmentAccountOutcome.Created);
        var sites = 0;
        foreach (var request in DevelopmentSeedData.SiteRequests)
        {
            if (await CreateSiteAsync(owner.Account!.Value, request, cancellationToken))
            {
                sites++;
            }
        }

        LogSeeded(logger, created, sites);
        return new DevelopmentSeedReport(true, true, created, sites);
    }

    // One account, or null when the configured password is not one the platform allows.
    private async Task<DevelopmentAccount?> EnsureAsync(string email, CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAsync(email, options.Value.Password, cancellationToken);
        return account.Outcome == DevelopmentAccountOutcome.PasswordRefused ? null : account;
    }

    // One site, in a scope of its own: creating a site enters it, and a scope may enter only one (tenancy.md rule 2).
    private async Task<bool> CreateSiteAsync(AccountId owner, CreateSiteRequest request, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<ISitesFacade>()
            .CreateAsync(owner, request, cancellationToken);
        if (created.IsSuccess)
        {
            return true;
        }

        if (created.Error.Code == SiteErrors.SlugUnavailable.Code)
        {
            // The address is already held - by the site a run before made, or by one the developer renamed into its
            // place. Either way it is left exactly as it is: a slug is never reissued (SITE-002).
            LogSiteHeld(logger, request.Slug);
            return false;
        }

        if (created.Error.Code == AccountErrors.EmailNotVerified.Code)
        {
            // The owner's account was already there with its address unconfirmed - signed up by hand, say - and the
            // seed leaves an existing account exactly as it is, so it has reached the sites an unconfirmed account may
            // own (PP2.10). Expected, not a failure: a local run must still start.
            LogOwnerUnconfirmed(logger, request.Slug);
            return false;
        }

        throw new InvalidOperationException(
            $"The development seed could not create the site {request.Slug}: '{created.Error.Code}'.");
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "No development seed password is set, so no accounts or sites were seeded. To seed them, run "
            + "'dotnet user-secrets set DevSeed:Password <password> --project src/PantryPress.AppHost' and start again.")]
    private static partial void LogNoPassword(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "A site already holds the address {Slug}, so the development seed left it alone")]
    private static partial void LogSiteHeld(ILogger logger, string slug);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The seeded owner's email address is not confirmed, so it may own no more sites and {Slug} was not "
            + "created. Confirm the address, or let the seed make the account itself.")]
    private static partial void LogOwnerUnconfirmed(ILogger logger, string slug);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Development seed applied: {AccountsCreated} accounts and {SitesCreated} sites created")]
    private static partial void LogSeeded(ILogger logger, int accountsCreated, int sitesCreated);
}
