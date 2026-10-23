using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Platform.Contracts;
using PantryPress.Domain.Modules.Platform.Facades;

namespace PantryPress.Tests.Platform;

// PP1.4: every real platform-role change is audited with who asked and why, and a repeat changes and audits nothing
// (ACCT-005).
public sealed class PlatformAdminFacadeTests
{
    private const string Email = "ops.lead@example.test";
    private const string Actor = PlatformAdminActors.OperatorConfiguration;

    private readonly FakeAccountRoles _accounts = new();
    private readonly FakeLogger<PlatformAdminFacade> _logger = new();

    [Fact]
    public async Task A_grant_records_who_asked_which_account_and_why()
    {
        var account = _accounts.AddAccount(Email);

        var outcome = await Facade().GrantAsync(Email, Actor, "on-call rotation", CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(account, outcome.Value.Account);
        Assert.True(outcome.Value.Changed);
        var audit = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal("PlatformAdminGranted", audit.Id.Name);
        Assert.Equal(LogLevel.Information, audit.Level);
        Assert.Contains(Actor, audit.Message, StringComparison.Ordinal);
        Assert.Contains(account.Value.ToString(), audit.Message, StringComparison.Ordinal);
        Assert.Contains("on-call rotation", audit.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Granting_again_changes_nothing_and_audits_nothing()
    {
        _accounts.AddAccount(Email, holdsRole: true);

        var outcome = await Facade().GrantAsync(Email, Actor, "listed", CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.False(outcome.Value.Changed);
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task A_revocation_is_audited_and_a_second_one_is_not()
    {
        var account = _accounts.AddAccount(Email, holdsRole: true);
        var facade = Facade();

        var first = await facade.RevokeAsync(account, Actor, "left the team", CancellationToken.None);
        var second = await facade.RevokeAsync(account, Actor, "left the team", CancellationToken.None);

        Assert.True(first.Value.Changed);
        Assert.False(second.Value.Changed);
        var audit = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal("PlatformAdminRevoked", audit.Id.Name);
        Assert.Contains(account.Value.ToString(), audit.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_address_with_no_account_is_refused_and_never_audited()
    {
        var outcome = await Facade().GrantAsync(Email, Actor, "listed", CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(AccountErrors.AccountNotFound.Code, outcome.Error.Code);
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task A_refused_change_is_not_audited_as_if_it_happened()
    {
        var account = _accounts.AddAccount(Email);
        _accounts.FailWith = AccountErrors.ChangedElsewhere;

        var grant = await Facade().GrantAsync(Email, Actor, "listed", CancellationToken.None);
        var revoke = await Facade().RevokeAsync(account, Actor, "unlisted", CancellationToken.None);

        Assert.False(grant.IsSuccess);
        Assert.False(revoke.IsSuccess);
        Assert.Equal(AccountErrors.ChangedElsewhere.Code, grant.Error.Code);
        Assert.Equal(AccountErrors.ChangedElsewhere.Code, revoke.Error.Code);
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task The_audit_line_never_carries_the_email_address()
    {
        var account = _accounts.AddAccount(Email);
        var facade = Facade();

        await facade.GrantAsync(Email, Actor, "listed", CancellationToken.None);
        await facade.RevokeAsync(account, Actor, "unlisted", CancellationToken.None);

        Assert.All(_logger.Collector.GetSnapshot(), record =>
        {
            Assert.DoesNotContain("ops.lead", record.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("example.test", record.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    private PlatformAdminFacade Facade() => new(_accounts, _logger);
}
