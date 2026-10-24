using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using PantryPress.Domain.Managers.Audit;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Platform;
using PantryPress.Domain.Modules.Platform.Contracts;
using PantryPress.Domain.Modules.Platform.Facades;

namespace PantryPress.Tests.Platform;

// PP1.4: the operator's list is the whole truth about who holds PlatformAdmin, and an empty list is no opinion at all
// (ACCT-005).
public sealed class PlatformAdminReconcilerTests
{
    private readonly FakePlatformAdminFacade _admins = new();
    private readonly FakeLogger<PlatformAdminReconciler> _logger = new();

    [Fact]
    public async Task A_listed_address_is_granted_the_role()
    {
        var account = _admins.AddAccount("ops@example.test");

        var report = await Reconciler("ops@example.test").RunAsync(CancellationToken.None);

        Assert.Equal(new PlatformAdminReconcileReport(true, Granted: 1, Revoked: 0, Unresolved: 0), report);
        Assert.Equal(new[] { account }, _admins.Holders);
    }

    [Fact]
    public async Task A_holder_who_is_not_listed_loses_the_role()
    {
        var listed = _admins.AddAccount("ops@example.test");
        var former = _admins.AddAccount("former@example.test", holdsRole: true);

        var report = await Reconciler("ops@example.test").RunAsync(CancellationToken.None);

        Assert.Equal(new PlatformAdminReconcileReport(true, Granted: 1, Revoked: 1, Unresolved: 0), report);
        Assert.Equal(new[] { listed }, _admins.Holders);
        Assert.DoesNotContain(former, _admins.Holders);
    }

    [Fact]
    public async Task A_list_that_is_already_applied_changes_nothing()
    {
        var account = _admins.AddAccount("ops@example.test", holdsRole: true);

        var report = await Reconciler("ops@example.test").RunAsync(CancellationToken.None);

        Assert.Equal(new PlatformAdminReconcileReport(true, Granted: 0, Revoked: 0, Unresolved: 0), report);
        Assert.Equal(new[] { account }, _admins.Holders);
    }

    [Fact]
    public async Task An_address_with_no_account_is_a_masked_warning_not_a_failure()
    {
        _admins.AddAccount("ops@example.test");

        var report = await Reconciler("ops@example.test, newcomer@example.test").RunAsync(CancellationToken.None);

        Assert.Equal(new PlatformAdminReconcileReport(true, Granted: 1, Revoked: 0, Unresolved: 1), report);
        var warning = Assert.Single(_logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.Contains("n***@example.test", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("newcomer@", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_address_whose_account_never_confirmed_it_is_not_granted_and_loses_a_role_it_holds()
    {
        // Whoever signs up first with an address on the list is only claiming it until an email sent there is answered
        // (PP1.12), so the address resolves to nobody - which is also what takes the role from an account in that state.
        var squatter = _admins.AddAccount("ops@example.test", holdsRole: true, confirmed: false);

        var report = await Reconciler("ops@example.test").RunAsync(CancellationToken.None);

        Assert.Equal(new PlatformAdminReconcileReport(true, Granted: 0, Revoked: 1, Unresolved: 1), report);
        Assert.DoesNotContain(squatter, _admins.Holders);
        var warning = Assert.Single(_logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.Contains("o***@example.test", warning.Message, StringComparison.Ordinal);
        Assert.Contains("not confirmed", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ops@", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public async Task No_list_leaves_every_holder_alone(string? configured)
    {
        var holder = _admins.AddAccount("ops@example.test", holdsRole: true);

        var report = await Reconciler(configured).RunAsync(CancellationToken.None);

        Assert.Equal(PlatformAdminReconcileReport.NotConfigured, report);
        Assert.False(report.Configured);
        Assert.Equal(new[] { holder }, _admins.Holders);
        Assert.Empty(_admins.Calls);
    }

    [Fact]
    public async Task A_list_that_is_not_addresses_fails_the_run_instead_of_revoking_everyone()
    {
        var holder = _admins.AddAccount("ops@example.test", holdsRole: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Reconciler("nonsense").RunAsync(CancellationToken.None));

        Assert.Equal(new[] { holder }, _admins.Holders);
    }

    [Fact]
    public async Task A_failure_other_than_a_missing_account_fails_the_run()
    {
        _admins.AddAccount("ops@example.test");
        _admins.FailWith = AccountErrors.ChangedElsewhere;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Reconciler("ops@example.test").RunAsync(CancellationToken.None));

        Assert.Contains("o***@example.test", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ops@", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_change_is_asked_for_by_the_operator_configuration_with_a_reason()
    {
        _admins.AddAccount("ops@example.test");
        _admins.AddAccount("former@example.test", holdsRole: true);

        await Reconciler("ops@example.test").RunAsync(CancellationToken.None);

        Assert.All(_admins.Calls, call => Assert.Equal(PlatformAdminActors.OperatorConfiguration, call.Actor));
        Assert.Contains(_admins.Calls, call => call.Kind == "grant" && call.Reason.Contains("listed", StringComparison.Ordinal));
        Assert.Contains(
            _admins.Calls,
            call => call.Kind == "revoke" && call.Reason.Contains("no longer", StringComparison.Ordinal));
    }

    private PlatformAdminReconciler Reconciler(string? admins) =>
        new(_admins, Options.Create(new PlatformOptions { Admins = admins ?? string.Empty }), _logger);

    private sealed record Call(string Kind, AuditActor Actor, string Reason);

    // The audited facade as the reconciliation sees it: accounts by address, who holds the role, and what was asked.
    private sealed class FakePlatformAdminFacade : IPlatformAdminFacade
    {
        private readonly Dictionary<string, AccountId> _accounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<AccountId> _holders = [];
        private readonly HashSet<AccountId> _unconfirmed = [];

        public Error? FailWith { get; set; }

        public List<Call> Calls { get; } = [];

        public IReadOnlyCollection<AccountId> Holders => _holders;

        public AccountId AddAccount(string email, bool holdsRole = false, bool confirmed = true)
        {
            var account = new AccountId(Guid.NewGuid());
            _accounts[email] = account;
            if (!confirmed)
            {
                _unconfirmed.Add(account);
            }

            if (holdsRole)
            {
                _holders.Add(account);
            }

            return account;
        }

        public Task<Result<PlatformAdminOutcome>> GrantAsync(
            string email,
            AuditActor actor,
            string reason,
            CancellationToken cancellationToken)
        {
            Calls.Add(new Call("grant", actor, reason));
            if (FailWith is { } error)
            {
                return Task.FromResult(Result<PlatformAdminOutcome>.Failure(error));
            }

            if (!_accounts.TryGetValue(email, out var account))
            {
                return Task.FromResult(Result<PlatformAdminOutcome>.Failure(AccountErrors.AccountNotFound));
            }

            if (_unconfirmed.Contains(account))
            {
                return Task.FromResult(Result<PlatformAdminOutcome>.Failure(AccountErrors.EmailNotVerified));
            }

            return Task.FromResult(Result<PlatformAdminOutcome>.Success(
                new PlatformAdminOutcome(account, _holders.Add(account))));
        }

        public Task<Result<PlatformAdminOutcome>> RevokeAsync(
            AccountId account,
            AuditActor actor,
            string reason,
            CancellationToken cancellationToken)
        {
            Calls.Add(new Call("revoke", actor, reason));
            return Task.FromResult(FailWith is { } error
                ? Result<PlatformAdminOutcome>.Failure(error)
                : Result<PlatformAdminOutcome>.Success(new PlatformAdminOutcome(account, _holders.Remove(account))));
        }

        public Task<Result<IReadOnlyList<AccountId>>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<AccountId>>.Success([.. _holders]));
    }
}
